using DevStudio.Core.Build;
using DevStudio.Core.Processes;
using DevStudio.Core.Projects;

namespace DevStudio.Core.Run;

/// <summary>
/// Orchestrates Run: resolves an <see cref="IRunAdapter"/>, optionally builds first through the
/// existing <see cref="BuildService"/> (never invoking <c>dotnet build</c> itself — SKILL.md
/// §11), starts the application, and tracks its unbounded lifetime until it exits or is
/// stopped. A separate service from <see cref="BuildService"/> on purpose (SKILL.md §2): they
/// share <see cref="IProcessRunner"/> through their respective adapters, not a merged service.
/// </summary>
public sealed class RunService
{
    private readonly IReadOnlyList<IRunAdapter> _adapters;
    private readonly BuildService _buildService;
    private IRunningApplication? _current;
    private Task? _monitorTask;

    public RunService(IEnumerable<IRunAdapter> adapters, BuildService buildService)
    {
        _adapters = adapters.ToList();
        _buildService = buildService;
    }

    public RunStatus Status { get; private set; } = RunStatus.NotStarted;

    /// <summary>Minimal in-memory run history (mirrors <see cref="BuildService.LastResult"/>).</summary>
    public RunResult? LastResult { get; private set; }

    public event EventHandler<RunStatus>? StatusChanged;
    public event EventHandler<RunResult>? Completed;

    public bool HasAdapterFor(ProjectType projectType) => _adapters.Any(a => a.SupportsProjectType(projectType));

    /// <summary>
    /// Starts (or, if <see cref="RunConfiguration.BuildBeforeRun"/>, builds then starts) the
    /// configured application. Never runs two instances at once (SKILL.md §21, §52) — throws if
    /// a run is already starting/running/stopping, the same single-flight contract as <see
    /// cref="BuildService.ExecuteAsync"/>.
    /// </summary>
    public async Task StartAsync(
        RunConfiguration configuration,
        IProcessOutputSink? buildOutputSink = null,
        IProcessOutputSink? runOutputSink = null,
        CancellationToken cancellationToken = default)
    {
        if (Status is RunStatus.Starting or RunStatus.Running or RunStatus.Stopping)
        {
            throw new InvalidOperationException("An application is already running. Stop it before starting another.");
        }

        SetStatus(RunStatus.Starting);
        var startedAt = DateTimeOffset.UtcNow;

        var adapter = _adapters.FirstOrDefault(a => a.SupportsProjectType(configuration.Target.ProjectType));
        if (adapter is null)
        {
            Complete(new RunResult(RunStatus.FailedToStart, null, TimeSpan.Zero, configuration, string.Empty, startedAt, DateTimeOffset.UtcNow,
                $"No run adapter is available for project type '{configuration.Target.ProjectType}'."));
            return;
        }

        if (configuration.BuildBeforeRun)
        {
            var buildResult = await _buildService.ExecuteAsync(configuration.Target, configuration.BuildConfiguration, BuildOperation.Build, buildOutputSink, cancellationToken).ConfigureAwait(false);
            if (buildResult.Status != BuildStatus.Succeeded)
            {
                Complete(new RunResult(RunStatus.FailedToStart, null, DateTimeOffset.UtcNow - startedAt, configuration, buildResult.Output, startedAt, DateTimeOffset.UtcNow,
                    "Run aborted because build failed."));
                return;
            }
        }

        IRunningApplication application;
        try
        {
            application = await adapter.StartAsync(configuration, runOutputSink, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Complete(new RunResult(RunStatus.FailedToStart, null, DateTimeOffset.UtcNow - startedAt, configuration, string.Empty, startedAt, DateTimeOffset.UtcNow, ex.Message));
            return;
        }

        _current = application;
        SetStatus(RunStatus.Running);
        _monitorTask = MonitorAsync(application);
    }

    private async Task MonitorAsync(IRunningApplication application)
    {
        var result = await application.WaitForExitAsync().ConfigureAwait(false);
        _current = null;
        Complete(result);
    }

    /// <summary>Requests termination of the running application (SKILL.md §20); a no-op if
    /// nothing is running. The actual status transition to <see cref="RunStatus.Terminated"/>
    /// happens once <see cref="MonitorAsync"/> observes the real process exit.</summary>
    public void Stop()
    {
        if (Status != RunStatus.Running || _current is null) return;

        SetStatus(RunStatus.Stopping);
        _current.Stop();
    }

    /// <summary>Stop, wait for the real process to actually exit, then Start again (SKILL.md
    /// §21) — never a second instance running alongside the first.</summary>
    public async Task RestartAsync(
        RunConfiguration configuration,
        IProcessOutputSink? buildOutputSink = null,
        IProcessOutputSink? runOutputSink = null,
        CancellationToken cancellationToken = default)
    {
        if (_current is not null)
        {
            var monitorTask = _monitorTask;
            Stop();

            if (monitorTask is not null)
            {
                await Task.WhenAny(monitorTask, Task.Delay(TimeSpan.FromSeconds(10), cancellationToken)).ConfigureAwait(false);
            }

            if (_current is not null)
            {
                Complete(new RunResult(RunStatus.FailedToStart, null, TimeSpan.Zero, configuration, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    "Restart aborted because the existing process could not be terminated."));
                return;
            }
        }

        await StartAsync(configuration, buildOutputSink, runOutputSink, cancellationToken).ConfigureAwait(false);
    }

    private void Complete(RunResult result)
    {
        LastResult = result;
        SetStatus(result.Status);
        Completed?.Invoke(this, result);
    }

    private void SetStatus(RunStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }
}
