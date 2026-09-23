using System.Net.WebSockets;
using MassiveDotNet.WebSocket.Internal;
using NodaTime;
using NodaTime.Testing;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// Massive answers a connection it is about to evict with
/// <c>{"ev":"status","status":"max_connections",...}</c> and then closes it. Before #69 the SDK
/// parsed that status and discarded it unless a subscribe happened to be in flight, so the abort
/// that followed was indistinguishable from a slow-consumer close or a network drop -- the one
/// disconnect cause that announces itself was the one the SDK could not report (D42).
/// </summary>
public class EvictionTests
{
    private const string Connected = """[{"ev":"status","status":"connected","message":"Connected Successfully"}]""";
    private const string AuthSuccess = """[{"ev":"status","status":"auth_success","message":"authenticated"}]""";

    // Verbatim from the frame the live service sent this repo on 2026-09-09, trailing space and
    // all: the point of reporting the server's own words is that they are not reworded here.
    private const string EvictionMessage = "Maximum number of websocket connections exceeded. ";

    private const string MaxConnections =
        $$"""[{"ev":"status","status":"max_connections","message":"{{EvictionMessage}}"}]""";

    private static MassiveStreamOptions FastReconnect() => new()
    {
        ApiKey = "k",
        HandshakeTimeout = Duration.FromMilliseconds(200),
        Reconnect = new MassiveStreamReconnectOptions
        {
            InitialBackoff = Duration.FromMilliseconds(1),
            MaxBackoff = Duration.FromMilliseconds(5),
            Jitter = 0,
        },
    };

    private static async Task<MassiveStreamConnection> ConnectAsync(
        MassiveStreamOptions options, MassiveWebSocketFactory factory)
    {
        MassiveStreamConnection connection = new(
            options, MassiveMarket.Stocks, factory, new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        return connection;
    }

    // The case the SDK could not report at all: nothing in flight, so D37's refusal slots are both
    // unarmed and RecordRefusal drops the message on the floor. The consumer saw only the abort.
    [Fact]
    public async Task AnEvictionWithNothingInFlightBecomesTheCauseTheStreamStoppedFor()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamOptions options = new() { ApiKey = "k", Reconnect = null };

        await using MassiveStreamConnection connection = await ConnectAsync(options, () => socket);

        TaskCompletionSource<Exception> faulted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Faulted += error => faulted.TrySetResult(error);

        socket.EnqueueText(MaxConnections);
        socket.AbortNext();

        Exception error = await faulted.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        MassiveStreamEvictedException evicted = Assert.IsType<MassiveStreamEvictedException>(error);

        Assert.Equal(EvictionMessage, evicted.ServerMessage);
        Assert.Contains(EvictionMessage, evicted.Message, StringComparison.Ordinal);

        // The drop is still what physically ended the read, and its close code is worth keeping:
        // substituting the cause must not throw away the evidence it substitutes for.
        Assert.IsType<WebSocketException>(evicted.InnerException);

        Assert.Equal(1, connection.EvictionCount);
        Assert.Equal(EvictionMessage, connection.LastEvictionMessage);
    }

    // Reconnect is ON by default, so this -- not the terminal path above -- is the shape a consumer
    // running the defaults actually meets: the socket comes back and Faulted never fires at all.
    // Without the cumulative pair the eviction would still be invisible on every default
    // configuration, which is the whole reason it is recorded as state rather than only as a cause.
    [Fact]
    public async Task AnEvictionIsStillReadableAfterTheStreamReconnects()
    {
        FakeWebSocket first = new();
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        await using MassiveStreamConnection connection = await ConnectAsync(
            FastReconnect(), () => created++ == 0 ? first : second);

        // Read from inside the handler, not after awaiting it: that is the moment a consumer's own
        // Reconnected handler and the DI package's log bridge get, and it is the moment the pair
        // has to be readable at. A value that is only correct once the test catches up later would
        // not be worth having.
        TaskCompletionSource<string?> observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Reconnected += _ => observed.TrySetResult(connection.LastEvictionMessage);

        first.EnqueueText(MaxConnections);
        first.AbortNext();

        string? message = await observed.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Equal(EvictionMessage, message);
        Assert.Equal(1, connection.EvictionCount);
        Assert.Equal(1, connection.ReconnectCount);
    }

    // The status is the connection's cause AND the in-flight subscribe's refusal, not a choice
    // between them (#69's open design point). This is the shape observed live on 2026-09-09: the
    // subscribe genuinely will not be honoured, so D37's report of what the server said is exactly
    // as right as it was before -- and the acknowledgement bookkeeping behind it is untouched.
    [Fact]
    public async Task AnEvictionDuringASubscribeStillRefusesThatSubscribe()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamOptions options = new()
        {
            ApiKey = "k",
            HandshakeTimeout = Duration.FromMilliseconds(200),
            Reconnect = null,
        };

        await using MassiveStreamConnection connection = await ConnectAsync(options, () => socket);

