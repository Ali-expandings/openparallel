#!/usr/bin/env bash
# Builds OpenParallel for Linux on macOS: self-contained linux-arm64 and linux-x64 folders
# (OpenParallel + opctl + install.sh), the Linux stand-in app for the tests, and with
# --package the downloads OpenParallel-<version>-linux-<arch>.tar.gz.
#   RIDS="linux-arm64" scripts/build-linux.sh      # just one architecture
set -euo pipefail
cd "$(dirname "$0")/.."
. scripts/env.sh

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
common=(-c Release --self-contained true -p:DebugType=none --nologo)
for rid in ${RIDS:-linux-arm64 linux-x64}; do
  out="artifacts/$rid/OpenParallel"
  rm -rf "artifacts/$rid"
  dotnet publish src/OpenParallel.App "${common[@]}" -r "$rid" -o "$out" | grep -E "error|->" || true
  dotnet publish src/OpenParallel.Cli "${common[@]}" -r "$rid" -o "$out" | grep -E "error|->" || true
  dotnet publish tests/fixtures/FakeLinuxApp "${common[@]}" -r "$rid" -o "artifacts/$rid/fixture" | grep -E "error|->" || true
  find "artifacts/$rid" -name '*.pdb' -delete
  cp packaging/linux/install.sh packaging/linux/uninstall.sh "$out/"
  cp src/OpenParallel.App/Assets/OpenParallel.png "$out/openparallel.png"
  chmod +x "$out/OpenParallel" "$out/opctl" "$out/install.sh" "$out/uninstall.sh" "artifacts/$rid/fixture/fakelinuxapp"
  test -f "$out/OpenParallel" && test -f "$out/opctl"
  echo "built $(du -sh "$out" | cut -f1) in $out"
  if [[ "${1:-}" == "--package" ]]; then
    tar -C "artifacts/$rid" -czf "artifacts/OpenParallel-$version-$rid.tar.gz" OpenParallel
    echo "packaged $(du -h "artifacts/OpenParallel-$version-$rid.tar.gz" | cut -f1) artifacts/OpenParallel-$version-$rid.tar.gz"
  fi
done
