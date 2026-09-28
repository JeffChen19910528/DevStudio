using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace DevStudio.Core.Lsp;

/// <summary>
/// Orchestrates one LSP session's JSON-RPC traffic over an <see cref="IJsonRpcTransport"/>
/// (SKILL.md §5, §7, §45): assigns each outgoing request a unique <c>id</c>, correlates the
/// eventual response regardless of arrival order, dispatches unsolicited notifications, and
/// answers server-initiated requests (real servers send these — e.g. <c>workspace/configuration</c>,
/// <c>client/registerCapability</c> — and will stall waiting for a response if none is ever
/// sent). Depends only on <see cref="IJsonRpcTransport"/>, so this is unit-testable with a fake
/// transport. Structurally mirrors <c>Core.Dap.DapClient</c>, but is a separate type per its own
/// JSON-RPC 2.0 semantics (id-only-on-request/response, distinct notification shape) — see
/// ADR-008 for why these were not unified into one generic RPC client.
/// </summary>
public sealed class JsonRpcClient : IAsyncDisposable
{
    private readonly IJsonRpcTransport _transport;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonRpcResponse>> _pending = new();
    private readonly ConcurrentDictionary<string, Func<JsonNode?, JsonNode?>> _serverRequestHandlers = new();
    private long _nextId;
    private Task? _readLoop;
    private CancellationTokenSource? _readLoopCts;

    public JsonRpcClient(IJsonRpcTransport transport) => _transport = transport;

    public event Action<JsonRpcNotification>? NotificationReceived;
    public event Action<Exception>? Faulted;

    /// <summary>Registers how to answer a server-initiated request for one method. Requests for
    /// an unregistered method get a safe default: a successful <c>null</c> result, so the server
    /// never blocks waiting for a response DevStudio doesn't have an opinion about.</summary>
    public void RegisterServerRequestHandler(string method, Func<JsonNode?, JsonNode?> handler) =>
        _serverRequestHandlers[method] = handler;

    public void Start(CancellationToken cancellationToken = default)
    {
        _readLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _readLoop = RunReadLoopAsync(_readLoopCts.Token);
    }

    public async Task<JsonRpcResponse> SendRequestAsync(string method, JsonNode? @params = null, CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _nextId).ToString();
        var request = new JsonRpcRequest(id, method, @params);
        var completion = new TaskCompletionSource<JsonRpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        try
        {
            await _transport.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }

        return await completion.Task.ConfigureAwait(false);
    }

    public Task SendNotificationAsync(string method, JsonNode? @params = null, CancellationToken cancellationToken = default) =>
        _transport.WriteAsync(new JsonRpcNotification(method, @params), cancellationToken);

    private async Task RunReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _transport.ReadMessagesAsync(cancellationToken).ConfigureAwait(false))
            {
                switch (message)
                {
                    case JsonRpcResponse response:
                        if (_pending.TryRemove(response.Id, out var completion))
                        {
                            completion.TrySetResult(response);
                        }
                        break;
                    case JsonRpcNotification notification:
                        NotificationReceived?.Invoke(notification);
                        break;
                    case JsonRpcRequest request:
                        _ = HandleServerRequestAsync(request, cancellationToken);
                        break;
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

    private async Task HandleServerRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        JsonNode? result = null;
        if (_serverRequestHandlers.TryGetValue(request.Method, out var handler))
        {
            try { result = handler(request.Params); }
            catch { result = null; } // A misbehaving handler must not stall the server's request.
        }

        try
        {
            await _transport.WriteAsync(new JsonRpcResponse(request.Id, result), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort — the transport may already be faulted/closing.
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
