#!/bin/bash
# Compiles Content/Shaders/*.fx to .fxb (XNA4 effect bytecode, translated to Metal at runtime by MojoShader).
#
# Requires wine and fxc.exe from the DirectX SDK (June 2010) in tools/fxc/ (not redistributable, so not included):
#   curl -LO https://download.microsoft.com/download/a/e/7/ae743f1f-632b-4809-87a9-aa1bb3458e31/DXSDK_Jun10.exe
#   cabextract -F 'DXSDK/Utilities/bin/x64/fxc.exe' DXSDK_Jun10.exe
#   cabextract -F 'DXSDK/Redist/Jun2010_D3DCompiler_43_x64.cab' DXSDK_Jun10.exe
#   cabextract Jun2010_D3DCompiler_43_x64.cab   (puts D3DCompiler_43.dll next to fxc.exe)
#   brew install --cask wine-stable
set -euo pipefail
cd "$(dirname "$0")/.."

WINE="${WINE:-wine}"
command -v "$WINE" >/dev/null || { echo "wine not found - brew install --cask wine-stable"; exit 1; }
[ -f tools/fxc/fxc.exe ] || { echo "tools/fxc/fxc.exe not found - see the restore steps at the top of this script"; exit 1; }

shopt -s nullglob
for fx in Content/Shaders/*.fx; do
    fxb="${fx%.fx}.fxb"
    echo "fxc: $fx → $fxb"
    "$WINE" tools/fxc/fxc.exe /nologo /T fx_2_0 /Fo "$fxb" "$fx"
done
echo "done"
