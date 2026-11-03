# Release report — not a release

## Implemented in source (unverified)

Local server definitions with GUID folders and atomic JSON, catalog port uniqueness on creation, Java discovery in PATH/JAVA_HOME and /usr/lib/jvm, Mojang release metadata and SHA-1-verified server download, explicit per-server EULA prompt, single-server foreground process runner with redirected console streams, readiness and graceful stop, PID/start-time survivor check, per-server lock, and a read-only Avalonia inventory. Multiple separate CLI invocations are intended to run independent servers. Fixture checks are provided but have not been executed locally.

## Verification

Environment: Debian 12 x86-64 sandbox, no desktop session, .NET SDK or Java. No Kubuntu version, KDE, display backend, Minecraft version, loader, or Java version tested. `dotnet restore`, build, test, publish, Java and live server execution could not be run. Outbound access to NuGet, Microsoft SDK downloads and Mojang was unavailable. CI may provide independent build evidence; check its actual run. No screenshots or binaries were generated in this environment. No live EULA acceptance occurred. A container/CI build is not Kubuntu desktop verification.

## Not implemented or not verified

This is not the requested complete product. Native Wayland/XWayland, X11, fractional scaling, keyboard accessibility, .desktop launcher and native runtime dependencies remain unverified. The desktop has no onboarding, selected-server tabs, controls, console or shutdown handling. There is no server edit, duplicate, delete, persistent state machine, descendant tracking, full crash recovery, configurable data root UI, reliable memory/CPU/player metrics, port collision checks for auxiliary ports, memory budget warning, or UI progress/cancellation. Process ownership has a crash window immediately after launch; never trust it to safely manage valuable data. Port preflight has an unavoidable bind race. Java architecture detection and managed runtime installation are absent. Fabric, Forge, NeoForge, mods, players, property editor, world import/export, backups, restore, full diagnostics and networking UI are absent. There are no backup safety guarantees. The exact compatibility of later Java versions is not inferred; exact major is conservatively required. Installer metadata is not cryptographically authenticated beyond HTTPS and the upstream SHA-1 artifact digest. Schema migrations and durable fsync are absent. Live and mock-process integration, race, corruption, recovery, real loader, Java, UI and desktop tests requested in the specification have not been written or performed. The included fixture executable covers only its named checks. No screenshot may be represented as a finished product.

## Screenshot reproduction once completed

Run the app on a real Kubuntu desktop and capture the dashboard, overview, real console session, mods and worlds/backups with Spectacle. Only the inventory exists now; the remaining requested views do not exist. Do not substitute synthetic logs or mockups.
