using DevStudio.Core.Projects;

namespace DevStudio.Core.Debug;

/// <summary>
/// Drives one real debugger through DAP (SKILL.md §2): <c>DebugService → IDebuggerAdapter →
/// DAP Client → real debugger → real application</c>. An adapter is responsible for verifying
/// its own required debugger is actually installed before starting anything — never a fabricated
/// success and never an attempted launch against a missing debugger (mirrors
/// <c>Build.IBuildAdapter</c>/<c>Run.IRunAdapter</c>'s "check first" contract).
/// </summary>
public interface IDebuggerAdapter
{
    bool SupportsProjectType(ProjectType projectType);

    /// <summary>Launches the real debugger and the real target application, performs the DAP
    /// <c>initialize</c>/<c>launch</c> handshake, and returns a handle for everything after that
    /// (breakpoints, stepping, inspection). Throws if the debugger could not be resolved/started
    /// — never returns a session that silently does nothing.</summary>
    Task<IActiveDebugSession> StartAsync(DebugConfiguration configuration, CancellationToken cancellationToken = default);
}

/// <summary>One live DAP session (SKILL.md §11–§13). Every method here corresponds to one real
/// DAP request; <see cref="DebugService"/> is the only thing that calls it, and never exposes
/// this interface (or raw DAP JSON) to a ViewModel.</summary>
public interface IActiveDebugSession : IAsyncDisposable
{
    int? ProcessId { get; }

    /// <summary>Raised for a real <c>stopped</c> event (breakpoint hit, step completed, pause,
    /// exception, ...).</summary>
    event Action<StoppedInfo>? Stopped;

    /// <summary>Raised for a real <c>continued</c> event.</summary>
    event Action? Continued;

    /// <summary>Raised for a real <c>output</c> event: (category, text).</summary>
    event Action<string, string>? OutputReceived;

    /// <summary>Raised once for a real <c>terminated</c>/<c>exited</c> event, or if the debugger
    /// process itself exits unexpectedly.</summary>
    event Action<DebugResult>? Terminated;

    Task<IReadOnlyList<BreakpointVerification>> SetBreakpointsAsync(string sourcePath, IReadOnlyList<Breakpoint> breakpoints, CancellationToken cancellationToken = default);
    Task ConfigurationDoneAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StackFrameInfo>> GetStackTraceAsync(int threadId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Scope>> GetScopesAsync(int frameId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Variable>> GetVariablesAsync(int variablesReference, CancellationToken cancellationToken = default);

    Task ContinueAsync(int threadId, CancellationToken cancellationToken = default);
    Task PauseAsync(int threadId, CancellationToken cancellationToken = default);
    Task StepOverAsync(int threadId, CancellationToken cancellationToken = default);
    Task StepIntoAsync(int threadId, CancellationToken cancellationToken = default);
    Task StepOutAsync(int threadId, CancellationToken cancellationToken = default);

    /// <summary><paramref name="terminateDebuggee"/> distinguishes DAP <c>disconnect</c>
    /// (leave the debuggee running) from a disconnect that also asks the adapter to terminate it
    /// (SKILL.md §37) — the caller decides which the user asked for.</summary>
    Task DisconnectAsync(bool terminateDebuggee, CancellationToken cancellationToken = default);
}
