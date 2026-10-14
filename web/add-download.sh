#!/usr/bin/env bash
# Puts a file on the website at /download/<its name>, split into parts small enough for
# Cloudflare Pages (functions/download/[file].js joins them). Then run ./deploy.sh.
#   ./add-download.sh ../desktop/artifacts/OpenParallel-1.0.0-windows-x64.zip
set -euo pipefail
cd "$(dirname "$0")"
file="$1"; name="$(basename "$file")"
[[ -f "$file" ]] || { echo "no such file: $file" >&2; exit 1; }
mkdir -p downloads
rm -f "downloads/$name".part* "downloads/$name.json"
split -b 20m -a 1 "$file" "downloads/$name.part"
size=$(stat -f %z "$file")
sha=$(shasum -a 256 "$file" | cut -d' ' -f1)
case "$name" in *.zip) type=application/zip ;; *.tar.gz) type=application/gzip ;; *) type=application/octet-stream ;; esac
parts=$(cd downloads && ls "$name".part? | sed 's/.*/"&"/' | paste -sd, -)
printf '{"name":"%s","size":%s,"sha256":"%s","type":"%s","parts":[%s]}\n' "$name" "$size" "$sha" "$type" "$parts" > "downloads/$name.json"
echo "ready: /download/$name ($size bytes, sha256 $sha)"
