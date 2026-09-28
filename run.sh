#!/usr/bin/env bash
# One-click launcher for DevStudio on Linux/macOS.
# First run: chmod +x run.sh
# Then:      ./run.sh
set -e
cd "$(dirname "$0")"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "[DevStudio] .NET SDK not found on PATH."
    echo "Please install the .NET 10 SDK from https://dotnet.microsoft.com/download and try again."
    exit 1
fi

echo "[DevStudio] Starting DevStudio..."
dotnet run --project src/DevStudio.App/DevStudio.App.csproj -c Release
