namespace Harbor.Core;

// Directory names are IDs, never user-supplied names.
public sealed record ServerDefinition(Guid Id, string Name, string MinecraftVersion,
    string JavaExecutable, int MemoryMiB, int Port, int SchemaVersion = 1);

public static class ServerValidation
{
    public static void Check(ServerDefinition server)
    {
        if (server.SchemaVersion != 1) throw new InvalidDataException("Unsupported server schema; update Harbor before opening this server.");
        if (server.Id == Guid.Empty) throw new ArgumentException("Server ID cannot be empty.");
        if (string.IsNullOrWhiteSpace(server.Name) || server.Name.Length > 100 || server.Name.Any(char.IsControl))
            throw new ArgumentException("Name must be 1–100 characters without control characters.");
        if (string.IsNullOrWhiteSpace(server.MinecraftVersion) || server.MinecraftVersion.Length > 64 || server.MinecraftVersion.Any(char.IsControl))
            throw new ArgumentException("Choose a valid Minecraft version.");
        if (string.IsNullOrWhiteSpace(server.JavaExecutable) || server.JavaExecutable.Contains('\0')) throw new ArgumentException("Choose a Java executable.");
        if (server.MemoryMiB is < 512 or > 131072) throw new ArgumentException("Memory must be between 512 and 131072 MiB.");
        if (server.Port is < 1 or > 65535) throw new ArgumentException("Port must be between 1 and 65535.");
    }
}
