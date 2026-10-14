#!/usr/bin/env bash
# Run a Windows program inside the test bottle: windows/scripts/wine.sh <program> [args...]
set -euo pipefail
. "$(dirname "$0")/env.sh"
exec "$OP_WINE" "$@"
