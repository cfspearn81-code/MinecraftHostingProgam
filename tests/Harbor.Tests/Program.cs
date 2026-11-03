using Harbor.Core;
using Harbor.Foundation;

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
    Console.WriteLine("PASS: persistence, Unicode/space paths, duplicate ports, validation, exclusive lock");
    return 0;
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
