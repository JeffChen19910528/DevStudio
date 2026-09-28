using DevStudio.Infrastructure.Rpc;

namespace DevStudio.Infrastructure.Dap;

/// <summary>DAP's wire framing is exactly <see cref="ContentLengthFrameWriter"/> (SKILL.md §6
/// [Phase 7]: shared with LSP rather than duplicated).</summary>
public sealed class DapFrameWriter
{
    private readonly ContentLengthFrameWriter _inner;

    public DapFrameWriter(Stream stream) => _inner = new ContentLengthFrameWriter(stream);

    public Task WritePayloadAsync(string json, CancellationToken cancellationToken = default) =>
        _inner.WritePayloadAsync(json, cancellationToken);
}
