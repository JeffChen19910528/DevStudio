namespace DevStudio.Core.Run;

/// <summary>A run's own state machine — distinct from <see
/// cref="DevStudio.Core.Build.BuildStatus"/> (SKILL.md §14): a build is a bounded operation that
/// always finishes, a run is unbounded until the application exits or is stopped.</summary>
public enum RunStatus
{
    NotStarted,
    Starting,
    Running,
    Stopping,

    /// <summary>The application's own process exited on its own (any exit code).</summary>
    Exited,

    /// <summary>The application process never started — adapter/toolchain unavailable, the
    /// project isn't runnable, a required build failed first, or the working directory/build
    /// output doesn't exist.</summary>
    FailedToStart,

    Cancelled,

    /// <summary>The user explicitly requested Stop and the process was killed — distinct from
    /// <see cref="Exited"/> so the UI never reports "Application failed" for a deliberate stop
    /// (SKILL.md §33).</summary>
    Terminated
}
