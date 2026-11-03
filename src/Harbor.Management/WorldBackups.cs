using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Harbor.Foundation;

namespace Harbor.Management;

// World-only snapshots. The caller never holds the server lock: these methods take it once.
public sealed class WorldBackups(ServerStore store)
{
    private const long MaxTotalBytes = 4L * 1024 * 1024 * 1024;
    private const int MaxEntries = 100_000;
    private const int MaxManifestBytes = 16 * 1024 * 1024;
    private sealed record EntryInfo(string Path, long Length, string Sha256);
    private sealed record Manifest(int SchemaVersion, string Kind, List<EntryInfo> Files);
    public sealed record Backup(Guid Id, DateTimeOffset ModifiedUtc, long Bytes);
    private static string BackupDirectory(string serverDir) => Path.Combine(serverDir, "backups");
    private static string BackupPath(string serverDir, Guid backupId) => Path.Combine(BackupDirectory(serverDir), backupId.ToString("D") + ".zip");

    public async Task<IReadOnlyList<Backup>> ListAsync(Guid serverId, CancellationToken ct = default)
    {
        await using var gate = store.AcquireServer(serverId);
        ct.ThrowIfCancellationRequested();
        var dir = BackupDirectory(store.DirectoryFor(serverId));
        if (!Directory.Exists(dir)) return Array.Empty<Backup>();
        RejectLink(dir);
        return Directory.EnumerateFiles(dir, "*.zip").Select(p => (Path: p, Id: Guid.TryParseExact(Path.GetFileNameWithoutExtension(p), "D", out var id) ? id : Guid.Empty))
            .Where(x => x.Id != Guid.Empty).Select(x => { RejectLink(x.Path); var f = new FileInfo(x.Path); return new Backup(x.Id, f.LastWriteTimeUtc, f.Length); })
            .OrderByDescending(x => x.ModifiedUtc).ToArray();
    }
    public async Task<Guid> CreateAsync(Guid serverId, CancellationToken ct = default)
    {
        await using var gate = store.AcquireServer(serverId);
        var dir = store.DirectoryFor(serverId);
        ProcessOwnership.EnsureNoSurvivor(dir);
        return await CreateLockedAsync(dir, ct);
    }
    public async Task DeleteAsync(Guid serverId, Guid backupId, CancellationToken ct = default)
    {
        await using var gate = store.AcquireServer(serverId);
        ct.ThrowIfCancellationRequested();
        var dir = store.DirectoryFor(serverId);
        ProcessOwnership.EnsureNoSurvivor(dir);
        var backupDir = BackupDirectory(dir); RejectLink(backupDir);
        var path = BackupPath(dir, backupId); RejectLink(path);
        if (!File.Exists(path)) throw new FileNotFoundException("Backup not found.", path);
        File.Delete(path);
    }
    // Returns the ID of the automatically created recovery backup, when an existing world was replaced.
    public async Task<Guid?> RestoreAsync(Guid serverId, Guid backupId, CancellationToken ct = default)
    {
        await using var gate = store.AcquireServer(serverId);
        var dir = store.DirectoryFor(serverId);
        ProcessOwnership.EnsureNoSurvivor(dir);
        var backupDir = BackupDirectory(dir); RejectLink(backupDir);
        var path = BackupPath(dir, backupId); RejectLink(path);
        if (!File.Exists(path)) throw new FileNotFoundException("Backup not found.", path);
        var stage = Path.Combine(dir, ".world-stage-" + Guid.NewGuid().ToString("N"));
        var previous = Path.Combine(dir, ".world-previous-" + Guid.NewGuid().ToString("N"));
        var world = Path.Combine(dir, "world");
        bool movedOld = false, installed = false;
        try
        {
            Directory.CreateDirectory(stage);
            await ExtractVerifiedAsync(path, stage, ct);
            Guid? recovery = null;
            if (Directory.Exists(world)) { RejectLink(world); recovery = await CreateLockedAsync(dir, ct); }
            else if (File.Exists(world)) throw new IOException("The world path is not a directory.");
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(world)) { Directory.Move(world, previous); movedOld = true; }
            try { Directory.Move(stage, world); installed = true; }
            catch { if (movedOld) { Directory.Move(previous, world); movedOld = false; } throw; }
            // After the swap succeeds, failure to clean up leaves an extra recoverable copy.
            if (movedOld) Directory.Delete(previous, true);
            return recovery;
        }
        finally
        {
            if (!installed && Directory.Exists(stage)) Directory.Delete(stage, true);
        }
    }
    private static async Task<Guid> CreateLockedAsync(string dir, CancellationToken ct)
    {
        var world = Path.Combine(dir, "world");
        if (!Directory.Exists(world) || !File.Exists(Path.Combine(world, "level.dat"))) throw new InvalidOperationException("No recognizable default world/level.dat exists. Only the default world directory is supported.");
        RejectLink(world);
        var backupDir = BackupDirectory(dir); Directory.CreateDirectory(backupDir); RejectLink(backupDir);
        var id = Guid.NewGuid(); var destination = BackupPath(dir, id);
        var temporary = Path.Combine(backupDir, "." + id.ToString("N") + ".tmp");
        try
        {
            var manifest = new List<EntryInfo>(); long total = 0;
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var path in EnumerateSafe(world))
                {
                    ct.ThrowIfCancellationRequested();
                    var relative = Path.GetRelativePath(world, path).Replace(Path.DirectorySeparatorChar, '/');
                    ValidateRelative(relative);
                    var length = new FileInfo(path).Length;
                    total = checked(total + length);
                    if (length < 0 || total > MaxTotalBytes || manifest.Count >= MaxEntries) throw new InvalidDataException("World exceeds the backup size or file-count limit (4 GiB, 100,000 files).");
                    var entry = zip.CreateEntry("world/" + relative, CompressionLevel.Optimal);
                    await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                    await using var output = entry.Open();
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var copied = await CopyAndHashAsync(input, output, hash, MaxTotalBytes - (total - length), ct);
                    if (copied != length) throw new IOException("A world file changed during backup; retry with the server stopped.");
                    manifest.Add(new EntryInfo(relative, copied, Convert.ToHexString(hash.GetHashAndReset())));
                }
                var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new Manifest(1, "harbor-world", manifest));
                if (manifestBytes.Length > MaxManifestBytes) throw new InvalidDataException("World manifest exceeds 16 MiB.");
                var metadata = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
                await using var writer = metadata.Open();
                await writer.WriteAsync(manifestBytes, ct);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination);
            return id;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static IEnumerable<string> EnumerateSafe(string root)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop(); RejectLink(directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                RejectLink(path);
                if (Directory.Exists(path)) pending.Push(path);
                else if (File.Exists(path)) yield return path;
                else throw new InvalidDataException("Unsupported filesystem entry in world: " + path);
            }
        }
    }
    private static async Task ExtractVerifiedAsync(string archive, string stage, CancellationToken ct)
    {
        await using var input = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count > MaxEntries + 1) throw new InvalidDataException("Too many archive entries.");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var item in zip.Entries)
        {
            if (((item.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Symlink archive entries are not allowed.");
            if (!entries.TryAdd(item.FullName, item)) throw new InvalidDataException("Duplicate archive entry.");
        }
        if (!entries.Remove("manifest.json", out var manifestEntry) || manifestEntry.Length > MaxManifestBytes) throw new InvalidDataException("Missing or oversized backup manifest.");
        await using var manifestStream = manifestEntry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<Manifest>(manifestStream, cancellationToken: ct) ?? throw new InvalidDataException("Invalid manifest.");
        if (manifest.SchemaVersion != 1 || manifest.Kind != "harbor-world" || manifest.Files is null || manifest.Files.Count > MaxEntries) throw new InvalidDataException("Unsupported backup manifest.");
        long total = 0; var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (item is null) throw new InvalidDataException("Invalid manifest entry.");
            ValidateRelative(item.Path);
            if (!seen.Add(item.Path) || !entries.Remove("world/" + item.Path, out var source)) throw new InvalidDataException("Missing or duplicate file in backup.");
            if (item.Length < 0 || source.Length != item.Length || item.Sha256 is null || item.Sha256.Length != 64) throw new InvalidDataException("Invalid file metadata.");
            total = checked(total + item.Length);
            if (total > MaxTotalBytes) throw new InvalidDataException("Backup exceeds the 4 GiB extraction limit.");
            var target = Path.Combine(stage, item.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var from = source.Open();
            await using var to = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var count = await CopyAndHashAsync(from, to, hash, item.Length, ct);
            if (count != item.Length || !Convert.ToHexString(hash.GetHashAndReset()).Equals(item.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Backup file checksum or length mismatch: " + item.Path);
        }
        if (entries.Count != 0 || !seen.Contains("level.dat")) throw new InvalidDataException("Backup contains unexpected entries or lacks level.dat.");
    }
    private static async Task<long> CopyAndHashAsync(Stream input, Stream output, IncrementalHash hash, long limit, CancellationToken ct)
    {
        var buffer = new byte[81920]; long total = 0; int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            total = checked(total + read);
            if (total > limit) throw new InvalidDataException("File exceeds declared or maximum size.");
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return total;
    }
    private static void ValidateRelative(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\\') || path.Contains('\0') || path.StartsWith('/') || path.Split('/').Any(x => x is "" or "." or ".."))
            throw new InvalidDataException("Unsafe archive path.");
    }
    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Symbolic links are not supported: " + path);
    }
}
