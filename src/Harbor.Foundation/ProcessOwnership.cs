using System.Diagnostics;
using System.Text.Json;
namespace Harbor.Foundation;
public sealed record ProcessRecord(int Pid, long StartUtcTicks);
public static class ProcessOwnership
{
    public static string PathFor(string directory) => Path.Combine(directory, ".harbor-process.json");
    public static void EnsureNoSurvivor(string directory)
    {
        var path = PathFor(directory);
        if (!File.Exists(path)) return;
        ProcessRecord record;
        try { record = JsonSerializer.Deserialize<ProcessRecord>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty process record."); }
        catch (JsonException ex) { throw new InvalidDataException("Unreadable process record; inspect it manually before proceeding.", ex); }
        try
        {
            using var process = Process.GetProcessById(record.Pid);
            if (!process.HasExited && process.StartTime.ToUniversalTime().Ticks == record.StartUtcTicks)
                throw new InvalidOperationException($"A previously owned process (PID {record.Pid}) is still alive. Do not start or modify this server; stop it safely first.");
        }
        catch (ArgumentException) { /* PID no longer exists. */ }
        File.Delete(path);
    }
}
