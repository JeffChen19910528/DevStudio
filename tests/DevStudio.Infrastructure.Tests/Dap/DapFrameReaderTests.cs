using System.Text;
using DevStudio.Core.Dap;
using DevStudio.Infrastructure.Dap;
using Xunit;

namespace DevStudio.Infrastructure.Tests.Dap;

/// <summary>
/// Byte-level DAP framing tests (SKILL.md §5) against real <see cref="Stream"/> reads returned
/// in adversarial chunk sizes — no process needed to prove header/payload splitting is handled
/// correctly, per SKILL.md §57's "use fake DAP transport for deterministic protocol tests."
/// </summary>
public class DapFrameReaderTests
{
    private static byte[] Frame(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
        return header.Concat(payload).ToArray();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(4096)]
    public async Task A_single_message_is_read_correctly_regardless_of_read_chunk_size(int chunkSize)
    {
        var bytes = Frame("{\"seq\":1,\"type\":\"event\",\"event\":\"initialized\"}");
        var reader = new DapFrameReader(new ChunkedMemoryStream(bytes, chunkSize));

        var payload = await reader.ReadPayloadAsync();

        Assert.Equal("{\"seq\":1,\"type\":\"event\",\"event\":\"initialized\"}", payload);
    }

    [Fact]
    public async Task Multiple_messages_already_present_in_one_physical_read_are_each_returned_separately()
    {
        var bytes = Frame("{\"seq\":1,\"type\":\"event\",\"event\":\"a\"}")
            .Concat(Frame("{\"seq\":2,\"type\":\"event\",\"event\":\"b\"}"))
            .ToArray();
        // A chunk size larger than both frames combined forces both to land in the reader's
        // buffer from a single stream.Read call.
        var reader = new DapFrameReader(new ChunkedMemoryStream(bytes, bytes.Length));

        var first = await reader.ReadPayloadAsync();
        var second = await reader.ReadPayloadAsync();

        Assert.Contains("\"event\":\"a\"", first);
        Assert.Contains("\"event\":\"b\"", second);
    }

    [Fact]
    public async Task A_header_split_across_multiple_physical_reads_is_still_parsed_correctly()
    {
        var bytes = Frame("{\"seq\":1,\"type\":\"event\",\"event\":\"initialized\"}");
        // Chunk size of 1 forces "Content-Length: 47\r\n\r\n" to arrive one byte per read.
        var reader = new DapFrameReader(new ChunkedMemoryStream(bytes, 1));

        var payload = await reader.ReadPayloadAsync();

        Assert.Contains("initialized", payload);
    }

    [Fact]
    public async Task A_payload_split_across_multiple_physical_reads_is_still_assembled_correctly()
    {
        var longPayload = "{\"seq\":1,\"type\":\"event\",\"event\":\"output\",\"body\":{\"output\":\"" + new string('x', 500) + "\"}}";
        var bytes = Frame(longPayload);
        var reader = new DapFrameReader(new ChunkedMemoryStream(bytes, 17)); // deliberately not aligned to any boundary

        var payload = await reader.ReadPayloadAsync();

        Assert.Equal(longPayload, payload);
    }

    [Fact]
    public async Task A_missing_Content_Length_header_throws_DapProtocolException()
    {
        var bytes = Encoding.ASCII.GetBytes("X-Other-Header: 5\r\n\r\nhello");
        var reader = new DapFrameReader(new ChunkedMemoryStream(bytes, 4096));

        await Assert.ThrowsAsync<DapProtocolException>(() => reader.ReadPayloadAsync());
    }

    [Fact]
    public async Task A_non_numeric_Content_Length_value_throws_DapProtocolException()
    {
        var bytes = Encoding.ASCII.GetBytes("Content-Length: not-a-number\r\n\r\nhello");
        var reader = new DapFrameReader(new ChunkedMemoryStream(bytes, 4096));

        await Assert.ThrowsAsync<DapProtocolException>(() => reader.ReadPayloadAsync());
    }

    [Fact]
    public async Task A_negative_Content_Length_value_throws_DapProtocolException()
    {
        var bytes = Encoding.ASCII.GetBytes("Content-Length: -1\r\n\r\n");
        var reader = new DapFrameReader(new ChunkedMemoryStream(bytes, 4096));

        await Assert.ThrowsAsync<DapProtocolException>(() => reader.ReadPayloadAsync());
    }

    [Fact]
    public async Task A_stream_ending_cleanly_between_messages_returns_null_rather_than_throwing()
    {
        var reader = new DapFrameReader(new ChunkedMemoryStream(Array.Empty<byte>(), 4096));

        var payload = await reader.ReadPayloadAsync();

        Assert.Null(payload);
    }

    [Fact]
    public async Task A_stream_ending_mid_header_throws_DapProtocolException_instead_of_returning_null()
    {
        var bytes = Encoding.ASCII.GetBytes("Content-Length: 5\r\n"); // no blank-line terminator
        var reader = new DapFrameReader(new ChunkedMemoryStream(bytes, 4096));

        await Assert.ThrowsAsync<DapProtocolException>(() => reader.ReadPayloadAsync());
    }

    [Fact]
    public async Task A_stream_ending_mid_payload_throws_DapProtocolException()
    {
        var full = Frame("{\"seq\":1,\"type\":\"event\",\"event\":\"initialized\"}");
        var truncated = full[..(full.Length - 10)]; // cut off the last few payload bytes
        var reader = new DapFrameReader(new ChunkedMemoryStream(truncated, 4096));

        await Assert.ThrowsAsync<DapProtocolException>(() => reader.ReadPayloadAsync());
    }
}
