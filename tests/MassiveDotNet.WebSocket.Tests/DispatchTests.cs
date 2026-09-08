using System.Text;
using System.Text.Json;
using MassiveDotNet.WebSocket.Events;
using MassiveDotNet.WebSocket.Internal;
using NodaTime;
using NodaTime.Testing;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// Covers the dispatch that routes an incoming event to its topic sink by <c>ev</c> code, the
/// defects Task 10's review round 1 found there, and the fixes for them: the mixed status/data
/// frame that used to lose ticks (T1/ruling), the sink table's write race that could silently
/// discard a registration (F1), OnMessage no longer gating whether dispatch runs (F2), reading
/// <c>ev</c> through JsonValueReader plus the Skip() regression coverage that was missing (T4
/// ruling / F3), the deterministic mid-dispatch-registration invariant that replaced a
/// thread-based test proving nothing (F4), and completing every sink on disposal (F5).
/// BackpressureTests covers TopicSink/MassiveTopicSubscription in isolation; this file covers
/// MassiveStreamConnection's routing and lifecycle around them.
/// </summary>
public class DispatchTests
{
    private const string Connected = """[{"ev":"status","status":"connected","message":"Connected Successfully"}]""";
    private const string AuthSuccess = """[{"ev":"status","status":"auth_success","message":"authenticated"}]""";

    private static async Task<MassiveStreamConnection> ConnectAsync(FakeWebSocket socket)
    {
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamConnection connection = new(
            new MassiveStreamOptions { ApiKey = "k", HandshakeTimeout = Duration.FromMilliseconds(200) },
            MassiveMarket.Stocks,
            () => socket,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        return connection;
    }

    // Same causality fix as SubscriptionTests.SubscribeAfterSendAsync: the read loop is already
    // parked on its next receive once StartReading has run, so the acknowledgement frame must not
    // be enqueued until the request it answers has actually been sent.
    private static async Task<Task> SubscribeAfterSendAsync(
        MassiveStreamConnection connection, FakeWebSocket socket, string topicCode, string[] tickers, string ackFrame)
    {
        TaskCompletionSource sent = socket.SentSignal;
        Task subscribeTask = connection.SubscribeAsync(topicCode, tickers, TestContext.Current.CancellationToken);

        await sent.Task.WaitAsync(TestContext.Current.CancellationToken);
        socket.EnqueueText(ackFrame);

        return subscribeTask;
    }

    // RULING T1. Before the fix, ReadLoopAsync did `if (DispatchStatusEvents(...)) { continue; }` --
    // a frame carrying a status event never reached OnMessage at all, so a tick sharing that frame
    // vanished with no trace. This frame carries both in one array; both must arrive.
    [Fact]
    public async Task AFrameCarryingBothAStatusAndATickDeliversBoth()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        TopicSink<StockTrade> sink = new("T", capacity: 8, new StockTradeConverter(new TickerPool(16)));
        connection.AddSink(sink);

        Task subscribeTask1 = await SubscribeAfterSendAsync(
            connection,
            socket,
            "T",
            ["AAPL"],
            """[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"},{"ev":"T","sym":"AAPL","i":"1","p":1,"s":1,"t":1,"q":1}]""");

        // The status half: SubscribeAsync's wait only ever resolves once OnStatus matched this
        // exact acknowledgement text, so this not throwing/timing out IS the status path firing.
        await subscribeTask1;

        // A second, later subscribe as a completion barrier for the DATA half. OnStatus's
        // TrySetResult (above) schedules SubscribeAsync's continuation asynchronously, so
        // subscribeTask1 resuming does NOT prove the read loop has also finished the SAME frame's
        // `await handler(...)` (the trade dispatch) -- those race on different threads. What IS
        // guaranteed is loop order: the read loop cannot even begin reading a later frame until the
        // current frame's entire iteration, status dispatch AND OnMessage dispatch, has completed.
        // So once THIS second acknowledgement lands, the first frame's trade is guaranteed already
        // written to the sink.
        Task subscribeTask2 = await SubscribeAfterSendAsync(
            connection, socket, "T", ["MSFT"], """[{"ev":"status","status":"success","message":"subscribed to: T.MSFT"}]""");
        await subscribeTask2;

        sink.Complete();

        List<string> tickers = [];
        await foreach (StockTrade trade in sink.Subscription.WithCancellation(TestContext.Current.CancellationToken))
        {
            tickers.Add(trade.Ticker);
        }

        // The data half: the trade from the SAME frame as the status event reached its sink.
        Assert.Equal(["AAPL"], tickers);
    }

