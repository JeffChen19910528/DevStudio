namespace DevStudio.Infrastructure.Tests.Dap;

/// <summary>
/// Hands back bytes from a fixed backing buffer in caller-controlled chunk sizes, so a frame
/// reader's byte-level parsing can be tested against genuinely adversarial read boundaries
/// (a header split mid-line, a payload split mid-UTF-8-sequence, several whole messages already
/// sitting in one read) without spawning a real process (SKILL.md §5–§6 [Phase 6]).
/// </summary>
public sealed class ChunkedMemoryStream : Stream
{
    private readonly byte[] _data;
    private readonly int _chunkSize;
    private int _position;

    public ChunkedMemoryStream(byte[] data, int chunkSize)
    {
        _data = data;
        _chunkSize = chunkSize;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = _data.Length - _position;
        if (remaining <= 0) return 0;
        var toCopy = Math.Min(Math.Min(count, _chunkSize), remaining);
        Array.Copy(_data, _position, buffer, offset, toCopy);
        _position += toCopy;
        return toCopy;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromResult(Read(buffer, offset, count));

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var remaining = _data.Length - _position;
        if (remaining <= 0) return ValueTask.FromResult(0);
        var toCopy = Math.Min(Math.Min(buffer.Length, _chunkSize), remaining);
        _data.AsSpan(_position, toCopy).CopyTo(buffer.Span);
        _position += toCopy;
        return ValueTask.FromResult(toCopy);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _position; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
