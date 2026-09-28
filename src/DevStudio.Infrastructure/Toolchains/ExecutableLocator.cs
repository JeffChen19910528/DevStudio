namespace DevStudio.Infrastructure.Toolchains;

/// <summary>Resolves an executable name to a full path via PATH, without assuming an extension
/// (SKILL.md §7: "do not assume .exe on Linux/macOS"). Read-only — never modifies PATH.</summary>
public static class ExecutableLocator
{
    public static string? FindOnPath(string executableName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable)) return null;

        // Real, Phase 13 finding: some Windows PATH directories (observed: Node.js's own install
        // directory) contain an extension-less POSIX shell-script twin alongside the real Windows
        // launcher (e.g. a bare "npm" shell script next to "npm.cmd", shipped for WSL/git-bash
        // users) — CreateProcess can "find" that file but cannot run it ("not a valid application
        // for this OS platform"). Checking the real Windows launcher extensions first means a
        // Windows-runnable match always wins over a same-named non-Windows script; the bare name
        // is still tried last so a tool that genuinely ships as an extension-less native binary
        // is still found.
        var candidateNames = OperatingSystem.IsWindows()
            ? new[] { executableName + ".exe", executableName + ".cmd", executableName + ".bat", executableName }
            : new[] { executableName };

        foreach (var directory in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;

            foreach (var name in candidateNames)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
