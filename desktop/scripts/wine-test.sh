#!/usr/bin/env bash
# End-to-end test of the Windows build inside the Sikarugir test bottle.
# --setup-ui installs the build and stand-in apps (with Start menu shortcuts) and stops there.
set -euo pipefail
cd "$(dirname "$0")/.."
. scripts/env.sh
[[ -x "$OP_WINE" ]] || { echo "Test bottle not found: $OP_BOTTLE"; exit 1; }
"$(dirname "$OP_WINE")/wineserver" -p 30 >/dev/null 2>&1 || true   # keep Wine warm between commands
exec /usr/bin/python3 -I scripts/wine_test.py "$@"