    // F1 (Task 10 review round 1). AddSink's read-copy-publish is not atomic: two concurrent calls
    // can both read the same snapshot, each build a copy holding only its own addition, and the
    // second Volatile.Write silently discards the first sink -- a consumer that subscribes, gets
    // acknowledged, and then never receives anything, with nothing anywhere throwing. Eight
    // threads, 200 distinct topics each -- so every registration is a genuine table growth, not a
    // value replace -- concurrently, and every one of the 1,600 must still be reachable afterward.
    // The outcome checked is a final state after every thread has joined, not a timing window, so
    // this is deterministic rather than merely "probably enough contention": under the fix, the
    // count can only ever be exactly 1,600.
    [Fact]
    public async Task RegisteringManySinksConcurrentlyLosesNone()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        const int ThreadCount = 8;
        const int TopicsPerThread = 200;

        List<TopicSink<StockTrade>> sinks = [];

        for (int t = 0; t < ThreadCount; t++)
        {
            for (int i = 0; i < TopicsPerThread; i++)
            {
                sinks.Add(new TopicSink<StockTrade>($"T{t}-{i}", capacity: 1, new StockTradeConverter(new TickerPool(1))));
            }
        }

        Thread[] threads = new Thread[ThreadCount];

        for (int t = 0; t < ThreadCount; t++)
        {
            int threadIndex = t;

            // Reading sinks[...] concurrently across threads is safe: the list itself is never
            // mutated once built above, only indexed -- each thread touches a disjoint slice.
            threads[t] = new Thread(() =>
            {
                for (int i = 0; i < TopicsPerThread; i++)
                {
                    connection.AddSink(sinks[(threadIndex * TopicsPerThread) + i]);
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

        // One event per registered topic, all in a single frame, so one Dispatch call's snapshot
        // must contain every sink that actually survived registration.
        string frame = "[" + string.Join(
            ',', sinks.Select(sink => $$"""{"ev":"{{sink.TopicCode}}","sym":"AAPL","i":"1","p":1,"s":1,"t":1,"q":1}""")) + "]";
        socket.EnqueueText(frame);

        Task barrier = await SubscribeAfterSendAsync(
            connection, socket, "T", ["BARRIER"], """[{"ev":"status","status":"success","message":"subscribed to: T.BARRIER"}]""");
        await barrier;

        int received = 0;

        foreach (TopicSink<StockTrade> sink in sinks)
        {
            sink.Complete();

            await foreach (StockTrade trade in sink.Subscription.WithCancellation(TestContext.Current.CancellationToken))
            {
                received++;
            }
        }

        Assert.Equal(ThreadCount * TopicsPerThread, received);
    }

    // RULING T4, first half: reading "ev" through JsonValueReader, the way every other field in
    // this codebase is read, rather than reader.GetString() directly -- which throws
    // InvalidOperationException on a non-string token, not the JsonException every other malformed
    // field throws. The same class of finding as Task 9's "sym".
    [Theory]
    [InlineData("""{"ev":{"not":"a string"},"x":1}""")]
    [InlineData("""{"ev":123,"x":1}""")]
    public void AMalformedEventCodeSurfacesAsAJsonException(string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        Utf8JsonReader reader = new(bytes);
        reader.Read(); // StartObject

        // Utf8JsonReader is a ref struct, so Assert.Throws (a delegate) cannot close over it.
        bool threw = false;

        try
        {
            MassiveStreamConnection.ReadEventCode(ref reader);
        }
        catch (JsonException)
        {
            threw = true;
        }

        Assert.True(threw);
    }

    // RULING T4, second half. ReadEventCode carries no shared or static state, so a throw reading
    // one malformed object must not leave anything behind that corrupts an unrelated, later,
    // well-formed read -- exactly the property Task 8's missing-Skip bug broke for
    // CountStatusEvents, its sibling.
    [Fact]
    public void AWellFormedEventCodeStillReadsCorrectlyAfterAMalformedOneWasRefused()
    {
        byte[] malformed = Encoding.UTF8.GetBytes("""{"ev":{"not":"a string"},"x":1}""");
        Utf8JsonReader badReader = new(malformed);
        badReader.Read();

        try
        {
            MassiveStreamConnection.ReadEventCode(ref badReader);
        }
        catch (JsonException)
        {
            // Expected -- see AMalformedEventCodeSurfacesAsAJsonException.
        }

        byte[] wellFormed = Encoding.UTF8.GetBytes("""{"ev":"T","sym":"AAPL"}""");
        Utf8JsonReader goodReader = new(wellFormed);
        goodReader.Read();

        string? code = MassiveStreamConnection.ReadEventCode(ref goodReader);

        Assert.Equal("T", code);
        Assert.Equal(JsonTokenType.EndObject, goodReader.TokenType);
    }

    // The connection-level shape of the same requirement: a malformed ev surfaces as a
    // JsonException at the point a real frame is dispatched, faulting the read loop's task the same
    // way every other unrecoverable frame does (see ReadLoopTests) -- it is not swallowed.
    [Fact]
    public async Task AFrameWithAMalformedEventCodeFaultsTheReadLoop()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        socket.EnqueueText("""[{"ev":{"not":"a string"},"x":1}]""");

        await Assert.ThrowsAsync<JsonException>(
            async () => await connection.ReadLoopTask.WaitAsync(TestContext.Current.CancellationToken));
        Assert.True(connection.ReadLoopTask.IsFaulted);
    }

    // F3 (Task 10 review round 1). ReadEventCode's Skip() applies to BOTH branches -- "ev" itself
    // (a genuine no-op, since JsonValueReader.ReadString already leaves the reader on the scalar
    // it read or has thrown) and every OTHER property, where it is load-bearing: without it, a
    // property whose value is a nested container is walked INTO rather than stepped over, and the
    // reader misreads whatever comes after as if it belonged to the outer event -- the exact shape
    // of the bug Task 8 found in ReadEventCode's sibling, CountStatusEvents. Deleting the Skip()
    // line left all 111 shipped tests green, because nothing exercised an unrecognised property
    // with a nested value. Each case here is a frame with TWO events: the first carries an unknown
    // property ("meta") holding a nested object or array, the second is an ordinary well-formed
    // trade that must still be routed to its sink despite the first event's shape.
    [Theory]
    [InlineData("""{"x":1}""")]
    [InlineData("""[1,2,3]""")]
    public async Task AnUnknownPropertyWithANestedContainerDoesNotHideTheEventAfterIt(string nestedValue)
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        TopicSink<StockTrade> sink = new("T", capacity: 8, new StockTradeConverter(new TickerPool(16)));
        connection.AddSink(sink);

        string frame = $$"""
            [{"ev":"T","meta":{{nestedValue}},"sym":"AAPL","i":"1","p":1,"s":1,"t":1,"q":1},{"ev":"T","sym":"MSFT","i":"2","p":2,"s":2,"t":2,"q":2}]
            """;
        socket.EnqueueText(frame);

        Task barrier = await SubscribeAfterSendAsync(
            connection, socket, "T", ["BARRIER"], """[{"ev":"status","status":"success","message":"subscribed to: T.BARRIER"}]""");
        await barrier;

        sink.Complete();

        List<string> tickers = [];
        await foreach (StockTrade trade in sink.Subscription.WithCancellation(TestContext.Current.CancellationToken))
        {
            tickers.Add(trade.Ticker);
        }

        // Both events reached the sink: the first (whose OWN parse is unaffected, since the
        // converter's replay starts fresh from its own bookmark and has always skipped unknown
        // properties correctly) and the second, which only arrives if ReadEventCode's Skip() kept
        // the OUTER dispatch loop correctly positioned to find it.
        Assert.Equal(["AAPL", "MSFT"], tickers);
    }

    // A no-op ITopicSink whose Write runs an arbitrary side effect, for F4 below: TopicSink<T>
    // itself has no seam for injecting behaviour into a write, and driving the real dispatch loop
    // is the point (as opposed to unit-testing AddSink/Dispatch in isolation).
    private sealed class SideEffectSink(string topicCode, Action onWrite) : ITopicSink
    {
        public string TopicCode { get; } = topicCode;

        public void Write(ref Utf8JsonReader reader) => onWrite();

        public void Complete()
        {
        }
    }

    // F4 (Task 10 review round 1), replacing a thread-based test that passed identically whether
    // AddSink was copy-on-write or a plain in-place Dictionary mutation, and therefore proved
    // nothing. This is the deterministic invariant instead: a sink's Write() call registers a
    // SECOND sink mid-dispatch, on a frame that also carries an event for the topic the new sink
    // claims -- immediately after, in the SAME frame. Dispatch takes its table snapshot once, at
    // the top of the frame, before FIRST's Write runs, so SECOND must NOT receive that event; it
    // must still start receiving on the very next frame, once Dispatch takes a fresh snapshot.
    // Under a plain in-place Dictionary this is red (the mutation is visible immediately, so the
    // AAPL event below would also arrive) -- verified by temporarily reverting the fix; see the
    // task report for the actual numbers observed.
    [Fact]
    public async Task ASinkRegisteredMidDispatchStartsReceivingOnlyOnTheNextFrame()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        TopicSink<StockTrade> secondSink = new("SECOND", capacity: 8, new StockTradeConverter(new TickerPool(16)));
        bool addSinkCalled = false;

        SideEffectSink firstSink = new("FIRST", () =>
        {
            addSinkCalled = true;
            connection.AddSink(secondSink);
        });

        connection.AddSink(firstSink);

        // Frame 1: FIRST's Write registers secondSink mid-dispatch; the SECOND event right after it,
        // in the SAME frame, targets the topic that registration just claimed.
        socket.EnqueueText("""[{"ev":"FIRST"},{"ev":"SECOND","sym":"AAPL","i":"1","p":1,"s":1,"t":1,"q":1}]""");

        // Frame 2: another SECOND event, in its own frame, arriving after registration has had a
        // whole frame to take effect.
        socket.EnqueueText("""[{"ev":"SECOND","sym":"MSFT","i":"2","p":2,"s":2,"t":2,"q":2}]""");

        Task barrier = await SubscribeAfterSendAsync(
            connection, socket, "T", ["BARRIER"], """[{"ev":"status","status":"success","message":"subscribed to: T.BARRIER"}]""");
        await barrier;

        Assert.True(addSinkCalled);
        secondSink.Complete();

        List<string> tickers = [];
        await foreach (StockTrade trade in secondSink.Subscription.WithCancellation(TestContext.Current.CancellationToken))
        {
            tickers.Add(trade.Ticker);
        }

        Assert.Equal(["MSFT"], tickers);
    }

