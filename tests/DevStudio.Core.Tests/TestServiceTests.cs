using DevStudio.Core.Build;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Testing;
using DevStudio.Core.Toolchains;
using DevStudio.Core.Workspace;
using Xunit;

namespace DevStudio.Core.Tests;

file sealed class FakeTestAdapter : ITestAdapter
{
    private readonly ProjectType _supportedType;
    public List<ProjectInfo> DiscoverCalls { get; } = new();
    public List<ProjectInfo> RunCalls { get; } = new();
    public List<bool> SkipBuildValuesSeen { get; } = new();
    public IReadOnlyList<TestCase> TestsToReturn { get; set; } = Array.Empty<TestCase>();
    public IReadOnlyList<TestResult> ResultsToReturn { get; set; } = Array.Empty<TestResult>();
    public bool ThrowOnRun { get; set; }
    public bool HangUntilCancelled { get; set; }

    public FakeTestAdapter(ProjectType supportedType = ProjectType.DotNet) => _supportedType = supportedType;

    public bool SupportsProjectType(ProjectType projectType) => projectType == _supportedType;

    public Task<IReadOnlyList<TestCase>> DiscoverTestsAsync(ProjectInfo project, BuildConfiguration configuration, CancellationToken cancellationToken = default)
    {
        DiscoverCalls.Add(project);
        return Task.FromResult(TestsToReturn);
    }

    public async Task<IReadOnlyList<TestResult>> RunTestsAsync(ProjectInfo project, BuildConfiguration configuration, TestFilter? filter, bool skipBuild, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
    {
        RunCalls.Add(project);
        SkipBuildValuesSeen.Add(skipBuild);
        if (ThrowOnRun) throw new InvalidOperationException("simulated test adapter failure");
        if (HangUntilCancelled)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        return ResultsToReturn;
    }
}

public class TestServiceTests
{
    private static ProjectInfo MakeProject(ProjectType type = ProjectType.DotNet) =>
        new("proj-1", "App.Tests", "/repo", type, "/repo/App.Tests.csproj", new[] { "C#" }, Array.Empty<string>(), Array.Empty<ProjectCapability>());

    private static BuildTarget MakeTarget(ProjectInfo project) =>
        new(BuildTargetKind.Project, project.Name, project.ProjectFile!, project.RootPath, project.ProjectType);

    private sealed class StubBuildAdapter : IBuildAdapter
    {
        private readonly BuildStatus _status;
        public StubBuildAdapter(BuildStatus status) => _status = status;
        public bool SupportsProjectType(ProjectType projectType) => true;
        public Task<BuildResult> ExecuteAsync(BuildRequest request, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BuildResult(_status, _status == BuildStatus.Succeeded ? 0 : 1, TimeSpan.Zero, request.Target, request.Operation, Array.Empty<Diagnostics.Diagnostic>(), string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    private static BuildService MakeBuildService(BuildStatus status = BuildStatus.Succeeded) =>
        new(new[] { new StubBuildAdapter(status) });

    [Fact]
    public async Task DiscoverAsync_returns_the_adapters_real_tests_and_raises_TestsDiscovered()
    {
        var adapter = new FakeTestAdapter { TestsToReturn = new[] { new TestCase("t1", "t1", "t1", "proj-1", null, null, Array.Empty<string>(), null) } };
        var service = new TestService(new[] { adapter }, MakeBuildService());
        IReadOnlyList<TestCase>? raised = null;
        service.TestsDiscovered += (_, tests) => raised = tests;

        var tests = await service.DiscoverAsync(MakeProject(), BuildConfiguration.Debug);

        Assert.Single(tests);
        Assert.Same(tests, raised);
        Assert.Same(tests, service.DiscoveredTests);
    }

    [Fact]
    public async Task RunAsync_builds_first_then_runs_when_BuildBeforeTest_is_true()
    {
        var adapter = new FakeTestAdapter();
        var service = new TestService(new[] { adapter }, MakeBuildService(BuildStatus.Succeeded));
        var project = MakeProject();

        var result = await service.RunAsync(MakeTarget(project), project, BuildConfiguration.Debug, buildBeforeTest: true);

        Assert.Single(adapter.RunCalls);
        Assert.True(adapter.SkipBuildValuesSeen[0]); // build already happened — adapter told not to build again
        Assert.Equal(TestRunState.Completed, result.State);
    }

    [Fact]
    public async Task A_failed_build_blocks_the_test_run_and_never_calls_the_adapter()
    {
        var adapter = new FakeTestAdapter();
        var service = new TestService(new[] { adapter }, MakeBuildService(BuildStatus.Failed));
        var project = MakeProject();

        var result = await service.RunAsync(MakeTarget(project), project, BuildConfiguration.Debug, buildBeforeTest: true);

        Assert.Empty(adapter.RunCalls);
        Assert.Equal(TestRunState.BlockedByBuildFailure, result.State);
        Assert.Equal("Test run aborted because build failed.", result.Message);
    }

    [Fact]
    public async Task BuildBeforeTest_false_skips_the_build_and_tells_the_adapter_to_build_itself()
    {
        var buildService = MakeBuildService(BuildStatus.Failed); // would fail the run if ever invoked
        var adapter = new FakeTestAdapter();
        var service = new TestService(new[] { adapter }, buildService);
        var project = MakeProject();

        var result = await service.RunAsync(MakeTarget(project), project, BuildConfiguration.Debug, buildBeforeTest: false);

        Assert.Single(adapter.RunCalls);
        Assert.False(adapter.SkipBuildValuesSeen[0]);
        Assert.Equal(TestRunState.Completed, result.State);
    }

    [Fact]
    public async Task No_adapter_for_the_project_type_reports_Failed_without_building()
    {
        var buildRan = false;
        var buildService = new BuildService(new[] { new RecordingBuildAdapter(() => buildRan = true) });
        var adapter = new FakeTestAdapter(ProjectType.DotNet);
        var service = new TestService(new[] { adapter }, buildService);
        var project = MakeProject(ProjectType.Rust);

        var result = await service.RunAsync(MakeTarget(project), project, BuildConfiguration.Debug);

        Assert.Equal(TestRunState.Failed, result.State);
        Assert.False(buildRan);
        Assert.Empty(adapter.RunCalls);
    }

    private sealed class RecordingBuildAdapter : IBuildAdapter
    {
        private readonly Action _onExecute;
        public RecordingBuildAdapter(Action onExecute) => _onExecute = onExecute;
        public bool SupportsProjectType(ProjectType projectType) => true;
        public Task<BuildResult> ExecuteAsync(BuildRequest request, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default)
        {
            _onExecute();
            return Task.FromResult(new BuildResult(BuildStatus.Succeeded, 0, TimeSpan.Zero, request.Target, request.Operation, Array.Empty<Diagnostics.Diagnostic>(), string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
    }

    [Fact]
    public async Task An_adapter_exception_is_reported_as_Failed_not_thrown()
    {
        var adapter = new FakeTestAdapter { ThrowOnRun = true };
        var service = new TestService(new[] { adapter }, MakeBuildService());
        var project = MakeProject();

        var result = await service.RunAsync(MakeTarget(project), project, BuildConfiguration.Debug);

        Assert.Equal(TestRunState.Failed, result.State);
        Assert.Equal("simulated test adapter failure", result.Message);
    }

    [Fact]
    public async Task Starting_a_run_while_one_is_already_active_throws_instead_of_overlapping()
    {
        var adapter = new FakeTestAdapter { HangUntilCancelled = true };
        var service = new TestService(new[] { adapter }, MakeBuildService());
        var project = MakeProject();

        var firstRun = service.RunAsync(MakeTarget(project), project, BuildConfiguration.Debug);
        await Task.Delay(20); // let it reach IsRunning
        Assert.True(service.IsRunning);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(MakeTarget(project), project, BuildConfiguration.Debug));

        service.Cancel();
        await firstRun;
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task Cancel_reports_the_run_as_Cancelled_not_Failed()
    {
        var adapter = new FakeTestAdapter { HangUntilCancelled = true };
        var service = new TestService(new[] { adapter }, MakeBuildService());
        var project = MakeProject();

        var runTask = service.RunAsync(MakeTarget(project), project, BuildConfiguration.Debug);
        await Task.Delay(20);
        service.Cancel();
        var result = await runTask;

        Assert.Equal(TestRunState.Cancelled, result.State);
    }

    [Fact]
    public void HasAdapterFor_reflects_registered_adapters()
    {
        var adapter = new FakeTestAdapter(ProjectType.DotNet);
        var service = new TestService(new[] { adapter }, MakeBuildService());

        Assert.True(service.HasAdapterFor(ProjectType.DotNet));
        Assert.False(service.HasAdapterFor(ProjectType.Rust));
    }
}
