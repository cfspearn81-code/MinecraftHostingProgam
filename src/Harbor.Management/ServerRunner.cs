using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Harbor.Core;
using Harbor.Foundation;

namespace Harbor.Management;

// One runner owns the server lock for the entire child lifetime.
public sealed class ServerRunner(ServerStore store)
{
    public async Task RunAsync(ServerDefinition server, bool proof, CancellationToken ct)
    {
        await using var gate = store.AcquireServer(server.Id);
        var dir = store.DirectoryFor(server.Id);
        ProcessOwnership.EnsureNoSurvivor(dir);
        var jar = Path.Combine(dir, "server.jar");
        var metadataPath = Path.Combine(dir, "installation.json");
        if (!File.Exists(jar) || !File.Exists(metadataPath)) throw new InvalidOperationException("Server is not installed; run install first.");
        using var installation = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath, ct));
        var hash = installation.RootElement.GetProperty("Sha1").GetString();
        var requiredJava = installation.RootElement.GetProperty("JavaMajor").GetInt32();
        await using (var stream = File.OpenRead(jar))
            if (!Convert.ToHexString(await SHA1.HashDataAsync(stream, ct)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Installed JAR checksum does not match installation metadata. Reinstall it.");
        var java = await JavaProbe.ProbeAsync(server.JavaExecutable, ct);
        if (java.Major != requiredJava) throw new InvalidOperationException($"This server requires Java {requiredJava}; selected Java is {java.Major}.");
        if (!File.Exists(Path.Combine(dir, "eula.txt")) || !File.ReadLines(Path.Combine(dir, "eula.txt")).Any(l => l.Trim() == "eula=true"))
            throw new InvalidOperationException("EULA not accepted. Read https://aka.ms/MinecraftEULA and run accept-eula explicitly.");
        var props = Path.Combine(dir, "server.properties");
        if (!File.Exists(props)) await ServerStore.AtomicWriteAsync(props, $"server-port={server.Port}\nonline-mode=true\n", ct);
        else
        {
            // Do not guess what a customized Java properties file means.
            var lines = await File.ReadAllLinesAsync(props, ct);
            var ports = lines.Where(l => l.TrimStart().StartsWith("server-port=", StringComparison.Ordinal)).ToArray();
            if (ports.Length != 1 || ports[0].Trim().Split('=', 2)[1].Trim() != server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture))
                throw new InvalidOperationException("server.properties port differs from the Harbor definition or is ambiguous; reconcile it before starting.");
        }
        try { var listener = new TcpListener(IPAddress.Any, server.Port); listener.Start(); listener.Stop(); }
        catch (SocketException ex) { throw new InvalidOperationException($"Port {server.Port} is unavailable: {ex.Message}", ex); }
        var start = new ProcessStartInfo(server.JavaExecutable) { WorkingDirectory = dir, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add($"-Xmx{server.MemoryMiB}M"); start.ArgumentList.Add("-jar"); start.ArgumentList.Add("server.jar"); start.ArgumentList.Add("nogui");
        // Open the log before starting Java so an inaccessible log cannot orphan a new child.
        await using var log = new StreamWriter(new FileStream(Path.Combine(dir, "harbor-console.log"), FileMode.Append, FileAccess.Write, FileShare.Read, 4096, true)) { AutoFlush = true };
        using var child = Process.Start(start) ?? throw new IOException("Java did not start.");
        try { await ServerStore.AtomicWriteAsync(ProcessOwnership.PathFor(dir), JsonSerializer.Serialize(new ProcessRecord(child.Id, child.StartTime.ToUniversalTime().Ticks)), CancellationToken.None); }
        catch { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } throw; }
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logGate = new SemaphoreSlim(1, 1);
        async Task DrainAsync(StreamReader reader, string source)
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                var rendered = $"[{DateTimeOffset.Now:O}] [{source}] {line}";
                await logGate.WaitAsync();
                try { Console.WriteLine(rendered); await log.WriteLineAsync(rendered); }
                finally { logGate.Release(); }
                if (line.Contains("Done (", StringComparison.Ordinal) && line.Contains("For help", StringComparison.Ordinal)) ready.TrySetResult();
                if (line.Contains("players online", StringComparison.OrdinalIgnoreCase)) response.TrySetResult();
            }
        }
        var output = DrainAsync(child.StandardOutput, "out");
        var error = DrainAsync(child.StandardError, "err");
        var exited = child.WaitForExitAsync();
        var success = false;
        try
        {
            Console.WriteLine("Starting Java; waiting for Minecraft readiness. Ctrl+C requests a graceful stop.");
            var timeout = Task.Delay(TimeSpan.FromMinutes(5), ct);
            var first = await Task.WhenAny(ready.Task, exited, timeout);
            if (first != ready.Task) { ct.ThrowIfCancellationRequested(); throw new InvalidOperationException(first == exited ? "Java exited before Minecraft was ready; inspect the log above." : "Startup timed out; inspect the log above."); }
            Console.WriteLine("Minecraft is ready.");
            if (proof)
            {
                await child.StandardInput.WriteLineAsync("list");
                first = await Task.WhenAny(response.Task, exited, Task.Delay(TimeSpan.FromSeconds(30), ct));
                if (first != response.Task) { ct.ThrowIfCancellationRequested(); throw new InvalidOperationException("No observed response to list; inspect the log."); }
                Console.WriteLine("Observed the server's player-list response; stopping gracefully.");
                success = true;
            }
            else
            {
                Console.WriteLine("Enter Minecraft commands; type stop to stop the server.");
                while (!child.HasExited && !ct.IsCancellationRequested)
                {
                    var input = Console.In.ReadLineAsync(ct).AsTask();
                    first = await Task.WhenAny(input, exited);
                    if (first == exited) break;
                    var line = await input;
                    if (line is null || line.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase)) break;
                    if (line.Contains('\r') || line.Contains('\n')) continue;
                    await child.StandardInput.WriteLineAsync(line);
                }
                success = true;
            }
        }
        finally
        {
            if (!child.HasExited)
            {
                try { await child.StandardInput.WriteLineAsync("stop"); } catch (IOException) { }
                var stopped = await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(60)));
                if (stopped != exited)
                {
                    Console.Error.WriteLine($"Graceful stop has taken more than 60 seconds. Still supervising owned PID {child.Id}; do not close this terminal. Inspect server output. No automatic force-stop is performed.");
                    await exited;
                }
            }
            if (child.HasExited)
            {
                await Task.WhenAll(output, error);
                File.Delete(ProcessOwnership.PathFor(dir));
            }
        }
        if (!child.HasExited) throw new InvalidOperationException("Server did not exit; see ownership record.");
        if (!success || child.ExitCode != 0) throw new InvalidOperationException($"Server exited with code {child.ExitCode}.");
        Console.WriteLine("Server exited gracefully (exit code 0).");
    }
}
