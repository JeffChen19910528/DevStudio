using DevStudio.Core.Build;
using DevStudio.Core.Debug;
using DevStudio.Core.Diagnostics;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Run;
using DevStudio.Core.Workspace;
using Xunit;

namespace DevStudio.Core.Tests;

file sealed class FakeActiveDebugSession : IActiveDebugSession
{
    public bool StoppedCalledWithTerminate { get; private set; }
    public List<(string Source, IReadOnlyList<Breakpoint> Breakpoints)> BreakpointCalls { get; } = new();
    public bool ConfigurationDoneCalled { get; private set; }
    public int? ContinuedThreadId { get; private set; }
    public int? StepOverThreadId { get; private set; }

    public int? ProcessId => 1234;
    public event Action<StoppedInfo>? Stopped;
#pragma warning disable CS0067 // required by IActiveDebugSession; not exercised by these tests
    public event Action? Continued;
#pragma warning restore CS0067
    public event Action<string, string>? OutputReceived;
    public event Action<DebugResult>? Terminated;

    public void RaiseStopped(StoppedInfo info) => Stopped?.Invoke(info);
    public void RaiseOutput(string category, string text) => OutputReceived?.Invoke(category, text);
    public void RaiseTerminated(DebugResult result) => Terminated?.Invoke(result);

    public Task<IReadOnlyList<BreakpointVerification>> SetBreakpointsAsync(string sourcePath, IReadOnlyList<Breakpoint> breakpoints, CancellationToken cancellationToken = default)
    {
        BreakpointCalls.Add((sourcePath, breakpoints));
        return Task.FromResult<IReadOnlyList<BreakpointVerification>>(breakpoints.Select(b => new BreakpointVerification(b.Id, true, null, b.Line, b.Column)).ToList());
    }

    public Task ConfigurationDoneAsync(CancellationToken cancellationToken = default)
    {
        ConfigurationDoneCalled = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ThreadInfo>>(new List<ThreadInfo> { new(1, "Main Thread") });

    public Task<IReadOnlyList<StackFrameInfo>> GetStackTraceAsync(int threadId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StackFrameInfo>>(new List<StackFrameInfo> { new(1, "Main", "/repo/Program.cs", 3, 1) });

    public Task<IReadOnlyList<Scope>> GetScopesAsync(int frameId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Scope>>(new List<Scope> { new("Locals", 100, false) });

    public Task<IReadOnlyList<Variable>> GetVariablesAsync(int variablesReference, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Variable>>(new List<Variable> { new("count", "10", "int", 0) });

    public Task ContinueAsync(int threadId, CancellationToken cancellationToken = default) { ContinuedThreadId = threadId; return Task.CompletedTask; }
    public Task PauseAsync(int threadId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StepOverAsync(int threadId, CancellationToken cancellationToken = default) { StepOverThreadId = threadId; return Task.CompletedTask; }
    public Task StepIntoAsync(int threadId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StepOutAsync(int threadId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DisconnectAsync(bool terminateDebuggee, CancellationToken cancellationToken = default)
    {
        StoppedCalledWithTerminate = terminateDebuggee;
        RaiseTerminated(new DebugResult(DebugSessionState.Terminated, null!, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

file sealed class FakeDebuggerAdapter : IDebuggerAdapter
{
    private readonly ProjectType _supportedType;
    public List<DebugConfiguration> StartedConfigurations { get; } = new();
    public List<FakeActiveDebugSession> CreatedSessions { get; } = new();
    public bool ThrowOnStart { get; set; }

    public FakeDebuggerAdapter(ProjectType supportedType = ProjectType.DotNet) => _supportedType = supportedType;

    public bool SupportsProjectType(ProjectType projectType) => projectType == _supportedType;

    public Task<IActiveDebugSession> StartAsync(DebugConfiguration configuration, CancellationToken cancellationToken = default)
    {
        StartedConfigurations.Add(configuration);
        if (ThrowOnStart) throw new InvalidOperationException("simulated debugger failure");

        var session = new FakeActiveDebugSession();
        CreatedSessions.Add(session);
        return Task.FromResult<IActiveDebugSession>(session);
    }
}

public class DebugServiceTests
{
    private static BuildTarget MakeTarget(ProjectType type = ProjectType.DotNet) =>
        new(BuildTargetKind.Project, "App", "/repo/App.csproj", "/repo", type);

    private static DebugConfiguration MakeConfiguration(bool buildBeforeDebug = true, ProjectType type = ProjectType.DotNet) =>
        new(new RunConfiguration("App", MakeTarget(type), BuildConfiguration.Debug), BuildBeforeDebug: buildBeforeDebug);

    private sealed class StubBuildAdapter : IBuildAdapter
    {
        private readonly BuildStatus _status;
        public StubBuildAdapter(BuildStatus status) => _status = status;
        public bool SupportsProjectType(ProjectType projectType) => true;
        public Task<BuildResult> ExecuteAsync(BuildRequest request, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BuildResult(_status, _status == BuildStatus.Succeeded ? 0 : 1, TimeSpan.Zero, request.Target, request.Operation, Array.Empty<Diagnostic>(), string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    private static BuildService MakeBuildService(BuildStatus status = BuildStatus.Succeeded) =>
        new(new[] { new StubBuildAdapter(status) });

    [Fact]
    public async Task Start_builds_first_then_launches_the_adapter_and_sends_configurationDone()
    {
        var adapter = new FakeDebuggerAdapter();
        var service = new DebugService(new[] { adapter }, MakeBuildService(BuildStatus.Succeeded));

        await service.StartAsync(MakeConfiguration());

        Assert.Single(adapter.StartedConfigurations);
        Assert.True(adapter.CreatedSessions[0].ConfigurationDoneCalled);
        Assert.Equal(DebugSessionState.Running, service.State);
    }

    [Fact]
    public async Task A_failed_build_blocks_debug_and_never_launches_the_adapter()
    {
        var adapter = new FakeDebuggerAdapter();
        var service = new DebugService(new[] { adapter }, MakeBuildService(BuildStatus.Failed));

        await service.StartAsync(MakeConfiguration());

        Assert.Empty(adapter.StartedConfigurations);
        Assert.Equal(DebugSessionState.Failed, service.State);
        Assert.Equal("Debug aborted because build failed.", service.LastResult?.Message);
    }

    [Fact]
    public async Task No_adapter_for_the_project_type_reports_Failed_without_building()
    {
        var adapter = new FakeDebuggerAdapter(ProjectType.DotNet);
        var service = new DebugService(new[] { adapter }, MakeBuildService());

        await service.StartAsync(MakeConfiguration(type: ProjectType.Rust));

        Assert.Equal(DebugSessionState.Failed, service.State);
        Assert.Empty(adapter.StartedConfigurations);
    }

    [Fact]
    public async Task An_adapter_exception_is_reported_as_Failed_not_thrown()
    {
        var adapter = new FakeDebuggerAdapter { ThrowOnStart = true };
        var service = new DebugService(new[] { adapter }, MakeBuildService());

        await service.StartAsync(MakeConfiguration());

        Assert.Equal(DebugSessionState.Failed, service.State);
        Assert.Equal("simulated debugger failure", service.LastResult?.Message);
    }

    [Fact]
    public async Task Starting_while_already_active_throws_instead_of_launching_a_second_session()
    {
        var adapter = new FakeDebuggerAdapter();
        var service = new DebugService(new[] { adapter }, MakeBuildService());
        await service.StartAsync(MakeConfiguration());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(MakeConfiguration()));
        Assert.Single(adapter.StartedConfigurations);
    }

    [Fact]
    public async Task Breakpoints_set_before_starting_are_sent_to_the_session_on_start()
    {
        var adapter = new FakeDebuggerAdapter();
        var service = new DebugService(new[] { adapter }, MakeBuildService());
        var breakpoint = new Breakpoint(Guid.NewGuid(), "/repo/Program.cs", 3);

        await service.SetBreakpointsAsync("/repo/Program.cs", new[] { breakpoint });
        await service.StartAsync(MakeConfiguration());

        var session = adapter.CreatedSessions[0];
        Assert.Single(session.BreakpointCalls);
        Assert.Equal("/repo/Program.cs", session.BreakpointCalls[0].Source);
    }

    [Fact]
    public async Task A_stopped_event_transitions_to_Paused_and_records_the_current_thread()
    {
        var adapter = new FakeDebuggerAdapter();
        var service = new DebugService(new[] { adapter }, MakeBuildService());
        await service.StartAsync(MakeConfiguration());
        StoppedInfo? received = null;
        service.Stopped += (_, info) => received = info;

        adapter.CreatedSessions[0].RaiseStopped(new StoppedInfo("breakpoint", 7, true, null));

        Assert.Equal(DebugSessionState.Paused, service.State);
        Assert.Equal(7, service.CurrentThreadId);
        Assert.Equal("breakpoint", received?.Reason);
    }

    [Fact]
    public async Task ContinueAsync_forwards_the_current_thread_id_to_the_session()
    {
        var adapter = new FakeDebuggerAdapter();
        var service = new DebugService(new[] { adapter }, MakeBuildService());
        await service.StartAsync(MakeConfiguration());
        adapter.CreatedSessions[0].RaiseStopped(new StoppedInfo("breakpoint", 7, true, null));

        await service.ContinueAsync();

        Assert.Equal(7, adapter.CreatedSessions[0].ContinuedThreadId);
    }

    [Fact]
    public async Task StepOverAsync_forwards_the_current_thread_id_to_the_session()
    {
        var adapter = new FakeDebuggerAdapter();
        var service = new DebugService(new[] { adapter }, MakeBuildService());
        await service.StartAsync(MakeConfiguration());
        adapter.CreatedSessions[0].RaiseStopped(new StoppedInfo("breakpoint", 3, true, null));

        await service.StepOverAsync();

        Assert.Equal(3, adapter.CreatedSessions[0].StepOverThreadId);
    }

    [Fact]
    public async Task StopAsync_disconnects_the_session_and_reaches_Terminated()
    {
        var adapter = new FakeDebuggerAdapter();
        var service = new DebugService(new[] { adapter }, MakeBuildService());
        await service.StartAsync(MakeConfiguration());

        await service.StopAsync();

        Assert.True(adapter.CreatedSessions[0].StoppedCalledWithTerminate);
        Assert.Equal(DebugSessionState.Terminated, service.State);
    }

    [Fact]
    public async Task HasAdapterFor_reflects_registered_adapters()
    {
        var adapter = new FakeDebuggerAdapter(ProjectType.DotNet);
        var service = new DebugService(new[] { adapter }, MakeBuildService());

        Assert.True(service.HasAdapterFor(ProjectType.DotNet));
        Assert.False(service.HasAdapterFor(ProjectType.Rust));
    }
}
