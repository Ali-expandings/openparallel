#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="${VERSION:-$(/usr/bin/jq -r .version "$ROOT/project.json")}"
BUILD_NUMBER="${BUILD_NUMBER:-1}"
SIGN_IDENTITY="${SIGN_IDENTITY:--}"
NOTARY_PROFILE="${NOTARY_PROFILE:-}"
BUILD_UNIVERSAL="${BUILD_UNIVERSAL:-1}"

BUILD_APP="$ROOT/.build/OpenParallel.app"
DIST_DIR="$ROOT/dist"
DIST_APP="$DIST_DIR/OpenParallel.app"
ZIP_PATH="$DIST_DIR/OpenParallel.zip"
ICONSET="$ROOT/.build/OpenParallel.iconset"
ICON_SOURCE="$ROOT/.build/OpenParallel-icon-1024.png"
CONTENTS="$BUILD_APP/Contents"
MACOS="$CONTENTS/MacOS"
RESOURCES="$CONTENTS/Resources"

cd "$ROOT"

./scripts/test.sh
swift build -c release
if [[ "$BUILD_UNIVERSAL" == "1" ]]; then
  swift build -c release --triple x86_64-apple-macosx14.0 --scratch-path .build-x86_64
fi

/bin/rm -rf "$BUILD_APP" "$DIST_DIR" "$ICONSET" "$ICON_SOURCE"
mkdir -p "$MACOS" "$RESOURCES" "$ICONSET" "$DIST_DIR"

if [[ "$BUILD_UNIVERSAL" == "1" ]]; then
  X86_BINARY="$ROOT/.build-x86_64/x86_64-apple-macosx/release/OpenParallel"
  x86_is_current() {
    [[ -x "$X86_BINARY" ]] || return 1
    ! /usr/bin/find "$ROOT/Sources/OpenParallel" -type f -name '*.swift' -newer "$X86_BINARY" | /usr/bin/grep -q .
  }
  for _ in {1..120}; do
    x86_is_current && break
    /bin/sleep 1
  done
  x86_is_current
  /usr/bin/lipo -create "$ROOT/.build/release/OpenParallel" "$X86_BINARY" -output "$MACOS/OpenParallel"
else
  cp "$ROOT/.build/release/OpenParallel" "$MACOS/OpenParallel"
fi

/usr/bin/swift "$ROOT/scripts/generate-icon.swift" "$ICON_SOURCE"
for spec in "16 icon_16x16.png" "32 icon_16x16@2x.png" "32 icon_32x32.png" "64 icon_32x32@2x.png" "128 icon_128x128.png" "256 icon_128x128@2x.png" "256 icon_256x256.png" "512 icon_256x256@2x.png" "512 icon_512x512.png" "1024 icon_512x512@2x.png"; do
  size="${spec%% *}"
  file="${spec#* }"
  /usr/bin/sips -z "$size" "$size" "$ICON_SOURCE" --out "$ICONSET/$file" >/dev/null
done
/usr/bin/iconutil -c icns "$ICONSET" -o "$RESOURCES/OpenParallel.icns"
# Public project facts (report address, repository) shared with the website and other platforms.
cp "$ROOT/project.json" "$RESOURCES/project.json"

cat > "$CONTENTS/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key>
  <string>OpenParallel</string>
  <key>CFBundleIdentifier</key>
  <string>dev.openparallel.app</string>
  <key>CFBundleName</key>
  <string>OpenParallel</string>
  <key>CFBundleDisplayName</key>
  <string>OpenParallel</string>
  <key>CFBundleIconFile</key>
  <string>OpenParallel.icns</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>$VERSION</string>
  <key>CFBundleVersion</key>
  <string>$BUILD_NUMBER</string>
  <key>LSApplicationCategoryType</key>
  <string>public.app-category.utilities</string>
  <key>LSMinimumSystemVersion</key>
  <string>14.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key>
      <string>Application</string>
      <key>CFBundleTypeRole</key>
      <string>Viewer</string>
      <key>LSHandlerRank</key>
      <string>None</string>
      <key>LSItemContentTypes</key>
      <array>
        <string>com.apple.application-bundle</string>
      </array>
    </dict>
  </array>
</dict>
</plist>
PLIST

printf 'APPL????' > "$CONTENTS/PkgInfo"
/usr/bin/plutil -lint "$CONTENTS/Info.plist" >/dev/null

if [[ "$SIGN_IDENTITY" == "-" ]]; then
  /usr/bin/codesign --force --sign - "$BUILD_APP" >/dev/null
  SIGNING_MODE="ad-hoc local build"
else
  /usr/bin/codesign --force --options runtime --timestamp --sign "$SIGN_IDENTITY" "$BUILD_APP"
  SIGNING_MODE="Developer ID"
fi

/usr/bin/codesign --verify --deep --strict "$BUILD_APP"
/usr/bin/ditto "$BUILD_APP" "$DIST_APP"
/usr/bin/ditto -c -k --sequesterRsrc --keepParent "$DIST_APP" "$ZIP_PATH"

if [[ -n "$NOTARY_PROFILE" ]]; then
  if [[ "$SIGN_IDENTITY" == "-" ]]; then
    printf 'NOTARY_PROFILE requires Developer ID SIGN_IDENTITY.\n' >&2
    exit 64
  fi
  /usr/bin/xcrun notarytool submit "$ZIP_PATH" --keychain-profile "$NOTARY_PROFILE" --wait
  /usr/bin/xcrun stapler staple "$DIST_APP"
  /bin/rm -f "$ZIP_PATH"
  /usr/bin/ditto -c -k --sequesterRsrc --keepParent "$DIST_APP" "$ZIP_PATH"
fi

(
  cd "$DIST_DIR"
  /usr/bin/shasum -a 256 OpenParallel.zip > OpenParallel.zip.sha256
)

/usr/bin/codesign --verify --deep --strict "$DIST_APP"
if [[ "$SIGN_IDENTITY" != "-" ]]; then
  /usr/sbin/spctl --assess --type execute --verbose=2 "$DIST_APP"
fi

printf 'OpenParallel %s (%s)\n' "$VERSION" "$BUILD_NUMBER"
printf 'Signing: %s\n' "$SIGNING_MODE"
printf '%s\n' "$DIST_APP" "$ZIP_PATH" "$ZIP_PATH.sha256"
