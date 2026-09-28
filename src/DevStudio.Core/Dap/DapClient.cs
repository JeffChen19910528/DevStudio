using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace DevStudio.Core.Dap;

/// <summary>
/// Orchestrates one DAP session over an <see cref="IDapTransport"/> (SKILL.md §6–§7): assigns
/// each outgoing request a unique <c>seq</c>, correlates the eventual response by
/// <c>request_seq</c> regardless of arrival order (responses are explicitly not assumed to
/// arrive in request order), and raises <see cref="EventReceived"/> for anything the adapter
/// sends unprompted (<c>stopped</c>, <c>output</c>, <c>terminated</c>, ...). Depends only on
/// <see cref="IDapTransport"/> — no stream/process code here, so this class is unit-testable
/// with a fake transport.
/// </summary>
public sealed class DapClient : IAsyncDisposable
{
    private readonly IDapTransport _transport;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<DapResponse>> _pending = new();
    private int _nextSeq;
    private Task? _readLoop;
    private CancellationTokenSource? _readLoopCts;

    public DapClient(IDapTransport transport) => _transport = transport;

    /// <summary>Raised for every DAP event the adapter sends (never for responses).</summary>
    public event Action<DapEvent>? EventReceived;

    /// <summary>Raised once if the read loop terminates abnormally (malformed message, transport
    /// closed unexpectedly, ...) — every request still awaiting a response is failed with the
    /// same exception rather than hanging forever.</summary>
    public event Action<Exception>? Faulted;

    /// <summary>Begins consuming <see cref="IDapTransport.ReadMessagesAsync"/> in the background.
    /// Must be called once before any <see cref="SendRequestAsync"/>.</summary>
    public void Start(CancellationToken cancellationToken = default)
    {
        _readLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _readLoop = RunReadLoopAsync(_readLoopCts.Token);
    }

    public async Task<DapResponse> SendRequestAsync(string command, JsonNode? arguments = null, CancellationToken cancellationToken = default)
    {
        var seq = Interlocked.Increment(ref _nextSeq);
        var request = new DapRequest(seq, command, arguments);
        var completion = new TaskCompletionSource<DapResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[seq] = completion;

        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        try
        {
            await _transport.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(seq, out _);
            throw;
        }

        return await completion.Task.ConfigureAwait(false);
    }

    private async Task RunReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _transport.ReadMessagesAsync(cancellationToken).ConfigureAwait(false))
            {
                switch (message)
                {
                    case DapResponse response:
                        if (_pending.TryRemove(response.RequestSeq, out var completion))
                        {
                            completion.TrySetResult(response);
                        }
                        break;
                    case DapEvent evt:
                        EventReceived?.Invoke(evt);
                        break;
                    // A DapRequest arriving from the adapter (a "reverse request") is not
                    // needed for vsdbg's launch-mode debugging in Phase 6 and is intentionally
                    // ignored rather than guessed at.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path (DisposeAsync cancels the loop).
        }
        catch (Exception ex)
        {
            foreach (var pending in _pending.Values) pending.TrySetException(ex);
            _pending.Clear();
            Faulted?.Invoke(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _readLoopCts?.Cancel();
        if (_readLoop is not null)
        {
            try { await _readLoop.ConfigureAwait(false); }
            catch { /* already surfaced via Faulted, or a normal cancellation */ }
        }
        foreach (var pending in _pending.Values) pending.TrySetCanceled();
        _pending.Clear();
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
