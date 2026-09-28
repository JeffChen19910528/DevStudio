using DevStudio.Core.Processes;

namespace DevStudio.Infrastructure.Tests.TestSupport;

/// <summary>
/// A controllable <see cref="IRunningProcess"/> for unit-testing code that calls
/// <see cref="IProcessRunner.Start"/> (Phase 5 Run, and the interactive terminal) without
/// spawning a real process. Call <see cref="CompleteWith"/> to simulate the process exiting on
/// its own, or call <see cref="Kill"/> (as production code would) to simulate a deliberate stop.
/// </summary>
public sealed class FakeRunningProcess : IRunningProcess
{
    private readonly TaskCompletionSource<ProcessResult> _exitSource = new();

    public int ProcessId { get; }
    public bool HasExited { get; private set; }
    public bool KillCalled { get; private set; }
    public List<string> WrittenInput { get; } = new();
    public Stream? StandardInput { get; set; }
    public Stream? StandardOutput { get; set; }

    public FakeRunningProcess(int processId = 4242) => ProcessId = processId;

    public void CompleteWith(int exitCode, string standardOutput = "", string standardError = "")
    {
        HasExited = true;
        _exitSource.TrySetResult(new ProcessResult(exitCode, standardOutput, standardError, TimeSpan.Zero, false, false));
    }

    public Task<ProcessResult> WaitForExitAsync(CancellationToken cancellationToken = default) => _exitSource.Task;

    public Task WriteInputAsync(string text, CancellationToken cancellationToken = default)
    {
        WrittenInput.Add(text);
        return Task.CompletedTask;
    }

    public void Kill()
    {
        KillCalled = true;
        if (!HasExited) CompleteWith(-1);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
