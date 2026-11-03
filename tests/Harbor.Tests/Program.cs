using Harbor.Core;
using Harbor.Foundation;
using Harbor.Management;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

var root = Path.Combine(Path.GetTempPath(), "Harbor tests 世界 " + Guid.NewGuid().ToString("N"));
try
{
    var store = new ServerStore(root);
    var server = new ServerDefinition(Guid.NewGuid(), "Test 世界", "1.21.1", "/some path/java", 1024, 25565);
    await store.CreateAsync(server);
    if (await store.LoadAsync(server.Id) != server) throw new Exception("Persistence round trip failed");
    if ((await store.ListAsync()).Count != 1) throw new Exception("List failed");
    try { await store.CreateAsync(server with { Id = Guid.NewGuid() }); throw new Exception("Duplicate port accepted"); }
    catch (InvalidOperationException) { }
    try { ServerValidation.Check(server with { Port = 65536 }); throw new Exception("Invalid port accepted"); }
    catch (ArgumentException) { }
    try { ServerValidation.Check(server with { MemoryMiB = 0 }); throw new Exception("Invalid memory accepted"); }
    catch (ArgumentException) { }
    await using (store.AcquireServer(server.Id))
    {
        try { await using var other = store.AcquireServer(server.Id); throw new Exception("Duplicate lock accepted"); }
        catch (InvalidOperationException) { }
    }
    var world = Path.Combine(store.DirectoryFor(server.Id), "world");
    Directory.CreateDirectory(Path.Combine(world, "DIM-1"));
    await File.WriteAllTextAsync(Path.Combine(world, "level.dat"), "before");
    await File.WriteAllTextAsync(Path.Combine(world, "DIM-1", "世界.dat"), "dimension");
    var backups = new WorldBackups(store);
    var snapshot = await backups.CreateAsync(server.Id);
    await File.WriteAllTextAsync(Path.Combine(world, "level.dat"), "after");
    var recovery = await backups.RestoreAsync(server.Id, snapshot);
    if (recovery is null || await File.ReadAllTextAsync(Path.Combine(world, "level.dat")) != "before" || await File.ReadAllTextAsync(Path.Combine(world, "DIM-1", "世界.dat")) != "dimension") throw new Exception("World restore or recovery failed");
    var listed = await backups.ListAsync(server.Id);
    if (listed.Count != 2) throw new Exception("Recovery snapshot missing");
    await backups.RestoreAsync(server.Id, recovery.Value);
    if (await File.ReadAllTextAsync(Path.Combine(world, "level.dat")) != "after") throw new Exception("Recovery backup failed");
    var invalidId = Guid.NewGuid();
    var invalidPath = Path.Combine(store.DirectoryFor(server.Id), "backups", invalidId + ".zip");
    using (var zip = ZipFile.Open(invalidPath, ZipArchiveMode.Create))
    {
        var manifest = zip.CreateEntry("manifest.json");
        using var writer = new StreamWriter(manifest.Open());
        await writer.WriteAsync("{\"SchemaVersion\":1,\"Kind\":\"harbor-world\",\"Files\":[{\"Path\":\"../escape\",\"Length\":1,\"Sha256\":\"0000000000000000000000000000000000000000000000000000000000000000\"}]}");
    }
    try { await backups.RestoreAsync(server.Id, invalidId); throw new Exception("Unsafe archive accepted"); }
    catch (InvalidDataException) { }
    if (await File.ReadAllTextAsync(Path.Combine(world, "level.dat")) != "after") throw new Exception("Invalid restore changed original world");
    var corruptId = Guid.NewGuid();
    var corruptPath = Path.Combine(store.DirectoryFor(server.Id), "backups", corruptId + ".zip");
    File.Copy(Path.Combine(store.DirectoryFor(server.Id), "backups", snapshot + ".zip"), corruptPath);
    using (var zip = ZipFile.Open(corruptPath, ZipArchiveMode.Update))
    {
        zip.GetEntry("world/level.dat")!.Delete();
        using var writer = new StreamWriter(zip.CreateEntry("world/level.dat").Open());
        await writer.WriteAsync("bef0re");
    }
    try { await backups.RestoreAsync(server.Id, corruptId); throw new Exception("Corrupt backup accepted"); }
    catch (InvalidDataException) { }
    if (await File.ReadAllTextAsync(Path.Combine(world, "level.dat")) != "after") throw new Exception("Corrupt restore changed original world");
    var symlinkArchiveId = Guid.NewGuid();
    var symlinkArchive = Path.Combine(store.DirectoryFor(server.Id), "backups", symlinkArchiveId + ".zip");
    using (var zip = ZipFile.Open(symlinkArchive, ZipArchiveMode.Create))
        zip.CreateEntry("world/link").ExternalAttributes = unchecked((int)0xA1FF0000);
    try { await backups.RestoreAsync(server.Id, symlinkArchiveId); throw new Exception("Archive symlink accepted"); }
    catch (InvalidDataException) { }
    await using (store.AcquireServer(server.Id))
    {
        try { await backups.CreateAsync(server.Id); throw new Exception("Backup while locked accepted"); }
        catch (InvalidOperationException) { }
    }
    var link = Path.Combine(world, "outside-link"); File.CreateSymbolicLink(link, Path.Combine(root, "outside"));
    try { await backups.CreateAsync(server.Id); throw new Exception("Symbolic link accepted"); }
    catch (InvalidDataException) { }
    File.Delete(link);
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await backups.CreateAsync(server.Id, cancelled.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { }
    await backups.DeleteAsync(server.Id, invalidId);
    if (OperatingSystem.IsLinux())
    {
        // Test fixture, not a real Java or Minecraft server; never used by the shipping application.
        var fakeJava = Path.Combine(root, "fake java");
        await File.WriteAllTextAsync(fakeJava, "#!/bin/sh\nif [ \"$1\" = \"-version\" ]; then echo 'openjdk version \"21.0.1\"' >&2; exit 0; fi\necho 'Done (1.0s)! For help, type help'\nwhile IFS= read -r line; do case \"$line\" in list) echo 'There are 0 of a max of 20 players online:';; stop) exit 0;; esac; done\n");
        File.SetUnixFileMode(fakeJava, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
        }
        var fake1 = new ServerDefinition(Guid.NewGuid(), "Fixture one", "fixture", fakeJava, 512, FreePort());
        var port2 = FreePort(); while (port2 == fake1.Port) port2 = FreePort();
        var fake2 = fake1 with { Id = Guid.NewGuid(), Name = "Fixture two", Port = port2 };
        foreach (var fake in new[] { fake1, fake2 })
        {
            await store.CreateAsync(fake);
            var directory = store.DirectoryFor(fake.Id);
            var jar = Path.Combine(directory, "server.jar"); await File.WriteAllTextAsync(jar, "fixture-not-minecraft");
            var hash = Convert.ToHexString(SHA1.HashData(await File.ReadAllBytesAsync(jar)));
            await File.WriteAllTextAsync(Path.Combine(directory, "installation.json"), JsonSerializer.Serialize(new { Sha1 = hash, JavaMajor = 21 }));
            // This is an isolated fake process fixture, not EULA acceptance for a Minecraft server.
            await File.WriteAllTextAsync(Path.Combine(directory, "eula.txt"), "eula=true\n");
        }
        await using (store.AcquireServer(fake1.Id))
        {
            try { await new ServerRunner(store).RunAsync(fake1, true, CancellationToken.None); throw new Exception("Duplicate start accepted"); }
            catch (InvalidOperationException) { }
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await Task.WhenAll(new ServerRunner(store).RunAsync(fake1, true, timeout.Token), new ServerRunner(store).RunAsync(fake2, true, timeout.Token));
        foreach (var fake in new[] { fake1, fake2 })
        {
            var directory = store.DirectoryFor(fake.Id);
            if (File.Exists(ProcessOwnership.PathFor(directory))) throw new Exception("Ownership record not cleared");
            var log = await File.ReadAllTextAsync(Path.Combine(directory, "harbor-console.log"));
            if (!log.Contains("Done (") || !log.Contains("players online")) throw new Exception("Mock lifecycle output missing");
        }
    }
    Console.WriteLine("PASS: persistence, validation, locks, backup/recovery and archive safety; mock-process lifecycle and isolation (not Minecraft)");
    return 0;
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
