namespace DevStudio.Core.Lsp;

/// <summary>The LSP transport boundary (SKILL.md §5–§7 [Phase 7]) — mirrors <c>Core.Dap
/// .IDapTransport</c>'s shape (kept as its own interface, not shared, since the message types
/// it moves are LSP's own <see cref="JsonRpcMessage"/>, not DAP's).</summary>
public interface IJsonRpcTransport : IAsyncDisposable
{
    Task WriteAsync(JsonRpcMessage message, CancellationToken cancellationToken = default);

    /// <summary>Yields every message as it is fully framed and parsed. Throws <see
    /// cref="JsonRpcProtocolException"/> if a message could not be parsed (SKILL.md §6) — never
    /// an unhandled exception of another type.</summary>
    IAsyncEnumerable<JsonRpcMessage> ReadMessagesAsync(CancellationToken cancellationToken = default);
}
