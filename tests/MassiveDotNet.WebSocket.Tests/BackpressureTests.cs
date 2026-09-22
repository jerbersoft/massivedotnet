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

    private static void FeedAggregate(TopicSink<StockAggregate> sink, string json)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        reader.Read();
        sink.Write(ref reader);
    }

    private static TopicSink<StockAggregate> CreateAggregateSink(int capacity) =>
        new("AM", capacity, new StockAggregateConverter(new TickerPool(16), "AM"));

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

    // Issue #65 / D-W19. Before this, a value the converter refused threw out of Write, escaped
    // Dispatch, missed the read loop's reconnect filter (G3, deliberately), and ended the entire
    // connection -- taking every OTHER topic sharing that socket down with it. One symbol's bad
    // field, on a topic the consumer may not even have subscribed to, killed the trade feed. Now
    // the one event is dropped and counted, and the next one parses.
    //
    // Both refusal cases the production log could not tell apart, because the drop must not depend
    // on which one it was. "z":12.0 is the one easiest to overlook -- a valid JSON number, a whole
    // value, refused anyway because the token is not an integer token -- and the second is a value
    // genuinely past long's range.
    [Theory]
    [InlineData("12.0")]
    [InlineData("9223372036854775808")]
    public async Task AnEventTheConverterRefusesIsDroppedAndCountedRatherThanThrown(string badAverageTradeSize)
    {
        TopicSink<StockAggregate> sink = CreateAggregateSink(capacity: 8);

        FeedAggregate(sink, $$"""{"ev":"AM","sym":"MSFT","v":1,"z":{{badAverageTradeSize}},"s":1,"e":2}""");
        FeedAggregate(sink, """{"ev":"AM","sym":"MSFT","v":1,"z":7,"s":3,"e":4}""");
        sink.Complete();

        List<long> sizes = [];
        await foreach (StockAggregate bar in sink.Subscription.WithCancellation(TestContext.Current.CancellationToken))
        {
            sizes.Add(bar.AverageTradeSize);
        }

        // The malformed bar is gone and the well-formed one that followed it arrived.
        Assert.Equal([7L], sizes);

        // Its own counter, not DroppedCount (D-W20). A buffer overflow is the consumer's own
        // backpressure and they fix it by raising TopicBufferCapacity; this is Massive's wire being
        // off-schema and there is nothing they can do about it. Folding the two together would hand
        // a consumer advice that cannot work.
        Assert.Equal(1, sink.Subscription.MalformedCount);
        Assert.Equal(0, sink.Subscription.DroppedCount);
    }

    // The evidence route. A malformed event was terminal until issue #65, so its message reached a
    // consumer through Faulted; now that the connection survives, this seam is the ONLY way the
    // refused value leaves the SDK. Without it, the fix for the outage would swallow the evidence
    // for the fix that is still outstanding (whether `z` needs a wider read at all).
    [Fact]
    public void ARefusedEventRaisesEventMalformedCarryingTheValue()
    {
        TopicSink<StockAggregate> sink = CreateAggregateSink(capacity: 8);

        JsonException? observed = null;
        sink.EventMalformed += error => observed = error;

        FeedAggregate(sink, """{"ev":"AM","sym":"MSFT","v":1,"z":12.0,"s":1,"e":2}""");

        // The `!` is not redundant: `observed` is assigned inside a lambda, which the nullable
        // analyzer cannot follow across the raise, so Assert.NotNull does not narrow it here.
        Assert.NotNull(observed);
        Assert.Contains("StockAggregate.z", observed!.Message, StringComparison.Ordinal);
        Assert.Contains("12.0", observed!.Message, StringComparison.Ordinal);
    }
}
