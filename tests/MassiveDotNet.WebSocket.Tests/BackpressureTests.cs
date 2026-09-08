using System.Text;
using System.Text.Json;
using MassiveDotNet.WebSocket.Events;
using MassiveDotNet.WebSocket.Internal;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

public class BackpressureTests
{
    private static TopicSink<StockTrade> CreateSink(int capacity) =>
        new("T", capacity, new StockTradeConverter(new TickerPool(16)));

    private static void Feed(TopicSink<StockTrade> sink, int sequence)
    {
        byte[] json = Encoding.UTF8.GetBytes(
            $$"""{"ev":"T","sym":"MSFT","i":"{{sequence}}","p":1,"s":1,"t":1,"q":{{sequence}}}""");

        Utf8JsonReader reader = new(json);
        reader.Read();
        sink.Write(ref reader);
    }

    [Fact]
    public async Task EventsArriveInOrderWithinATopic()
    {
        TopicSink<StockTrade> sink = CreateSink(capacity: 8);

        Feed(sink, 1);
        Feed(sink, 2);
        sink.Complete();

        List<long> sequences = [];
        await foreach (StockTrade trade in sink.Subscription.WithCancellation(TestContext.Current.CancellationToken))
        {
            sequences.Add(trade.SequenceNumber);
        }

        Assert.Equal([1, 2], sequences);
    }

    // The buffer is the whole memory budget: capacity times event size, times topics subscribed.
    // Nothing grows with time or with messages received.
    [Fact]
    public async Task AFullBufferDropsTheOldestAndKeepsTheNewest()
    {
        TopicSink<StockTrade> sink = CreateSink(capacity: 2);

        Feed(sink, 1);
        Feed(sink, 2);
        Feed(sink, 3);
        sink.Complete();

        List<long> sequences = [];
        await foreach (StockTrade trade in sink.Subscription.WithCancellation(TestContext.Current.CancellationToken))
        {
            sequences.Add(trade.SequenceNumber);
        }

        Assert.Equal([2, 3], sequences);
    }

    // D-W4's cost is that a consumer who never reads this loses data quietly. The count is what
    // makes that recoverable, and it is exact rather than inferred: Channel reports the eviction.
    [Fact]
    public void EveryDropIsCountedExactly()
    {
        TopicSink<StockTrade> sink = CreateSink(capacity: 2);

        for (int i = 1; i <= 7; i++)
        {
            Feed(sink, i);
        }

        Assert.Equal(5, sink.Subscription.DroppedCount);
    }

    [Fact]
    public void WritingNeverBlocksEvenWhenTheBufferIsFull()
    {
        TopicSink<StockTrade> sink = CreateSink(capacity: 1);

        // If a full buffer could block the writer, this would deadlock: every topic shares one
        // read loop, so a waiting writer stalls the socket and gets the connection dropped.
        for (int i = 0; i < 1000; i++)
        {
            Feed(sink, i);
        }

        Assert.Equal(999, sink.Subscription.DroppedCount);
    }

    // Final review, F2: the guard used to be a one-way latch, so a consumer whose `await foreach`
    // was cancelled -- a timeout, a shutdown, a caller taking a break -- permanently burned that
    // topic's only sequence. Re-subscribing hands back this same object (D-W3 gives a topic one
    // buffer however many times it is subscribed), so the next consumer got an
    // InvalidOperationException claiming the sequence was "already being enumerated" when nobody
    // was, with no recovery short of tearing down the whole stream. D-W3's argument is about two
    // loops stealing from each other CONCURRENTLY, which ASecondEnumerationThrows below still pins.
    [Fact]
    public async Task ACancelledEnumerationReleasesTheSequenceForTheNextConsumer()
    {
        TopicSink<StockTrade> sink = CreateSink(capacity: 4);
        Feed(sink, 1);

        using CancellationTokenSource cts = new();

        await foreach (StockTrade _ in sink.Subscription.WithCancellation(cts.Token))
        {
            await cts.CancelAsync();
            break;
        }

        Feed(sink, 2);

        // The whole point: this must not throw. Before the fix it threw InvalidOperationException.
        await using IAsyncEnumerator<StockTrade> second =
            sink.Subscription.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await second.MoveNextAsync());
    }

    // SingleReader is a performance contract the runtime does not police, so the guard is ours.
    [Fact]
    public void ASecondEnumerationThrows()
    {
        TopicSink<StockTrade> sink = CreateSink(capacity: 4);

        _ = sink.Subscription.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.Throws<InvalidOperationException>(() =>
            sink.Subscription.GetAsyncEnumerator(TestContext.Current.CancellationToken));
    }
}