        // Same causality fix as SubscriptionTests.SubscribeAfterSendAsync: the read loop is already
        // parked on its next receive, so a frame enqueued before the send lands could be consumed
        // before SubscribeAsync has published what it waits on.
        TaskCompletionSource sent = socket.SentSignal;
        Task subscribeTask = connection.SubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);
        await sent.Task.WaitAsync(TestContext.Current.CancellationToken);
        socket.EnqueueText(MaxConnections);

        MassiveStreamSubscriptionException refused =
            await Assert.ThrowsAsync<MassiveStreamSubscriptionException>(async () => await subscribeTask);

        Assert.Equal(EvictionMessage, refused.ServerMessage);
        Assert.Equal(1, refused.Unacknowledged);
        Assert.Equal("T.AAPL", refused.Parameters);

        // Recorded for the connection as well, from the same frame, without either report
        // consuming what the other needs.
        Assert.Equal(1, connection.EvictionCount);
        Assert.Equal(EvictionMessage, connection.LastEvictionMessage);
    }

    // The guard on the substitution: a drop that no status preceded must keep reporting exactly
    // what it reported before, or every ordinary network failure starts naming a cause it does not
    // have.
    [Fact]
    public async Task ADropWithNoStatusBeforeItIsReportedUnchanged()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamOptions options = new() { ApiKey = "k", Reconnect = null };

        await using MassiveStreamConnection connection = await ConnectAsync(options, () => socket);

        TaskCompletionSource<Exception> faulted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Faulted += error => faulted.TrySetResult(error);

        socket.AbortNext();

        Exception error = await faulted.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.IsType<WebSocketException>(error);
        Assert.Equal(0, connection.EvictionCount);
        Assert.Null(connection.LastEvictionMessage);
    }

    // The pending cause is scoped to ONE socket. Without that clear it would sit armed for the
    // connection's whole life and charge the next unrelated drop -- minutes or hours later, on a
    // socket that was never evicted -- to an eviction that had already been survived.
    [Fact]
    public async Task AnEvictionIsNotChargedToTheNextSocketsDrop()
    {
        FakeWebSocket first = new();
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        MassiveStreamOptions options = FastReconnect();

        await using MassiveStreamConnection connection = await ConnectAsync(
            options, () => created++ == 0 ? first : second);

        TaskCompletionSource reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Reconnected += _ => reconnected.TrySetResult();

        first.EnqueueText(MaxConnections);
        first.AbortNext();

        await reconnected.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        TaskCompletionSource<Exception> faulted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Faulted += error => faulted.TrySetResult(error);

        // TryReconnectAsync re-reads _options.Reconnect on every call, so turning it off here makes
        // the SECOND drop terminal and therefore observable -- the only way to reach Faulted twice
        // on one connection.
        options.Reconnect = null;
        second.AbortNext();

        Exception error = await faulted.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.IsType<WebSocketException>(error);

        // The cumulative record is deliberately NOT cleared: the eviction really did happen, and a
        // consumer asking "has this stream ever been evicted" is asking a different question from
        // "did this particular drop happen because of one".
        Assert.Equal(1, connection.EvictionCount);
        Assert.Equal(EvictionMessage, connection.LastEvictionMessage);
    }

    // A clean close carrying the status is the other shape the vendor describes, and FrameReader
    // reports it as MassiveStreamException rather than WebSocketException -- so the substitution
    // must not be keyed on the drop's own type.
    [Fact]
    public async Task AnEvictionFollowedByACleanCloseIsReportedTheSameWay()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamOptions options = new() { ApiKey = "k", Reconnect = null };

        await using MassiveStreamConnection connection = await ConnectAsync(options, () => socket);

        TaskCompletionSource<Exception> faulted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Faulted += error => faulted.TrySetResult(error);

        socket.EnqueueText(MaxConnections);
        socket.EnqueueClose(WebSocketCloseStatus.NormalClosure, "bye");

        Exception error = await faulted.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        MassiveStreamEvictedException evicted = Assert.IsType<MassiveStreamEvictedException>(error);

        Assert.Equal(EvictionMessage, evicted.ServerMessage);
        Assert.IsType<MassiveStreamException>(evicted.InnerException, exactMatch: false);
    }

    // The façade's own surface, not the connection's. Both properties are public and frozen by rule
    // 14, so the forwarding is worth one assertion of its own rather than being covered only
    // indirectly by the DI package's log bridge -- a stream that reported 0 and null forever would
    // leave every default configuration exactly as blind as before the fix.
    [Fact]
    public async Task TheFacadeReportsTheEvictionTheConnectionRecorded()
    {
        FakeWebSocket first = new();
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        MassiveStreamClient client = new(FastReconnect());

        await using MassiveStockStream stream = await client.ConnectStocksAsync(
            () => created++ == 0 ? first : second, TestContext.Current.CancellationToken);

        Assert.Equal(0, stream.EvictionCount);
        Assert.Null(stream.LastEvictionMessage);

        TaskCompletionSource reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        stream.Reconnected += _ => reconnected.TrySetResult();

        first.EnqueueText(MaxConnections);
        first.AbortNext();

        await reconnected.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Equal(1, stream.EvictionCount);
        Assert.Equal(EvictionMessage, stream.LastEvictionMessage);
    }
}
