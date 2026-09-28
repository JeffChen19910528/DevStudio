using DevStudio.Core.Processes;
using DevStudio.Core.Projects;

namespace DevStudio.Core.Build;

/// <summary>
/// Dispatches a build request to the right <see cref="IBuildAdapter"/> and enforces "one build
/// at a time" (SKILL.md §17) — a second call while one is running throws rather than silently
/// starting an overlapping build; the UI is expected to guard its own Build button, and this is
/// the defense-in-depth backstop. Never executes anything itself — dispatch and single-flight
/// tracking only.
/// </summary>
public sealed class BuildService
{
    private readonly IReadOnlyList<IBuildAdapter> _adapters;
    private CancellationTokenSource? _currentCts;

    public BuildService(IEnumerable<IBuildAdapter> adapters) => _adapters = adapters.ToList();

    public bool IsRunning => _currentCts is not null;

    /// <summary>Minimal in-memory build history (SKILL.md §28) — just the most recent result.</summary>
    public BuildResult? LastResult { get; private set; }

    public bool HasAdapterFor(ProjectType projectType) => _adapters.Any(a => a.SupportsProjectType(projectType));

    public async Task<BuildResult> ExecuteAsync(
        BuildTarget target,
        DevStudio.Core.Workspace.BuildConfiguration configuration,
        BuildOperation operation,
        IProcessOutputSink? outputSink = null,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("A build is already running. Cancel it or wait for it to finish before starting another.");
        }

        var adapter = _adapters.FirstOrDefault(a => a.SupportsProjectType(target.ProjectType));
        if (adapter is null)
        {
            var unavailable = new BuildResult(
                BuildStatus.Unavailable, -1, TimeSpan.Zero, target, operation,
                Array.Empty<Diagnostics.Diagnostic>(), string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                $"No build adapter is available for project type '{target.ProjectType}'. Build support for this ecosystem is a later phase.");
            LastResult = unavailable;
            return unavailable;
        }

        _currentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var result = await adapter.ExecuteAsync(new BuildRequest(target, configuration, operation), outputSink, _currentCts.Token).ConfigureAwait(false);
            LastResult = result;
            return result;
        }
        finally
        {
            _currentCts.Dispose();
            _currentCts = null;
        }
    }

    /// <summary>Requests cancellation of whatever build is currently running; a no-op if none is (SKILL.md §16).</summary>
    public void CancelCurrentBuild() => _currentCts?.Cancel();
}
