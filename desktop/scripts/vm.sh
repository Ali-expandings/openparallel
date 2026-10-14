#!/usr/bin/env bash
# Drives the Linux test VM in Parallels Desktop from the Mac, without touching the Mac's own
# keyboard or windows: commands run inside the VM as its desktop user (prlctl exec), and
# screenshots come from Parallels (prlctl capture).
#   scripts/vm.sh info                   the VM, its user, and where this folder appears inside it
#   scripts/vm.sh run '<command>'        run a shell command as the desktop user, with DISPLAY set
#   scripts/vm.sh root '<command>'       run as root (installing packages)
#   scripts/vm.sh test                   the end-to-end test (scripts/linux-test.sh) inside the VM
#   scripts/vm.sh shot <out.png>         screenshot of the VM's screen
#   scripts/vm.sh key <keys>             xdotool key, e.g. ctrl+n   (needs xdotool in the VM)
#   scripts/vm.sh type '<text>'          xdotool type
#   scripts/vm.sh click <x> <y> [1|3]    xdotool click at screen coordinates
# OP_VM picks the VM by name (default: the first running Linux VM).
set -euo pipefail
cd "$(dirname "$0")/.."
PRLCTL="/Applications/Parallels Desktop.app/Contents/MacOS/prlctl"
[[ -x "$PRLCTL" ]] || { echo "Parallels Desktop not found"; exit 1; }

vm="${OP_VM:-$("$PRLCTL" list --no-header -o name,status 2>/dev/null | awk '$NF=="running"{NF--; print; exit}')}"
[[ -n "$vm" ]] || { echo "No running VM. Start the Linux VM in Parallels first (prlctl list -a)."; exit 1; }

guest() { "$PRLCTL" exec "$vm" "$@"; }
user=$(guest sh -c "getent passwd 1000 | cut -d: -f1" | tr -d '\r')
uid=$(guest id -u "$user" | tr -d '\r')
session="DISPLAY=:0 XDG_RUNTIME_DIR=/run/user/$uid DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/$uid/bus WAYLAND_DISPLAY=wayland-0"
# Parallels shares the Mac's home at /media/psf/Home when "Share Mac folders" is on.
shared="/media/psf/Home${PWD#$HOME}"

as_user() { guest su - "$user" -c "export $session; $1"; }

case "${1:-info}" in
  info)
    echo "VM: $vm   user: $user ($uid)"
    if guest test -d "$shared"; then echo "This folder in the VM: $shared"; else echo "Shared folders are off: turn on Parallels > VM settings > Options > Sharing > Share Mac folders."; fi
    ;;
  run) as_user "$2" ;;
  root) guest sh -c "$2" ;;
  test) as_user "bash '$shared/scripts/linux-test.sh'" ;;
  shot) "$PRLCTL" capture "$vm" --file "$2" >/dev/null && echo "$2" ;;
  key) as_user "xdotool key --clearmodifiers $2" ;;
  type) as_user "xdotool type --delay 40 '$2'" ;;
  click) as_user "xdotool mousemove $2 $3 click ${4:-1}" ;;
  *) sed -n '2,15p' "$0"; exit 1 ;;
esac
