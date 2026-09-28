namespace DevStudio.Core.Processes;

public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    bool WasCancelled,
    bool WasTimedOut);
