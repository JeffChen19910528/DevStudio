#!/usr/bin/env bash
# Builds standalone, single-file DevStudio executables that do NOT require the .NET SDK
# to be installed on the machine that runs them. Output goes to publish/<platform>/.
set -e
cd "$(dirname "$0")/.."

if ! command -v dotnet >/dev/null 2>&1; then
    echo "[DevStudio] .NET SDK not found on PATH. Install it from https://dotnet.microsoft.com/download"
    exit 1
fi

PROJECT="src/DevStudio.App/DevStudio.App.csproj"

for rid in win-x64 linux-x64 osx-x64 osx-arm64; do
    echo "[DevStudio] Publishing $rid..."
    dotnet publish "$PROJECT" -c Release -r "$rid" --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -o "publish/$rid"
done

echo
echo "[DevStudio] Done. Standalone executables are under publish/<platform>/:"
echo "  Windows: publish/win-x64/DevStudio.App.exe"
echo "  Linux:   publish/linux-x64/DevStudio.App"
echo "  macOS:   publish/osx-x64/DevStudio.App  (or publish/osx-arm64/DevStudio.App on Apple Silicon)"
