using System.Text.Json.Nodes;
using System.Threading.Channels;
using DevStudio.Core.Lsp;
using Xunit;

namespace DevStudio.Core.Tests;

file sealed class FakeJsonRpcTransport : IJsonRpcTransport
{
    private readonly Channel<JsonRpcMessage> _incoming = Channel.CreateUnbounded<JsonRpcMessage>();
    public List<JsonRpcMessage> Sent { get; } = new();

    public Task WriteAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }

    public void Push(JsonRpcMessage message) => _incoming.Writer.TryWrite(message);
    public void FailReading(Exception ex) => _incoming.Writer.TryComplete(ex);

    public async IAsyncEnumerable<JsonRpcMessage> ReadMessagesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in _incoming.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return message;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class JsonRpcClientTests
{
    [Fact]
    public async Task Responses_are_correlated_by_id_even_when_they_arrive_out_of_order()
    {
        var transport = new FakeJsonRpcTransport();
        var client = new JsonRpcClient(transport);
        client.Start();

        var request1 = client.SendRequestAsync("a");
        var request2 = client.SendRequestAsync("b");
        var request3 = client.SendRequestAsync("c");
        await Task.Delay(10);

        var id1 = ((JsonRpcRequest)transport.Sent[0]).Id;
        var id2 = ((JsonRpcRequest)transport.Sent[1]).Id;
        var id3 = ((JsonRpcRequest)transport.Sent[2]).Id;

        transport.Push(new JsonRpcResponse(id3, JsonValue.Create("third")));
        transport.Push(new JsonRpcResponse(id1, JsonValue.Create("first")));
        transport.Push(new JsonRpcResponse(id2, JsonValue.Create("second")));

        Assert.Equal("first", (await request1).Result!.GetValue<string>());
        Assert.Equal("second", (await request2).Result!.GetValue<string>());
        Assert.Equal("third", (await request3).Result!.GetValue<string>());
        await client.DisposeAsync();
    }

    [Fact]
    public async Task An_error_response_is_reported_as_unsuccessful_with_the_error_message()
    {
        var transport = new FakeJsonRpcTransport();
        var client = new JsonRpcClient(transport);
        client.Start();

        var pending = client.SendRequestAsync("willFail");
        await Task.Delay(10);
        var id = ((JsonRpcRequest)transport.Sent[0]).Id;
        transport.Push(new JsonRpcResponse(id, null, new JsonRpcError(-32601, "Method not found")));

        var response = await pending;
        Assert.False(response.Success);
        Assert.Equal("Method not found", response.Error!.Message);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Notifications_are_dispatched_and_never_mistaken_for_responses()
    {
        var transport = new FakeJsonRpcTransport();
        var client = new JsonRpcClient(transport);
        var received = new List<string>();
        client.NotificationReceived += n => received.Add(n.Method);
        client.Start();

        transport.Push(new JsonRpcNotification("window/logMessage", JsonValue.Create("hi")));
        transport.Push(new JsonRpcNotification("textDocument/publishDiagnostics"));
        await Task.Delay(20);

        Assert.Equal(new[] { "window/logMessage", "textDocument/publishDiagnostics" }, received);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task A_server_initiated_request_with_a_registered_handler_is_answered_with_its_result()
    {
        var transport = new FakeJsonRpcTransport();
        var client = new JsonRpcClient(transport);
        client.RegisterServerRequestHandler("workspace/configuration", _ => new JsonArray(JsonValue.Create("configValue")));
        client.Start();

        transport.Push(new JsonRpcRequest("server-1", "workspace/configuration"));
        await Task.Delay(50);

        var sentResponse = Assert.IsType<JsonRpcResponse>(transport.Sent.Single());
        Assert.Equal("server-1", sentResponse.Id);
        Assert.True(sentResponse.Success);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task A_server_initiated_request_with_no_registered_handler_gets_a_safe_null_result_not_a_hang()
    {
        var transport = new FakeJsonRpcTransport();
        var client = new JsonRpcClient(transport);
        client.Start();

        transport.Push(new JsonRpcRequest("server-1", "client/registerCapability"));
        await Task.Delay(50);

        var sentResponse = Assert.IsType<JsonRpcResponse>(transport.Sent.Single());
        Assert.True(sentResponse.Success);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task A_transport_failure_faults_every_pending_request_instead_of_hanging_forever()
    {
        var transport = new FakeJsonRpcTransport();
        var client = new JsonRpcClient(transport);
        Exception? faulted = null;
        client.Faulted += ex => faulted = ex;
        client.Start();

        var pending = client.SendRequestAsync("threads");
        await Task.Delay(10);
        transport.FailReading(new JsonRpcProtocolException("Malformed JSON-RPC message: not valid JSON."));

        await Assert.ThrowsAsync<JsonRpcProtocolException>(() => pending);
        Assert.NotNull(faulted);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_cancels_any_still_pending_request()
    {
        var transport = new FakeJsonRpcTransport();
        var client = new JsonRpcClient(transport);
        client.Start();

        var pending = client.SendRequestAsync("threads");
        await client.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
}
