using System.Runtime.CompilerServices;
using DevStudio.Core.Lsp;
using DevStudio.Infrastructure.Rpc;

namespace DevStudio.Infrastructure.Lsp;

/// <summary>
/// The real <see cref="IJsonRpcTransport"/> — the shared <see cref="ContentLengthFrameReader"/>/
/// <see cref="ContentLengthFrameWriter"/> plus JSON-RPC message (de)serialization over a
/// language server process's raw stdin/stdout streams (SKILL.md §3, §6 [Phase 7]: reused from
/// Phase 6's <c>ProcessStartRequest.RawStdio</c> mode, not a second process abstraction).
/// </summary>
public sealed class StreamJsonRpcTransport : IJsonRpcTransport
{
    private readonly ContentLengthFrameReader _reader;
    private readonly ContentLengthFrameWriter _writer;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly bool _ownsStreams;

    public StreamJsonRpcTransport(Stream input, Stream output, bool ownsStreams = false)
    {
        _input = input;
        _output = output;
        _reader = new ContentLengthFrameReader(input);
        _writer = new ContentLengthFrameWriter(output);
        _ownsStreams = ownsStreams;
    }

    public Task WriteAsync(JsonRpcMessage message, CancellationToken cancellationToken = default) =>
        _writer.WritePayloadAsync(JsonRpcMessageSerializer.Serialize(message), cancellationToken);

    public async IAsyncEnumerable<JsonRpcMessage> ReadMessagesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            string? payload;
            try
            {
                payload = await _reader.ReadPayloadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Core.Rpc.RpcFramingException ex)
            {
                throw new JsonRpcProtocolException(ex.Message, ex);
            }

            if (payload is null) yield break; // clean end of stream between messages
            yield return JsonRpcMessageSerializer.Deserialize(payload);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsStreams)
        {
            _input.Dispose();
            _output.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}
