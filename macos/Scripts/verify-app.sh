#!/usr/bin/env bash
set -euo pipefail

APP="${1:?app bundle path is required}"
EXECUTABLE="$APP/Contents/MacOS/CodexQuotaRailMac"

test -d "$APP"
test -f "$APP/Contents/Info.plist"
test -x "$EXECUTABLE"
ARCHITECTURES="$(lipo -archs "$EXECUTABLE")"
grep -qw arm64 <<<"$ARCHITECTURES"
grep -qw x86_64 <<<"$ARCHITECTURES"
codesign --verify --deep --strict "$APP"
plutil -lint "$APP/Contents/Info.plist"
test "$(/usr/libexec/PlistBuddy -c 'Print :LSUIElement' "$APP/Contents/Info.plist")" = true
test "$(/usr/libexec/PlistBuddy -c 'Print :LSMinimumSystemVersion' "$APP/Contents/Info.plist")" = 13.0
printf 'verified architectures: %s\n' "$ARCHITECTURES"
