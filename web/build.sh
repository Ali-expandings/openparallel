#!/usr/bin/env bash
# Assembles the static site: site/ + the shared project facts + the compatibility register.
set -euo pipefail
cd "$(dirname "$0")"
rm -rf dist
cp -R site dist
cp ../project.json dist/project.json
mkdir -p dist/data
cp data/register.json dist/data/register.json
# Large downloads added with add-download.sh (git-ignored), served by functions/download/.
if [[ -d downloads ]]; then mkdir -p dist/files && cp downloads/* dist/files/; fi
echo "built $(du -sh dist | cut -f1) in web/dist"
