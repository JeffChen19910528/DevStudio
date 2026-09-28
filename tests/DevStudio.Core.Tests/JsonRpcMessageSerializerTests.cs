using System.Text.Json.Nodes;
using DevStudio.Core.Lsp;
using Xunit;

namespace DevStudio.Core.Tests;

public class JsonRpcMessageSerializerTests
{
    [Fact]
    public void A_request_round_trips_including_its_id_and_params()
    {
        var request = new JsonRpcRequest("1", "initialize", new JsonObject { ["processId"] = 42 });

        var json = JsonRpcMessageSerializer.Serialize(request);
        var parsed = Assert.IsType<JsonRpcRequest>(JsonRpcMessageSerializer.Deserialize(json));

        Assert.Equal("1", parsed.Id);
        Assert.Equal("initialize", parsed.Method);
        Assert.Equal(42, parsed.Params!["processId"]!.GetValue<int>());
    }

    [Fact]
    public void A_successful_response_round_trips_its_result()
    {
        var response = new JsonRpcResponse("2", JsonValue.Create("ok"));

        var json = JsonRpcMessageSerializer.Serialize(response);
        var parsed = Assert.IsType<JsonRpcResponse>(JsonRpcMessageSerializer.Deserialize(json));

        Assert.True(parsed.Success);
        Assert.Equal("ok", parsed.Result!.GetValue<string>());
    }

    [Fact]
    public void An_error_response_round_trips_its_code_and_message()
    {
        var response = new JsonRpcResponse("3", null, new JsonRpcError(-32601, "Method not found"));

        var json = JsonRpcMessageSerializer.Serialize(response);
        var parsed = Assert.IsType<JsonRpcResponse>(JsonRpcMessageSerializer.Deserialize(json));

        Assert.False(parsed.Success);
        Assert.Equal(-32601, parsed.Error!.Code);
        Assert.Equal("Method not found", parsed.Error.Message);
    }

    [Fact]
    public void A_notification_round_trips_and_has_no_id()
    {
        var notification = new JsonRpcNotification("initialized", new JsonObject());

        var json = JsonRpcMessageSerializer.Serialize(notification);
        var parsed = Assert.IsType<JsonRpcNotification>(JsonRpcMessageSerializer.Deserialize(json));

        Assert.Equal("initialized", parsed.Method);
        Assert.DoesNotContain("\"id\"", json);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void Malformed_input_throws_JsonRpcProtocolException_never_an_unhandled_exception_type(string malformed)
    {
        Assert.Throws<JsonRpcProtocolException>(() => JsonRpcMessageSerializer.Deserialize(malformed));
    }
}
