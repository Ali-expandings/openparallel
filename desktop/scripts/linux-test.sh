#!/usr/bin/env bash
# End-to-end test of the Linux build, run INSIDE the Linux machine (from the Mac: scripts/vm.sh test).
#   bash linux-test.sh [folder holding OpenParallel/ and fixture/]
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
case "$(uname -m)" in
  aarch64|arm64) rid=linux-arm64 ;;
  x86_64) rid=linux-x64 ;;
  *) echo "Unsupported CPU: $(uname -m)"; exit 1 ;;
esac
exec python3 -I "$here/linux_test.py" "${1:-$here/../artifacts/$rid}"
