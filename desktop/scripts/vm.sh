#!/usr/bin/env bash
# Drives the Linux test VM in Parallels Desktop from the Mac, without touching the Mac's own
# keyboard or windows: commands run inside the VM as its desktop user (prlctl exec), and
# screenshots come from Parallels (prlctl capture).
#   scripts/vm.sh info                   the VM, its user, and where this folder appears inside it
#   scripts/vm.sh run '<command>'        run a shell command as the desktop user, with DISPLAY set
#   scripts/vm.sh root '<command>'       run as root (installing packages)
#   scripts/vm.sh test                   the end-to-end test (scripts/linux-test.sh) inside the VM
#   scripts/vm.sh send / test-sent       without shared folders: copy the build in, then test it
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

# prlctl exec runs one string through the guest's shell, as root.
guest() { "$PRLCTL" exec "$vm" "$1"; }
q() { printf '%q' "$1"; }
user=$(guest 'getent passwd 1000 | cut -d: -f1' | tr -d '\r')
# The desktop session's own DISPLAY, XAUTHORITY (XWayland's is a random file name), WAYLAND_DISPLAY
# and D-Bus address, copied from one of the user's processes started inside the session (the
# compositor itself starts before XWayland and lacks them). Without them apps can't open windows.
session=$(guest "for p in \$(pgrep -u $user); do grep -qz '^DISPLAY=' /proc/\$p/environ 2>/dev/null && break; done; \
  tr '\\0' '\\n' < /proc/\$p/environ | grep -E '^(DISPLAY|XAUTHORITY|WAYLAND_DISPLAY|XDG_RUNTIME_DIR|DBUS_SESSION_BUS_ADDRESS|XDG_CURRENT_DESKTOP|XDG_SESSION_TYPE)=' \
  | sed 's/^/export /' | tr '\\n' ';'" | tr -d '\r')
# Parallels shares the Mac's home at /media/psf/Home when "Share Mac folders" is on.
shared="/media/psf/Home${PWD#$HOME}"

as_user() { guest "su - $user -c $(q "$session $1")"; }

case "${1:-info}" in
  info)
    echo "VM: $vm   user: $user   session: ${session:-none found}"
    if guest "test -d $(q "$shared")"; then echo "This folder in the VM: $shared"; else echo "Shared folders are off: turn on Parallels > VM settings > Options > Sharing > Share Mac folders."; fi
    ;;
  run) as_user "$2" ;;
  root) guest "$2" ;;
  test) as_user "bash $(q "$shared/scripts/linux-test.sh")" ;;
  send)
    # No shared folder: copy the Linux build and the test into ~/op-test through prlctl's stdin.
    rid="linux-$(guest 'uname -m' | tr -d '\r' | sed 's/aarch64/arm64/; s/x86_64/x64/')"
    tmp=$(mktemp -d); mkdir "$tmp/scripts"; cp scripts/linux_test.py scripts/linux-test.sh "$tmp/scripts/"
    tar -czf - -C "artifacts/$rid" OpenParallel fixture -C "$tmp" scripts \
      | guest "rm -rf ~$user/op-test; mkdir -p ~$user/op-test; tar -xzf - -C ~$user/op-test; chown -R $user: ~$user/op-test"
    rm -rf "$tmp"; echo "sent artifacts/$rid to ~$user/op-test" ;;
  test-sent) as_user "python3 -I ~/op-test/scripts/linux_test.py ~/op-test" ;;
  shot) "$PRLCTL" capture "$vm" --file "$2" >/dev/null && echo "$2" ;;
  key) as_user "xdotool key --clearmodifiers $2" ;;
  type) as_user "xdotool type --delay 40 '$2'" ;;
  click) as_user "xdotool mousemove $2 $3 click ${4:-1}" ;;
  *) sed -n '2,15p' "$0"; exit 1 ;;
esac
