using DevStudio.Core.Build;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;
using DevStudio.Core.Workspace;

namespace DevStudio.Core.Testing;

/// <summary>
/// Orchestrates test discovery and execution (SKILL.md §2, §22): picks the right <see
/// cref="ITestAdapter"/>, reuses <see cref="Build.BuildService"/> for build-before-test (never
/// invokes <c>dotnet build</c> or <c>dotnet test</c>'s own implicit build directly — same rule
/// as <see cref="Run.RunService"/>/<see cref="Debug.DebugService"/>), and enforces one active
/// discovery-or-run at a time via a single-flight <see cref="CancellationTokenSource"/> —
/// structurally the same backstop <see cref="Build.BuildService.IsRunning"/> uses, since a test
/// run (like a build) is a single bounded operation, not a long-lived session like Run/Debug.
/// Workspace Trust is enforced by the ViewModel before calling this, exactly like
/// Build/Run/Debug/Language — Core has no UI/dialog dependency to check it here.
/// </summary>
public sealed class TestService
{
    private readonly IReadOnlyList<ITestAdapter> _adapters;
    private readonly BuildService _buildService;
    private CancellationTokenSource? _currentCts;

    public TestService(IEnumerable<ITestAdapter> adapters, BuildService buildService)
    {
        _adapters = adapters.ToList();
        _buildService = buildService;
    }

    public bool IsRunning => _currentCts is not null;
    public TestRunState State { get; private set; } = TestRunState.NotStarted;
    public IReadOnlyList<TestCase> DiscoveredTests { get; private set; } = Array.Empty<TestCase>();
    public TestRunResult? LastResult { get; private set; }

    public event EventHandler<TestRunState>? StateChanged;
    public event EventHandler<IReadOnlyList<TestCase>>? TestsDiscovered;
    public event EventHandler<TestRunResult>? Completed;

    public bool HasAdapterFor(ProjectType projectType) => _adapters.Any(a => a.SupportsProjectType(projectType));

    public async Task<IReadOnlyList<TestCase>> DiscoverAsync(ProjectInfo project, BuildConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("A test discovery or run is already active. Stop it before starting another.");
        }

        var adapter = _adapters.FirstOrDefault(a => a.SupportsProjectType(project.ProjectType));
        if (adapter is null)
        {
            SetState(TestRunState.Failed);
            return Array.Empty<TestCase>();
        }

        _currentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        SetState(TestRunState.Discovering);
        try
        {
            var tests = await adapter.DiscoverTestsAsync(project, configuration, _currentCts.Token).ConfigureAwait(false);
            DiscoveredTests = tests;
            TestsDiscovered?.Invoke(this, tests);
            SetState(TestRunState.Completed);
            return tests;
        }
        catch (OperationCanceledException)
        {
            SetState(TestRunState.Cancelled);
            return Array.Empty<TestCase>();
        }
        finally
        {
            _currentCts?.Dispose();
            _currentCts = null;
        }
    }

    public async Task<TestRunResult> RunAsync(
        BuildTarget target,
        ProjectInfo project,
        BuildConfiguration configuration,
        TestFilter? filter = null,
        bool buildBeforeTest = true,
        IProcessOutputSink? buildOutputSink = null,
        IProcessOutputSink? testOutputSink = null,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("A test discovery or run is already active. Stop it before starting another.");
        }

        var adapter = _adapters.FirstOrDefault(a => a.SupportsProjectType(project.ProjectType));
        if (adapter is null)
        {
            return Complete(new TestRunResult(TestRunState.Failed, Array.Empty<TestResult>(), $"No test adapter is available for project type '{project.ProjectType}'."));
        }

        if (buildBeforeTest)
        {
            SetState(TestRunState.Starting);
            var buildResult = await _buildService.ExecuteAsync(target, configuration, BuildOperation.Build, buildOutputSink, cancellationToken).ConfigureAwait(false);
            if (buildResult.Status != BuildStatus.Succeeded)
            {
                return Complete(new TestRunResult(TestRunState.BlockedByBuildFailure, Array.Empty<TestResult>(), "Test run aborted because build failed."));
            }
        }

        _currentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        SetState(TestRunState.Running);
        try
        {
            var results = await adapter.RunTestsAsync(project, configuration, filter, buildBeforeTest, testOutputSink, _currentCts.Token).ConfigureAwait(false);
            return Complete(new TestRunResult(TestRunState.Completed, results));
        }
        catch (OperationCanceledException)
        {
            return Complete(new TestRunResult(TestRunState.Cancelled, Array.Empty<TestResult>(), "Test run cancelled."));
        }
        catch (Exception ex)
        {
            return Complete(new TestRunResult(TestRunState.Failed, Array.Empty<TestResult>(), ex.Message));
        }
        finally
        {
            _currentCts?.Dispose();
            _currentCts = null;
        }
    }

    /// <summary>Cancels whichever discovery or run is currently active — a no-op if none is.
    /// The adapter is responsible for translating this into a real
    /// <see cref="Core.Processes.IRunningProcess.Kill"/>/process-tree termination via the
    /// cancellation token it was given (SKILL.md §17).</summary>
    public void Cancel() => _currentCts?.Cancel();

    private TestRunResult Complete(TestRunResult result)
    {
        LastResult = result;
        SetState(result.State);
        Completed?.Invoke(this, result);
        return result;
    }

    private void SetState(TestRunState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
