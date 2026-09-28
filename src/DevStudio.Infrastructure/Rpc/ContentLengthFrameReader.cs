using System.Text;
using DevStudio.Core.Rpc;

namespace DevStudio.Infrastructure.Rpc;

/// <summary>
/// The <c>Content-Length: &lt;n&gt;\r\n\r\n&lt;n bytes of UTF-8 JSON&gt;</c> wire framing shared
/// by DAP and LSP (SKILL.md §6 [Phase 7]) — protocol-agnostic: it knows nothing about DAP's or
/// LSP's message shapes, only about bytes. Extracted out of Phase 6's <c>Infrastructure.Dap
/// .DapFrameReader</c> (which now delegates here, preserving its own behavior/tests unchanged)
/// so LSP does not duplicate this parsing. Deliberately does not use
/// <c>StreamReader.ReadLine()</c> — headers are read byte-by-byte to their blank-line
/// terminator, and the payload is read as an exact byte count, so a header or payload split
/// across multiple physical reads, and multiple whole messages already sitting in one physical
/// read, are all handled correctly (see <c>ContentLengthFrameReaderTests</c>, and the
/// DAP-specific regression coverage in <c>DapFrameReaderTests</c>).
/// </summary>
public sealed class ContentLengthFrameReader
{
    private const int MaxContentLength = 64 * 1024 * 1024; // 64 MiB — generous, but not unbounded
    private readonly Stream _stream;
    private readonly List<byte> _buffer = new();
    private readonly byte[] _readBuffer = new byte[8192];

    public ContentLengthFrameReader(Stream stream) => _stream = stream;

    /// <summary>Reads and returns the next full JSON payload, or null if the stream ended before
    /// another complete message arrived. Throws <see cref="RpcFramingException"/> for a
    /// malformed/missing Content-Length header or a stream ending mid-message — unrecoverable;
    /// the caller should treat the transport as faulted rather than continue reading.</summary>
    public async Task<string?> ReadPayloadAsync(CancellationToken cancellationToken = default)
    {
        var headerEnd = await FindHeaderTerminatorAsync(cancellationToken).ConfigureAwait(false);
        if (headerEnd is null) return null; // stream ended cleanly between messages

        var headerBytes = _buffer.GetRange(0, headerEnd.Value);
        _buffer.RemoveRange(0, headerEnd.Value + 4); // also consume the trailing \r\n\r\n

        var contentLength = ParseContentLength(Encoding.ASCII.GetString(headerBytes.ToArray()));

        while (_buffer.Count < contentLength)
        {
            var read = await _stream.ReadAsync(_readBuffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new RpcFramingException($"Stream ended after {_buffer.Count} of {contentLength} expected payload bytes.");
            }
            _buffer.AddRange(_readBuffer.AsSpan(0, read).ToArray());
        }

        var payloadBytes = _buffer.GetRange(0, contentLength);
        _buffer.RemoveRange(0, contentLength);
        return Encoding.UTF8.GetString(payloadBytes.ToArray());
    }

    private async Task<int?> FindHeaderTerminatorAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var index = IndexOfTerminator(_buffer);
            if (index >= 0) return index;

            var read = await _stream.ReadAsync(_readBuffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (_buffer.Count == 0) return null;
                throw new RpcFramingException("Stream ended in the middle of a message header.");
            }
            _buffer.AddRange(_readBuffer.AsSpan(0, read).ToArray());
        }
    }

    private static int IndexOfTerminator(List<byte> buffer)
    {
        for (var i = 0; i + 3 < buffer.Count; i++)
        {
            if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
            {
                return i;
            }
        }
        return -1;
    }

    private static int ParseContentLength(string headerText)
    {
        foreach (var line in headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator < 0) continue;
            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (!string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;

            if (!int.TryParse(value, out var length) || length < 0 || length > MaxContentLength)
            {
                throw new RpcFramingException($"Malformed Content-Length header value: '{value}'.");
            }
            return length;
        }

        throw new RpcFramingException("Message header did not contain a Content-Length field.");
    }
}
