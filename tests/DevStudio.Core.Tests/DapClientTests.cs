using System.Text.Json.Nodes;
using System.Threading.Channels;
using DevStudio.Core.Dap;
using Xunit;

namespace DevStudio.Core.Tests;

/// <summary>An in-memory <see cref="IDapTransport"/> for testing <see cref="DapClient"/>'s
/// correlation/event-dispatch logic without a real process or byte-level framing (that framing
/// is tested separately, against real streams, in <c>DapFrameReaderTests</c>).</summary>
file sealed class FakeDapTransport : IDapTransport
{
    private readonly Channel<DapProtocolMessage> _incoming = Channel.CreateUnbounded<DapProtocolMessage>();
    public List<DapProtocolMessage> Sent { get; } = new();
    public bool ThrowOnRead { get; set; }

    public Task WriteAsync(DapProtocolMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }

    public void Push(DapProtocolMessage message) => _incoming.Writer.TryWrite(message);
    public void CompleteReading() => _incoming.Writer.TryComplete();
    public void FailReading(Exception ex) => _incoming.Writer.TryComplete(ex);

    public async IAsyncEnumerable<DapProtocolMessage> ReadMessagesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in _incoming.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return message;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public class DapClientTests
{
    [Fact]
    public async Task SendRequestAsync_assigns_increasing_sequence_numbers()
    {
        var transport = new FakeDapTransport();
        var client = new DapClient(transport);
        client.Start();

        var firstTask = client.SendRequestAsync("threads");
        var secondTask = client.SendRequestAsync("threads");
        await Task.Delay(10);

        Assert.Equal(2, transport.Sent.Count);
        var firstRequest = Assert.IsType<DapRequest>(transport.Sent[0]);
        var secondRequest = Assert.IsType<DapRequest>(transport.Sent[1]);
        Assert.True(secondRequest.Seq > firstRequest.Seq);

        transport.Push(new DapResponse(100, secondRequest.Seq, true, "threads"));
        transport.Push(new DapResponse(101, firstRequest.Seq, true, "threads"));

        await firstTask;
        await secondTask;
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Responses_are_correlated_by_request_seq_even_when_they_arrive_out_of_order()
    {
        var transport = new FakeDapTransport();
        var client = new DapClient(transport);
        client.Start();

        var request1 = client.SendRequestAsync("a");
        var request2 = client.SendRequestAsync("b");
        var request3 = client.SendRequestAsync("c");
        await Task.Delay(10);

        var seq1 = ((DapRequest)transport.Sent[0]).Seq;
        var seq2 = ((DapRequest)transport.Sent[1]).Seq;
        var seq3 = ((DapRequest)transport.Sent[2]).Seq;

        // Deliberately out of order: 3, 1, 2.
        transport.Push(new DapResponse(200, seq3, true, "c", Body: JsonValue.Create("third")));
        transport.Push(new DapResponse(201, seq1, true, "a", Body: JsonValue.Create("first")));
        transport.Push(new DapResponse(202, seq2, true, "b", Body: JsonValue.Create("second")));

        var response1 = await request1;
        var response2 = await request2;
        var response3 = await request3;

        Assert.Equal("first", response1.Body!.GetValue<string>());
        Assert.Equal("second", response2.Body!.GetValue<string>());
        Assert.Equal("third", response3.Body!.GetValue<string>());
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Events_are_dispatched_and_never_mistaken_for_responses()
    {
        var transport = new FakeDapTransport();
        var client = new DapClient(transport);
        var receivedEvents = new List<string>();
        client.EventReceived += evt => receivedEvents.Add(evt.EventName);
        client.Start();

        transport.Push(new DapEvent(1, "initialized"));
        transport.Push(new DapEvent(2, "output", JsonValue.Create("hello")));
        await Task.Delay(20);

        Assert.Equal(new[] { "initialized", "output" }, receivedEvents);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task A_transport_failure_faults_every_pending_request_instead_of_hanging_forever()
    {
        var transport = new FakeDapTransport();
        var client = new DapClient(transport);
        Exception? faulted = null;
        client.Faulted += ex => faulted = ex;
        client.Start();

        var pending = client.SendRequestAsync("threads");
        await Task.Delay(10);
        transport.FailReading(new DapProtocolException("Malformed DAP message: not valid JSON."));

        await Assert.ThrowsAsync<DapProtocolException>(() => pending);
        Assert.NotNull(faulted);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_cancels_any_still_pending_request()
    {
        var transport = new FakeDapTransport();
        var client = new DapClient(transport);
        client.Start();

        var pending = client.SendRequestAsync("threads");
        await client.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
}
