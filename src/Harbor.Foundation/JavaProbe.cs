using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Harbor.Foundation;
public sealed record JavaInfo(string Executable, int Major, string Output);
public static partial class JavaProbe
{
    [GeneratedRegex("version \\\"(?:1\\.)?(\\d+)")]
    private static partial Regex VersionRegex();
    public static async Task<JavaInfo> ProbeAsync(string executable, CancellationToken ct = default)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("-version");
        using var process = Process.Start(start) ?? throw new IOException("Could not start Java.");
        var stderr = process.StandardError.ReadToEndAsync(ct);
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        try { await process.WaitForExitAsync(ct); }
        catch { if (!process.HasExited) process.Kill(); throw; }
        var output = (await stderr) + (await stdout);
        var match = VersionRegex().Match(output);
        if (process.ExitCode != 0 || !match.Success || !int.TryParse(match.Groups[1].Value, out var major))
            throw new InvalidOperationException($"Unable to determine Java version from {executable}: {output}");
        return new JavaInfo(executable, major, output.Trim());
    }
    public static IEnumerable<string> Candidates()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal) { "java" };
        var home = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(home)) paths.Add(Path.Combine(home, "bin", "java"));
        if (Directory.Exists("/usr/lib/jvm"))
            foreach (var dir in Directory.EnumerateDirectories("/usr/lib/jvm"))
                if (File.Exists(Path.Combine(dir, "bin", "java"))) paths.Add(Path.Combine(dir, "bin", "java"));
        return paths;
    }
}
