#!/bin/sh
set -eu
DATA=${XDG_DATA_HOME:-"$HOME/.local/share"}
rm -f "$DATA/applications/harbor.desktop" "$DATA/icons/hicolor/scalable/apps/harbor.svg"
echo "Removed launcher and icon; Harbor server data was preserved."
