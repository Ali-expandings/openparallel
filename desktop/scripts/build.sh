#!/usr/bin/env bash
# Builds the Windows version on macOS: OpenParallel.exe + opctl.exe (self-contained, win-x64,
# published as a folder because single-file bundles do not load under Wine), and the fixture
# app used by the Wine tests.
set -euo pipefail
cd "$(dirname "$0")/.."
. scripts/env.sh

OUT=artifacts/win-x64
rm -rf "$OUT" artifacts/fixture-runtime
common=(-c Release -r win-x64 --self-contained true -p:DebugType=none --nologo)
dotnet publish src/OpenParallel.App "${common[@]}" -o "$OUT" | grep -E "error|->" || true
dotnet publish src/OpenParallel.Cli "${common[@]}" -o "$OUT" | grep -E "error|->" || true
dotnet publish tests/fixtures/FakeApp "${common[@]}" -o artifacts/fixture-runtime | grep -E "error|->" || true
test -f "$OUT/OpenParallel.exe" && test -f "$OUT/opctl.exe" && test -f artifacts/fixture-runtime/FakeApp.exe
# SkiaSharp and HarfBuzz ship ~100 MB of native debug symbols; users never need them.
find "$OUT" artifacts/fixture-runtime -maxdepth 1 -name '*.pdb' -delete
echo "built $(du -sh "$OUT" | cut -f1) in $OUT"

# --zip: the download, a folder named OpenParallel inside a zip.
if [[ "${1:-}" == "--zip" ]]; then
  version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
  zip="artifacts/OpenParallel-$version-windows-x64.zip"
  rm -rf artifacts/package "$zip"
  mkdir -p artifacts/package
  cp -R "$OUT" artifacts/package/OpenParallel
  (cd artifacts/package && zip -qr -9 "../$(basename "$zip")" OpenParallel)
  rm -rf artifacts/package
  echo "packaged $(du -h "$zip" | cut -f1) $zip"
fi
