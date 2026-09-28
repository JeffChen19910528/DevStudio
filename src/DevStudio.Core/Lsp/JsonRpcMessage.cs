using System.Text.Json.Nodes;

namespace DevStudio.Core.Lsp;

/// <summary>
/// The three JSON-RPC 2.0 message shapes LSP uses (SKILL.md §5 [Phase 7]) — kept as its own
/// domain model, separate from <c>Core.Dap</c>'s DAP messages, even though both protocols are
/// conceptually similar (request/response/notification-or-event with async correlation): DAP
/// uses a global <c>seq</c> across every message kind, while JSON-RPC's <c>id</c> only exists on
/// requests/responses and notifications carry none at all — forcing them into one shared model
/// would blur that real difference (see ADR-008). <see cref="Params"/>/<see cref="Result"/> are
/// left as <see cref="JsonNode"/> rather than per-method strong types, matching how <c>Core.Dap</c>
/// treats DAP bodies: LSP defines dozens of request shapes, and hard-coding every one into Core
/// would duplicate the protocol instead of orchestrating it.
/// </summary>
public abstract record JsonRpcMessage;

/// <summary>An outgoing or incoming request. <see cref="Id"/> is always carried as its string
/// form even when the wire value was a JSON number — DevStudio always mints its own outgoing ids
/// as small integers and only needs exact-match correlation, never numeric arithmetic on them.</summary>
public sealed record JsonRpcRequest(string Id, string Method, JsonNode? Params = null) : JsonRpcMessage;

public sealed record JsonRpcNotification(string Method, JsonNode? Params = null) : JsonRpcMessage;

public sealed record JsonRpcResponse(string Id, JsonNode? Result = null, JsonRpcError? Error = null) : JsonRpcMessage
{
    public bool Success => Error is null;
}

public sealed record JsonRpcError(int Code, string Message, JsonNode? Data = null);

/// <summary>A JSON-RPC message that could not be parsed at all (SKILL.md §6) — mirrors
/// <c>Core.Dap.DapProtocolException</c>'s role for DAP.</summary>
public sealed class JsonRpcProtocolException : Exception
{
    public JsonRpcProtocolException(string message) : base(message) { }
    public JsonRpcProtocolException(string message, Exception innerException) : base(message, innerException) { }
}
