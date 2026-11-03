using System.Security.Cryptography;
using System.Text.Json;
using Harbor.Core;
using Harbor.Foundation;

namespace Harbor.Installers;
public sealed class VanillaInstaller(HttpClient http, ServerStore store)
{
    private const string Manifest = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    public sealed record Release(string Id, string MetadataUrl);
    public sealed record ServerArtifact(string Url, string Sha1, int JavaMajor);
    public async Task<IReadOnlyList<Release>> ReleasesAsync(CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync(Manifest, ct));
        return doc.RootElement.GetProperty("versions").EnumerateArray()
            .Where(x => x.GetProperty("type").GetString() == "release")
            .Select(x => new Release(x.GetProperty("id").GetString()!, x.GetProperty("url").GetString()!)).ToArray();
    }
    public async Task<ServerArtifact> ResolveAsync(string version, CancellationToken ct = default)
    {
        var release = (await ReleasesAsync(ct)).FirstOrDefault(r => r.Id == version) ?? throw new InvalidOperationException("Release not found in Mojang's manifest.");
        using var doc = JsonDocument.Parse(await http.GetStringAsync(release.MetadataUrl, ct));
        var root = doc.RootElement;
        if (!root.GetProperty("downloads").TryGetProperty("server", out var server)) throw new InvalidOperationException("This release has no server download in Mojang metadata.");
        var java = root.TryGetProperty("javaVersion", out var j) ? j.GetProperty("majorVersion").GetInt32() : 8;
        return new ServerArtifact(server.GetProperty("url").GetString()!, server.GetProperty("sha1").GetString()!, java);
    }
    public async Task InstallAsync(ServerDefinition definition, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        await using var gate = store.AcquireServer(definition.Id);
        ProcessOwnership.EnsureNoSurvivor(store.DirectoryFor(definition.Id));
        var artifact = await ResolveAsync(definition.MinecraftVersion, ct);
        var java = await JavaProbe.ProbeAsync(definition.JavaExecutable, ct);
        if (java.Major != artifact.JavaMajor) throw new InvalidOperationException($"Minecraft {definition.MinecraftVersion} requests Java {artifact.JavaMajor}; selected Java is {java.Major}. Select the required major.");
        var dir = store.DirectoryFor(definition.Id);
        var temp = Path.Combine(dir, ".server-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using var response = await http.GetAsync(artifact.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920]; int count; long total = 0;
                while ((count = await source.ReadAsync(buffer, ct)) != 0) { await target.WriteAsync(buffer.AsMemory(0, count), ct); total += count; progress?.Report(total); }
            }
            await using var check = File.OpenRead(temp);
            var actual = Convert.ToHexString(await SHA1.HashDataAsync(check, ct));
            if (!actual.Equals(artifact.Sha1, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Server download checksum mismatch; retry the installation.");
            check.Close();
            File.Move(temp, Path.Combine(dir, "server.jar"), true);
            await ServerStore.AtomicWriteAsync(Path.Combine(dir, "installation.json"), JsonSerializer.Serialize(artifact), ct);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
