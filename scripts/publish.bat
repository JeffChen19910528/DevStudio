@echo off
REM Builds standalone, single-file DevStudio executables that do NOT require the .NET SDK
REM to be installed on the machine that runs them. Output goes to publish\<platform>\.
setlocal
cd /d "%~dp0\.."

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [DevStudio] .NET SDK not found on PATH. Install it from https://dotnet.microsoft.com/download
    exit /b 1
)

set PROJECT=src\DevStudio.App\DevStudio.App.csproj

echo [DevStudio] Publishing win-x64...
dotnet publish %PROJECT% -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -o publish\win-x64
if errorlevel 1 goto :error

echo [DevStudio] Publishing linux-x64...
dotnet publish %PROJECT% -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -o publish\linux-x64
if errorlevel 1 goto :error

echo [DevStudio] Publishing osx-x64...
dotnet publish %PROJECT% -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -o publish\osx-x64
if errorlevel 1 goto :error

echo [DevStudio] Publishing osx-arm64...
dotnet publish %PROJECT% -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -o publish\osx-arm64
if errorlevel 1 goto :error

echo.
echo [DevStudio] Done. Standalone executables are under publish\^<platform^>\.
echo   Windows: publish\win-x64\DevStudio.App.exe
echo   Linux:   publish\linux-x64\DevStudio.App
echo   macOS:   publish\osx-x64\DevStudio.App  (or publish\osx-arm64\DevStudio.App on Apple Silicon)
exit /b 0

:error
echo [DevStudio] Publish failed.
exit /b 1
