namespace DevStudio.Core.Testing;

/// <summary>Normalized test outcome (SKILL.md §6 [Phase 8]) — DevStudio's own vocabulary, kept
/// separate from whatever outcome strings the underlying runner (VSTest's TRX <c>outcome</c>
/// attribute, in the .NET case) actually uses. The UI never needs to understand
/// xUnit/NUnit/MSTest-specific result semantics.</summary>
public enum TestOutcome
{
    Discovered,
    Queued,
    Running,
    Passed,
    Failed,
    Skipped,
    NotRun,
    Cancelled,
    Error,
}

/// <summary>A test run's own lifecycle (SKILL.md §14, §22, §28) — deliberately not <see
/// cref="Build.BuildStatus"/>/<see cref="Run.RunStatus"/> reused; <see
/// cref="BlockedByBuildFailure"/> and <see cref="BlockedByTrust"/> are states neither of those
/// enums has a reason to represent.</summary>
public enum TestRunState
{
    NotStarted,
    Starting,
    Discovering,
    Running,
    Completed,
    Failed,
    Cancelled,
    BlockedByBuildFailure,
    BlockedByTrust,
}
