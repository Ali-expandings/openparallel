#!/usr/bin/env bash
# Assembles the static site: site/ + the shared project facts + the compatibility register.
set -euo pipefail
cd "$(dirname "$0")"
rm -rf dist
cp -R site dist
cp ../project.json dist/project.json
# The author's signature and links go into every footer as plain HTML (no script needed).
/usr/bin/python3 -I - ../project.json dist <<'EOF'
import html, json, pathlib, sys
author = json.load(open(sys.argv[1])).get("author") or {}
signature = (author.get("signature") or "").strip()
links = [l for l in author.get("links") or []
         if (l.get("label") or "").strip() and (l.get("url") or "").strip().startswith(("https://", "http://"))]
markup = ""
if signature:
    anchors = "".join(f'<a href="{html.escape(l["url"].strip())}" rel="me noopener">{html.escape(l["label"].strip())}</a>' for l in links)
    nav = f'<nav aria-label="{html.escape(signature)}">{anchors}</nav>' if anchors else ""
    markup = f'<div class="credit"><span>{html.escape(signature)}</span>{nav}</div>'
for page in pathlib.Path(sys.argv[2]).rglob("*.html"):
    text = page.read_text()
    page.write_text(text.replace("<!-- author: filled in by build.sh from project.json -->", markup))
EOF
mkdir -p dist/data
cp data/register.json dist/data/register.json
# Large downloads added with add-download.sh (git-ignored), served by functions/download/.
if [[ -d downloads ]]; then mkdir -p dist/files && cp downloads/* dist/files/; fi
echo "built $(du -sh dist | cut -f1) in web/dist"
