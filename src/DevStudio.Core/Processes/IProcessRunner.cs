namespace DevStudio.Core.Processes;

/// <summary>
/// Secure process execution abstraction (SKILL.md §12). This is the ONLY sanctioned way
/// for any adapter to launch an external tool. There must never be a companion
/// "ExecuteCommand(string command)" API that accepts a raw shell command line.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Starts the process and streams output as it arrives.</summary>
    IRunningProcess Start(ProcessStartRequest request, IProcessOutputSink? outputSink = null);

    /// <summary>Starts the process, waits for completion, and returns the captured result.</summary>
    Task<ProcessResult> RunAsync(ProcessStartRequest request, IProcessOutputSink? outputSink = null, CancellationToken cancellationToken = default);
}

/// <summary>Receives live stdout/stderr lines from a running process, kept separate per SKILL.md §12.</summary>
public interface IProcessOutputSink
{
    void OnStandardOutput(string line);
    void OnStandardError(string line);
}

/// <summary>A handle to an in-flight process, including its full child process tree.</summary>
public interface IRunningProcess : IAsyncDisposable
{
    int ProcessId { get; }
    bool HasExited { get; }

    Task<ProcessResult> WaitForExitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes text to the process's standard input. Only valid when the originating
    /// <see cref="ProcessStartRequest.RedirectInput"/> was true (e.g. an interactive terminal
    /// session); throws <see cref="InvalidOperationException"/> otherwise.
    /// </summary>
    Task WriteInputAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Terminates the process and its entire process tree (SKILL.md §12, §30).</summary>
    void Kill();

    /// <summary>Raw stdin stream — only non-null when the originating
    /// <see cref="ProcessStartRequest.RawStdio"/> was true (SKILL.md §43 [Phase 6]: a binary
    /// framed protocol like DAP cannot go through the line-based <see cref="IProcessOutputSink"/>
    /// pipeline). Null for every other request, preserving existing behavior.</summary>
    Stream? StandardInput { get; }

    /// <summary>Raw stdout stream — see <see cref="StandardInput"/>.</summary>
    Stream? StandardOutput { get; }
}
