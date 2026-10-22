#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="$ROOT/dist/OpenParallel.app"
ZIP="$ROOT/dist/OpenParallel.zip"

cd "$ROOT"

./scripts/test.sh
/bin/bash -n scripts/build-app.sh scripts/test.sh scripts/verify-release.sh

[[ -d "$APP" ]]
[[ -f "$ZIP" ]]
[[ -f "$ZIP.sha256" ]]
[[ -x "$APP/Contents/MacOS/OpenParallel" ]]
[[ -f "$APP/Contents/Resources/OpenParallel.icns" ]]
/usr/bin/lipo "$APP/Contents/MacOS/OpenParallel" -verify_arch arm64 x86_64

/usr/bin/plutil -lint "$APP/Contents/Info.plist" >/dev/null
[[ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$APP/Contents/Info.plist")" == "$(/usr/bin/jq -r .version "$ROOT/project.json")" ]]
[[ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$APP/Contents/Info.plist")" == "dev.openparallel.app" ]]
/usr/bin/codesign --verify --deep --strict "$APP"
/usr/bin/unzip -tq "$ZIP" >/dev/null
(
  cd "$ROOT/dist"
  /usr/bin/shasum -a 256 -c OpenParallel.zip.sha256
)

if rg -n 'IdentityCloner|CloneCLI|export HOME=|XDG_CONFIG_HOME|XDG_DATA_HOME|/clone/' Sources/OpenParallel README.md scripts/build-app.sh; then
  printf 'Release contains obsolete copied-clone or global HOME/XDG isolation path.\n' >&2
  exit 1
fi

for app in /Applications/Claude.app /Applications/ChatGPT.app /Applications/Antigravity.app '/Applications/Antigravity IDE.app'; do
  [[ -d "$app" ]] || continue
  output="$("$APP/Contents/MacOS/OpenParallel" inspect --src "$app" --json)"
  /usr/bin/jq -e . >/dev/null <<< "$output"
  rg -q '"launchMode" : "linked-signed-source"' <<< "$output"
  rg -q '"normalHome" : true' <<< "$output"
done

for blocked_app in '/Applications/Telegram Lite.app'; do
  [[ -d "$blocked_app" ]] || continue
  blocked_output="$("$APP/Contents/MacOS/OpenParallel" inspect --src "$blocked_app" --json)"
  /usr/bin/jq -e '.multiInstance == false and .separateData == false' >/dev/null <<< "$blocked_output"
done

# WhatsApp: the public build refuses it and points at a Web shortcut. A build
# with private extensions runs their own checks instead.
if [[ -d /Applications/WhatsApp.app ]]; then
  whatsapp_output="$("$APP/Contents/MacOS/OpenParallel" inspect --src /Applications/WhatsApp.app --json)"
  if [[ -x "$ROOT/scripts/private/verify-release.sh" && -d "$ROOT/Sources/OpenParallel/Private" ]]; then
    "$ROOT/scripts/private/verify-release.sh" "$APP" "$whatsapp_output"
  else
    /usr/bin/jq -e '.multiInstance == false and .separateData == false' >/dev/null <<< "$whatsapp_output"
  fi
  # The vendor bundle must never be reported as a place OpenParallel writes to.
  /usr/bin/codesign --verify --strict /Applications/WhatsApp.app
fi

WEB_NAME="OpenParallel Release Web $$"
WEB_OUT="$(/usr/bin/mktemp -d)"
WEB_SLUG="$(printf '%s' "$WEB_NAME" | /usr/bin/tr '[:upper:]' '[:lower:]' | /usr/bin/tr -cs 'a-z0-9' '-' | /usr/bin/sed 's/^-//;s/-$//')"
WEB_PROFILE="$HOME/Library/Application Support/OpenParallel/Instances/$WEB_SLUG"
trap '/bin/rm -rf "$WEB_OUT" "$WEB_PROFILE"' EXIT
web_build="$("$APP/Contents/MacOS/OpenParallel" web-build --url https://example.com --name "$WEB_NAME" --out "$WEB_OUT")"
WEB_WRAPPER="$(printf '%s\n' "$web_build" | /usr/bin/sed -n 's/^wrapper=//p')"
[[ -d "$WEB_WRAPPER" ]]
[[ -x "$WEB_WRAPPER/Contents/MacOS/OpenParallelWebRunner" ]]
[[ "$(/usr/libexec/PlistBuddy -c 'Print :OpenParallelLaunchMode' "$WEB_WRAPPER/Contents/Info.plist")" == "web-frame" ]]
/usr/bin/codesign --verify --deep --strict "$WEB_WRAPPER"

printf 'Release verification PASS\n'
