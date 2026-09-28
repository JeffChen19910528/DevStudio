using DevStudio.Infrastructure.Toolchains;

namespace DevStudio.Infrastructure.Debug;

public sealed record NetCoreDebuggerResolution(bool Found, string? ExecutablePath, string? Source, string? Message);

/// <summary>
/// Finds a real, usable DAP-mode .NET debugger (SKILL.md §8–§9 [Phase 6]) without ever assuming
/// its location from a Visual Studio installation. Two real candidates were actually
/// investigated on this development machine (see ADR-007's Consequences for the full story):
///
/// <list type="bullet">
/// <item><b>vsdbg</b> — genuinely present via the VS Code C# extension's private install
/// (confirmed with a real <see cref="File.Exists"/> check and a real DAP <c>initialize</c>
/// exchange). Its own license, printed in its startup banner, restricts use to Visual Studio
/// Code / Visual Studio / Visual Studio for Mac; it enforces this with a proprietary handshake
/// that a real integration test confirmed refuses a non-Microsoft-IDE client (<c>configurationDone</c>
/// fails with an internal "handshake" error). DevStudio does not attempt to spoof or reverse
/// engineer that handshake — this resolver therefore does not select vsdbg.</item>
/// <item><b>netcoredbg</b> (Samsung, MIT-licensed, <see
/// href="https://github.com/Samsung/netcoredbg"/>) — the same <c>--interpreter=vscode</c> DAP
/// contract, no client-identity restriction. A real integration test confirmed the full
/// initialize → launch → setBreakpoints → configurationDone → real breakpoint hit sequence
/// against it. This is the debugger DevStudio actually resolves and drives.</item>
/// </list>
///
/// Every candidate is verified with a real <see cref="File.Exists"/> check; nothing is ever
/// assumed present.
/// </summary>
public sealed class NetCoreDebuggerResolver
{
    private const string OverrideEnvironmentVariable = "DEVSTUDIO_NETCOREDBG_PATH";

    public NetCoreDebuggerResolution Resolve()
    {
        var overridePath = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return File.Exists(overridePath)
                ? new NetCoreDebuggerResolution(true, overridePath, $"{OverrideEnvironmentVariable} override", null)
                : new NetCoreDebuggerResolution(false, null, null, $"{OverrideEnvironmentVariable} was set to '{overridePath}', but no file exists there.");
        }

        var executableName = OperatingSystem.IsWindows() ? "netcoredbg.exe" : "netcoredbg";

        var onPath = ExecutableLocator.FindOnPath("netcoredbg");
        if (onPath is not null)
        {
            return new NetCoreDebuggerResolution(true, onPath, "PATH", null);
        }

        var wingetCandidate = FindWinGetInstall(executableName);
        if (wingetCandidate is not null)
        {
            return new NetCoreDebuggerResolution(true, wingetCandidate, "winget install (Samsung.NetCoreDbg)", null);
        }

        return new NetCoreDebuggerResolution(false, null, null,
            "Debug unavailable: .NET debugger adapter (netcoredbg) not found. Install it " +
            "(e.g. `winget install Samsung.NetCoreDbg`), or set DEVSTUDIO_NETCOREDBG_PATH to a real netcoredbg executable.");
    }

    /// <summary>The real, verified install location `winget install Samsung.NetCoreDbg` uses on
    /// this machine — checked with <see cref="File.Exists"/>, never assumed.</summary>
    private static string? FindWinGetInstall(string executableName)
    {
        var packagesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
        if (!Directory.Exists(packagesRoot)) return null;

        foreach (var packageDir in Directory.EnumerateDirectories(packagesRoot, "Samsung.NetCoreDbg_*"))
        {
            var candidate = Path.Combine(packageDir, "netcoredbg", executableName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
