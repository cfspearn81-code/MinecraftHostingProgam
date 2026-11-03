using System.Text.Json;
using Harbor.Core;

namespace Harbor.Foundation;

public sealed class ServerStore
{
    public string Root { get; }
    public ServerStore(string? root = null)
    {
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(data) || !Path.IsPathRooted(data))
            data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        Root = Path.GetFullPath(root ?? Path.Combine(data, "harbor"));
        Directory.CreateDirectory(Path.Combine(Root, "servers"));
    }
    public string DirectoryFor(Guid id) => Path.Combine(Root, "servers", id.ToString("D"));
    public async Task<ServerDefinition> LoadAsync(Guid id, CancellationToken ct = default)
    {
        var path = Path.Combine(DirectoryFor(id), "harbor.json");
        await using var file = File.OpenRead(path);
        var server = await JsonSerializer.DeserializeAsync<ServerDefinition>(file, cancellationToken: ct) ?? throw new InvalidDataException("Empty server definition.");
        ServerValidation.Check(server);
        if (server.Id != id) throw new InvalidDataException("Server ID does not match directory.");
        return server;
    }
    public async Task<IReadOnlyList<ServerDefinition>> ListAsync(CancellationToken ct = default)
    {
        var result = new List<ServerDefinition>();
        foreach (var dir in Directory.EnumerateDirectories(Path.Combine(Root, "servers")))
            if (Guid.TryParse(Path.GetFileName(dir), out var id)) result.Add(await LoadAsync(id, ct));
        return result;
    }
    public async Task CreateAsync(ServerDefinition server, CancellationToken ct = default)
    {
        ServerValidation.Check(server);
        await using var global = AcquireGlobal();
        if ((await ListAsync(ct)).Any(s => s.Port == server.Port)) throw new InvalidOperationException("That port is already configured for another Harbor server.");
        var dir = DirectoryFor(server.Id);
        if (Directory.Exists(dir)) throw new IOException("Server ID already exists.");
        Directory.CreateDirectory(dir);
        try { await AtomicWriteAsync(Path.Combine(dir, "harbor.json"), JsonSerializer.Serialize(server, new JsonSerializerOptions { WriteIndented = true }), ct); }
        catch { Directory.Delete(dir, true); throw; }
    }
    public FileStream AcquireGlobal() => Acquire(Path.Combine(Root, ".catalog.lock"));
    public FileStream AcquireServer(Guid id)
    {
        var dir = DirectoryFor(id);
        if (!File.Exists(Path.Combine(dir, "harbor.json"))) throw new FileNotFoundException("Server definition not found.");
        return Acquire(Path.Combine(dir, ".harbor.lock"));
    }
    private static FileStream Acquire(string path)
    {
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new InvalidOperationException("Another Harbor operation owns the lock. Try again after it finishes.", ex); }
    }
    public static async Task AtomicWriteAsync(string path, string contents, CancellationToken ct = default)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, contents, ct); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
