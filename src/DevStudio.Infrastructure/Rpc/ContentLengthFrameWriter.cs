using System.Text;

namespace DevStudio.Infrastructure.Rpc;

/// <summary>Writes one <c>Content-Length</c>-framed payload to a <see cref="Stream"/> — the
/// write-side counterpart to <see cref="ContentLengthFrameReader"/>, shared by DAP and LSP
/// (SKILL.md §6 [Phase 7]).</summary>
public sealed class ContentLengthFrameWriter
{
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public ContentLengthFrameWriter(Stream stream) => _stream = stream;

    public async Task WritePayloadAsync(string json, CancellationToken cancellationToken = default)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(json);
        var headerBytes = Encoding.ASCII.GetBytes($"Content-Length: {payloadBytes.Length}\r\n\r\n");

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(payloadBytes, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
