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
}
