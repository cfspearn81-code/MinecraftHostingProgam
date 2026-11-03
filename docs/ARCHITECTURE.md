# Architecture (initial milestone)

- Core: validated immutable server definition and shared types. No UI dependencies.
- Foundation: XDG data root, atomic JSON writes, server and catalog locks, Java executable probing and conservative process ownership records.
- Installers: the single Mojang vanilla catalog resolver and checksum-verifying installer.
- Management: the single foreground process runner, redirected streams, readiness, console input and graceful stop.
- CLI: composition root for the above services. The desktop composition root only constructs a read-only store/view model. Constructor injection is used directly; no service container is needed yet.
- App: Avalonia native desktop inventory preview. Management features are deliberately not exposed as nonfunctional controls.

Each `servers/<uuid>/harbor.json` has schema version 1 (identity, name, release, Java executable, max heap and port). Unknown schema versions are refused; migrations are not implemented. Server directory contains downloaded `server.jar`, installation metadata, server files, an EULA file only on explicit acceptance, console logs and an ownership record. Nothing is bundled in source. Atomic writes use a same-directory temporary file and rename; directory fsync/crash durability is not guaranteed. One catalog lock serializes creation and port selection; a per-server file lock serializes installation, acceptance and the entire lifecycle. An ownership record holds PID and start time; existing matching processes block operations, and mismatched/dead records are discarded. Never signal a process from a stale record. Crash immediately after process creation but before recording remains an unresolved race.

Lifecycle is currently implicit: install holds the lock, then run verifies artifacts/EULA/Java, starts, observes readiness, accepts commands, sends stop and observes exit. No persistent authoritative state machine, cross-process state notifications, descendants tracking, restore, deletion, or force-stop exists yet. A port preflight is advisory; Java's bind result is authoritative. The console logger appends full output and drains stdout and stderr concurrently. There is no UI console/bounded view yet. File locking semantics require verification on real Kubuntu filesystems.
