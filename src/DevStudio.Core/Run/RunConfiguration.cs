using DevStudio.Core.Build;
using DevStudio.Core.Workspace;

namespace DevStudio.Core.Run;

/// <summary>
/// A normalized, UI-independent run configuration (SKILL.md §5). Never a command string —
/// <see cref="Arguments"/> is a real array, each element one logical application argument, so
/// e.g. <c>--name</c>, <c>DevStudio Test</c> stays two elements rather than being naively split
/// or re-joined. <see cref="EnvironmentVariables"/> is held in memory only for this phase and
/// is never persisted (SKILL.md §27) — see ADR-006 for why.
/// </summary>
public sealed record RunConfiguration(
    string Name,
    BuildTarget Target,
    BuildConfiguration BuildConfiguration,
    IReadOnlyList<string> Arguments = null!,
    string? WorkingDirectoryOverride = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    bool BuildBeforeRun = true)
{
    public IReadOnlyList<string> Arguments { get; init; } = Arguments ?? Array.Empty<string>();
}
