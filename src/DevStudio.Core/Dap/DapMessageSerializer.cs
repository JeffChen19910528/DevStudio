using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevStudio.Core.Dap;

/// <summary>
/// Pure JSON &lt;-&gt; <see cref="DapProtocolMessage"/> conversion — no stream/process I/O here
/// (that lives in <c>Infrastructure.Dap</c>'s framing layer). Field names match the DAP spec
/// exactly (SKILL.md §6–§7): requests carry <c>command</c>/<c>arguments</c>, responses carry
/// <c>request_seq</c>/<c>success</c>/<c>command</c>/<c>message</c>/<c>body</c>, events carry
/// <c>event</c>/<c>body</c>.
/// </summary>
public static class DapMessageSerializer
{
    public static string Serialize(DapProtocolMessage message)
    {
        var root = new JsonObject { ["seq"] = message.Seq };

        switch (message)
        {
            case DapRequest request:
                root["type"] = "request";
                root["command"] = request.Command;
                if (request.Arguments is not null) root["arguments"] = request.Arguments.DeepClone();
                break;
            case DapResponse response:
                root["type"] = "response";
                root["request_seq"] = response.RequestSeq;
                root["success"] = response.Success;
                root["command"] = response.Command;
                if (response.Message is not null) root["message"] = response.Message;
                if (response.Body is not null) root["body"] = response.Body.DeepClone();
                break;
            case DapEvent evt:
                root["type"] = "event";
                root["event"] = evt.EventName;
                if (evt.Body is not null) root["body"] = evt.Body.DeepClone();
                break;
            default:
                throw new DapProtocolException($"Unknown DAP message shape: {message.GetType()}.");
        }

        return root.ToJsonString();
    }

    /// <summary>Parses one DAP JSON payload. Throws <see cref="DapProtocolException"/> — never
    /// an unhandled exception type — on anything malformed (SKILL.md §46), so a transport's read
    /// loop can catch exactly one exception type for "this message could not be understood."</summary>
    public static DapProtocolMessage Deserialize(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new DapProtocolException("Malformed DAP message: not valid JSON.", ex);
        }

        if (node is not JsonObject obj)
        {
            throw new DapProtocolException("Malformed DAP message: expected a JSON object.");
        }

        var seq = ReadInt(obj, "seq") ?? throw new DapProtocolException("Malformed DAP message: missing 'seq'.");
        var type = obj["type"]?.GetValue<string>() ?? throw new DapProtocolException("Malformed DAP message: missing 'type'.");

        return type switch
        {
            "request" => new DapRequest(
                seq,
                obj["command"]?.GetValue<string>() ?? throw new DapProtocolException("Malformed DAP request: missing 'command'."),
                obj["arguments"]?.DeepClone()),
            "response" => new DapResponse(
                seq,
                ReadInt(obj, "request_seq") ?? throw new DapProtocolException("Malformed DAP response: missing 'request_seq'."),
                obj["success"]?.GetValue<bool>() ?? throw new DapProtocolException("Malformed DAP response: missing 'success'."),
                obj["command"]?.GetValue<string>() ?? string.Empty,
                obj["message"]?.GetValue<string>(),
                obj["body"]?.DeepClone()),
            "event" => new DapEvent(
                seq,
                obj["event"]?.GetValue<string>() ?? throw new DapProtocolException("Malformed DAP event: missing 'event'."),
                obj["body"]?.DeepClone()),
            _ => throw new DapProtocolException($"Malformed DAP message: unknown type '{type}'."),
        };
    }

    private static int? ReadInt(JsonObject obj, string property)
    {
        var value = obj[property];
        if (value is null) return null;
        try { return value.GetValue<int>(); }
        catch (Exception) { return null; }
    }
}
