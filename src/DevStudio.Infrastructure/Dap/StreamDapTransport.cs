using System.Runtime.CompilerServices;
using DevStudio.Core.Dap;

namespace DevStudio.Infrastructure.Dap;

/// <summary>
/// The real <see cref="IDapTransport"/> — DAP framing (<see cref="DapFrameReader"/>/<see
/// cref="DapFrameWriter"/>) plus message (de)serialization over a debugger process's raw
/// stdin/stdout streams (SKILL.md §42–§43: reused from <c>IProcessRunner</c>'s
/// <c>ProcessStartRequest.RawStdio</c> mode, not a second process abstraction).
/// </summary>
public sealed class StreamDapTransport : IDapTransport
{
    private readonly DapFrameReader _reader;
    private readonly DapFrameWriter _writer;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly bool _ownsStreams;

    public StreamDapTransport(Stream input, Stream output, bool ownsStreams = false)
    {
        _input = input;
        _output = output;
        _reader = new DapFrameReader(input);
        _writer = new DapFrameWriter(output);
        _ownsStreams = ownsStreams;
    }

    public Task WriteAsync(DapProtocolMessage message, CancellationToken cancellationToken = default) =>
        _writer.WritePayloadAsync(DapMessageSerializer.Serialize(message), cancellationToken);

    public async IAsyncEnumerable<DapProtocolMessage> ReadMessagesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var payload = await _reader.ReadPayloadAsync(cancellationToken).ConfigureAwait(false);
            if (payload is null) yield break; // clean end of stream between messages
            yield return DapMessageSerializer.Deserialize(payload);
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
