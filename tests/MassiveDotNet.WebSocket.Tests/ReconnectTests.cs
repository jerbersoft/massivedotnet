using System.Net.WebSockets;
using MassiveDotNet.WebSocket.Events;
using MassiveDotNet.WebSocket.Internal;
using NodaTime;
using NodaTime.Testing;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

public class ReconnectTests
{
    private const string Connected = """[{"ev":"status","status":"connected","message":"Connected Successfully"}]""";
    private const string AuthSuccess = """[{"ev":"status","status":"auth_success","message":"authenticated"}]""";
    private const string SubscribedTrades = """[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""";

    private static MassiveStreamOptions FastReconnect() => new()
    {
        ApiKey = "k",
        Reconnect = new MassiveStreamReconnectOptions
        {
            InitialBackoff = Duration.FromMilliseconds(1),
            MaxBackoff = Duration.FromMilliseconds(5),
            Jitter = 0,
        },
    };

    private static TopicSink<StockTrade> CreateSink() =>
        new("T", capacity: 8, new StockTradeConverter(new TickerPool(16)));

    // Bounded rather than awaited directly: if a terminal stop never completed the sink, this would
    // hang until the test runner's own much longer timeout instead of failing fast and readably
    // (same shape as DispatchTests.DisposingTheConnectionCompletesEveryRegisteredSink).
    private static async Task<List<StockTrade>> ReadAllAsync(TopicSink<StockTrade> sink, CancellationToken cancellationToken)
    {
        Task<List<StockTrade>> consumer = Task.Run(async () =>
        {
            List<StockTrade> received = [];

            await foreach (StockTrade trade in sink.Subscription.WithCancellation(cancellationToken))
            {
                received.Add(trade);
            }

            return received;
        }, cancellationToken);

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Duration.FromSeconds(5).ToTimeSpan());

