#!/bin/sh
# Installs OpenParallel for this user, no sudo: the app in ~/.local/opt/OpenParallel, the
# command `openparallel` in ~/.local/bin, and an entry in the applications menu.
set -eu
here="$(cd "$(dirname "$0")" && pwd)"
data="${XDG_DATA_HOME:-$HOME/.local/share}"
dest="$HOME/.local/opt/OpenParallel"

if [ "$here" != "$dest" ]; then
  mkdir -p "$HOME/.local/opt"
  rm -rf "$dest.new"
  cp -R "$here" "$dest.new"
  rm -rf "$dest"
  mv "$dest.new" "$dest"
fi
chmod +x "$dest/OpenParallel" "$dest/opctl" "$dest/install.sh" "$dest/uninstall.sh"

mkdir -p "$HOME/.local/bin" "$data/applications" "$data/icons/hicolor/256x256/apps"
ln -sf "$dest/OpenParallel" "$HOME/.local/bin/openparallel"
cp "$dest/openparallel.png" "$data/icons/hicolor/256x256/apps/openparallel.png"
cat > "$data/applications/openparallel.desktop" <<ENTRY
[Desktop Entry]
Type=Application
Version=1.5
Name=OpenParallel
Comment=Run a second copy of an app with its own login and data
Exec="$dest/OpenParallel"
Icon=openparallel
Terminal=false
Categories=Utility;
StartupWMClass=OpenParallel
ENTRY
update-desktop-database "$data/applications" >/dev/null 2>&1 || true
gtk-update-icon-cache -f -t "$data/icons/hicolor" >/dev/null 2>&1 || true
echo "OpenParallel is installed. Open it from the applications menu, or run: openparallel"
