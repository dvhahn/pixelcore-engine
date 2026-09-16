#!/bin/bash
# Packages the release publish output as a signed (ad hoc) macOS app bundle.
#   dotnet publish -c Release -r osx-arm64
#   scripts/package-app.sh               -> bin/Release/PixelCore.app
# KEEP_PDB=1 keeps pdb files (used by build-test-app.sh). SKIP_LAUNCH_GATE=1 skips the launch check.
set -euo pipefail
cd "$(dirname "$0")/.."

PUBLISH=bin/Release/net8.0/osx-arm64/publish
APP=bin/Release/PixelCore.app

if [ ! -f "$PUBLISH/PixelCore" ]; then
    echo "no publish output - run: dotnet publish -c Release -r osx-arm64"
    exit 1
fi

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key><string>PixelCore</string>
    <key>CFBundleIdentifier</key><string>com.example.pixelcore</string>
    <key>CFBundleName</key><string>PixelCore</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>0.5.0</string>
    <key>CFBundleVersion</key><string>1</string>
    <key>LSMinimumSystemVersion</key><string>11.0</string>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

EXCLUDES=(--exclude '*.dsym')
[ "${KEEP_PDB:-}" = "1" ] || EXCLUDES+=(--exclude '*.pdb')

mkdir -p "$APP/Contents/Resources"
rsync -a "${EXCLUDES[@]}" --exclude 'Content' --exclude 'FNA.dll.config' \
      "$PUBLISH/" "$APP/Contents/MacOS/"
rsync -a "$PUBLISH/Content" "$APP/Contents/Resources/"
if [ -f "$PUBLISH/FNA.dll.config" ]; then
    rsync -a "$PUBLISH/FNA.dll.config" "$APP/Contents/Resources/"
fi

ln -sfn ../Resources/Content "$APP/Contents/MacOS/Content"
if [ -f "$APP/Contents/Resources/FNA.dll.config" ]; then
    ln -sfn ../Resources/FNA.dll.config "$APP/Contents/MacOS/FNA.dll.config"
fi

xattr -cr "$APP"

codesign --force --deep -s - "$APP"

if ! codesign --verify --deep --strict "$APP"; then
    echo "✘ the bundle signature is invalid - double-clicking may launch a different copy" >&2
    exit 1
fi
echo "✔ codesign --verify --deep --strict passed"

if [ "${SKIP_LAUNCH_GATE:-}" != "1" ]; then
    APP_ABS="$(cd "$APP" && pwd)"

    snapshot() { ps -Ao pid=,comm= | awk '$2 ~ /PixelCore$/ {print $1 "\t" $2}'; }
    BEFORE="$(snapshot || true)"
    OTHERS="$(printf '%s\n' "$BEFORE" | grep -c . || true)"
    if [ "${OTHERS:-0}" -gt 0 ]; then
        echo "  (${OTHERS} other PixelCore instance(s) running - unrelated to the gate verdict and left alone)"
    fi

    open -n "$APP_ABS"

    MINE=""
    for _ in $(seq 1 24); do
        MINE="$(ps -Ao pid=,comm= | awk -v p="$APP_ABS/" 'index($2, p) == 1 {print $1}' | tail -1)"
        [ -n "$MINE" ] && break
        sleep 0.5
    done

    if [ -n "$MINE" ]; then
        kill "$MINE" 2>/dev/null || true
        echo "✔ open -n launched this bundle: $APP_ABS/Contents/MacOS/PixelCore"
    else
        AFTER="$(snapshot || true)"
        NEW="$(comm -13 <(printf '%s\n' "$BEFORE" | sort) <(printf '%s\n' "$AFTER" | sort) | grep . || true)"
        if [ -n "$NEW" ]; then
            NEWPATH="$(printf '%s\n' "$NEW" | head -1 | cut -f2)"
            NEWID="$(defaults read "${NEWPATH%/Contents/MacOS/*}/Contents/Info" CFBundleIdentifier 2>/dev/null || echo '(not a bundle)')"
            MYID="$(defaults read "$APP_ABS/Contents/Info" CFBundleIdentifier 2>/dev/null || echo '?')"
            echo "✘ a different copy launched: $NEWPATH" >&2
            echo "   its bundle id = $NEWID, ours = $MYID" >&2
            echo "   (an old app registered under the same id - delete it or change the bundle id)" >&2
            exit 1
        fi
        echo "✘ the app did not launch - check the signature and the bundle structure" >&2
        exit 1
    fi
fi

dotnet restore > /dev/null 2>&1 || true

echo "-> $APP (double-clickable)"
