# Kubuntu verification checklist (not yet run)

- Record distribution version, x86-64 architecture, Plasma version, session type and actual Avalonia backend, Java and Minecraft versions.
- Check native libraries, app startup from terminal and per-user launcher; test X11 and Wayland session via documented backend/XWayland.
- Test keyboard navigation, resizing and 100%, 125%, 150% scaling.
- Run opt-in vanilla proof in README after personally accepting the EULA, and retain actual output separately from source control.
- Exercise two servers, port conflicts, unexpected Java exit, cancellation, recovery, and close behavior. Do not claim absent features work.
