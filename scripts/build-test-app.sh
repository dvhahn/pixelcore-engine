#!/bin/bash
# Builds a test .app without NativeAOT: faster to produce, and stack traces keep file and line numbers.
#   scripts/build-test-app.sh            -> bin/Release/PixelCore.app
#   scripts/build-test-app.sh <folder>   also copies the app there
# Not for distribution - release builds use: dotnet publish -c Release -r osx-arm64 && scripts/package-app.sh
set -euo pipefail
cd "$(dirname "$0")/.."

echo "> publish (AOT off)"
dotnet publish -c Release -r osx-arm64 -p:NoAot=true > /dev/null

echo "> packaging (pdb included - file and line numbers in stack traces)"
KEEP_PDB=1 scripts/package-app.sh > /dev/null
APP=bin/Release/PixelCore.app

if [ $# -ge 1 ]; then
    DEST="$1"
    mkdir -p "$DEST"
    rm -rf "$DEST/PixelCore.app"
    cp -R "$APP" "$DEST/PixelCore.app"
    echo "→ $DEST/PixelCore.app"
else
    echo "→ $APP"
fi
echo "   (test build - no AOT, not for release)"
