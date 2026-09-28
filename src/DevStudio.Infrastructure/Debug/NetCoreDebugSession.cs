using System.Text.Json.Nodes;
using DevStudio.Core.Dap;
using DevStudio.Core.Debug;
using DevStudio.Core.Processes;

namespace DevStudio.Infrastructure.Debug;

/// <summary>
/// One real DAP session against a launched <c>netcoredbg</c> process (SKILL.md §18–§21, §33–§37).
/// Owns the DAP handshake ordering that was verified against the real adapter, not assumed
/// (SKILL.md §18): <c>initialize</c> → (send, don't await) <c>launch</c> → wait for the real
/// <c>initialized</c> event → caller sends <c>setBreakpoints</c> → <c>configurationDone</c> →
/// only then confirm the pending <c>launch</c> response actually succeeded. This ordering is
/// required because netcoredbg does not complete its <c>launch</c> response until after
/// <c>configurationDone</c> — awaiting it any earlier would deadlock.
/// </summary>
internal sealed class NetCoreDebugSession : IActiveDebugSession
{
    private readonly DapClient _client;
    private readonly IRunningProcess _process;
    private readonly TaskCompletionSource _initializedEvent = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task<DapResponse>? _launchResponseTask;
    private bool _terminatedFired;
    private int? _capturedExitCode;

    public NetCoreDebugSession(DapClient client, IRunningProcess process)
    {
        _client = client;
        _process = process;
        _client.EventReceived += OnEvent;
        _client.Faulted += OnFaulted;
    }

    public int? ProcessId => _process.ProcessId;

