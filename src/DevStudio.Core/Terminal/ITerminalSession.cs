namespace DevStudio.Core.Terminal;

public enum TerminalOutputStream
{
    StandardOutput,
    StandardError
}

public sealed record TerminalOutputEventArgs(string Text, TerminalOutputStream Stream);

/// <summary>
/// A basic interactive terminal session: a real shell child process with piped stdin/stdout
/// (SKILL.md §14–§15). This is deliberately NOT a full VT100/pty emulation — there is no
/// ConPTY/pty allocation, so full-screen interactive programs (vim, htop, ...) will not render
/// correctly. That limitation is documented rather than hidden. It is built strictly on top of
/// <see cref="Processes.IProcessRunner"/> — never a second, incompatible process abstraction.
/// </summary>
public interface ITerminalSession : IAsyncDisposable
{
    bool IsRunning { get; }

    event EventHandler<TerminalOutputEventArgs>? OutputReceived;
    event EventHandler? Exited;

    Task StartAsync(string workingDirectory, CancellationToken cancellationToken = default);

    Task SendInputAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// No-op placeholder: without a real pty, the child shell is never informed of terminal
    /// size. Kept on the interface so a future pty-backed implementation is a drop-in
    /// replacement rather than an interface break.
    /// </summary>
    void Resize(int columns, int rows);

    void Terminate();

    Task RestartAsync(CancellationToken cancellationToken = default);
}
