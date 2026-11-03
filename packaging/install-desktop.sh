#!/bin/sh
set -eu
HERE=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
case "$HERE" in *'"'*|*'`'*|*'$'*|*'\\'*|*'%'*|*'
'*) echo "Install path contains a character unsupported by this installer; move the release to a simpler path." >&2; exit 1;; esac
DATA=${XDG_DATA_HOME:-"$HOME/.local/share"}
mkdir -p "$DATA/applications" "$DATA/icons/hicolor/scalable/apps"
cp "$HERE/harbor.svg" "$DATA/icons/hicolor/scalable/apps/harbor.svg"
cat > "$DATA/applications/harbor.desktop" <<ENTRY
[Desktop Entry]
Type=Application
Name=Harbor
Comment=Local Minecraft server manager (preview)
Exec="$HERE/harbor"
Icon=harbor
Terminal=false
Categories=Game;Utility;
ENTRY
echo "Installed a per-user launcher. Data was not changed."
