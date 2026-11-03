# Progress

## 2026-11-03 — initial implementation

- Inspected repository at d6fc11f; only README existed and no AGENTS.md was found. Working in the Arena-provided branch.
- Established .NET 10 solution, stable Avalonia 11.3.22, architecture layers, an initial desktop inventory, local JSON persistence, Java probing, Mojang vanilla metadata/JAR checksum installation and foreground CLI lifecycle/proof path. Build and fixture checks passed in GitHub Actions; live verification has not run.
- Environment observed: Debian 12 container, x86-64, no .NET SDK, no Java, no display. `curl` to api.nuget.org, builds.dotnet.microsoft.com and piston-meta.mojang.com failed; GitHub and npm registry were reachable. SDK 10.0.402 and Avalonia 11.3.22 confirmed as published stable releases through GitHub API. No Kubuntu, X11/Wayland, Minecraft or Java version was tested.
- GitHub Actions runs 41665050637 (5974d3b) and 41665317874 (6906f3c) on ubuntu-24.04 passed restore, Release build, the offline fixture executable, and self-contained linux-x64 preview packaging. The artifact was uploaded but sandbox download failed because GitHub redirected to a blocked Azure blob host. It has not been run. Node 20 action deprecation warnings were reported.
- `sh -n` passed for all three launcher scripts. A Debian shell smoke test installed and uninstalled the desktop entry from a path with spaces and confirmed server data was preserved; it did not launch KDE or the app.
- Next concrete task: run CI and correct compile/runtime failures, then on a networked Kubuntu host run the opt-in real vanilla proof without automatic EULA acceptance; close the process ownership crash window before expanding lifecycle operations.

Unresolved: all user-requested features beyond this narrow initial slice; see RELEASE-REPORT.md. No screenshot, portable binary, live Minecraft output, GUI accessibility test, or native dependencies verification was produced locally.

## 2026-11-03 — world-only backup milestone in progress

- Added CLI creation, listing, confirmed restoration and confirmed deletion of world-only snapshots. Archives contain a manifest and per-file SHA-256 hashes; restoration validates into a staging directory and creates a recovery snapshot before replacement. Operations use the existing server lock and survivor check.
- These snapshots include only the default `world/` directory, including its nested dimensions. They exclude configuration, server JAR, mods, player lists and other server files; they are not full-server backups. Custom level-name directories, external world import/export, and Bedrock conversion are not supported. 4 GiB uncompressed and 100,000-file limits apply. Disk-space failures abort staging or the recovery backup before replacement; there is no proactive free-space estimate. Crash recovery of an interrupted directory swap is manual via the `.world-previous-*` folder and recovery backup. No safety guarantee against a separate hostile local user racing filesystem operations is made.
- Added fixture tests for round trip, recovery, malicious paths, symlink rejection and cancellation. CI run 41666125939 passed these checks. Real Minecraft worlds are still untested.

## 2026-11-03 — desktop backup controls and lifecycle fixtures

- Connected create/list/restore/delete world-only backups to the Avalonia inventory, including confirmation, cancellation and close blocking while an operation is in progress. These UI controls are compiled but have not been exercised in a graphical session. The desktop still cannot create/install/start/stop servers.
- Added isolated shell-process fixtures for two concurrent independent fake servers, command/ready/exit flow and duplicate-start lock rejection, plus corrupted archive and archive-symlink checks. The mock prints are not Minecraft evidence and are never used by the application. GitHub Actions [run 41666453944](https://github.com/cfspearn81-code/MinecraftHostingProgam/actions/runs/41666453944) passed restore, build, these fixtures, and preview publishing at commit 4d5d7b5. Live Minecraft, real worlds and GUI behavior remain unverified.
- Next: verify the real vanilla flow on a networked host with user EULA consent; close the child-start ownership gap; integrate safe lifecycle/onboarding into the desktop and implement remaining requested features.

- Added an opt-in `live-download` CI job: workflow dispatch with `verify_download=true`, or a push whose commit subject contains `[live-download]`, downloads Mojang Minecraft 1.21.1 using Java 21 and verifies its published checksum. It does not start Minecraft or accept its EULA; its temporary data is removed. GitHub Actions [run 41666778179](https://github.com/cfspearn81-code/MinecraftHostingProgam/actions/runs/41666778179) passed the opt-in download check and standard build/test/publish. This proves download and SHA-1 verification for Minecraft 1.21.1 against Mojang using Temurin Java 21 on the ubuntu-24.04 hosted runner, not server startup. The exact Java patch version could not be read because CI logs redirect to a blocked host. Actual live startup remains a separate explicitly consented test.
