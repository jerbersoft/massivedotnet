using MassiveDotNet.WebSocket.Internal;
using NodaTime;
using NodaTime.Testing;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

public class SubscriptionTests
{
    private const string Connected = """[{"ev":"status","status":"connected","message":"Connected Successfully"}]""";
    private const string AuthSuccess = """[{"ev":"status","status":"auth_success","message":"authenticated"}]""";

    private static async Task<MassiveStreamConnection> ConnectAsync(FakeWebSocket socket)
    {
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamConnection connection = new(
            // A short HandshakeTimeout, not the 10-second default: AShortfallOfAcknowledgementsThrows
            // genuinely exercises the real CancellationTokenSource bound below (nothing else can
            // resolve a shortfall -- the read loop is left with no further frame to read), and a
            // FakeWebSocket gives this SDK full control over timing, so that wait has no business
            // costing ten real seconds per run.
            new MassiveStreamOptions { ApiKey = "k", HandshakeTimeout = Duration.FromMilliseconds(200) },
            MassiveMarket.Stocks,
            () => socket,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        return connection;
    }

    // A real server cannot acknowledge a subscribe before it has received it, but a FakeWebSocket
    // can: the read loop is already running (StartReading, above) and idle-parked on its next
    // receive, so a frame enqueued before SubscribeAsync is even called can be consumed as soon as
    // it lands, racing SubscribeAsync's own publish of what it is waiting for. Capturing
    // socket.SentSignal before starting the call, then awaiting it before enqueueing the
    // acknowledgement, restores real causality: the fake cannot "answer" until the request it is
    // answering has actually been sent, because SubscribeAsync publishes what it awaits strictly
    // before it sends (see MassiveStreamConnection.SubscribeAsync). Proven necessary, not just
    // defensive: without this, AShortfallOfAcknowledgementsThrows and
    // WildcardsSubscribeLikeAnyOtherTicker below fail on nearly every run.
    private static async Task<Task> SubscribeAfterSendAsync(
        MassiveStreamConnection connection, FakeWebSocket socket, string topicCode, string[] tickers, string ackFrame)
    {
        TaskCompletionSource sent = socket.SentSignal;
        Task subscribeTask = connection.SubscribeAsync(topicCode, tickers, TestContext.Current.CancellationToken);

        await sent.Task.WaitAsync(TestContext.Current.CancellationToken);
        socket.EnqueueText(ackFrame);

        return subscribeTask;
    }

    [Fact]
    public async Task ItSendsOneCommaSeparatedSubscribeAndWaitsForEveryAcknowledgement()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        await await SubscribeAfterSendAsync(
            connection,
            socket,
            "T",
            ["AAPL", "MSFT"],
            """[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"},{"ev":"status","status":"success","message":"subscribed to: T.MSFT"}]""");

        Assert.Contains("""{"action":"subscribe","params":"T.AAPL,T.MSFT"}""", socket.Sent);
    }

    // The finding that shaped the design: the server drops an unrecognised pair in silence, so a
    // subscription that does not exist is indistinguishable from a quiet market (D-W2).
    [Fact]
    public async Task AShortfallOfAcknowledgementsThrows()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        Task subscribeTask = await SubscribeAfterSendAsync(
            connection,
            socket,
            "T",
            ["AAPL", "MSFT"],
            """[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""");

        MassiveStreamSubscriptionException error =
            await Assert.ThrowsAsync<MassiveStreamSubscriptionException>(async () => await subscribeTask);

        Assert.Equal(1, error.Unacknowledged);
        Assert.Contains("T.AAPL,T.MSFT", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WildcardsSubscribeLikeAnyOtherTicker()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        await await SubscribeAfterSendAsync(
            connection,
            socket,
            "T",
            ["*"],
            """[{"ev":"status","status":"success","message":"subscribed to: T.*"}]""");

        Assert.Contains("""{"action":"subscribe","params":"T.*"}""", socket.Sent);
    }

    [Fact]
    public async Task TheRegistryRemembersWhatReconnectMustReplay()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        await await SubscribeAfterSendAsync(
            connection, socket, "T", ["AAPL"], """[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""");
        await await SubscribeAfterSendAsync(
            connection, socket, "Q", ["MSFT"], """[{"ev":"status","status":"success","message":"subscribed to: Q.MSFT"}]""");

