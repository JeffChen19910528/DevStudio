using System.Text;
using DevStudio.Core.Rpc;
using DevStudio.Infrastructure.Rpc;
using DevStudio.Infrastructure.Tests.Dap;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Rpc;

/// <summary>
/// The shared, protocol-agnostic framing DAP and LSP both build on (SKILL.md §6 [Phase 7]).
/// <c>DapFrameReaderTests</c> already regression-covers this exact logic through DAP's own
/// wrapper; these tests exercise the shared class directly, plus a large-payload case relevant
/// to LSP (a completion list can be far larger than a typical DAP message).
/// </summary>
public class ContentLengthFrameReaderTests
{
    private static byte[] Frame(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
        return header.Concat(payload).ToArray();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(4096)]
    public async Task A_single_message_is_read_correctly_regardless_of_read_chunk_size(int chunkSize)
    {
        var bytes = Frame("{\"jsonrpc\":\"2.0\",\"method\":\"initialized\",\"params\":{}}");
        var reader = new ContentLengthFrameReader(new ChunkedMemoryStream(bytes, chunkSize));

        var payload = await reader.ReadPayloadAsync();

        Assert.Equal("{\"jsonrpc\":\"2.0\",\"method\":\"initialized\",\"params\":{}}", payload);
    }

    [Fact]
    public async Task A_large_payload_such_as_a_real_completion_list_is_read_correctly()
    {
        var largeArray = string.Join(",", Enumerable.Range(0, 2000).Select(i => $"\"item{i}\""));
        var json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":[" + largeArray + "]}";
        var bytes = Frame(json);
        var reader = new ContentLengthFrameReader(new ChunkedMemoryStream(bytes, 4096));

        var payload = await reader.ReadPayloadAsync();

        Assert.Equal(json, payload);
    }

    [Fact]
    public async Task Multiple_messages_already_present_in_one_physical_read_are_each_returned_separately()
    {
        var bytes = Frame("{\"a\":1}").Concat(Frame("{\"b\":2}")).ToArray();
        var reader = new ContentLengthFrameReader(new ChunkedMemoryStream(bytes, bytes.Length));

        Assert.Equal("{\"a\":1}", await reader.ReadPayloadAsync());
        Assert.Equal("{\"b\":2}", await reader.ReadPayloadAsync());
    }

    [Fact]
    public async Task A_missing_Content_Length_header_throws_RpcFramingException()
    {
        var bytes = Encoding.ASCII.GetBytes("X-Other-Header: 5\r\n\r\nhello");
        var reader = new ContentLengthFrameReader(new ChunkedMemoryStream(bytes, 4096));

        await Assert.ThrowsAsync<RpcFramingException>(() => reader.ReadPayloadAsync());
    }

    [Fact]
    public async Task A_stream_ending_cleanly_between_messages_returns_null_rather_than_throwing()
    {
        var reader = new ContentLengthFrameReader(new ChunkedMemoryStream(Array.Empty<byte>(), 4096));

        Assert.Null(await reader.ReadPayloadAsync());
    }
}