    // F5 (Task 10 review round 1). TopicSink.Complete() existed since Task 10's first round but
    // nothing in src/ ever called it, so a consumer's `await foreach` never ended even after the
    // connection it was reading from had been disposed -- a hang, not an error. Disposal must
    // complete every registered sink so a PENDING enumeration (started before disposal, blocked
    // waiting for the next event) unblocks rather than hanging forever.
    [Fact]
    public async Task DisposingTheConnectionCompletesEveryRegisteredSink()
    {
        await using FakeWebSocket socket = new();
        MassiveStreamConnection connection = await ConnectAsync(socket);

        TopicSink<StockTrade> sink = new("T", capacity: 8, new StockTradeConverter(new TickerPool(16)));
        connection.AddSink(sink);

        Task<List<StockTrade>> consumer = Task.Run(async () =>
        {
            List<StockTrade> received = [];

            await foreach (StockTrade trade in sink.Subscription.WithCancellation(TestContext.Current.CancellationToken))
            {
                received.Add(trade);
            }

            return received;
        }, TestContext.Current.CancellationToken);

        // Not required for correctness -- Complete() unblocks the consumer whenever it started --
        // but gives it a moment to actually be parked on the empty channel, so this exercises the
        // PENDING case the fix is about rather than a foreach that has not started yet.
        await Task.Delay(Duration.FromMilliseconds(20).ToTimeSpan(), TestContext.Current.CancellationToken);

        await connection.DisposeAsync();

        using CancellationTokenSource cts =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(Duration.FromSeconds(5).ToTimeSpan());

        // Bounded rather than awaited directly: if Complete() were never wired, this would hang
        // until the test runner's own much longer timeout instead of failing fast and readably.
        List<StockTrade> result = await consumer.WaitAsync(cts.Token);

        Assert.Empty(result);
    }

    // A sink registered after disposal would never be completed -- the completion loop has already
    // run -- so its consumer's `await foreach` would hang forever with nothing left alive to end it.
    // That is F5's hang arriving through the one door F5 did not close. Refused rather than
    // completed-on-arrival: subscribing to a disposed connection is a programming error, and the
    // BCL's answer to that is ObjectDisposedException, not a sequence that ends before it starts.
    [Fact]
    public async Task RegisteringASinkAfterDisposalIsRefusedRatherThanLeavingItUncompleted()
    {
        await using FakeWebSocket socket = new();
        MassiveStreamConnection connection = await ConnectAsync(socket);

        await connection.DisposeAsync();

        TopicSink<StockTrade> late = new("T", capacity: 8, new StockTradeConverter(new TickerPool(16)));

        Assert.Throws<ObjectDisposedException>(() => connection.AddSink(late));
    }
}
