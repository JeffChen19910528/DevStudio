namespace DevStudio.Core.Run;

/// <summary>
/// A structured run outcome (SKILL.md §15–§16). <see cref="ExitCode"/> is nullable because
/// "the process never started" (<see cref="RunStatus.FailedToStart"/>) genuinely has no exit
/// code — it is never coerced to 0 or -1 to avoid a nullable field. A non-zero <see
/// cref="ExitCode"/> on <see cref="RunStatus.Exited"/> is not itself an error: the application
/// may use non-zero exit codes intentionally.
/// </summary>
public sealed record RunResult(
    RunStatus Status,
    int? ExitCode,
    TimeSpan Duration,
    RunConfiguration Configuration,
    string Output,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? Message = null);
