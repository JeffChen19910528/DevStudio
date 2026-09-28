namespace DevStudio.Core.Dap;

/// <summary>
/// The DAP protocol boundary (SKILL.md §4): reads/writes framed <see cref="DapProtocolMessage"/>
/// values over whatever medium the concrete debugger uses (stdio for vsdbg — see
/// <c>Infrastructure.Dap.StreamDapTransport</c>). <see cref="DapClient"/> depends only on this
/// interface, never on a concrete stream/process, so protocol-level correlation/event-dispatch
/// logic can be unit tested against a fake transport without spawning anything.
/// </summary>
public interface IDapTransport : IAsyncDisposable
{
    Task WriteAsync(DapProtocolMessage message, CancellationToken cancellationToken = default);

    /// <summary>Yields every message as it is fully framed and parsed. Ends when the underlying
    /// medium closes; throws <see cref="DapProtocolException"/> if a message could not be
    /// parsed (SKILL.md §46) — never an unhandled exception of another type.</summary>
    IAsyncEnumerable<DapProtocolMessage> ReadMessagesAsync(CancellationToken cancellationToken = default);
}
