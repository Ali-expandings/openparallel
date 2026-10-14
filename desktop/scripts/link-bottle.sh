#!/usr/bin/env bash
# Links the Sikarugir test bottle to the Windows build: double-clicking the wrapper app opens
# C:\OpenParallel\OpenParallel.exe, with OpenParallel's icon. (Sikarugir's own Configure edits
# this Info.plist the same way; the wrapper's launcher is a symlink, so it is not re-signed.)
# Stop test runs first (wineserver -k): the wrapper waits while another Wine session is open.
set -euo pipefail
cd "$(dirname "$0")/.."
. scripts/env.sh
PLIST="$OP_BOTTLE/Contents/Info.plist"
[[ -f "$OP_BOTTLE/Contents/drive_c/OpenParallel/OpenParallel.exe" ]] || { echo "Install the build first: scripts/wine-test.sh --setup-ui"; exit 1; }

/usr/bin/plutil -replace "Program Name and Path" -string "/OpenParallel/OpenParallel.exe" "$PLIST"

ICONSET="$(mktemp -d)/OpenParallel.iconset"
mkdir -p "$ICONSET"
for spec in "16 icon_16x16.png" "32 icon_16x16@2x.png" "32 icon_32x32.png" "64 icon_32x32@2x.png" "128 icon_128x128.png" "256 icon_128x128@2x.png" "256 icon_256x256.png"; do
  /usr/bin/sips -z "${spec%% *}" "${spec%% *}" src/OpenParallel.App/Assets/OpenParallel.png --out "$ICONSET/${spec#* }" >/dev/null
done
/usr/bin/iconutil -c icns "$ICONSET" -o "$OP_BOTTLE/Contents/Resources/OpenParallel.icns"
rm -rf "$(dirname "$ICONSET")"
/usr/bin/plutil -replace CFBundleIconFile -string OpenParallel.icns "$PLIST"

touch "$OP_BOTTLE"   # Finder picks up the new icon
echo "Linked. Double-click $OP_BOTTLE to open OpenParallel for Windows."
