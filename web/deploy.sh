#!/usr/bin/env bash
# Builds the site and publishes it to Cloudflare Pages (project "openparallel" unless
# OP_PAGES_PROJECT says otherwise). Needs `npx wrangler login` once on this Mac.
#   ./deploy.sh            production: https://openparallel.pages.dev
#   ./deploy.sh preview    a preview URL that leaves production alone
set -euo pipefail
cd "$(dirname "$0")"
project="${OP_PAGES_PROJECT:-openparallel}"
branch=main
[[ "${1:-}" == "preview" ]] && branch=preview
./build.sh
export WRANGLER_SEND_METRICS=false
npx --yes wrangler@4 pages deploy dist --project-name="$project" --branch="$branch" --commit-dirty=true
