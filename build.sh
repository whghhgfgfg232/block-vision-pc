#!/bin/bash
set -e
echo "=== Block Vision PC Build ==="
CONFIG=${1:-Release}
RUNTIME=${2:-win-x64}

dotnet build src/BlockVision.Core/BlockVision.Core.csproj -c $CONFIG
dotnet build src/BlockVision.SDK/BlockVision.SDK.csproj -c $CONFIG
dotnet publish src/BlockVision.App/BlockVision.App.csproj -c $CONFIG -r $RUNTIME --self-contained true -p:PublishSingleFile=true -o publish/app
dotnet publish src/BlockVision.CLI/BlockVision.CLI.csproj -c $CONFIG -r $RUNTIME --self-contained true -p:PublishSingleFile=true -o publish/cli

mkdir -p dist/BlockVisionPC
cp -r publish/app/* dist/BlockVisionPC/ || true
cp -r publish/cli/* dist/BlockVisionPC/ || true
cp -r examples dist/BlockVisionPC/ || true
cp README.md INSTALL.md dist/BlockVisionPC/ || true
cp -r docs dist/BlockVisionPC/ || true

echo "Build complete! Output: dist/BlockVisionPC"
ls -lh dist/BlockVisionPC | head -n 20
