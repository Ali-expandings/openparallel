#!/bin/sh
# Removes OpenParallel itself. Instances you made (their menu entries and data) are left
# alone: move them to the Trash from OpenParallel first if you want them gone.
set -eu
data="${XDG_DATA_HOME:-$HOME/.local/share}"
rm -f "$HOME/.local/bin/openparallel" "$data/applications/openparallel.desktop" "$data/icons/hicolor/256x256/apps/openparallel.png"
rm -rf "$HOME/.local/opt/OpenParallel"
update-desktop-database "$data/applications" >/dev/null 2>&1 || true
echo "OpenParallel is removed. Your instances are still in $data/OpenParallel."
