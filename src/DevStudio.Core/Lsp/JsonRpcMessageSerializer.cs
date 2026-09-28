using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevStudio.Core.Lsp;

/// <summary>Pure JSON &lt;-&gt; <see cref="JsonRpcMessage"/> conversion — no stream/process I/O
/// here (that lives in <c>Infrastructure.Lsp</c>'s framing layer, itself built on the same
/// shared <c>Infrastructure.Rpc.ContentLengthFrameReader/Writer</c> DAP uses).</summary>
public static class JsonRpcMessageSerializer
{
    public static string Serialize(JsonRpcMessage message)
    {
        var root = new JsonObject { ["jsonrpc"] = "2.0" };

        switch (message)
        {
            case JsonRpcRequest request:
                root["id"] = ToIdNode(request.Id);
                root["method"] = request.Method;
                if (request.Params is not null) root["params"] = request.Params.DeepClone();
                break;
            case JsonRpcNotification notification:
                root["method"] = notification.Method;
                if (notification.Params is not null) root["params"] = notification.Params.DeepClone();
                break;
            case JsonRpcResponse response:
                root["id"] = ToIdNode(response.Id);
                if (response.Error is not null)
                {
                    root["error"] = new JsonObject
                    {
                        ["code"] = response.Error.Code,
                        ["message"] = response.Error.Message,
                        ["data"] = response.Error.Data?.DeepClone(),
                    };
                }
                else
                {
                    root["result"] = response.Result?.DeepClone() ?? JsonValue.Create((object?)null);
                }
                break;
            default:
                throw new JsonRpcProtocolException($"Unknown JSON-RPC message shape: {message.GetType()}.");
        }

        return root.ToJsonString();
    }

    /// <summary>Parses one JSON-RPC payload. Throws <see cref="JsonRpcProtocolException"/> —
    /// never an unhandled exception type — on anything malformed (SKILL.md §6).</summary>
    public static JsonRpcMessage Deserialize(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new JsonRpcProtocolException("Malformed JSON-RPC message: not valid JSON.", ex);
        }

        if (node is not JsonObject obj)
        {
            throw new JsonRpcProtocolException("Malformed JSON-RPC message: expected a JSON object.");
        }

        var idNode = obj["id"];
        var hasId = idNode is not null;
        var method = obj["method"]?.GetValue<string>();

        if (method is not null)
        {
            return hasId
                ? new JsonRpcRequest(IdToString(idNode!), method, obj["params"]?.DeepClone())
                : new JsonRpcNotification(method, obj["params"]?.DeepClone());
        }

        if (hasId)
        {
            var errorNode = obj["error"] as JsonObject;
            var error = errorNode is null
                ? null
                : new JsonRpcError(
                    errorNode["code"]?.GetValue<int>() ?? 0,
                    errorNode["message"]?.GetValue<string>() ?? string.Empty,
                    errorNode["data"]?.DeepClone());
            return new JsonRpcResponse(IdToString(idNode!), obj["result"]?.DeepClone(), error);
        }

        throw new JsonRpcProtocolException("Malformed JSON-RPC message: has neither 'method' nor 'id'.");
    }

    private static JsonNode ToIdNode(string id) =>
        long.TryParse(id, out var number) ? JsonValue.Create(number) : JsonValue.Create(id);

    private static string IdToString(JsonNode idNode) => idNode switch
    {
        JsonValue value when value.TryGetValue(out long longValue) => longValue.ToString(),
        JsonValue value when value.TryGetValue(out string? stringValue) => stringValue ?? string.Empty,
        _ => idNode.ToString(),
    };
}