        return await consumer.WaitAsync(cts.Token);
    }

    [Fact]
    public async Task ADroppedConnectionIsReestablishedAndSubscriptionsReplayed()
    {
        FakeWebSocket first = new();
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);
        second.EnqueueText(SubscribedTrades);

        await using MassiveStreamConnection connection = new(
            FastReconnect(),
            MassiveMarket.Stocks,
            () => created++ == 0 ? first : second,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        // A real server cannot acknowledge a subscribe before it has received it, but a
        // FakeWebSocket can: the read loop is already running (StartReading, above) and
        // idle-parked on its next receive, so a frame enqueued before SubscribeAsync is even
        // called can be consumed as soon as it lands, racing SubscribeAsync's own publish of what
        // it is waiting for -- observed as a genuine flake under full-suite parallel load (a
        // MassiveStreamSubscriptionException from the 10-second HandshakeTimeout default), not a
        // hypothetical. Same causality fix as SubscriptionTests.SubscribeAfterSendAsync and
        // DispatchTests.SubscribeAfterSendAsync: capture SentSignal before starting the call, then
        // await it before enqueueing the acknowledgement, so the fake cannot "answer" until the
        // request it is answering has actually been sent.
        TaskCompletionSource sent = first.SentSignal;
        Task subscribeTask = connection.SubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);
        await sent.Task.WaitAsync(TestContext.Current.CancellationToken);
        first.EnqueueText(SubscribedTrades);
        await subscribeTask;

        TaskCompletionSource reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Reconnected += _ => reconnected.TrySetResult();

        first.AbortNext();
        await reconnected.Task.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Equal(1, connection.ReconnectCount);
        Assert.Contains("""{"action":"auth","params":"k"}""", second.Sent);
        Assert.Contains("""{"action":"subscribe","params":"T.AAPL"}""", second.Sent);
    }

    // Reconnecting against a key the server rejects hammers it until the account is limited, and
    // the server closes abruptly after auth_failed anyway, so a retry loop reconnects into a
    // refusal (D-W6).
    [Fact]
    public async Task AuthenticationFailureIsTerminalAndNeverRetried()
    {
        int created = 0;

        FakeWebSocket first = new();
        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        FakeWebSocket second = new();
        second.EnqueueText(Connected);
        second.EnqueueText("""[{"ev":"status","status":"auth_failed","message":"authentication failed"}]""");

        await using MassiveStreamConnection connection = new(
            FastReconnect(),
            MassiveMarket.Stocks,
            () => created++ == 0 ? first : second,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        TaskCompletionSource<Exception> faulted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Faulted += error => faulted.TrySetResult(error);

        first.AbortNext();
        Exception error = await faulted.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.IsType<MassiveStreamAuthenticationException>(error);
        Assert.Equal(2, created);   // one retry attempt, refused, then stop
    }

    [Fact]
    public void BackoffGrowsGeometricallyAndIsCappedAtMaxBackoff()
    {
        MassiveStreamReconnectOptions options = new()
        {
            InitialBackoff = Duration.FromSeconds(1),
            MaxBackoff = Duration.FromSeconds(4),
            BackoffMultiplier = 2.0,
            Jitter = 0,
        };

        Assert.Equal(Duration.FromSeconds(1), MassiveStreamConnection.BackoffFor(options, attempt: 0, jitterFactor: 0));
        Assert.Equal(Duration.FromSeconds(2), MassiveStreamConnection.BackoffFor(options, attempt: 1, jitterFactor: 0));
        Assert.Equal(Duration.FromSeconds(4), MassiveStreamConnection.BackoffFor(options, attempt: 2, jitterFactor: 0));
        Assert.Equal(Duration.FromSeconds(4), MassiveStreamConnection.BackoffFor(options, attempt: 9, jitterFactor: 0));
    }

    [Fact]
    public void JitterMovesTheDelayWithinTheConfiguredProportion()
    {
        MassiveStreamReconnectOptions options = new()
        {
            InitialBackoff = Duration.FromSeconds(10),
            MaxBackoff = Duration.FromSeconds(60),
            Jitter = 0.2,
        };

        Assert.Equal(Duration.FromSeconds(8), MassiveStreamConnection.BackoffFor(options, attempt: 0, jitterFactor: -1));
        Assert.Equal(Duration.FromSeconds(12), MassiveStreamConnection.BackoffFor(options, attempt: 0, jitterFactor: 1));
    }

    [Fact]
    public async Task ReconnectDisabledSurfacesTheDropInsteadOfHidingIt()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamOptions options = new() { ApiKey = "k", Reconnect = null };

        await using MassiveStreamConnection connection = new(
            options, MassiveMarket.Stocks, () => socket, new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        TaskCompletionSource<Exception> faulted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Faulted += error => faulted.TrySetResult(error);

        socket.AbortNext();
        Exception error = await faulted.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Equal(0, connection.ReconnectCount);
        Assert.NotNull(error);
    }

    // G7 (Task 11 review): narrowing ReadLoopTests' ACleanCloseFaultsTheLoopTaskWithAStreamException
    // and AnAbruptDropFaultsTheLoopTask to Reconnect = null -- so they keep pinning how a drop is
    // REPORTED when it is not being reconnected -- gives up coverage of a clean close under
    // reconnect-on. This is that coverage restored: a graceful server-initiated close is exactly as
    // reconnectable as an abrupt drop. FrameReader does not distinguish the two -- both surface as
    // "the stream closed while a message was being read" -- so the reconnect filter (WebSocketException
    // or MassiveStreamException) must not either.
    [Fact]
    public async Task ACleanCloseReconnectsJustLikeAnAbruptDrop()
    {
        FakeWebSocket first = new();
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        await using MassiveStreamConnection connection = new(
            FastReconnect(),
            MassiveMarket.Stocks,
            () => created++ == 0 ? first : second,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        TaskCompletionSource reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Reconnected += _ => reconnected.TrySetResult();

        first.EnqueueClose(WebSocketCloseStatus.NormalClosure, "bye");
        await reconnected.Task.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Equal(1, connection.ReconnectCount);
    }

    // Finding 6's fix (Task 11 review round 1) rebuilt LastReconnected as a raw long written with
    // Volatile and read back against a NoReconnectYet sentinel, so that a cross-thread reader can
    // never observe a half-written Instant. None of that machinery was covered: nothing asserted
    // LastReconnected at all, so an inverted sentinel or the wrong epoch unit would have shipped
    // silently. An injected FakeClock is what makes the value exact rather than merely non-null,
    // and it is deliberately set to a NON-zero instant, so that an assertion on the exact value can
    // catch a write and a read that disagree about the epoch unit. Watched failing both ways before
    // being committed: writing ToUnixTimeMilliseconds against a FromUnixTimeTicks read fails on the
    // value, and a getter that never yields null fails on the Assert.Null above. What it does NOT
    // pin is the sentinel's particular value -- the field is initialised to whatever the sentinel
    // is, so 0 would pass here too, and only a connection reconnecting at exactly the Unix epoch
    // would tell them apart. long.MinValue is right because it sits outside any range a live clock
    // produces; that is an argument, not something this test proves.
    [Fact]
    public async Task LastReconnectedIsUnsetUntilAReconnectAndThenReadsTheInjectedClock()
    {
        FakeWebSocket first = new();
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        Instant reconnectedAt = Instant.FromUnixTimeSeconds(1_757_000_000);

        await using MassiveStreamConnection connection = new(
            FastReconnect(),
            MassiveMarket.Stocks,
            () => created++ == 0 ? first : second,
            new FakeClock(reconnectedAt));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        Assert.Null(connection.LastReconnected);

        TaskCompletionSource reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Reconnected += _ => reconnected.TrySetResult();

        first.AbortNext();
        await reconnected.Task.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Equal(reconnectedAt, connection.LastReconnected);
    }

    // The FOURTH appearance of one shape on this branch: a consumer's event handler throwing, on a
    // thread that treats an escaping exception as fatal. Task 11 fixed it for Faulted, Task 12's
    // review round 1 fixed it for DropObserved -- and Reconnected, raised twenty lines from
    // Faulted's own guard, was still a bare Invoke inside TryReconnectAsync's try. None of that
    // try's catches (auth, OCE, WebSocketException-or-MassiveStreamException) match an arbitrary
    // handler exception, so it escaped to ReadLoopAsync's outer catch and killed the stream through
    // StopPermanently -- immediately after a reconnect that had just SUCCEEDED, which is the worst
    // possible moment to discard a healthy connection.
    [Fact]
    public async Task AThrowingReconnectedHandlerNeitherKillsTheStreamNorStarvesOtherHandlers()
    {
        FakeWebSocket first = new();
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        await using MassiveStreamConnection connection = new(
            FastReconnect(),
            MassiveMarket.Stocks,
            () => created++ == 0 ? first : second,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        List<Exception> faults = [];
        connection.Faulted += faults.Add;

        // Registered BEFORE the good handler on purpose: a multicast delegate stops at the first
        // throwing subscriber, so the reverse order would pass even with no guard at all.
        connection.Reconnected += _ => throw new InvalidOperationException("handler blew up");

        TaskCompletionSource reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Reconnected += _ => reconnected.TrySetResult();

        first.AbortNext();
        await reconnected.Task.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Equal(1, connection.ReconnectCount);
        Assert.Empty(faults);
        Assert.False(connection.ReadLoopTask.IsFaulted);
    }

    // G4 (Task 11 review): the brief's TryReconnectAsync raised Faulted for two of the three
    // terminal stops and completed a sink for none of them, so every terminal stop left a
    // consumer's `await foreach` parked forever -- exactly the hang Task 10's F5 closed, arriving
    // through a third door. This pins the first terminal stop: reconnect declined outright.
    [Fact]
    public async Task ATerminalStopWithReconnectDisabledCompletesARegisteredSink()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamOptions options = new() { ApiKey = "k", Reconnect = null };

        await using MassiveStreamConnection connection = new(
            options, MassiveMarket.Stocks, () => socket, new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        TopicSink<StockTrade> sink = CreateSink();
        connection.AddSink(sink);
        connection.StartReading();

        socket.AbortNext();

        List<StockTrade> received = await ReadAllAsync(sink, TestContext.Current.CancellationToken);

        Assert.Empty(received);
    }

    // G4/G3 together: a malformed event is a terminal stop even with reconnect ON, since it never
    // enters the reconnect filter at all (see MassiveStreamConnection.ReadLoopAsync). This pins the
    // second terminal stop: a non-reconnectable exception out of Dispatch.
    [Fact]
    public async Task ATerminalStopFromANonReconnectableJsonExceptionCompletesARegisteredSink()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        // Reconnect stays ON here, deliberately: G3's point is that a malformed frame is not
        // reconnected even when reconnect is available, so this must still be a terminal stop.
        await using MassiveStreamConnection connection = new(
            FastReconnect(), MassiveMarket.Stocks, () => socket, new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        TopicSink<StockTrade> sink = CreateSink();
        connection.AddSink(sink);
        connection.StartReading();

        socket.EnqueueText("""[{"ev":{"not":"a string"},"x":1}]""");

        List<StockTrade> received = await ReadAllAsync(sink, TestContext.Current.CancellationToken);

        Assert.Empty(received);
    }

    // G4, from the other direction: F5's rule ("never complete on a transient read-loop fault") is
    // unchanged by this task, so a drop that reconnects successfully must NOT complete the sink --
    // getting this backwards would silently break every consumer's stream on the very first drop.
    // A real event sent AFTER the reconnect is the evidence: if the sequence had been (wrongly)
    // completed on the transient recovery, TopicSink.Write's TryWrite would be a silent no-op
    // against an already-completed channel and this would never arrive, surfacing as a timeout
    // rather than a wrong value.
    [Fact]
    public async Task ATransientDropThatReconnectsSuccessfullyDoesNotCompleteTheSink()
    {
        FakeWebSocket first = new();
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        await using MassiveStreamConnection connection = new(
            FastReconnect(),
            MassiveMarket.Stocks,
            () => created++ == 0 ? first : second,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        TopicSink<StockTrade> sink = CreateSink();
        connection.AddSink(sink);
        connection.StartReading();

        TaskCompletionSource reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Reconnected += _ => reconnected.TrySetResult();

        first.AbortNext();
        await reconnected.Task.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        second.EnqueueText("""[{"ev":"T","sym":"AAPL","i":"1","p":1,"s":1,"t":1,"q":1}]""");

        await using IAsyncEnumerator<StockTrade> enumerator =
            sink.Subscription.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        using CancellationTokenSource cts =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(Duration.FromSeconds(5).ToTimeSpan());

        bool moved = await enumerator.MoveNextAsync().AsTask().WaitAsync(cts.Token);

        Assert.True(moved);
        Assert.Equal("AAPL", enumerator.Current.Ticker);
    }

    // Finding 2 (Task 11 review round 1): Faulted is a multicast delegate that runs consumer code
    // synchronously on the read-loop thread. Before the fix, one throwing handler propagated out of
    // StopPermanently, replaced the original cause, and skipped CompleteAllSinks() entirely --
    // reintroducing the exact G4 hang for every registered sink AND silently starving every other
    // Faulted subscriber. Two handlers here: the first always throws, the second records what it saw.
    [Fact]
    public async Task AThrowingFaultedHandlerDoesNotPreventSinkCompletionOrOtherHandlers()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamOptions options = new() { ApiKey = "k", Reconnect = null };

        await using MassiveStreamConnection connection = new(
            options, MassiveMarket.Stocks, () => socket, new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        TopicSink<StockTrade> sink = CreateSink();
        connection.AddSink(sink);
        connection.StartReading();

        TaskCompletionSource<Exception> secondHandlerSaw = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Faulted += _ => throw new InvalidOperationException("handler blew up");
        connection.Faulted += error => secondHandlerSaw.TrySetResult(error);

        socket.AbortNext();

        Exception seen = await secondHandlerSaw.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);
        Assert.IsType<WebSocketException>(seen);

        List<StockTrade> received = await ReadAllAsync(sink, TestContext.Current.CancellationToken);
        Assert.Empty(received);
    }

    // Finding 3 (Task 11 review round 1): ConnectAsync bounds its handshake with a linked CTS at
    // HandshakeTimeout. Before the fix, that timeout firing during a reconnect attempt produced an
    // OperationCanceledException matching neither TryReconnectAsync's transient filter nor
    // ReadLoopAsync's outer OCE filter (the SHUTDOWN token was never cancelled), so one slow
    // handshake killed the stream for good -- exactly the ordinary shape of a partial outage, when
    // reconnect matters most. second never sends "connected", so its handshake times out; third
    // completes it. Reconnected firing at all -- rather than Faulted -- is the proof.
    [Fact]
    public async Task AHandshakeTimeoutDuringReconnectIsTransientNotTerminal()
    {
        FakeWebSocket first = new();
        FakeWebSocket second = new();
        FakeWebSocket third = new();
        FakeWebSocket[] sockets = [first, second, third];
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        third.EnqueueText(Connected);
        third.EnqueueText(AuthSuccess);

        MassiveStreamOptions options = new()
        {
            ApiKey = "k",
            HandshakeTimeout = Duration.FromMilliseconds(100),
            Reconnect = new MassiveStreamReconnectOptions
            {
                InitialBackoff = Duration.FromMilliseconds(1),
                MaxBackoff = Duration.FromMilliseconds(5),
                Jitter = 0,
            },
        };

        await using MassiveStreamConnection connection = new(
            options, MassiveMarket.Stocks, () => sockets[created++], new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        TaskCompletionSource reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Reconnected += _ => reconnected.TrySetResult();

        TaskCompletionSource<Exception> faulted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Faulted += error => faulted.TrySetResult(error);

        first.AbortNext();

        await reconnected.Task.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Equal(3, created);
        Assert.Equal(1, connection.ReconnectCount);
        Assert.False(faulted.Task.IsCompleted);
    }

    // Finding 4 (Task 11 review round 1): SendActionAsync reached for _socket with no coordination
    // while TryReconnectAsync swapped it -- ConnectAsync assigns _socket at its first statement and
    // only then connects and authenticates, so a caller's frame could land on a live-but-
    // unauthenticated socket ahead of the auth frame, and UnsubscribeAsync (which waits for no
    // acknowledgement) would still remove the pair from the registry regardless. Gating the
    // reconnect's own handshake makes this deterministic: a concurrent UnsubscribeAsync must not
    // reach the socket until the reconnect's auth-and-replay has finished.
    [Fact]
    public async Task AConcurrentUnsubscribeDuringReconnectWaitsForTheHandshakeToFinish()
    {
        FakeWebSocket first = new();
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        await using MassiveStreamConnection connection = new(
            FastReconnect(),
            MassiveMarket.Stocks,
            () => created++ == 0 ? first : second,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        // Gate the reconnect's handshake so this test can deterministically observe what a
        // concurrent caller does while it is still in flight, rather than racing real thread
        // scheduling against a handshake that otherwise completes synchronously.
        second.GateNextConnect();
        first.AbortNext();

        // Give the read loop a moment to actually reach the gate (it runs on a background
        // Task.Run), so this exercises the PENDING case rather than a call that has not started yet.
        await Task.Delay(Duration.FromMilliseconds(50).ToTimeSpan(), TestContext.Current.CancellationToken);

        Task unsubscribeTask = connection.UnsubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        // The gate is still held, so the unsubscribe must not have reached the socket yet: it is
        // parked on _subscribeGate behind the in-flight reconnect attempt.
        await Task.Delay(Duration.FromMilliseconds(50).ToTimeSpan(), TestContext.Current.CancellationToken);
        Assert.Empty(second.Sent);

        second.ReleaseConnect();

        await unsubscribeTask.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        // The reconnect's own auth frame landed on the new socket first -- never mid-handshake
        // ahead of it.
        Assert.Equal("""{"action":"auth","params":"k"}""", second.Sent[0]);
        Assert.Equal("""{"action":"unsubscribe","params":"T.AAPL"}""", second.Sent[1]);
    }
}
