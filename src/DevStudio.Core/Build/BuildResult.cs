using DevStudio.Core.Diagnostics;

namespace DevStudio.Core.Build;

/// <summary>
/// A structured build outcome (SKILL.md §6). <see cref="Status"/> is always derived from the
/// real process exit code / cancellation / adapter availability — never assumed successful
/// merely because the process started, and never "Succeeded" when <see cref="ExitCode"/> is
/// non-zero.
/// </summary>
public sealed record BuildResult(
    BuildStatus Status,
    int ExitCode,
    TimeSpan Duration,
    BuildTarget Target,
    BuildOperation Operation,
    IReadOnlyList<Diagnostic> Diagnostics,
    string Output,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string? Message = null);
