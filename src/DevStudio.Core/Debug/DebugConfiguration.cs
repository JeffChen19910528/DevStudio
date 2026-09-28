using DevStudio.Core.Run;

namespace DevStudio.Core.Debug;

/// <summary>Reuses <see cref="Run.RunConfiguration"/> rather than duplicating target/arguments/
/// working-directory/environment modeling (SKILL.md §14 [Phase 6]) and adds only the
/// debugger-specific options DevStudio actually enforces — never a setting the debugger can't
/// honor.</summary>
public sealed record DebugConfiguration(
    RunConfiguration RunConfiguration,
    bool StopAtEntry = false,
    bool JustMyCode = true,
    bool BuildBeforeDebug = true);

public sealed record DebugResult(
    DebugSessionState State,
    DebugConfiguration Configuration,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int? ExitCode = null,
    string? Message = null);
