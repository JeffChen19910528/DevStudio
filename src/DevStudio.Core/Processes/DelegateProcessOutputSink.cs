namespace DevStudio.Core.Processes;

/// <summary>Small reusable <see cref="IProcessOutputSink"/> so callers don't each write their
/// own private relay class.</summary>
public sealed class DelegateProcessOutputSink : IProcessOutputSink
{
    private readonly Action<string> _onStandardOutput;
    private readonly Action<string> _onStandardError;

    public DelegateProcessOutputSink(Action<string> onStandardOutput, Action<string> onStandardError)
    {
        _onStandardOutput = onStandardOutput;
        _onStandardError = onStandardError;
    }

    public void OnStandardOutput(string line) => _onStandardOutput(line);
    public void OnStandardError(string line) => _onStandardError(line);
}
