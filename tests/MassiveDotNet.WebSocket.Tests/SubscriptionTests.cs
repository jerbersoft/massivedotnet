using System.Text;
using System.Text.Json;
using MassiveDotNet.WebSocket.Internal;
using NodaTime;
using NodaTime.Testing;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

public class SubscriptionTests
{
    private const string Connected = """[{"ev":"status","status":"connected","message":"Connected Successfully"}]""";
    private const string AuthSuccess = """[{"ev":"status","status":"auth_success","message":"authenticated"}]""";

    private static async Task<MassiveStreamConnection> ConnectAsync(
        FakeWebSocket socket, string? afterHandshake = null)
    {
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        // Queued with the handshake rather than after it so the read loop meets it with nothing in
        // flight -- see ARefusalWithNothingInFlightIsNotHeldAgainstTheNextSubscribe.
        if (afterHandshake is not null)
        {
            socket.EnqueueText(afterHandshake);
        }

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

    // An `ev` that is not a string is malformed, and the two halves answer it differently on purpose:
    // the counter reports no status events, so the frame goes to dispatch, while a parse of it fails
    // closed rather than inventing a StatusMessage. What neither may do is walk INTO the object and
    // misread the rest of the event as its properties -- before the Skip that now guards this, the
    // counter did exactly that and stopped counting early.
    [Fact]
    public void AMalformedEventKindIsRefusedRatherThanMisread()
    {
        // The genuine status event AFTER the malformed one is what makes this bite. Without the skip,
        // the counter walks into the `ev` object, reads its members as the outer event's properties,
        // then meets `"status"` where it expects the next event to start and stops -- reporting one
        // event as zero. Sized by that, Parse would throw on a frame carrying a real acknowledgement.
        ReadOnlySpan<byte> payload =
            """[{"ev":{"not":"a string"},"x":1},{"ev":"status","status":"success","message":"m"}]"""u8;

        int counted = MassiveStreamConnection.CountStatusEvents(payload, out bool isArray);

        Assert.True(isArray);
        Assert.Equal(1, counted);

        // Span<T> is a ref struct, so Assert.Throws cannot close over it.
        bool threw = false;

        try
        {
            StatusMessage.Parse(payload, new StatusMessage[counted]);
        }
        catch (JsonException)
        {
            threw = true;
        }

        Assert.True(threw);
    }

    // CountStatusEvents sizes the buffer that StatusMessage.Parse then fills, and Parse THROWS rather
    // than truncating when it does not fit. Nothing but this test makes the two agree: the retry that
    // used to cover a disagreement was removed with F4's allocation fix, so a divergence now surfaces
    // as a thrown subscribe on a perfectly good frame. Every shape here is one the counter and the
    // parser could plausibly disagree about.
    [Theory]
    [InlineData("""[]""")]
    [InlineData("""[{"ev":"status","status":"success","message":"subscribed to: T.A"}]""")]
    [InlineData("""[{"ev":"status","status":"success","message":"a"},{"ev":"status","status":"success","message":"b"}]""")]
    [InlineData("""[{"ev":"T","sym":"AAPL","p":1.0}]""")]
    [InlineData("""[{"ev":"T","sym":"AAPL"},{"ev":"status","status":"success","message":"m"}]""")]
    [InlineData("""[{"ev":"status","status":"success","message":"m","extra":{"nested":[1,2,3]}}]""")]
    [InlineData("""[{"extra":{"ev":"status"},"ev":"status","status":"success","message":"m"}]""")]
    public void TheCounterAndTheParserAgreeOnHowManyStatusEventsAFrameHolds(string json)
    {
        ReadOnlySpan<byte> payload = Encoding.UTF8.GetBytes(json);

        int counted = MassiveStreamConnection.CountStatusEvents(payload, out bool isArray);

        Assert.True(isArray);

        Span<StatusMessage> destination = new StatusMessage[counted];
        int parsed = StatusMessage.Parse(payload, destination);

        Assert.Equal(counted, parsed);
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

    // Issue #60. A subscribe the server refuses is answered -- {"ev":"status","status":"error",
    // "message":"not authorized"} for a topic the key's plan does not include, observed live on
    // 2026-09-08 -- and OnStatus discarded any status that was not `success`, so the caller was
    // told the server "ignores a topic code it does not recognise". That is the inference D-W2
    // makes from SILENCE, and repeating it here contradicts what the server actually said: it
    // sends a consumer to check their topic spelling when the fix is their plan.
    [Fact]
    public async Task AServersRefusalIsReportedInsteadOfTheInferredSilence()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        Task subscribeTask = await SubscribeAfterSendAsync(
            connection,
            socket,
            "NOI",
            ["AAPL"],
            """[{"ev":"status","status":"error","message":"not authorized"}]""");

        MassiveStreamSubscriptionException error =
            await Assert.ThrowsAsync<MassiveStreamSubscriptionException>(async () => await subscribeTask);

        // Verbatim, and retained as its own property rather than left only inside the prose: the
        // SDK cannot categorise what the server said (D-W6's argument for auth_failed applies
        // unchanged), so a consumer who wants to act on it gets the words without parsing a
        // sentence this SDK is free to reword.
        Assert.Equal("not authorized", error.ServerMessage);
        Assert.Contains("not authorized", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ignores a topic code", error.Message, StringComparison.Ordinal);

        // The count is still real evidence and still what a handler acts on -- the refusal changes
        // what the shortfall SAYS, never that there was one.
        Assert.Equal(1, error.Unacknowledged);
        Assert.Equal("NOI.AAPL", error.Parameters);
    }

    // The other half of #60, and the reason the wording is chosen rather than replaced: when the
    // server says nothing at all, D-W2's inference is the only evidence there is and is exactly
    // right. A fix that reworded unconditionally would trade one wrong message for another.
    [Fact]
    public async Task ASilentShortfallStillReportsTheInferredCause()
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

        Assert.Null(error.ServerMessage);
        Assert.Contains("ignores a topic code", error.Message, StringComparison.Ordinal);
    }

    // A status event carrying no prose is silence with a label on it, so it must not switch the
    // message onto the "the server refused with:" wording and then name nothing.
    [Fact]
    public async Task AnErrorStatusWithNoMessageFallsBackToTheInferredCause()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        Task subscribeTask = await SubscribeAfterSendAsync(
            connection,
            socket,
            "NOI",
            ["AAPL"],
            """[{"ev":"status","status":"error"}]""");

        MassiveStreamSubscriptionException error =
            await Assert.ThrowsAsync<MassiveStreamSubscriptionException>(async () => await subscribeTask);

        Assert.Null(error.ServerMessage);
        Assert.Contains("ignores a topic code", error.Message, StringComparison.Ordinal);
    }

