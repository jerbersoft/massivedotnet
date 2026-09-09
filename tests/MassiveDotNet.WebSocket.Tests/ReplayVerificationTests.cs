using MassiveDotNet.WebSocket.Events;
using MassiveDotNet.WebSocket.Internal;
using NodaTime;
using NodaTime.Testing;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// Issue #52: a reconnect replays the subscription registry but never verifies that the server
/// acknowledged any of it, while <c>Reconnected</c> and <c>ReconnectCount</c> report the reconnect
/// as successful either way. D33's own "why" column records the behaviour that makes this matter --
/// the server silently drops a topic it does not recognise rather than rejecting it -- so a replay
/// nobody checks can leave a topic unsubscribed on a connection the SDK calls healthy, which is
/// indistinguishable from a quiet market (D29's argument).
/// </summary>
public class ReplayVerificationTests
{
    private const string Connected = """[{"ev":"status","status":"connected","message":"Connected Successfully"}]""";
    private const string AuthSuccess = """[{"ev":"status","status":"auth_success","message":"authenticated"}]""";

    // Short enough that a withheld acknowledgement is reported inside a test's patience, long
    // enough that it is not racing ordinary scheduling. It bounds the replay's verification window
    // exactly as it already bounds a caller's own subscribe (see MassiveStreamOptions).
    private static MassiveStreamOptions FastReconnect() => new()
    {
        ApiKey = "k",
        HandshakeTimeout = Duration.FromMilliseconds(250),
        Reconnect = new MassiveStreamReconnectOptions
        {
            InitialBackoff = Duration.FromMilliseconds(1),
            MaxBackoff = Duration.FromMilliseconds(5),
            Jitter = 0,
        },
    };