    public event Action<StoppedInfo>? Stopped;
    public event Action? Continued;
    public event Action<string, string>? OutputReceived;
    public event Action<DebugResult>? Terminated;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _client.Start(cancellationToken);
        var arguments = new JsonObject
        {
            ["clientID"] = "devstudio",
            ["adapterID"] = "coreclr",
            ["pathFormat"] = "path",
            ["linesStartAt1"] = true,
            ["columnsStartAt1"] = true,
            ["supportsVariableType"] = true,
            ["supportsRunInTerminalRequest"] = false,
        };
        var response = await _client.SendRequestAsync("initialize", arguments, cancellationToken).ConfigureAwait(false);
        RequireSuccess(response, "initialize");
    }

    /// <summary>Sends <c>launch</c> without awaiting its response — see the type-level remark
    /// for why. The response is confirmed later, in <see cref="ConfigurationDoneAsync"/>.</summary>
    public void BeginLaunch(DebugConfiguration configuration, string programPath, string workingDirectory)
    {
        var runConfig = configuration.RunConfiguration;
        var launchArgs = new JsonObject
        {
            ["name"] = runConfig.Name,
            ["type"] = "coreclr",
            ["request"] = "launch",
            ["program"] = programPath,
            ["cwd"] = workingDirectory,
            ["stopAtEntry"] = configuration.StopAtEntry,
            ["justMyCode"] = configuration.JustMyCode,
            ["args"] = new JsonArray(runConfig.Arguments.Select(a => (JsonNode)JsonValue.Create(a)).ToArray()),
        };
        if (runConfig.EnvironmentVariables is { Count: > 0 } env)
        {
            var envObject = new JsonObject();
            foreach (var (key, value) in env) envObject[key] = value;
            launchArgs["env"] = envObject;
        }

        _launchResponseTask = _client.SendRequestAsync("launch", launchArgs, CancellationToken.None);
    }

    public Task WaitForInitializedEventAsync(CancellationToken cancellationToken) =>
        _initializedEvent.Task.WaitAsync(cancellationToken);

    public async Task<IReadOnlyList<BreakpointVerification>> SetBreakpointsAsync(string sourcePath, IReadOnlyList<Breakpoint> breakpoints, CancellationToken cancellationToken = default)
    {
        var enabled = breakpoints.Where(b => b.Enabled).ToList();
        var arguments = new JsonObject
        {
            ["source"] = new JsonObject { ["path"] = sourcePath },
            ["breakpoints"] = new JsonArray(enabled.Select(b =>
            {
                JsonObject bp = new() { ["line"] = b.Line };
                if (b.Column is { } column) bp["column"] = column;
                return (JsonNode)bp;
            }).ToArray()),
        };

        var response = await _client.SendRequestAsync("setBreakpoints", arguments, cancellationToken).ConfigureAwait(false);
        var disabledVerifications = breakpoints.Where(b => !b.Enabled)
            .Select(b => new BreakpointVerification(b.Id, false, "Disabled.", null, null));

        if (!response.Success)
        {
            return enabled.Select(b => new BreakpointVerification(b.Id, false, response.Message, null, null))
                .Concat(disabledVerifications).ToList();
        }

        var reportedBreakpoints = (response.Body as JsonObject)?["breakpoints"] as JsonArray;
        var verifications = new List<BreakpointVerification>();
        for (var i = 0; i < enabled.Count; i++)
        {
            var reported = reportedBreakpoints is not null && i < reportedBreakpoints.Count ? reportedBreakpoints[i] as JsonObject : null;
            verifications.Add(new BreakpointVerification(
                enabled[i].Id,
                reported?["verified"]?.GetValue<bool>() ?? false,
                reported?["message"]?.GetValue<string>(),
                TryGetInt(reported, "line"),
                TryGetInt(reported, "column")));
        }
        verifications.AddRange(disabledVerifications);
        return verifications;
    }

    public async Task ConfigurationDoneAsync(CancellationToken cancellationToken = default)
    {
        var response = await _client.SendRequestAsync("configurationDone", null, cancellationToken).ConfigureAwait(false);
        RequireSuccess(response, "configurationDone");

        if (_launchResponseTask is { } launchTask)
        {
            var launchResponse = await launchTask.ConfigureAwait(false);
            RequireSuccess(launchResponse, "launch");
        }
    }

    public async Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _client.SendRequestAsync("threads", null, cancellationToken).ConfigureAwait(false);
        RequireSuccess(response, "threads");
        var threads = (response.Body as JsonObject)?["threads"] as JsonArray;
        return threads?.Select(t => new ThreadInfo(
            (t as JsonObject)?["id"]?.GetValue<int>() ?? 0,
            (t as JsonObject)?["name"]?.GetValue<string>() ?? string.Empty)).ToList()
            ?? new List<ThreadInfo>();
    }

    public async Task<IReadOnlyList<StackFrameInfo>> GetStackTraceAsync(int threadId, CancellationToken cancellationToken = default)
    {
        var response = await _client.SendRequestAsync("stackTrace", new JsonObject { ["threadId"] = threadId }, cancellationToken).ConfigureAwait(false);
        RequireSuccess(response, "stackTrace");
        var frames = (response.Body as JsonObject)?["stackFrames"] as JsonArray;
        return frames?.Select(f =>
        {
            var frame = f as JsonObject;
            var source = frame?["source"] as JsonObject;
            return new StackFrameInfo(
                frame?["id"]?.GetValue<int>() ?? 0,
                frame?["name"]?.GetValue<string>() ?? string.Empty,
                source?["path"]?.GetValue<string>(),
                frame?["line"]?.GetValue<int>() ?? 0,
                frame?["column"]?.GetValue<int>() ?? 0);
        }).ToList() ?? new List<StackFrameInfo>();
    }

    public async Task<IReadOnlyList<Scope>> GetScopesAsync(int frameId, CancellationToken cancellationToken = default)
    {
        var response = await _client.SendRequestAsync("scopes", new JsonObject { ["frameId"] = frameId }, cancellationToken).ConfigureAwait(false);
        RequireSuccess(response, "scopes");
        var scopes = (response.Body as JsonObject)?["scopes"] as JsonArray;
        return scopes?.Select(s =>
        {
            var scope = s as JsonObject;
            return new Scope(
                scope?["name"]?.GetValue<string>() ?? string.Empty,
                scope?["variablesReference"]?.GetValue<int>() ?? 0,
                scope?["expensive"]?.GetValue<bool>() ?? false);
        }).ToList() ?? new List<Scope>();
    }

    public async Task<IReadOnlyList<Variable>> GetVariablesAsync(int variablesReference, CancellationToken cancellationToken = default)
    {
        var response = await _client.SendRequestAsync("variables", new JsonObject { ["variablesReference"] = variablesReference }, cancellationToken).ConfigureAwait(false);
        RequireSuccess(response, "variables");
        var variables = (response.Body as JsonObject)?["variables"] as JsonArray;
        return variables?.Select(v =>
        {
            var variable = v as JsonObject;
            return new Variable(
                variable?["name"]?.GetValue<string>() ?? string.Empty,
                variable?["value"]?.GetValue<string>() ?? string.Empty,
                variable?["type"]?.GetValue<string>(),
                variable?["variablesReference"]?.GetValue<int>() ?? 0);
        }).ToList() ?? new List<Variable>();
    }

    public Task ContinueAsync(int threadId, CancellationToken cancellationToken = default) => SendThreadCommandAsync("continue", threadId, cancellationToken);
    public Task PauseAsync(int threadId, CancellationToken cancellationToken = default) => SendThreadCommandAsync("pause", threadId, cancellationToken);
    public Task StepOverAsync(int threadId, CancellationToken cancellationToken = default) => SendThreadCommandAsync("next", threadId, cancellationToken);
    public Task StepIntoAsync(int threadId, CancellationToken cancellationToken = default) => SendThreadCommandAsync("stepIn", threadId, cancellationToken);
    public Task StepOutAsync(int threadId, CancellationToken cancellationToken = default) => SendThreadCommandAsync("stepOut", threadId, cancellationToken);

    private async Task SendThreadCommandAsync(string command, int threadId, CancellationToken cancellationToken)
    {
        var response = await _client.SendRequestAsync(command, new JsonObject { ["threadId"] = threadId }, cancellationToken).ConfigureAwait(false);
        RequireSuccess(response, command);
    }

    public async Task DisconnectAsync(bool terminateDebuggee, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.SendRequestAsync("disconnect", new JsonObject { ["terminateDebuggee"] = terminateDebuggee }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort — the adapter or debuggee may already be gone.
        }
        finally
        {
            // Phase 6 is launch-only (Attach deferred — see ADR-007): netcoredbg is always the
            // parent of the debuggee it launched, so killing netcoredbg's entire process tree also
            // ends the debuggee regardless of `terminateDebuggee`. A true "leave it running"
            // detach is not reachable until a real Attach mode exists.
            FireTerminated(DebugSessionState.Terminated, null);
            await _client.DisposeAsync().ConfigureAwait(false);
            _process.Kill();
            await _process.DisposeAsync().ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync() => _terminatedFired ? ValueTask.CompletedTask : new ValueTask(DisconnectAsync(true));

    private void OnEvent(DapEvent evt)
    {
        var body = evt.Body as JsonObject;
        switch (evt.EventName)
        {
            case "initialized":
                _initializedEvent.TrySetResult();
                break;
            case "stopped":
                Stopped?.Invoke(new StoppedInfo(
                    body?["reason"]?.GetValue<string>() ?? "unknown",
                    TryGetInt(body, "threadId"),
                    body?["allThreadsStopped"]?.GetValue<bool>() ?? false,
                    body?["description"]?.GetValue<string>() ?? body?["text"]?.GetValue<string>()));
                break;
            case "continued":
                Continued?.Invoke();
                break;
            case "output":
                OutputReceived?.Invoke(body?["category"]?.GetValue<string>() ?? "console", body?["output"]?.GetValue<string>() ?? string.Empty);
                break;
            case "exited":
                // DAP's `exited` (carries the real exit code) and `terminated` (session-end
                // signal) are two separate events, and this real netcoredbg sends `terminated`
                // first — so exit code capture cannot be tied to whichever event happens to
                // finalize the session; it is captured here and consumed whenever the session
                // actually finalizes (see the `terminated` case).
                _capturedExitCode = TryGetInt(body, "exitCode");
                break;
            case "terminated":
                // Escapes to a separate task rather than awaiting inline: this handler runs
                // synchronously inside DapClient's own read loop, and cleanup needs to dispose
                // that same client (which awaits the read loop task) — awaiting it here would
                // be the loop awaiting itself. Real orphan-file-handle bug caught by
                // NetCoreDebugIntegrationTests' own temp-directory cleanup racing this exact path.
                _ = Task.Run(() => CleanupAfterAdapterTerminationAsync(DebugSessionState.Terminated, _capturedExitCode));
                break;
        }
    }

    /// <summary>Real cleanup for a termination the *adapter* initiated (debuggee exited on its
    /// own) — as opposed to <see cref="DisconnectAsync"/>, which is the debuggee-termination
    /// path DevStudio itself initiated. Both end up killing/disposing the same real netcoredbg
    /// process; this one just isn't triggered by an explicit Stop.</summary>
    private async Task CleanupAfterAdapterTerminationAsync(DebugSessionState state, int? exitCode)
    {
        if (_terminatedFired) return;
        _terminatedFired = true;
        try { await _client.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
        try { _process.Kill(); } catch { /* best-effort */ }
        try { await _process.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
        Terminated?.Invoke(new DebugResult(state, null!, DateTimeOffset.MinValue, DateTimeOffset.UtcNow, exitCode));
    }

    private void OnFaulted(Exception exception) => FireTerminated(DebugSessionState.Failed, null);

    private void FireTerminated(DebugSessionState state, int? exitCode)
    {
        if (_terminatedFired) return;
        _terminatedFired = true;
        // Configuration/StartedAt are filled in by DebugService, which is the layer that knows
        // them; this session only knows the outcome and (optionally) the real exit code.
        Terminated?.Invoke(new DebugResult(state, null!, DateTimeOffset.MinValue, DateTimeOffset.UtcNow, exitCode));
    }

    private static void RequireSuccess(DapResponse response, string command)
    {
        if (!response.Success)
        {
            throw new InvalidOperationException($"netcoredbg '{command}' request failed: {response.Message ?? "(no message)"}");
        }
    }

    private static int? TryGetInt(JsonObject? obj, string property)
    {
        var value = obj?[property];
        if (value is null) return null;
        try { return value.GetValue<int>(); }
        catch (Exception) { return null; }
    }
}
