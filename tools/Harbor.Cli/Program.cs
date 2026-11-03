using Harbor.Core;
using Harbor.Foundation;
using Harbor.Installers;
using Harbor.Management;
using System.Globalization;

const string usage = """
Harbor CLI — early vanilla-server proof, not a complete server manager
Usage (optionally set XDG_DATA_HOME to choose a data location):
  java                                  Probe Java candidates
  versions                              List official Minecraft releases
  create <name> <version> <java> <memory-MiB> <port>
  list                                  Show saved servers
  install <server-id>                   Download and verify official server JAR
  accept-eula <server-id>               Explicit interactive EULA acceptance
  run <server-id>                       Run with interactive console
  proof <server-id>                     Start, observe list response, stop
  backup <server-id>                    Snapshot the default world (stopped only)
  backups <server-id>                   List world snapshots
  restore <server-id> <backup-id>       Replace default world after confirmation
  delete-backup <server-id> <backup-id> Delete a snapshot after confirmation
All server data is local. Read https://aka.ms/MinecraftEULA before acceptance.
""";
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
try
{
    if (args.Length == 0 || args[0] is "help" or "--help") { Console.WriteLine(usage); return 0; }
    var store = new ServerStore();
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    var installer = new VanillaInstaller(http, store);
    var backups = new WorldBackups(store);
    Guid Id(int i) => args.Length > i && Guid.TryParse(args[i], out var id) ? id : throw new ArgumentException("Provide a valid server ID.");
    switch (args[0])
    {
        case "java" when args.Length == 1:
            foreach (var candidate in JavaProbe.Candidates())
                try { var j = await JavaProbe.ProbeAsync(candidate, cancel.Token); Console.WriteLine($"{j.Executable}: Java {j.Major}\n{j.Output}"); }
                catch (Exception e) when (e is not OperationCanceledException) { Console.Error.WriteLine($"{candidate}: {e.Message}"); }
            break;
        case "versions" when args.Length == 1:
            foreach (var release in await installer.ReleasesAsync(cancel.Token)) Console.WriteLine(release.Id);
            break;
        case "create" when args.Length == 6:
            var server = new ServerDefinition(Guid.NewGuid(), args[1], args[2], args[3], int.Parse(args[4], CultureInfo.InvariantCulture), int.Parse(args[5], CultureInfo.InvariantCulture));
            await store.CreateAsync(server, cancel.Token); Console.WriteLine($"Created {server.Name}: {server.Id}\nData: {store.DirectoryFor(server.Id)}"); break;
        case "list" when args.Length == 1:
            foreach (var s in await store.ListAsync(cancel.Token)) Console.WriteLine($"{s.Id}  {s.Name}  vanilla {s.MinecraftVersion}  {s.MemoryMiB} MiB  port {s.Port}");
            break;
        case "install" when args.Length == 2:
            await installer.InstallAsync(await store.LoadAsync(Id(1), cancel.Token), new Progress<long>(n => Console.Error.Write($"\rDownloaded {n:N0} bytes")), cancel.Token);
            Console.WriteLine("\nInstalled and verified the server JAR."); break;
        case "accept-eula" when args.Length == 2:
            var id = Id(1); await using (store.AcquireServer(id))
            {
                ProcessOwnership.EnsureNoSurvivor(store.DirectoryFor(id));
                Console.WriteLine("Read the Minecraft EULA: https://aka.ms/MinecraftEULA\nType I AGREE to record your acceptance for this server, or anything else to cancel:");
                if (Console.ReadLine() != "I AGREE") throw new InvalidOperationException("EULA was not accepted.");
                await ServerStore.AtomicWriteAsync(Path.Combine(store.DirectoryFor(id), "eula.txt"), "# Accepted explicitly in Harbor after reading https://aka.ms/MinecraftEULA\neula=true\n", cancel.Token);
                Console.WriteLine("EULA acceptance recorded locally.");
            }
            break;
        case "run" or "proof" when args.Length == 2:
            await new ServerRunner(store).RunAsync(await store.LoadAsync(Id(1), cancel.Token), args[0] == "proof", cancel.Token); break;
        case "backup" when args.Length == 2:
            Console.WriteLine($"Created world-only backup: {await backups.CreateAsync(Id(1), cancel.Token)}"); break;
        case "backups" when args.Length == 2:
            foreach (var b in await backups.ListAsync(Id(1), cancel.Token)) Console.WriteLine($"{b.Id}  {b.ModifiedUtc:O}  {b.Bytes} bytes");
            break;
        case "restore" when args.Length == 3:
            Console.WriteLine("This replaces the default world directory. The server must be stopped. A recovery backup will be created first. Type RESTORE to continue:");
            if (Console.ReadLine() != "RESTORE") throw new InvalidOperationException("Restore cancelled.");
            var recovery = await backups.RestoreAsync(Id(1), Id(2), cancel.Token);
            Console.WriteLine(recovery is null ? "World restored; no previous world existed." : $"World restored; recovery backup: {recovery}"); break;
        case "delete-backup" when args.Length == 3:
            Console.WriteLine("This permanently deletes the selected backup. Type DELETE to continue:");
            if (Console.ReadLine() != "DELETE") throw new InvalidOperationException("Deletion cancelled.");
            await backups.DeleteAsync(Id(1), Id(2), cancel.Token); Console.WriteLine("Backup deleted."); break;
        default: Console.Error.WriteLine(usage); return 2;
    }
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled."); return 130; }
catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); return 1; }
