# Harbor (early development preview)

Harbor is intended to be a local Minecraft Java server manager for Kubuntu. **This is an incomplete initial implementation, not a finished or verified desktop product.** The desktop is a read-only inventory; installation and lifecycle operations are currently CLI-only. Do not use it with valuable worlds without independent backups. See [release report](docs/RELEASE-REPORT.md) and [progress](docs/PROGRESS.md) for the precise scope and outstanding work.

## Requirements and build

.NET SDK **10.0.402**, network access to NuGet to restore Avalonia 11.3.22, and a compatible x86-64 Linux desktop. A server requires an appropriately licensed Minecraft Java Edition installation and an executable Linux Java runtime whose major version matches Mojang's metadata. Harbor does not include Java or Minecraft binaries. Downloading a server requires access to Mojang's metadata and distribution hosts.

```sh
dotnet restore Harbor.sln
dotnet build Harbor.sln -c Release --no-restore
dotnet run --project tests/Harbor.Tests -c Release --no-build
dotnet run --project src/Harbor.App -c Release --no-build
```

## Reproduce the vanilla proof (opt in)

This downloads and runs a real Minecraft server. Read and agree to the [Minecraft EULA](https://aka.ms/MinecraftEULA) yourself before proceeding. This is not a test fixture and may take several minutes. Choose a version from the actual catalog and install its required Java separately. Minecraft 1.21.1 ordinarily uses Java 21, but check the metadata and select a matching runtime; older versions may require Java 8 or 17. Harbor deliberately enforces the exact declared Java major.

```sh
# Optional: isolate this proof; use an absolute path, including spaces if desired.
export XDG_DATA_HOME="$HOME/Harbor proof data"
dotnet run --project tools/Harbor.Cli -- java
dotnet run --project tools/Harbor.Cli -- versions
# Replace the Java path and version after checking availability.
dotnet run --project tools/Harbor.Cli -- create "My server" 1.21.1 /usr/lib/jvm/java-21-openjdk-amd64/bin/java 2048 25565
dotnet run --project tools/Harbor.Cli -- list
# Copy the printed UUID to ID (do not include angle brackets).
ID='replace-with-server-uuid'
dotnet run --project tools/Harbor.Cli -- install "$ID"
dotnet run --project tools/Harbor.Cli -- accept-eula "$ID"
# Type I AGREE only if you have read and agree to the EULA.
dotnet run --project tools/Harbor.Cli -- proof "$ID"
# For interactive use instead: dotnet run --project tools/Harbor.Cli -- run "$ID"
```

The proof expects real readiness output and a real `list` response before requesting `stop`. It logs output to `harbor-console.log` inside the server directory. A zero exit code is required. If the process does not stop in 60 seconds, the tool reports the PID and retains the ownership record; inspect it rather than killing an arbitrary Java process. Ctrl+C requests graceful shutdown. Abrupt termination can leave a server running: if Harbor detects the recorded process with the same PID and start time it refuses a new operation. A small crash window exists between Java starting and recording ownership; this is not yet production-grade crash recovery. No daemon is implemented.

Server data is in `$XDG_DATA_HOME/harbor`, or `~/.local/share/harbor` when unset. The application never requires sudo. `server.properties` is created with online authentication enabled and the selected port. The server may bind LAN interfaces by default: a different device needs the host's LAN address and port. Internet access needs separate routing/firewall decisions; LAN presence does not prove public reachability. UFW, router forwarding, dynamic IP, CGNAT and IPv6 can affect access. Harbor does not modify any of them. A sleeping, logged-out, or powered-off laptop cannot reliably host: no sleep settings are changed.

## Packaging and uninstall

CI attempts a self-contained `linux-x64` preview artifact with SHA-256. Alternatively:

```sh
mkdir -p artifacts/harbor
dotnet publish src/Harbor.App -c Release -r linux-x64 --self-contained true -o artifacts/harbor/app
dotnet publish tools/Harbor.Cli -c Release -r linux-x64 --self-contained true -o artifacts/harbor/cli
cp packaging/harbor packaging/harbor.svg packaging/*-desktop.sh artifacts/harbor/
tar -C artifacts -czf harbor-linux-x64.tar.gz harbor
sha256sum harbor-linux-x64.tar.gz
```

Extract into a permanent user-owned directory and run `harbor/harbor`. `harbor/install-desktop.sh` installs a per-user launcher; `harbor/uninstall-desktop.sh` removes the launcher and icon but preserves all server data. To update, replace the extracted program directory and reinstall the launcher; keep your separate data directory. Native Avalonia dependencies (including X11/XWayland, fontconfig, FreeType, and graphics libraries), glibc/ICU and a graphical session remain required. Exact Kubuntu native package requirements and desktop launch are unverified. No native Wayland backend is claimed; session type and rendering backend are different things.

## Troubleshooting

- Missing Java or wrong major: use `java` to inspect candidates and select the required executable at creation; architecture probing and Java download are not yet implemented.
- Download/checksum failures: check connectivity and retry `install`; partial downloads are removed. Mojang publishes SHA-1 for this artifact; SHA-1 is used to match that published checksum, not as a modern signature.
- Port conflict: stop the conflicting service or create a server with another port. A port can be taken after the preflight check; inspect real startup output.
- EULA/startup failure: inspect the console and the server's own logs; do not accept an agreement automatically.
- The desktop is read-only and cannot stop servers. Do not start long-lived servers from a terminal you intend to close; background hosting and safe app-wide shutdown are not implemented.