    // A refusal that answers nothing belongs to nothing. The server can send one after a request
    // has already timed out and given up, and holding it would let it surface against whatever
    // subscribes next -- telling a caller their perfectly good request was refused, with a reason
    // collected before they made it.
    [Fact]
    public async Task ARefusalWithNothingInFlightIsNotHeldAgainstTheNextSubscribe()
    {
        await using FakeWebSocket socket = new();

        // The gate is only reached once the queue is genuinely empty, so awaiting it proves all
        // three frames were delivered AND that the third was processed -- the read loop handles a
        // frame before asking for the next one. Ordering, not a sleep.
        Task consumed = socket.GateReceiveAfter(3);

        await using MassiveStreamConnection connection = await ConnectAsync(
            socket, """[{"ev":"status","status":"error","message":"not authorized"}]""");

        await consumed.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);
        socket.ReleaseReceive();

        Task subscribeTask = await SubscribeAfterSendAsync(
            connection,
            socket,
            "T",
            ["AAPL", "MSFT"],
            """[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""");

        MassiveStreamSubscriptionException error =
            await Assert.ThrowsAsync<MassiveStreamSubscriptionException>(async () => await subscribeTask);

        Assert.Null(error.ServerMessage);
        Assert.Contains("ignores a topic code", error.Message, StringComparison.Ordinal);
    }

    // The same property one request later: a refusal is cleared with the slot it was collected
    // against, so it cannot be reported a second time to a caller it has nothing to do with.
    [Fact]
    public async Task ARefusalIsNotCarriedFromOneSubscribeToTheNext()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        Task refused = await SubscribeAfterSendAsync(
            connection,
            socket,
            "NOI",
            ["AAPL"],
            """[{"ev":"status","status":"error","message":"not authorized"}]""");

        await Assert.ThrowsAsync<MassiveStreamSubscriptionException>(async () => await refused);

        Task subscribeTask = await SubscribeAfterSendAsync(
            connection,
            socket,
            "T",
            ["AAPL", "MSFT"],
            """[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""");

        MassiveStreamSubscriptionException error =
            await Assert.ThrowsAsync<MassiveStreamSubscriptionException>(async () => await subscribeTask);

        Assert.Null(error.ServerMessage);
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
