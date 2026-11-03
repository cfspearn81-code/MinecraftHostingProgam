using Harbor.Core;
using Harbor.Foundation;
using Harbor.Management;
using System.IO.Compression;

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
    var link = Path.Combine(world, "outside-link"); File.CreateSymbolicLink(link, Path.Combine(root, "outside"));
    try { await backups.CreateAsync(server.Id); throw new Exception("Symbolic link accepted"); }
    catch (InvalidDataException) { }
    File.Delete(link);
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await backups.CreateAsync(server.Id, cancelled.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { }
    await backups.DeleteAsync(server.Id, invalidId);
    Console.WriteLine("PASS: persistence, Unicode/space paths, duplicate ports, validation, locks, backup/recovery, traversal and symlink rejection, cancellation");
    return 0;
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
