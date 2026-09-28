using System.Text;
using DevStudio.Core.Processes;
using DevStudio.Core.Run;

namespace DevStudio.Infrastructure.Run;

internal sealed class DotNetRunningApplication : IRunningApplication
{
    private readonly IRunningProcess _process;
    private readonly RunConfiguration _configuration;
    private readonly DateTimeOffset _startedAt;
    private readonly StringBuilder _output;
    private volatile bool _stopRequested;

    public DotNetRunningApplication(IRunningProcess process, RunConfiguration configuration, DateTimeOffset startedAt, StringBuilder output)
    {
        _process = process;
        _configuration = configuration;
        _startedAt = startedAt;
        _output = output;
    }

    public bool HasExited => _process.HasExited;

    public void Stop()
    {
        _stopRequested = true;
        _process.Kill();
    }

    public async Task<RunResult> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        var processResult = await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var completedAt = DateTimeOffset.UtcNow;

        // A stop-requested kill and a real "the app crashed/exited on its own" both surface as
        // the child process exiting — the only way to tell them apart is whether Stop() was
        // called first (SKILL.md §16, §33: never report a deliberate stop as a failure).
        var status = _stopRequested ? RunStatus.Terminated : RunStatus.Exited;

        string output;
        lock (_output) { output = _output.ToString(); }

        return new RunResult(status, processResult.ExitCode, completedAt - _startedAt, _configuration, output, _startedAt, completedAt);
    }

    public ValueTask DisposeAsync() => _process.DisposeAsync();
}
