namespace DevStudio.Infrastructure.Terminal;

/// <summary>
/// Picks the user's default shell per-platform (SKILL.md §14) instead of hard-coding one shell
/// for every OS (SKILL.md §32).
/// </summary>
public static class ShellLocator
{
    public static string GetDefaultShellExecutable()
    {
        if (OperatingSystem.IsWindows())
        {
            return FindOnPath("pwsh.exe") ?? FindOnPath("powershell.exe") ?? "cmd.exe";
        }

        var shellFromEnvironment = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrWhiteSpace(shellFromEnvironment) && File.Exists(shellFromEnvironment))
        {
            return shellFromEnvironment;
        }

        return FindOnPath("bash") ?? "/bin/sh";
    }

    private static string? FindOnPath(string executableName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable)) return null;

        foreach (var directory in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;

            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
