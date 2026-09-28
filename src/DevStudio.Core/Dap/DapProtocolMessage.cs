using System.Text.Json.Nodes;

namespace DevStudio.Core.Dap;

/// <summary>
/// The three message shapes defined by the Debug Adapter Protocol (SKILL.md §4–§7 [Phase 6]).
/// <see cref="Arguments"/>/<see cref="Body"/> are left as <see cref="JsonNode"/> rather than
/// strongly-typed per-command shapes: DAP defines dozens of request/response/event bodies, and
/// hard-coding every one into Core would duplicate the protocol instead of orchestrating it.
/// Adapters (Infrastructure) are responsible for building/reading the specific shapes they need
/// and translating them into DevStudio's own domain models (<c>Core.Debug</c>) before anything
/// reaches a ViewModel — no raw DAP JSON should ever reach the UI.
/// </summary>
public abstract record DapProtocolMessage(int Seq);

public sealed record DapRequest(int Seq, string Command, JsonNode? Arguments = null) : DapProtocolMessage(Seq);

public sealed record DapResponse(int Seq, int RequestSeq, bool Success, string Command, string? Message = null, JsonNode? Body = null) : DapProtocolMessage(Seq);

public sealed record DapEvent(int Seq, string EventName, JsonNode? Body = null) : DapProtocolMessage(Seq);

/// <summary>A DAP message that could not be parsed at all (SKILL.md §46) — carries the raw text
/// so a caller can log it without the whole transport crashing on malformed input.</summary>
public sealed class DapProtocolException : Exception
{
    public DapProtocolException(string message) : base(message) { }
    public DapProtocolException(string message, Exception innerException) : base(message, innerException) { }
}
