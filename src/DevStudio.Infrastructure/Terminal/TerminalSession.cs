using DevStudio.Core.Processes;
using DevStudio.Core.Terminal;

namespace DevStudio.Infrastructure.Terminal;

/// <summary>
/// Real shell child process wired through <see cref="IProcessRunner"/> — no separate process
/// abstraction and no shell-string execution (SKILL.md §15, §24). Piped stdio only; no
/// ConPTY/pty, so <see cref="Resize"/> is a documented no-op and full-screen terminal UIs are
/// out of scope for Phase 1.
/// </summary>
public sealed class TerminalSession : ITerminalSession
{
    private readonly IProcessRunner _processRunner;
    private readonly string _shellExecutable;
    private IRunningProcess? _runningProcess;
    private string? _workingDirectory;

    public TerminalSession(IProcessRunner processRunner, string? shellExecutable = null)
    {
        _processRunner = processRunner;
        _shellExecutable = shellExecutable ?? ShellLocator.GetDefaultShellExecutable();
    }

    public bool IsRunning => _runningProcess is { HasExited: false };

    public event EventHandler<TerminalOutputEventArgs>? OutputReceived;
    public event EventHandler? Exited;

    public Task StartAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        _workingDirectory = workingDirectory;

        var request = new ProcessStartRequest(
            _shellExecutable,
            Arguments: Array.Empty<string>(),
            WorkingDirectory: workingDirectory,
            RedirectInput: true);

        var sink = new RelayOutputSink(
            line => OutputReceived?.Invoke(this, new TerminalOutputEventArgs(line, TerminalOutputStream.StandardOutput)),
            line => OutputReceived?.Invoke(this, new TerminalOutputEventArgs(line, TerminalOutputStream.StandardError)));

        _runningProcess = _processRunner.Start(request, sink);

        _ = MonitorExitAsync(_runningProcess);

        return Task.CompletedTask;
    }

    private async Task MonitorExitAsync(IRunningProcess process)
    {
        await process.WaitForExitAsync().ConfigureAwait(false);
        Exited?.Invoke(this, EventArgs.Empty);
    }

    public Task SendInputAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_runningProcess is null)
        {
            throw new InvalidOperationException("Terminal session has not been started.");
        }

        return _runningProcess.WriteInputAsync(text, cancellationToken);
    }

    public void Resize(int columns, int rows)
    {
        // No pty is allocated (SKILL.md §15 allows a basic terminal first); the child shell is
        // never told about size changes. Documented limitation, tracked for a future pty-backed
        // implementation.
    }

    public void Terminate() => _runningProcess?.Kill();

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        Terminate();
        if (_runningProcess is not null)
        {
            await _runningProcess.DisposeAsync().ConfigureAwait(false);
        }

        if (_workingDirectory is not null)
        {
            await StartAsync(_workingDirectory, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Terminate();
        if (_runningProcess is not null)
        {
            await _runningProcess.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class RelayOutputSink : IProcessOutputSink
    {
        private readonly Action<string> _onOut;
        private readonly Action<string> _onErr;

        public RelayOutputSink(Action<string> onOut, Action<string> onErr)
        {
            _onOut = onOut;
            _onErr = onErr;
        }

        public void OnStandardOutput(string line) => _onOut(line);
        public void OnStandardError(string line) => _onErr(line);
    }
}
