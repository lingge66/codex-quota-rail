#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-0.1.0}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
MACOS_ROOT="$ROOT/macos"
ARTIFACTS="$ROOT/artifacts/macos"
BUILD_ROOT="$MACOS_ROOT/.build-release"
APP="$ARTIFACTS/CodexQuotaRailMac.app"
EXECUTABLE="$APP/Contents/MacOS/CodexQuotaRailMac"

read_config() {
    awk -F= -v key="$1" '$1 ~ "^[[:space:]]*" key "[[:space:]]*$" {value=$2; sub(/^[[:space:]]*/, "", value); sub(/[[:space:]]*$/, "", value); print value; exit}' "$MACOS_ROOT/Config/Branding.xcconfig"
}

PRODUCT_NAME="$(read_config PRODUCT_NAME)"
BUNDLE_IDENTIFIER="$(read_config PRODUCT_BUNDLE_IDENTIFIER)"
WEBSITE_URL="$(read_config LINGGE_WEBSITE_URL)"
UPDATE_REPOSITORY="$(read_config UPDATE_REPOSITORY)"
TARGET_BUNDLE_ID="$(read_config DEFAULT_TARGET_BUNDLE_ID)"

[[ "$BUNDLE_IDENTIFIER" =~ ^[A-Za-z0-9.-]+$ ]]
[[ "$TARGET_BUNDLE_ID" =~ ^[A-Za-z0-9.-]+$ ]]
[[ "$UPDATE_REPOSITORY" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]]
[[ "$WEBSITE_URL" =~ ^https://[^[:space:]\"]+$ ]]
test -n "$PRODUCT_NAME"

rm -rf "$BUILD_ROOT" "$ARTIFACTS"
mkdir -p "$BUILD_ROOT" "$APP/Contents/MacOS" "$APP/Contents/Resources"

swift build --package-path "$MACOS_ROOT" --configuration release --arch arm64 --scratch-path "$BUILD_ROOT/arm64"
swift build --package-path "$MACOS_ROOT" --configuration release --arch x86_64 --scratch-path "$BUILD_ROOT/x86_64"

ARM_BINARY="$(find "$BUILD_ROOT/arm64" -type f -path '*/release/CodexQuotaRailMac' | head -n 1)"
INTEL_BINARY="$(find "$BUILD_ROOT/x86_64" -type f -path '*/release/CodexQuotaRailMac' | head -n 1)"
test -n "$ARM_BINARY"
test -n "$INTEL_BINARY"
lipo -create "$ARM_BINARY" "$INTEL_BINARY" -output "$EXECUTABLE"
chmod 755 "$EXECUTABLE"

cp "$MACOS_ROOT/App/Resources/Info.plist" "$APP/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$APP/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion ${GITHUB_RUN_NUMBER:-1}" "$APP/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleDisplayName $PRODUCT_NAME" "$APP/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleName $PRODUCT_NAME" "$APP/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleIdentifier $BUNDLE_IDENTIFIER" "$APP/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Add :UpdateRepository string $UPDATE_REPOSITORY" "$APP/Contents/Info.plist"
cp "$MACOS_ROOT/Config/Defaults.json" "$APP/Contents/Resources/Defaults.json"
plutil -replace websiteURL -string "$WEBSITE_URL" "$APP/Contents/Resources/Defaults.json"
plutil -replace targetBundleIdentifiers -json "[\"$TARGET_BUNDLE_ID\"]" "$APP/Contents/Resources/Defaults.json"
cp -R "$MACOS_ROOT/Resources/Themes" "$APP/Contents/Resources/Themes"

ICON_SOURCE="$ROOT/src/CodexQuotaRail.App/Assets/LingGe.ico"
sips -s format png "$ICON_SOURCE" --out "$BUILD_ROOT/LingGe.png" >/dev/null
ICONSET="$BUILD_ROOT/AppIcon.iconset"
mkdir -p "$ICONSET"
for SIZE in 16 32 128 256 512; do
    sips -z "$SIZE" "$SIZE" "$BUILD_ROOT/LingGe.png" --out "$ICONSET/icon_${SIZE}x${SIZE}.png" >/dev/null
    DOUBLE=$((SIZE * 2))
    sips -z "$DOUBLE" "$DOUBLE" "$BUILD_ROOT/LingGe.png" --out "$ICONSET/icon_${SIZE}x${SIZE}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/LingGe.icns"
/usr/libexec/PlistBuddy -c "Add :CFBundleIconFile string LingGe" "$APP/Contents/Info.plist"

codesign --force --deep --sign - "$APP"
"$MACOS_ROOT/Scripts/verify-app.sh" "$APP"

ZIP="$ARTIFACTS/CodexQuotaRail-macOS-universal.app.zip"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"
cd "$ARTIFACTS"
shasum -a 256 "$(basename "$ZIP")" | awk '{print $1 "  " $2}' > SHA256SUMS.txt
printf '%s\n' "$VERSION" > VERSION.txt
printf '%s\n' "$ZIP"
