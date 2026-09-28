using DevStudio.Core.Dap;
using DevStudio.Core.Rpc;
using DevStudio.Infrastructure.Rpc;

namespace DevStudio.Infrastructure.Dap;

/// <summary>
/// DAP's wire framing is exactly the protocol-agnostic <see cref="ContentLengthFrameReader"/>
/// (SKILL.md §6 [Phase 7]: shared with LSP rather than duplicated) — this class only adapts its
/// generic <see cref="RpcFramingException"/> into DAP's own <see cref="DapProtocolException"/>,
/// preserving the exact behavior <c>DapFrameReaderTests</c> already verifies.
/// </summary>
public sealed class DapFrameReader
{
    private readonly ContentLengthFrameReader _inner;

    public DapFrameReader(Stream stream) => _inner = new ContentLengthFrameReader(stream);

    public async Task<string?> ReadPayloadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _inner.ReadPayloadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (RpcFramingException ex)
        {
            throw new DapProtocolException(ex.Message, ex);
        }
    }
}
