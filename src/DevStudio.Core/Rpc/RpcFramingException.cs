namespace DevStudio.Core.Rpc;

/// <summary>Malformed wire framing (bad/missing Content-Length, a stream ending mid-message) at
/// the protocol-agnostic byte level shared by DAP and LSP (SKILL.md §6 [Phase 7]: both use
/// identical <c>Content-Length</c> framing). Protocol-specific layers (<c>Core.Dap</c>,
/// <c>Core.Lsp</c>) catch and re-wrap this into their own exception type so each protocol's
/// callers only ever see one exception type for "this message could not be framed."</summary>
public sealed class RpcFramingException : Exception
{
    public RpcFramingException(string message) : base(message) { }
}
