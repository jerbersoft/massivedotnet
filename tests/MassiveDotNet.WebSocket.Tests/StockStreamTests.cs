using System.Net.WebSockets;
using MassiveDotNet.WebSocket.Events;
using NodaTime;
using NodaTime.Testing;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

// Shares the "StreamConcurrencyStress" collection with MassiveStreamClientTests -- see that
// class's remarks for why: this class's own ConcurrentFirstSubscribesToTheSameTopicAllReturnTheSameSubscription
// blocks dozens of raw OS threads on thread-pool-served continuations, and running it at the same
// time as MassiveStreamClientTests' own heavy test (xUnit runs different classes in parallel by
// default) starved the pool badly enough to produce a spurious multi-second timeout here.
[Collection("StreamConcurrencyStress")]
public class StockStreamTests
{
    private const string Connected = """[{"ev":"status","status":"connected","message":"Connected Successfully"}]""";
    private const string AuthSuccess = """[{"ev":"status","status":"auth_success","message":"authenticated"}]""";

    private static async Task<(MassiveStockStream Stream, FakeWebSocket Socket)> ConnectAsync()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamClient client = new(new MassiveStreamOptions { ApiKey = "k" });
        MassiveStockStream stream = await client.ConnectStocksAsync(() => socket, TestContext.Current.CancellationToken);

