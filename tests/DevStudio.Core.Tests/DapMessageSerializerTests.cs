using System.Text.Json.Nodes;
using DevStudio.Core.Dap;
using Xunit;

namespace DevStudio.Core.Tests;

public class DapMessageSerializerTests
{
    [Fact]
    public void A_request_round_trips_through_serialize_and_deserialize()
    {
        var request = new DapRequest(5, "launch", new JsonObject { ["program"] = "App.dll" });

        var json = DapMessageSerializer.Serialize(request);
        var parsed = Assert.IsType<DapRequest>(DapMessageSerializer.Deserialize(json));

        Assert.Equal(5, parsed.Seq);
        Assert.Equal("launch", parsed.Command);
        Assert.Equal("App.dll", parsed.Arguments!["program"]!.GetValue<string>());
    }

    [Fact]
    public void A_response_round_trips_including_request_seq_and_success()
    {
        var response = new DapResponse(6, RequestSeq: 5, Success: false, Command: "launch", Message: "boom", Body: null);

        var json = DapMessageSerializer.Serialize(response);
        var parsed = Assert.IsType<DapResponse>(DapMessageSerializer.Deserialize(json));

        Assert.Equal(5, parsed.RequestSeq);
        Assert.False(parsed.Success);
        Assert.Equal("boom", parsed.Message);
    }

    [Fact]
    public void An_event_round_trips_including_its_body()
    {
        var evt = new DapEvent(7, "stopped", new JsonObject { ["reason"] = "breakpoint", ["threadId"] = 1 });

        var json = DapMessageSerializer.Serialize(evt);
        var parsed = Assert.IsType<DapEvent>(DapMessageSerializer.Deserialize(json));

        Assert.Equal("stopped", parsed.EventName);
        Assert.Equal("breakpoint", parsed.Body!["reason"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{\"seq\": 1}")]
    [InlineData("{\"seq\": 1, \"type\": \"bogus\"}")]
    [InlineData("{\"seq\": 1, \"type\": \"request\"}")]
    [InlineData("[]")]
    public void Malformed_input_throws_DapProtocolException_never_an_unhandled_exception_type(string malformed)
    {
        Assert.Throws<DapProtocolException>(() => DapMessageSerializer.Deserialize(malformed));
    }
}