        Assert.Equal(["Q.MSFT", "T.AAPL"], connection.Registry.Parameters.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task UnsubscribeSendsTheActionAndForgetsThePair()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        await await SubscribeAfterSendAsync(
            connection, socket, "T", ["AAPL"], """[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""");

        await connection.UnsubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        Assert.Contains("""{"action":"unsubscribe","params":"T.AAPL"}""", socket.Sent);
        Assert.Empty(connection.Registry.Parameters);
    }

    // F1 from the round-1 review: the server acknowledges an unsubscribe with the very same
    // "success" status a subscribe gets -- confirmed live 2026-09-07, "unsubscribed to:
    // T.NOTATICKER". A version of this counting that decremented on any "success" event, regardless
    // of content, let that acknowledgement silently satisfy whatever subscribe happened to ask
    // next: subscribe, unsubscribe, then subscribe again with *nothing* enqueued returned success
    // instead of throwing. Matching by the exact acknowledgement text (see
    // MassiveStreamConnection._pendingAcks) closes this, because "unsubscribed to: T.AAPL" can
    // never match a key this connection only ever populates with "subscribed to: ...".
    [Fact]
    public async Task AnUnsubscribeAcknowledgementDoesNotCreditTheNextSubscribe()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        await await SubscribeAfterSendAsync(
            connection, socket, "T", ["AAPL"], """[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""");

        // UnsubscribeAsync does not wait for this, so it is simply enqueued for the read loop to
        // (correctly) ignore; the exact timing relative to UnsubscribeAsync's own return does not
        // matter, because the message text can never match a pending *subscribe*.
        socket.EnqueueText("""[{"ev":"status","status":"success","message":"unsubscribed to: T.AAPL"}]""");
        await connection.UnsubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        // Nothing is enqueued for this one: if the unsubscribe's acknowledgement above were still
        // (wrongly) banked toward whatever asks next, this would complete instantly instead of
        // timing out and throwing -- exactly the failure F1 named.
        MassiveStreamSubscriptionException error =
            await Assert.ThrowsAsync<MassiveStreamSubscriptionException>(async () =>
                await connection.SubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken));

        Assert.Equal(1, error.Unacknowledged);
    }

    // The registry is what Task 11's reconnect replays, so a partial shortfall must not discard
    // the pairs that genuinely were acknowledged along with the ones that were not.
    [Fact]
    public async Task APartialShortfallStillRegistersWhatWasAcknowledged()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        Task subscribeTask = await SubscribeAfterSendAsync(
            connection,
            socket,
            "T",
            ["AAPL", "MSFT"],
            """[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""");

        await Assert.ThrowsAsync<MassiveStreamSubscriptionException>(async () => await subscribeTask);

        Assert.Equal(["T.AAPL"], connection.Registry.Parameters);
    }

    // F4 from the round-1 review: sizing the parse destination from the payload's byte length
    // rather than its event count measured at 7.3 MB parsing a 178 KB frame, and 64 MiB on the
    // Large Object Heap at the 4 MiB default MaxMessageBytes -- reachable from an ordinary batched
    // multi-ticker acknowledgement. This does not re-measure bytes (no allocation-gate
    // infrastructure exists yet for this project -- see MassiveDotNet.Rest.Tests.AllocationTests
    // for the pattern D31 expects once one is added here), but it does prove the counting-based
    // sizing handles a frame wide enough that a byte-length-scaled destination would have been
    // wildly oversized, without throwing or hanging.
    [Fact]
    public async Task ALargeBatchOfAcknowledgementsInOneFrameStillCompletes()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        const int TickerCount = 3000;
        string[] tickers = [.. Enumerable.Range(0, TickerCount).Select(i => $"T{i}")];

        string ackFrame = "[" + string.Join(
            ',',
            tickers.Select(ticker => $$"""{"ev":"status","status":"success","message":"subscribed to: T.{{ticker}}"}""")) + "]";

        await await SubscribeAfterSendAsync(connection, socket, "T", tickers, ackFrame);

        Assert.Equal(TickerCount, connection.Registry.Parameters.Count);
    }

    [Theory]
    [InlineData(StockTopic.Trades, "T")]
    [InlineData(StockTopic.Quotes, "Q")]
    public void TopicsRenderAsTheirWireCodes(StockTopic topic, string expected) =>
        Assert.Equal(expected, topic.ToCode());
}
