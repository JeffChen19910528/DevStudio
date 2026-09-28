using DevStudio.Core.Build;
using DevStudio.Core.Platform;

namespace DevStudio.Core.Debug;

/// <summary>
/// Orchestrates one real debug session (SKILL.md §11): picks the right <see
/// cref="IDebuggerAdapter"/>, reuses <see cref="Build.BuildService"/> for build-before-debug
/// (never calls <c>dotnet build</c> directly — same rule as <see cref="Run.RunService"/>),
/// enforces one active session at a time, and converts the adapter's raw DAP-driven events into
/// normalized state (<see cref="DebugSessionState"/>, current thread/frame, breakpoints) that a
/// ViewModel can bind to without ever seeing a DAP message. Workspace Trust is enforced by the
/// ViewModel before calling this, exactly like <see cref="Build.BuildService"/>/<see
/// cref="Run.RunService"/> — Core has no UI/dialog dependency to check it here.
/// </summary>
public sealed class DebugService
{
    private readonly IReadOnlyList<IDebuggerAdapter> _adapters;
    private readonly BuildService _buildService;
    private readonly Dictionary<string, List<Breakpoint>> _breakpointsBySource = new(PathComparer.Comparer);
    private IActiveDebugSession? _current;

    public DebugService(IEnumerable<IDebuggerAdapter> adapters, BuildService buildService)
    {
        _adapters = adapters.ToList();
        _buildService = buildService;
    }

    public DebugSessionState State { get; private set; } = DebugSessionState.NotStarted;
    public DebugResult? LastResult { get; private set; }
    public int? CurrentThreadId { get; private set; }

    public event EventHandler<DebugSessionState>? StateChanged;
    public event EventHandler<StoppedInfo>? Stopped;
    public event EventHandler? Continued;
    public event EventHandler<(string Category, string Text)>? OutputReceived;

    /// <summary>Fires once when the session ends for any reason — a real DAP
    /// terminated/exited event, a failed start, a failed build, or an unavailable adapter — the
    /// same "final outcome" pattern <see cref="Run.RunService.Completed"/> uses.</summary>
    public event EventHandler<DebugResult>? Completed;

    public bool HasAdapterFor(Projects.ProjectType projectType) => _adapters.Any(a => a.SupportsProjectType(projectType));

    public bool IsActive => State is DebugSessionState.Starting or DebugSessionState.Running or DebugSessionState.Paused or DebugSessionState.Stopping;

    /// <summary>Breakpoints DevStudio wants set for a source file. Stored regardless of whether
    /// a session is active (SKILL.md §22 — in-memory only, never persisted this phase) and
    /// (re)sent to the live session immediately if one exists.</summary>
    public async Task<IReadOnlyList<BreakpointVerification>> SetBreakpointsAsync(string sourcePath, IReadOnlyList<Breakpoint> breakpoints, CancellationToken cancellationToken = default)
    {
        _breakpointsBySource[sourcePath] = breakpoints.ToList();
        if (_current is null) return breakpoints.Select(b => new BreakpointVerification(b.Id, Verified: false, Message: "No active debug session.", Line: null, Column: null)).ToList();
        return await _current.SetBreakpointsAsync(sourcePath, breakpoints, cancellationToken).ConfigureAwait(false);
    }

