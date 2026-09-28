@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [DevStudio] .NET SDK not found on PATH.
    echo Please install the .NET 10 SDK from https://dotnet.microsoft.com/download and try again.
    pause
    exit /b 1
)

echo [DevStudio] Starting DevStudio...
dotnet run --project src\DevStudio.App\DevStudio.App.csproj -c Release
if errorlevel 1 pause
