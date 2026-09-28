using DevStudio.Core.Build;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Run;
using DevStudio.Core.Workspace;
using Xunit;

namespace DevStudio.Core.Tests;

file sealed class FakeRunningApplication : IRunningApplication
{
    private readonly TaskCompletionSource<RunResult> _exitSource = new();
    private readonly RunConfiguration _configuration;

    public bool StopCalled { get; private set; }
    public bool HasExited { get; private set; }

    public FakeRunningApplication(RunConfiguration configuration) => _configuration = configuration;

    public void CompleteWith(RunStatus status, int? exitCode)
    {
        HasExited = true;
        _exitSource.TrySetResult(new RunResult(status, exitCode, TimeSpan.Zero, _configuration, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    public void Stop()
    {
        StopCalled = true;
        if (!HasExited) CompleteWith(RunStatus.Terminated, null);
    }

    public Task<RunResult> WaitForExitAsync(CancellationToken cancellationToken = default) => _exitSource.Task;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

file sealed class FakeRunAdapter : IRunAdapter
{
    private readonly ProjectType _supportedType;
    public List<RunConfiguration> StartedConfigurations { get; } = new();
    public List<FakeRunningApplication> CreatedApplications { get; } = new();
    public bool ThrowOnStart { get; set; }
    public bool AutoExitImmediately { get; set; } = true;

    public FakeRunAdapter(ProjectType supportedType = ProjectType.DotNet) => _supportedType = supportedType;

    public bool SupportsProjectType(ProjectType projectType) => projectType == _supportedType;

    public Task<IRunningApplication> StartAsync(RunConfiguration configuration, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        StartedConfigurations.Add(configuration);
        if (ThrowOnStart) throw new InvalidOperationException("simulated adapter failure");

        var application = new FakeRunningApplication(configuration);
        CreatedApplications.Add(application);
        if (AutoExitImmediately) application.CompleteWith(RunStatus.Exited, 0);
        return Task.FromResult<IRunningApplication>(application);
    }
}

public class RunServiceTests
{
    private static BuildTarget MakeTarget(ProjectType type = ProjectType.DotNet) =>
        new(BuildTargetKind.Project, "App", "/repo/App.csproj", "/repo", type);

    private static RunConfiguration MakeConfiguration(bool buildBeforeRun = true, ProjectType type = ProjectType.DotNet) =>
        new("App", MakeTarget(type), BuildConfiguration.Debug, BuildBeforeRun: buildBeforeRun);

    private static BuildService MakeBuildService(BuildStatus status = BuildStatus.Succeeded)
    {
        var adapter = new StubBuildAdapter(status);
        return new BuildService(new[] { adapter });
    }

    private sealed class StubBuildAdapter : IBuildAdapter
    {
        private readonly BuildStatus _status;
        public StubBuildAdapter(BuildStatus status) => _status = status;
        public bool SupportsProjectType(ProjectType projectType) => true;
        public Task<BuildResult> ExecuteAsync(BuildRequest request, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BuildResult(_status, _status == BuildStatus.Succeeded ? 0 : 1, TimeSpan.Zero, request.Target, request.Operation, Array.Empty<Core.Diagnostics.Diagnostic>(), string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Start_builds_first_then_launches_the_adapter_when_BuildBeforeRun_is_true()
    {
        var adapter = new FakeRunAdapter();
        var service = new RunService(new[] { adapter }, MakeBuildService(BuildStatus.Succeeded));

        await service.StartAsync(MakeConfiguration(buildBeforeRun: true));

        Assert.Single(adapter.StartedConfigurations);
        Assert.Equal(RunStatus.Exited, service.Status);
    }

    [Fact]
    public async Task A_failed_build_blocks_the_run_and_never_launches_the_adapter()
    {
        var adapter = new FakeRunAdapter();
        var service = new RunService(new[] { adapter }, MakeBuildService(BuildStatus.Failed));

        await service.StartAsync(MakeConfiguration(buildBeforeRun: true));

        Assert.Empty(adapter.StartedConfigurations);
        Assert.Equal(RunStatus.FailedToStart, service.Status);
        Assert.Equal("Run aborted because build failed.", service.LastResult?.Message);
    }

    [Fact]
    public async Task BuildBeforeRun_false_skips_the_build_and_launches_directly()
    {
        var buildService = MakeBuildService(BuildStatus.Failed); // would fail if ever invoked
        var adapter = new FakeRunAdapter();
        var service = new RunService(new[] { adapter }, buildService);

        await service.StartAsync(MakeConfiguration(buildBeforeRun: false));

        Assert.Single(adapter.StartedConfigurations);
        Assert.Equal(RunStatus.Exited, service.Status);
    }

    [Fact]
    public async Task No_adapter_for_the_project_type_reports_FailedToStart_without_building()
    {
        var buildBuiltAny = false;
        var buildService = new BuildService(new[] { new RecordingBuildAdapter(() => buildBuiltAny = true) });
        var adapter = new FakeRunAdapter(ProjectType.DotNet);
        var service = new RunService(new[] { adapter }, buildService);

        await service.StartAsync(MakeConfiguration(type: ProjectType.Rust));

        Assert.Equal(RunStatus.FailedToStart, service.Status);
        Assert.False(buildBuiltAny);
        Assert.Empty(adapter.StartedConfigurations);
    }

    private sealed class RecordingBuildAdapter : IBuildAdapter
    {
        private readonly Action _onExecute;
        public RecordingBuildAdapter(Action onExecute) => _onExecute = onExecute;
        public bool SupportsProjectType(ProjectType projectType) => true;
        public Task<BuildResult> ExecuteAsync(BuildRequest request, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
        {
            _onExecute();
            return Task.FromResult(new BuildResult(BuildStatus.Succeeded, 0, TimeSpan.Zero, request.Target, request.Operation, Array.Empty<Core.Diagnostics.Diagnostic>(), string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
    }

    [Fact]
    public async Task An_adapter_exception_is_reported_as_FailedToStart_not_thrown()
    {
        var adapter = new FakeRunAdapter { ThrowOnStart = true };
        var service = new RunService(new[] { adapter }, MakeBuildService());

        await service.StartAsync(MakeConfiguration());

        Assert.Equal(RunStatus.FailedToStart, service.Status);
        Assert.Equal("simulated adapter failure", service.LastResult?.Message);
    }

    [Fact]
    public async Task Starting_while_already_running_throws_instead_of_launching_a_second_instance()
    {
        var adapter = new FakeRunAdapter { AutoExitImmediately = false };
        var service = new RunService(new[] { adapter }, MakeBuildService());

        await service.StartAsync(MakeConfiguration());
        Assert.Equal(RunStatus.Running, service.Status);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(MakeConfiguration()));
        Assert.Single(adapter.StartedConfigurations);
    }

    [Fact]
    public async Task Stop_terminates_the_running_application_and_reports_Terminated_not_Exited()
    {
        var adapter = new FakeRunAdapter { AutoExitImmediately = false };
        var service = new RunService(new[] { adapter }, MakeBuildService());
        await service.StartAsync(MakeConfiguration());

        service.Stop();
        await WaitUntilAsync(() => service.Status is RunStatus.Terminated);

        Assert.True(adapter.CreatedApplications[0].StopCalled);
        Assert.Equal(RunStatus.Terminated, service.Status);
    }

    [Fact]
    public async Task Restart_stops_the_current_application_before_starting_a_new_one_never_two_at_once()
    {
        var adapter = new FakeRunAdapter { AutoExitImmediately = false };
        var service = new RunService(new[] { adapter }, MakeBuildService());
        await service.StartAsync(MakeConfiguration());
        var firstApplication = adapter.CreatedApplications[0];

        await service.RestartAsync(MakeConfiguration());

        Assert.True(firstApplication.StopCalled);
        Assert.Equal(2, adapter.StartedConfigurations.Count);
        Assert.Equal(RunStatus.Running, service.Status);
    }

    [Fact]
    public void HasAdapterFor_reflects_registered_adapters()
    {
        var adapter = new FakeRunAdapter(ProjectType.DotNet);
        var service = new RunService(new[] { adapter }, MakeBuildService());

        Assert.True(service.HasAdapterFor(ProjectType.DotNet));
        Assert.False(service.HasAdapterFor(ProjectType.Rust));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(condition(), "Condition was not met within the timeout.");
    }
}
