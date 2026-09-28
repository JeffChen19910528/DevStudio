namespace DevStudio.Core.Debug;

/// <summary>Its own state machine — deliberately not <see cref="Run.RunStatus"/> reused
/// (SKILL.md §12 [Phase 6]): a debug session has a <see cref="Paused"/> state a plain run never
/// has, and "stopped at a breakpoint" must never be represented as a generic error.</summary>
public enum DebugSessionState
{
    NotStarted,
    Starting,
    Running,
    Paused,
    Stopping,
    Terminated,
    Failed,
}