    [Fact]
    public async Task AReplayTheServerNeverAcknowledgesIsReportedAsLostSubscriptions()
    {
        FakeWebSocket first = new() { AutoAcknowledgeSubscribes = true };
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        // No acknowledgement for the replay: this is the server silently dropping what it was
        // asked to restore, which is precisely what nothing in the offline suite noticed before.
        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        await using MassiveStreamConnection connection = new(
            FastReconnect(),
            MassiveMarket.Stocks,
            () => created++ == 0 ? first : second,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        await connection.SubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        TaskCompletionSource<MassiveStreamSubscriptionException> lost =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.SubscriptionsLost += error => lost.TrySetResult(error);

        first.AbortNext();

        MassiveStreamSubscriptionException reported = await lost.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Equal(1, reported.Unacknowledged);
        Assert.Contains("T.AAPL", reported.Message, StringComparison.Ordinal);

        // The reconnect itself genuinely happened -- this is a degraded stream, not a failed one,
        // which is why it is its own signal rather than a change to what Reconnected means.
        Assert.Equal(1, connection.ReconnectCount);
    }

    // The other half of the pin: a replay the server DOES honour must stay silent. Without this,
    // the fix could satisfy the test above by reporting a loss on every reconnect, which is a
    // signal a consumer would learn to ignore -- the worst possible outcome for a warning whose
    // whole value is that it is rare and true.
    [Fact]
    public async Task AFullyAcknowledgedReplayReportsNothing()
    {
        FakeWebSocket first = new() { AutoAcknowledgeSubscribes = true };
        FakeWebSocket second = new() { AutoAcknowledgeSubscribes = true };
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

        await connection.SubscribeAsync("T", ["AAPL", "MSFT"], TestContext.Current.CancellationToken);

        List<MassiveStreamSubscriptionException> reported = [];
        connection.SubscriptionsLost += error => reported.Add(error);

        TaskCompletionSource reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Reconnected += _ => reconnected.TrySetResult();

        first.AbortNext();
        await reconnected.Task.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        // Awaiting the verification itself rather than sleeping past the deadline: a sleep long
        // enough to be safe makes the test slow, and one short enough to be fast turns a late raise
        // into a false pass.
        await connection.ReplayVerification.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Empty(reported);
    }

    // Acceptance criterion 2 of issue #52. The deadline must not park the read loop -- which is the
    // whole reason the wait was moved off it -- so a topic the server DID acknowledge keeps
    // delivering throughout a window another topic is timing out in.
    [Fact]
    public async Task TheDeadlineDoesNotParkTheReadLoopWhileItRuns()
    {
        FakeWebSocket first = new() { AutoAcknowledgeSubscribes = true };
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        // Connected and authenticated, but the replay goes unacknowledged: the connection spends
        // the next HandshakeTimeout inside its verification window.
        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        await using MassiveStreamConnection connection = new(
            FastReconnect(),
            MassiveMarket.Stocks,
            () => created++ == 0 ? first : second,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        TopicSink<StockTrade> sink = new("T", capacity: 8, new StockTradeConverter(new TickerPool(16)));
        connection.AddSink(sink);

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        await connection.SubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        TaskCompletionSource reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Reconnected += _ => reconnected.TrySetResult();

        first.AbortNext();
        await reconnected.Task.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        // The verification is still outstanding at this point -- Reconnected fires before the
        // acknowledgements it is waiting for could possibly have been declared missing.
        Assert.False(connection.ReplayVerification.IsCompleted);

        second.EnqueueText("""[{"ev":"T","sym":"AAPL","i":"1","p":1,"s":1,"t":1,"q":1}]""");

        StockTrade delivered = await ReadOneAsync(sink, TestContext.Current.CancellationToken);

        Assert.Equal("AAPL", delivered.Ticker);
    }

    // The replay's shortfall must not prune the registry: the pair is still what the caller asked
    // for, so the NEXT reconnect has to ask for it again. Dropping it would turn one server-side
    // silence into a subscription the SDK quietly gave up on forever.
    [Fact]
    public async Task AShortfallLeavesTheRegistryIntactSoTheNextReconnectReplaysItAgain()
    {
        FakeWebSocket first = new() { AutoAcknowledgeSubscribes = true };
        FakeWebSocket second = new();
        FakeWebSocket third = new();
        FakeWebSocket[] sockets = [first, second, third];
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        third.EnqueueText(Connected);
        third.EnqueueText(AuthSuccess);

        await using MassiveStreamConnection connection = new(
            FastReconnect(),
            MassiveMarket.Stocks,
            () => sockets[created++],
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        await connection.SubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        TaskCompletionSource<MassiveStreamSubscriptionException> lost =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.SubscriptionsLost += error => lost.TrySetResult(error);

        first.AbortNext();
        await lost.Task.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        TaskCompletionSource reconnectedAgain = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Reconnected += count =>
        {
            if (count == 2)
            {
                reconnectedAgain.TrySetResult();
            }
        };

        second.AbortNext();
        await reconnectedAgain.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Contains("""{"action":"subscribe","params":"T.AAPL"}""", third.Sent);
    }

    // Issue #62. One acknowledgement credits exactly ONE waiter, and the replay is credited first.
    // Pinning that needs a genuine collision, which is what the version of this test written for
    // #52 never produced: with the fake acknowledging every frame as part of the send itself, the
    // replay's answer is credited while the test is still awaiting Reconnected, so the caller's
    // slot is never live at the same time and one answer never has to choose between two waiters.
    // That test passed with the crediting order reversed AND with SubscribeAsync erasing the replay
    // slot outright -- a guard nobody has seen fail is indistinguishable from a clean tree (D31).
    // Here the answer is withheld until both slots owe the identical text, and then exactly one
    // arrives.
    [Fact]
    public async Task ACallerSubscribingToTheSamePairDoesNotStealTheReplaysAcknowledgement()
    {
        FakeWebSocket first = new() { AutoAcknowledgeSubscribes = true };
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        // Deliberately NOT auto-acknowledging: this test owns the timing of the single answer,
        // which is the whole point of it.
        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        await using MassiveStreamConnection connection = new(
            FastReconnect(),
            MassiveMarket.Stocks,
            () => created++ == 0 ? first : second,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        await connection.SubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        List<MassiveStreamSubscriptionException> reported = [];
        connection.SubscriptionsLost += error => reported.Add(error);

        TaskCompletionSource reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Reconnected += _ => reconnected.TrySetResult();

        first.AbortNext();
        await reconnected.Task.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        // The same pair the replay is still waiting on, so both slots owe byte-identical
        // acknowledgement text. Started rather than awaited: only one answer is coming, so exactly
        // one of the two waiters must go unanswered, and this is the one that should.
        Task subscribe = connection.SubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        // The SECOND send of that frame -- the first was the replay's own, so waiting on mere
        // presence would return before the caller had published anything to collide with.
        await second.WaitForSendAsync("""{"action":"subscribe","params":"T.AAPL"}""", count: 2)
            .WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        second.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""");

        await connection.ReplayVerification.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Empty(reported);

        // The other half of "exactly one waiter": the answer went to the replay, so the caller is
        // left short and told so. Crediting both from one message would leave this passing, which
        // is the shortfall that then never gets reported to anyone.
        MassiveStreamSubscriptionException starved =
            await Assert.ThrowsAsync<MassiveStreamSubscriptionException>(() => subscribe);

        Assert.Equal(1, starved.Unacknowledged);
    }

    // Issue #62, the second defect one test could not separate: SubscribeAsync assigns over
    // _pendingAcks, and had the replay shared that slot, a caller subscribing inside the
    // verification window would erase what the replay is waiting on.
    //
    // The consequence is NOT a false alarm, which is exactly what makes it easy to write a test
    // that cannot see it -- the first draft of this one asserted a false alarm and passed with the
    // slot erased. VerifyReplayAsync checks the slot is still the one it armed
    // (ReferenceEquals(_replayAcks, owed)) and returns SILENTLY when a later reconnect has taken it
    // over; an erased slot is indistinguishable from that hand-off, so the verification gives up
    // and tells nobody. A shortfall nobody is told about is the failure D33 exists to prevent,
    // arriving one level up. So this asserts the loss IS raised with a caller's subscribe landing
    // mid-window. A DIFFERENT pair isolates it from the crediting order above: the caller's slot
    // never holds the text the replay owes, so nothing here can be explained by precedence.
    [Fact]
    public async Task ACallerSubscribingInsideTheWindowDoesNotSilenceTheReplaysShortfall()
    {
        FakeWebSocket first = new() { AutoAcknowledgeSubscribes = true };
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

        await connection.SubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        TaskCompletionSource<MassiveStreamSubscriptionException> lost =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.SubscriptionsLost += error => lost.TrySetResult(error);

        TaskCompletionSource reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Reconnected += _ => reconnected.TrySetResult();

        // T.AAPL is replayed and never answered, so a genuine shortfall is counting down
        // throughout everything below.
        first.AbortNext();
        await reconnected.Task.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        // A pair the replay is not waiting on, acknowledged in full so the caller's subscribe
        // completes and its finally clears _pendingAcks -- exactly the assignment that would take
        // the replay's tracking with it were the two sharing one slot.
        Task subscribe = connection.SubscribeAsync("T", ["MSFT"], TestContext.Current.CancellationToken);

        await second.WaitForSendAsync("""{"action":"subscribe","params":"T.MSFT"}""")
            .WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        second.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: T.MSFT"}]""");
        await subscribe;

        // The overlap is the whole point, so it is asserted rather than hoped for: had the window
        // already closed, everything below would pass for the wrong reason.
        Assert.False(connection.ReplayVerification.IsCompleted);

        MassiveStreamSubscriptionException reported = await lost.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Equal(1, reported.Unacknowledged);
        Assert.Contains("T.AAPL", reported.Message, StringComparison.Ordinal);
    }

    // Only the pairs that actually went unanswered are named. A shortfall that reported the whole
    // request would send a consumer looking for a fault in topics that are delivering.
    [Fact]
    public async Task OnlyTheUnacknowledgedPairsAreNamed()
    {
        FakeWebSocket first = new() { AutoAcknowledgeSubscribes = true };
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

        await connection.SubscribeAsync("T", ["AAPL", "MSFT"], TestContext.Current.CancellationToken);

        TaskCompletionSource<MassiveStreamSubscriptionException> lost =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.SubscriptionsLost += error => lost.TrySetResult(error);

        first.AbortNext();

        // Answer exactly one of the two replayed pairs. The fake sends one subscribe frame per
        // parameter, so this is the shape a partially-entitled or partially-retired topic set takes.
        await second.WaitForSendAsync("""{"action":"subscribe","params":"T.MSFT"}""")
            .WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);
        second.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: T.MSFT"}]""");

        MassiveStreamSubscriptionException reported = await lost.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Equal(1, reported.Unacknowledged);
        Assert.Contains("T.AAPL", reported.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("T.MSFT", reported.Message, StringComparison.Ordinal);
    }

    // EventRaiser's rule, at the newest raise site: a multicast delegate stops invoking subscribers
    // the instant one throws, so a per-handler try/catch is what keeps a misbehaving consumer from
    // starving its neighbours -- and this raise runs on a task nobody awaits, where an escaping
    // exception would otherwise vanish entirely.
    [Fact]
    public async Task AThrowingSubscriptionsLostHandlerDoesNotStarveTheOthers()
    {
        FakeWebSocket first = new() { AutoAcknowledgeSubscribes = true };
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

        await connection.SubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        TaskCompletionSource<Exception> faulted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Faulted += error => faulted.TrySetResult(error);

        TaskCompletionSource survivor = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.SubscriptionsLost += _ => throw new InvalidOperationException("consumer bug");
        connection.SubscriptionsLost += _ => survivor.TrySetResult();

        first.AbortNext();

        await survivor.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        // The stream is degraded, not dead: a handler throwing about a shortfall must not become
        // the terminal stop the shortfall itself never was.
        Assert.False(faulted.Task.IsCompleted);
        Assert.False(connection.ReadLoopTask.IsCompleted);
    }

    // Disposal is not a shortfall. Without the cancellation check, tearing a stream down inside a
    // verification window would report every replayed pair as lost on the way out -- a warning
    // caused entirely by the consumer's own DisposeAsync.
    [Fact]
    public async Task DisposingInsideTheWindowReportsNothing()
    {
        FakeWebSocket first = new() { AutoAcknowledgeSubscribes = true };
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        MassiveStreamConnection connection = new(
            FastReconnect(),
            MassiveMarket.Stocks,
            () => created++ == 0 ? first : second,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        List<MassiveStreamSubscriptionException> reported = [];

        await using (connection)
        {
            await connection.ConnectAsync(TestContext.Current.CancellationToken);
            connection.StartReading();

            await connection.SubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

            connection.SubscriptionsLost += error => reported.Add(error);

            TaskCompletionSource reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.Reconnected += _ => reconnected.TrySetResult();

            first.AbortNext();
            await reconnected.Task.WaitAsync(
                Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

            Assert.False(connection.ReplayVerification.IsCompleted);
        }

        // DisposeAsync awaits the verification, so by here it has settled one way or the other.
        Assert.True(connection.ReplayVerification.IsCompleted);
        Assert.Empty(reported);
    }

    // The verification window is HandshakeTimeout long, which is ample room for a caller to
    // unsubscribe from a pair the replay is still waiting on. That pair is not lost -- they asked
    // for it to stop -- and reporting it would be a false alarm on the one signal that has to be
    // trusted.
    [Fact]
    public async Task APairUnsubscribedInsideTheWindowIsNotReportedAsLost()
    {
        FakeWebSocket first = new() { AutoAcknowledgeSubscribes = true };
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

        await connection.SubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        List<MassiveStreamSubscriptionException> reported = [];
        connection.SubscriptionsLost += error => reported.Add(error);

        TaskCompletionSource reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Reconnected += _ => reconnected.TrySetResult();

        first.AbortNext();
        await reconnected.Task.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        // Inside the window: the replay is still waiting on T.AAPL and the deadline has not passed.
        Assert.False(connection.ReplayVerification.IsCompleted);
        await connection.UnsubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        await connection.ReplayVerification.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Empty(reported);
    }

    // Issue #60 on the replay's side. Nobody is awaiting a reconnect's replay, so its shortfall
    // arrives through SubscriptionsLost rather than a throw -- but it is the same evidence and it
    // has to carry the same correction. A consumer whose plan lost an entitlement mid-session
    // learns it here, and the inferred wording would tell them their topic codes are wrong.
    [Fact]
    public async Task AReplayTheServerRefusesCarriesTheServersReason()
    {
        FakeWebSocket first = new() { AutoAcknowledgeSubscribes = true };
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

        await connection.SubscribeAsync("NOI", ["AAPL"], TestContext.Current.CancellationToken);

        TaskCompletionSource<MassiveStreamSubscriptionException> lost =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.SubscriptionsLost += error => lost.TrySetResult(error);

        first.AbortNext();

        await second.WaitForSendAsync("""{"action":"subscribe","params":"NOI.AAPL"}""")
            .WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);
        second.EnqueueText("""[{"ev":"status","status":"error","message":"not authorized"}]""");

        MassiveStreamSubscriptionException reported = await lost.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Equal("not authorized", reported.ServerMessage);
        Assert.Contains("not authorized", reported.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ignores a topic code", reported.Message, StringComparison.Ordinal);
    }

    // The attribution rule, and the reason it is the INVERSE of OnStatus's acknowledgement
    // crediting order. An acknowledgement names its pair, so the order there only breaks a tie
    // between two waiters owed byte-identical text. A refusal names nothing -- the frame observed
    // live carried no `params` and no topic, and arrived AHEAD of the acknowledgements for the
    // pairs that were accepted -- so precedence has to come from somewhere other than content.
    // "Whoever is sending" is that somewhere: _pendingAcks is armed only while SubscribeAsync
    // holds _subscribeGate, so armed and sending are the same thing, while the replay slot
    // deliberately outlives its reconnect and can be armed with nothing in flight at all.
    //
    // Getting this backwards is silent in both directions: the caller would be told the server
    // ignored their topic code while a healthy replay was blamed for a refusal aimed at someone
    // else. So both halves are asserted here, on DIFFERENT pairs, which is what stops either from
    // passing by coincidence.
    [Fact]
    public async Task ARefusalGoesToTheCallerWhoSentItNotTheReplayStillWaiting()
    {
        FakeWebSocket first = new() { AutoAcknowledgeSubscribes = true };
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

        await connection.SubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        TaskCompletionSource<MassiveStreamSubscriptionException> lost =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.SubscriptionsLost += error => lost.TrySetResult(error);

        TaskCompletionSource reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Reconnected += _ => reconnected.TrySetResult();

        // T.AAPL is replayed and never answered, so the replay slot is armed and counting down
        // throughout everything below -- but it has finished sending.
        first.AbortNext();
        await reconnected.Task.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Task subscribe = connection.SubscribeAsync("NOI", ["AAPL"], TestContext.Current.CancellationToken);

        await second.WaitForSendAsync("""{"action":"subscribe","params":"NOI.AAPL"}""")
            .WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        // The overlap is the whole point, so it is asserted rather than hoped for: had the replay's
        // window already closed, the refusal would have only one slot to land in and this would
        // pass without testing anything.
        Assert.False(connection.ReplayVerification.IsCompleted);

        second.EnqueueText("""[{"ev":"status","status":"error","message":"not authorized"}]""");

        MassiveStreamSubscriptionException caller =
            await Assert.ThrowsAsync<MassiveStreamSubscriptionException>(() => subscribe);

        Assert.Equal("NOI.AAPL", caller.Parameters);
        Assert.Equal("not authorized", caller.ServerMessage);

        // And the replay, which sent nothing while that refusal was in flight, reports its own
        // shortfall with no reason attached -- because it genuinely was not given one.
        MassiveStreamSubscriptionException reported = await lost.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Equal("T.AAPL", reported.Parameters);
        Assert.Null(reported.ServerMessage);
    }

    // The replay slot's own half of "a refusal is not carried forward". Clearing it when the
    // verification tears down is not enough: VerifyReplayAsync returns early WITHOUT clearing when
    // a later reconnect has taken the slot over, which is right there -- the slot is in use -- but
    // leaves the previous attempt's reason for the new one to inherit. So the arming clears it.
    //
    // Reachable whenever two reconnects land inside one HandshakeTimeout: the first replay is
    // refused out loud, the second is met with silence, and the second would report the first's
    // reason to a consumer whose second attempt was never given one.
    [Fact]
    public async Task ARefusalIsNotCarriedFromOneReplayToTheNext()
    {
        FakeWebSocket first = new() { AutoAcknowledgeSubscribes = true };
        FakeWebSocket second = new();
        FakeWebSocket third = new();
        FakeWebSocket[] sockets = [first, second, third];
        int created = 0;

        foreach (FakeWebSocket socket in sockets)
        {
            socket.EnqueueText(Connected);
            socket.EnqueueText(AuthSuccess);
        }

        await using MassiveStreamConnection connection = new(
            FastReconnect(),
            MassiveMarket.Stocks,
            () => sockets[created++],
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        await connection.SubscribeAsync("NOI", ["AAPL"], TestContext.Current.CancellationToken);

        List<MassiveStreamSubscriptionException> reported = [];
        connection.SubscriptionsLost += error => reported.Add(error);

        // Gated at 3 -- Connected, AuthSuccess, and the refusal -- so awaiting it proves the
        // refusal was processed and recorded against the FIRST replay before the second begins.
        Task refusalConsumed = second.GateReceiveAfter(3);

        first.AbortNext();

        await second.WaitForSendAsync("""{"action":"subscribe","params":"NOI.AAPL"}""")
            .WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);
        second.EnqueueText("""[{"ev":"status","status":"error","message":"not authorized"}]""");

        await refusalConsumed.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);
        second.ReleaseReceive();

        // The first replay's window must still be open, or its own teardown would clear the reason
        // and everything below would pass without testing anything. Asserted rather than assumed:
        // this turns that race into a loud failure instead of a false pass.
        Assert.Empty(reported);

        // The second replay is met with silence. Its shortfall is real, but it was given no reason.
        second.AbortNext();

        MassiveStreamSubscriptionException last = await WaitForReportAsync(
            reported, parameters: "NOI.AAPL", TestContext.Current.CancellationToken);

        Assert.Null(last.ServerMessage);
        Assert.Contains("ignores a topic code", last.Message, StringComparison.Ordinal);
        Assert.Contains("""{"action":"subscribe","params":"NOI.AAPL"}""", third.Sent);
    }

    // Polls the collected reports for one carrying no server reason. The first replay's own report
    // may or may not have landed by now -- it names the same pair and carries the refusal it
    // genuinely was given -- so this waits for the one the test is about rather than assuming an
    // ordering between two independent verification windows.
    private static async Task<MassiveStreamSubscriptionException> WaitForReportAsync(
        List<MassiveStreamSubscriptionException> reported, string parameters, CancellationToken cancellationToken)
    {
        Task<MassiveStreamSubscriptionException> poll = Task.Run(async () =>
        {
            while (true)
            {
                foreach (MassiveStreamSubscriptionException candidate in reported.ToArray())
                {
                    if (candidate.Parameters == parameters && candidate.ServerMessage is null)
                    {
                        return candidate;
                    }
                }

                await Task.Delay(Duration.FromMilliseconds(10).ToTimeSpan(), cancellationToken);
            }
        }, cancellationToken);

        return await poll.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), cancellationToken);
    }

    // Bounded rather than awaited directly: a sink nothing ever writes to would otherwise hang
    // until the runner's own much longer timeout instead of failing fast and readably (the shape
    // ReconnectTests.ReadAllAsync already uses).
    private static async Task<StockTrade> ReadOneAsync(TopicSink<StockTrade> sink, CancellationToken cancellationToken)
    {
        Task<StockTrade> consumer = Task.Run(async () =>
        {
            await foreach (StockTrade trade in sink.Subscription.WithCancellation(cancellationToken))
            {
                return trade;
            }

            throw new InvalidOperationException("The sink's sequence ended without delivering anything.");
        }, cancellationToken);

        return await consumer.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), cancellationToken);
    }
}