    public async Task StartAsync(DebugConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (IsActive)
        {
            throw new InvalidOperationException("A debug session is already active. Stop it before starting another.");
        }

        SetState(DebugSessionState.Starting);
        var startedAt = DateTimeOffset.UtcNow;
        var target = configuration.RunConfiguration.Target;

        var adapter = _adapters.FirstOrDefault(a => a.SupportsProjectType(target.ProjectType));
        if (adapter is null)
        {
            Complete(new DebugResult(DebugSessionState.Failed, configuration, startedAt, DateTimeOffset.UtcNow, Message: $"No debugger adapter is available for project type '{target.ProjectType}'."));
            return;
        }

        if (configuration.BuildBeforeDebug)
        {
            var buildResult = await _buildService.ExecuteAsync(target, configuration.RunConfiguration.BuildConfiguration, BuildOperation.Build, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (buildResult.Status != BuildStatus.Succeeded)
            {
                Complete(new DebugResult(DebugSessionState.Failed, configuration, startedAt, DateTimeOffset.UtcNow, Message: "Debug aborted because build failed."));
                return;
            }
        }

        IActiveDebugSession session;
        try
        {
            session = await adapter.StartAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Complete(new DebugResult(DebugSessionState.Failed, configuration, startedAt, DateTimeOffset.UtcNow, Message: ex.Message));
            return;
        }

        _current = session;
        session.Stopped += OnStopped;
        session.Continued += OnContinued;
        session.OutputReceived += OnOutput;
        session.Terminated += result => OnTerminated(result, configuration, startedAt);

        foreach (var (sourcePath, breakpoints) in _breakpointsBySource)
        {
            await session.SetBreakpointsAsync(sourcePath, breakpoints, cancellationToken).ConfigureAwait(false);
        }
        await session.ConfigurationDoneAsync(cancellationToken).ConfigureAwait(false);

        // Not StopAtEntry: DAP's own initial `stopped` event (if any, e.g. entry stop) already
        // drives Paused via OnStopped; otherwise the debuggee is simply running now.
        if (State == DebugSessionState.Starting) SetState(DebugSessionState.Running);
    }

    public Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync(CancellationToken cancellationToken = default) =>
        RequireSession().GetThreadsAsync(cancellationToken);

    public Task<IReadOnlyList<StackFrameInfo>> GetStackTraceAsync(int threadId, CancellationToken cancellationToken = default) =>
        RequireSession().GetStackTraceAsync(threadId, cancellationToken);

    public Task<IReadOnlyList<Scope>> GetScopesAsync(int frameId, CancellationToken cancellationToken = default) =>
        RequireSession().GetScopesAsync(frameId, cancellationToken);

    public Task<IReadOnlyList<Variable>> GetVariablesAsync(int variablesReference, CancellationToken cancellationToken = default) =>
        RequireSession().GetVariablesAsync(variablesReference, cancellationToken);

    public async Task ContinueAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentThreadId is not { } threadId) return;
        await RequireSession().ContinueAsync(threadId, cancellationToken).ConfigureAwait(false);
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentThreadId is not { } threadId) return;
        await RequireSession().PauseAsync(threadId, cancellationToken).ConfigureAwait(false);
    }

    public async Task StepOverAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentThreadId is not { } threadId) return;
        await RequireSession().StepOverAsync(threadId, cancellationToken).ConfigureAwait(false);
    }

    public async Task StepIntoAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentThreadId is not { } threadId) return;
        await RequireSession().StepIntoAsync(threadId, cancellationToken).ConfigureAwait(false);
    }

    public async Task StepOutAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentThreadId is not { } threadId) return;
        await RequireSession().StepOutAsync(threadId, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(bool terminateDebuggee = true, CancellationToken cancellationToken = default)
    {
        if (_current is null) return;
        SetState(DebugSessionState.Stopping);
        await _current.DisconnectAsync(terminateDebuggee, cancellationToken).ConfigureAwait(false);
    }

    private IActiveDebugSession RequireSession() =>
        _current ?? throw new InvalidOperationException("No active debug session.");

    private void OnStopped(StoppedInfo info)
    {
        CurrentThreadId = info.ThreadId;
        SetState(DebugSessionState.Paused);
        Stopped?.Invoke(this, info);
    }

    private void OnContinued()
    {
        SetState(DebugSessionState.Running);
        Continued?.Invoke(this, EventArgs.Empty);
    }

    private void OnOutput(string category, string text) => OutputReceived?.Invoke(this, (category, text));

    private void OnTerminated(DebugResult result, DebugConfiguration configuration, DateTimeOffset startedAt)
    {
        _current = null;
        CurrentThreadId = null;
        Complete(result with { Configuration = configuration, StartedAt = startedAt, CompletedAt = result.CompletedAt ?? DateTimeOffset.UtcNow });
    }

    private void Complete(DebugResult result)
    {
        LastResult = result;
        SetState(result.State);
        Completed?.Invoke(this, result);
    }

    private void SetState(DebugSessionState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
