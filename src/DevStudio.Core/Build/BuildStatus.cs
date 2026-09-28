namespace DevStudio.Core.Build;

/// <summary>Never represented as a bare bool (SKILL.md §6): success/failure/cancellation/timeout
/// /unavailability are distinct facts a UI must show differently.</summary>
public enum BuildStatus
{
    NotStarted,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    TimedOut,

    /// <summary>No adapter exists for this project type, or its required toolchain isn't
    /// installed — the build was never attempted, which is a different fact from "it ran and
    /// failed" (SKILL.md §32–§34).</summary>
    Unavailable
}