        return (stream, socket);
    }

    // Same shape as ConnectAsync, but with an injected clock and a caller-supplied options
    // instance -- H2's DropObserved throttle needs both: a small TopicBufferCapacity to force
    // drops, and a FakeClock to make the one-second throttle window deterministic rather than
    // racing the wall clock.
    private static async Task<(MassiveStockStream Stream, FakeWebSocket Socket)> ConnectAsync(
        MassiveStreamOptions options, IClock clock)
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        // The internal (options, clock) constructor is what makes the throttle testable at all --
        // see the ruling this test exists to pin (H2 in the Task 12 report).
        MassiveStreamClient client = new(options, clock);
        MassiveStockStream stream = await client.ConnectStocksAsync(() => socket, TestContext.Current.CancellationToken);

        return (stream, socket);
    }

    // A ninth pre-flight-adjacent finding (Task 12 report): the brief's own test bodies enqueued
    // each subscribe's acknowledgement BEFORE calling SubscribeTradesAsync/SubscribeQuotesAsync.
    // The read loop starts (StartReading, inside ConnectAsync above) before any of these tests ever
    // call a subscribe method, so it is already idle-parked on its next receive -- a frame enqueued
    // early can be consumed as soon as it lands, racing SubscribeAsync's own publish of what it is
    // waiting for. This is the exact flake ReconnectTests.ADroppedConnectionIsReestablishedAndSubscriptionsReplayed
    // and DispatchTests' SubscribeAfterSendAsync helper already document and guard against
    // elsewhere in this suite; the brief's verbatim tests simply did not carry the same guard.
    // It surfaced here as a genuine, reproducible failure once this task's own heavier concurrency
    // tests (H3) raised system load enough to flip the race -- watched failing for exactly that
    // reason before this fix (see the Task 12 report for the transcript). These two helpers apply
    // the same causality fix everywhere in this file: send first, wait for the send, then enqueue
    // the acknowledgement, so the fake can never "answer" before the request it is answering has
    // actually gone out.
    private static async Task<MassiveTopicSubscription<StockTrade>> SubscribeTradesAsync(
        MassiveStockStream stream, FakeWebSocket socket, string ticker, CancellationToken cancellationToken)
    {
        TaskCompletionSource sent = socket.SentSignal;
        Task<MassiveTopicSubscription<StockTrade>> task = stream.SubscribeTradesAsync([ticker], cancellationToken);

        await sent.Task.WaitAsync(cancellationToken);
        socket.EnqueueText($$"""[{"ev":"status","status":"success","message":"subscribed to: T.{{ticker}}"}]""");

        return await task;
    }

    private static async Task<MassiveTopicSubscription<StockQuote>> SubscribeQuotesAsync(
        MassiveStockStream stream, FakeWebSocket socket, string ticker, CancellationToken cancellationToken)
    {
        TaskCompletionSource sent = socket.SentSignal;
        Task<MassiveTopicSubscription<StockQuote>> task = stream.SubscribeQuotesAsync([ticker], cancellationToken);

        await sent.Task.WaitAsync(cancellationToken);
        socket.EnqueueText($$"""[{"ev":"status","status":"success","message":"subscribed to: Q.{{ticker}}"}]""");

        return await task;
    }

    [Fact]
    public async Task SubscribingTwiceToATopicWidensItRatherThanMintingASecondBuffer()
    {
        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync();
        await using MassiveStockStream _ = stream;

        MassiveTopicSubscription<StockTrade> first =
            await SubscribeTradesAsync(stream, socket, "AAPL", TestContext.Current.CancellationToken);

        MassiveTopicSubscription<StockTrade> second =
            await SubscribeTradesAsync(stream, socket, "MSFT", TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Contains("""{"action":"subscribe","params":"T.MSFT"}""", socket.Sent);
    }

    [Fact]
    public async Task TradesAndQuotesAreSeparateSequences()
    {
        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync();
        await using MassiveStockStream _ = stream;

        MassiveTopicSubscription<StockTrade> trades =
            await SubscribeTradesAsync(stream, socket, "AAPL", TestContext.Current.CancellationToken);

        MassiveTopicSubscription<StockQuote> quotes =
            await SubscribeQuotesAsync(stream, socket, "AAPL", TestContext.Current.CancellationToken);

        socket.EnqueueText(
            """[{"ev":"T","sym":"AAPL","i":"1","p":10,"s":1,"t":1,"q":1},{"ev":"Q","sym":"AAPL","bp":9,"ap":11,"t":1,"q":2}]""");

        await using IAsyncEnumerator<StockTrade> trade = trades.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await using IAsyncEnumerator<StockQuote> quote = quotes.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await trade.MoveNextAsync());
        Assert.Equal(10, trade.Current.Price);

        Assert.True(await quote.MoveNextAsync());
        Assert.Equal(9, quote.Current.BidPrice);
    }

    [Fact]
    public async Task OneFrameCarryingSeveralEventsIsSplitAcrossTheirTopics()
    {
        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync();
        await using MassiveStockStream _ = stream;

        MassiveTopicSubscription<StockTrade> trades =
            await SubscribeTradesAsync(stream, socket, "AAPL", TestContext.Current.CancellationToken);

        socket.EnqueueText(
            """[{"ev":"T","sym":"AAPL","i":"1","p":1,"s":1,"t":1,"q":1},{"ev":"T","sym":"AAPL","i":"2","p":2,"s":1,"t":2,"q":2}]""");

        await using IAsyncEnumerator<StockTrade> enumerator =
            trades.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(1, enumerator.Current.SequenceNumber);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(2, enumerator.Current.SequenceNumber);
    }

    // An event for a topic nobody subscribed to must not throw, because a wildcard subscription
    // and a server-side addition both produce exactly that.
    [Fact]
    public async Task AnEventForAnUnsubscribedTopicIsIgnored()
    {
        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync();
        await using MassiveStockStream _ = stream;

        MassiveTopicSubscription<StockTrade> trades =
            await SubscribeTradesAsync(stream, socket, "AAPL", TestContext.Current.CancellationToken);

        socket.EnqueueText("""[{"ev":"AM","sym":"AAPL","o":1,"c":2},{"ev":"T","sym":"AAPL","i":"1","p":1,"s":1,"t":1,"q":7}]""");

        await using IAsyncEnumerator<StockTrade> enumerator =
            trades.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(7, enumerator.Current.SequenceNumber);
    }

    // H1 (ruling override): the brief exposed Reconnected/ReconnectCount/LastReconnected on the
    // facade but not Faulted, even though Task 11 built an entire single-terminal-stop path whose
    // whole purpose is that a consumer learns WHY a stream stopped for good. Without forwarding it,
    // a consumer sees `await foreach` simply end and cannot tell an authentication failure from
    // their own disposal. Reconnect disabled is the simplest of the three terminal stops
    // (ReconnectTests.ATerminalStopWithReconnectDisabledCompletesARegisteredSink pins the same
    // stop at the connection level); this pins that the FACADE forwards it, not just the internal
    // connection.
    [Fact]
    public async Task ATerminalStopReachesAHandlerRegisteredThroughTheFacade()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamClient client = new(new MassiveStreamOptions { ApiKey = "k", Reconnect = null });
        MassiveStockStream stream = await client.ConnectStocksAsync(() => socket, TestContext.Current.CancellationToken);
        await using MassiveStockStream _ = stream;

        TaskCompletionSource<Exception> faulted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        stream.Faulted += error => faulted.TrySetResult(error);

        socket.AbortNext();

        Exception error = await faulted.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.IsType<WebSocketException>(error);
    }

    // H1, the removal path: unsubscribing through the facade must stop delivery to the connection,
    // the same way `add`/`remove` on Reconnected already does -- proven by registering, then
    // removing, a handler and confirming it is never invoked.
    [Fact]
    public async Task RemovingAFaultedHandlerThroughTheFacadeStopsItFromBeingCalled()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamClient client = new(new MassiveStreamOptions { ApiKey = "k", Reconnect = null });
        MassiveStockStream stream = await client.ConnectStocksAsync(() => socket, TestContext.Current.CancellationToken);
        await using MassiveStockStream _ = stream;

        bool called = false;
        void Handler(Exception error) => called = true;

        stream.Faulted += Handler;
        stream.Faulted -= Handler;

        TaskCompletionSource<Exception> faulted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        stream.Faulted += error => faulted.TrySetResult(error);

        socket.AbortNext();

        await faulted.Task.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.False(called);
    }

    // H2 (ruling override): DropObserved is throttled to at most once a second, measured on the
    // clock MassiveStreamClient already holds -- injecting it is what makes the throttle
    // deterministically testable rather than racing the wall clock. TopicBufferCapacity: 1 forces
    // every second write to evict the first. A widening SubscribeTradesAsync call after each burst
    // is the same causality barrier the rest of this file now uses throughout: the read loop is
    // single-threaded and processes frames strictly in order, so once the barrier's own
    // acknowledgement has been observed, every earlier frame -- including the drops it caused -- is
    // guaranteed to have already run.
    //
    // F7 (Task 12 review round 1): DropObserved now carries the topic code and that topic's own
    // running drop count rather than nothing at all -- updated here to the new signature and to
    // assert on both values, on top of the throttle behaviour itself, which is unchanged.
    [Fact]
    public async Task DropObservedIsThrottledToAtMostOncePerSecond()
    {
        FakeClock clock = new(Instant.FromUnixTimeSeconds(0));
        MassiveStreamOptions options = new() { ApiKey = "k", TopicBufferCapacity = 1 };

        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync(options, clock);
        await using MassiveStockStream _ = stream;

        MassiveTopicSubscription<StockTrade> trades =
            await SubscribeTradesAsync(stream, socket, "AAPL", TestContext.Current.CancellationToken);

        int observed = 0;
        string? lastTopicCode = null;
        long lastDroppedCount = 0;

        stream.DropObserved += (topicCode, droppedCount) =>
        {
            Interlocked.Increment(ref observed);
            lastTopicCode = topicCode;
            Interlocked.Exchange(ref lastDroppedCount, droppedCount);
        };

        // Capacity 1: three writes with nobody reading evict the first two -- two drops, still
        // inside the same second on the fake clock, so only ONE raise should get through.
        socket.EnqueueText(
            """[{"ev":"T","sym":"AAPL","i":"1","p":1,"s":1,"t":1,"q":1},{"ev":"T","sym":"AAPL","i":"2","p":2,"s":1,"t":2,"q":2},{"ev":"T","sym":"AAPL","i":"3","p":3,"s":1,"t":3,"q":3}]""");

        // Widening to a second ticker on the same topic is used purely as a completion barrier here.
        await SubscribeTradesAsync(stream, socket, "MSFT", TestContext.Current.CancellationToken);

        // trades.DroppedCount (read straight off the subscription) sees the TRUE total, 2 -- both
        // drops happened, regardless of throttling. lastDroppedCount is different on purpose: the
        // throttle is edge-triggered, so only the FIRST drop of this window ever reaches
        // RaiseSafely -- the second drop's own call to OnItemDropped is the one that gets
        // throttled out, so the value it would have carried (2) is never raised. What DID get
        // raised carried whatever the running total was at that first drop: 1.
        Assert.Equal(2, trades.DroppedCount);
        Assert.Equal(1, Interlocked.CompareExchange(ref observed, 0, 0));
        Assert.Equal("T", lastTopicCode);
        Assert.Equal(1, Interlocked.Read(ref lastDroppedCount));

        // Advance past the one-second window, then overflow again: this drop must get through.
        clock.Advance(Duration.FromSeconds(2));

        socket.EnqueueText(
            """[{"ev":"T","sym":"AAPL","i":"4","p":4,"s":1,"t":4,"q":4},{"ev":"T","sym":"AAPL","i":"5","p":5,"s":1,"t":5,"q":5}]""");

        await SubscribeTradesAsync(stream, socket, "GOOG", TestContext.Current.CancellationToken);

        // The buffer still held one unread item from the first burst (nothing in this test ever
        // reads trades), so both new writes evict something: true total is 2 (from the first
        // burst) + 2 = 4. The second window's own first drop is the third drop overall, so it
        // raises with the running total AT THAT POINT: 3, not 4 -- same reasoning as above, one
        // window later.
        Assert.Equal(4, trades.DroppedCount);
        Assert.Equal(2, Interlocked.CompareExchange(ref observed, 0, 0));
        Assert.Equal("T", lastTopicCode);
        Assert.Equal(3, Interlocked.Read(ref lastDroppedCount));
    }

    // F1 (Task 12 review round 1, CRITICAL): a throwing DropObserved handler used to propagate
    // straight into the read loop -- OnItemDropped runs there -- and terminate the whole stream:
    // Faulted fired with the HANDLER's own exception, and every sink completed, even though
    // nothing was actually wrong with the connection. A notification that events were dropped is
    // strictly worse than the drop it reports if it can silently end the entire live feed. This is
    // the reviewer's own probe, committed: one handler throws, a second must still see the
    // notification, Faulted must never fire, and the stream must still work afterward.
    [Fact]
    public async Task AThrowingDropObservedHandlerDoesNotKillTheStream()
    {
        FakeClock clock = new(Instant.FromUnixTimeSeconds(0));
        MassiveStreamOptions options = new() { ApiKey = "k", TopicBufferCapacity = 1 };

        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync(options, clock);
        await using MassiveStockStream _ = stream;

        MassiveTopicSubscription<StockTrade> trades =
            await SubscribeTradesAsync(stream, socket, "AAPL", TestContext.Current.CancellationToken);

        bool secondHandlerSaw = false;
        stream.DropObserved += (_, _) => throw new InvalidOperationException("handler blew up");
        stream.DropObserved += (_, _) => secondHandlerSaw = true;

        bool faulted = false;
        stream.Faulted += _ => faulted = true;

        // Capacity 1: two writes with nobody reading evicts the first -- one drop.
        socket.EnqueueText(
            """[{"ev":"T","sym":"AAPL","i":"1","p":1,"s":1,"t":1,"q":1},{"ev":"T","sym":"AAPL","i":"2","p":2,"s":1,"t":2,"q":2}]""");

        // Barrier: once acknowledged, the drop-inducing frame above is guaranteed to have already
        // run, throwing handler and all.
        await SubscribeTradesAsync(stream, socket, "MSFT", TestContext.Current.CancellationToken);

        Assert.Equal(1, trades.DroppedCount);
        Assert.True(secondHandlerSaw, "The second, well-behaved handler never ran -- the first handler's exception starved it.");
        Assert.False(faulted, "DropObserved's own throwing handler must never be treated as a stream fault.");

        // The stream must still be alive and fully functional: a further subscribe still widens
        // the same sequence, exactly as SubscribingTwiceToATopicWidensItRatherThanMintingASecondBuffer
        // pins for the ordinary (non-throwing) case.
        MassiveTopicSubscription<StockTrade> stillWorks =
            await SubscribeTradesAsync(stream, socket, "GOOG", TestContext.Current.CancellationToken);
        Assert.Same(trades, stillWorks);
    }

    // F5 (Task 12 review round 1): a disposed stream's ObjectDisposedException used to name
    // MassiveDotNet.WebSocket.Internal.MassiveStreamConnection -- a type a consumer never held a
    // reference to and cannot act on. Confirmed by the reviewer's own probe; this is that probe,
    // committed.
    [Fact]
    public async Task ADisposedStreamThrowsNamingItselfNotTheInternalConnection()
    {
        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync();
        _ = socket;

        await stream.DisposeAsync();

        ObjectDisposedException error = await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await stream.SubscribeTradesAsync(["AAPL"], TestContext.Current.CancellationToken));

        Assert.Equal(typeof(MassiveStockStream).FullName, error.ObjectName);
    }

    // H3(c) (ruling override): SubscribeTradesAsync's `if (_trades is null) { ... }` was unguarded.
    // Two concurrent first-time callers can both observe the field as null, each mint their own
    // TopicSink, and each register it with the connection -- the second AddSink call silently
    // overwrites the first in the connection's sink table, so a caller bound to the sink that lost
    // that race would receive nothing, ever, breaking the exact invariant
    // SubscribingTwiceToATopicWidensItRatherThanMintingASecondBuffer pins sequentially, just under
    // concurrency where that test cannot see it.
    //
    // A raw thread race on a single pair of calls was rejected: the return statement reads the
    // field only after its own network round trip completes, so whether two racing callers end up
    // with DIFFERENT Subscription instances depends on a very narrow interleaving of the mint and
    // the AddSink call that a two-thread race is not reliably going to hit -- confirmed empirically:
    // this sandbox has 16 logical cores, and with a thread count at or below that, every one of the
    // 16+ threads this was tried with got its own core and never needed to be preempted mid-section,
    // so the race never manifested in dozens of runs even against the unfixed code. 64 threads,
    // released simultaneously off a Barrier so the OS is forced to actually time-slice and preempt
    // mid-section, reproduced it reliably (confirmed red 8/8 runs against the unfixed shape, then
    // 5/5 more after a deliberate regression -- see the Task 12 report for both transcripts), the
    // same reasoning DispatchTests.RegisteringManySinksConcurrentlyLosesNone uses for high thread
    // counts on the same class of unguarded check-then-act race.
    //
    // H9 (Task 12 review round 1): a green run of this test is not proof the guard is intact on
    // every machine. The 64-thread count is what forces preemption HERE, on a 16-core sandbox; a
    // runner with materially more cores could let every thread run its critical section
    // uninterrupted the same way 16 threads did here, and the test would then pass whether or not
    // the fix is present -- a false negative, not a flake. Nothing currently sizes this against
    // Environment.ProcessorCount, so treat a pass here as coverage on THIS class of machine, not as
    // a portable guarantee.
    //
    // F2 (Task 12 review round 1): a prior version of this test drove the acknowledgement round
    // trip through a dedicated responder thread reacting to each send, and a 22-run full-solution
    // reproduction found it still flaked about 9% of the time -- a false FAILURE on correct code,
    // worse than the false negative above because it breaks a green build. FakeWebSocket.AutoAcknowledgeSubscribes
    // removes the scheduling dependency entirely rather than tuning around it: the acknowledgement
    // is queued synchronously as part of the send itself, before SubscribeAsync ever starts
    // waiting, so there is no window in which a separate thread or task could fail to "notice" a
    // send in time. What 64 threads still contend on is exactly and only the sink-creation race
    // this test exists to catch.
    [Fact]
    public async Task ConcurrentFirstSubscribesToTheSameTopicAllReturnTheSameSubscription()
    {
        const int ParticipantCount = 64;

        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync();
        await using MassiveStockStream _ = stream;

        socket.AutoAcknowledgeSubscribes = true;

        using Barrier startGate = new(ParticipantCount);
        MassiveTopicSubscription<StockTrade>[] results = new MassiveTopicSubscription<StockTrade>[ParticipantCount];
        Exception?[] failures = new Exception?[ParticipantCount];
        Thread[] threads = new Thread[ParticipantCount];

        for (int i = 0; i < ParticipantCount; i++)
        {
            int index = i;

            threads[i] = new Thread(() =>
            {
                try
                {
                    startGate.SignalAndWait();
                    results[index] = stream
                        .SubscribeTradesAsync(["AAPL"], TestContext.Current.CancellationToken)
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception error)
                {
                    failures[index] = error;
                }
            });
        }

        foreach (Thread thread in threads)
        {
            thread.Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        Assert.All(failures, failure => Assert.Null(failure));

        // Reference identity alone is not a reliable discriminator here and was watched proving
        // exactly that (see the Task 12 report): every caller reads the shared _trades field only
        // AFTER its own network round trip completes, and that late read almost always observes
        // whichever sink's field-write happened chronologically last -- REGARDLESS of which sink's
        // AddSink call happened last. Those two "lasts" can differ under the unfixed race (thread X
        // writes the field, thread Y's AddSink call lands after X's own, or vice versa), so every
        // caller can end up holding the SAME orphaned Subscription reference, one the connection's
        // dispatch table was never wired to. Assert.Single below passed even against the unfixed
        // code in repeated runs; the assertion that actually caught the bug is the one after it --
        // whether the subscription every caller was handed is the one the connection actually
        // dispatches to.
        MassiveTopicSubscription<StockTrade>[] distinct = [.. results.Distinct()];
        Assert.Single(distinct);

        socket.EnqueueText("""[{"ev":"T","sym":"AAPL","i":"final","p":42,"s":1,"t":1,"q":999}]""");

        // Not `await using`: ChannelReader<T>.ReadAllAsync's enumerator (what
        // MassiveTopicSubscription<T> hands out) throws NotSupportedException from its own
        // DisposeAsync when disposed after a MoveNextAsync it was driving was cancelled out from
        // under it -- a secondary fault from cleanup that masked this test's real, primary
        // assertion the first few times this was run against the unfixed code. Disposed manually,
        // only on the success path; the stream's own disposal a few lines below completes the
        // channel regardless.
        IAsyncEnumerator<StockTrade> enumerator = distinct[0].GetAsyncEnumerator(TestContext.Current.CancellationToken);

        using CancellationTokenSource receiveTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        receiveTimeout.CancelAfter(Duration.FromSeconds(5).ToTimeSpan());

        bool moved;

        try
        {
            moved = await enumerator.MoveNextAsync().AsTask().WaitAsync(receiveTimeout.Token);
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            moved = false;
        }

        Assert.True(
            moved,
            "The subscription every concurrent caller was handed never received the event the "
                + "connection actually dispatched -- it was orphaned by a lost sink registration.");

        // Read Current, THEN dispose -- DisposeAsync() resets the underlying
        // ChannelReader<T>.ReadAllAsync enumerator's Current back to default, so checking it
        // afterward would assert against a struct that was never actually read (a second, purely
        // test-authoring bug this test's own DEBUG instrumentation caught while chasing the real
        // one above; see the Task 12 report).
        Assert.Equal(999, enumerator.Current.SequenceNumber);

        if (moved)
        {
            await enumerator.DisposeAsync();
        }
    }
}
