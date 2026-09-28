namespace DevStudio.Infrastructure.Toolchains;

/// <summary>Resolves an executable name to a full path via PATH, without assuming an extension
/// (SKILL.md §7: "do not assume .exe on Linux/macOS"). Read-only — never modifies PATH.</summary>
public static class ExecutableLocator
{
    public static string? FindOnPath(string executableName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable)) return null;

        var candidateNames = OperatingSystem.IsWindows()
            ? new[] { executableName, executableName + ".exe", executableName + ".cmd", executableName + ".bat" }
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
