using DevStudio.Core.Processes;
using DevStudio.Core.Projects;

namespace DevStudio.Core.Run;

/// <summary>
/// Launches one project ecosystem's runnable application through <see cref="IProcessRunner"/>
/// (SKILL.md §2, §4 — the same shared process abstraction Build uses, not a second one). An
/// adapter validates its own toolchain/target before starting anything and throws with an
/// actionable message rather than fabricating a running application (SKILL.md §8, §37).
/// </summary>
public interface IRunAdapter
{
    bool SupportsProjectType(ProjectType projectType);

    Task<IRunningApplication> StartAsync(RunConfiguration configuration, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// A handle to one launched application (SKILL.md §13) — deliberately built on top of the
/// existing <see cref="IRunningProcess"/> rather than a parallel process abstraction.
/// </summary>
public interface IRunningApplication : IAsyncDisposable
{
    bool HasExited { get; }

    /// <summary>Requests termination (SKILL.md §20) — killing the underlying process tree via
    /// the same mechanism Build cancellation already uses.</summary>
    void Stop();

    /// <summary>Completes once the application has actually exited (naturally or via <see
    /// cref="Stop"/>), with the real exit code and a <see cref="RunResult.Status"/> that
    /// distinguishes the two cases (SKILL.md §16, §33).</summary>
    Task<RunResult> WaitForExitAsync(CancellationToken cancellationToken = default);
}
