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
