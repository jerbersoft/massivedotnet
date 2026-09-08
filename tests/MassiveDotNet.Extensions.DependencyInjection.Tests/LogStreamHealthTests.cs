using MassiveDotNet.WebSocket;
using MassiveDotNet.WebSocket.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Testing;
using Xunit;

namespace MassiveDotNet.Extensions.DependencyInjection.Tests;

/// <summary>
/// F3/H8 (Task 12 review round 1): <c>LogStreamHealth</c> is the only code in the SDK that formats
/// stream state into log text, which makes it the only place an API key could ever reach a log --
/// rule 11 says never. Before this, that held only by inspection (the two <c>[LoggerMessage]</c>
/// templates interpolate an <c>int</c> and a <c>long</c>, never <c>options.ApiKey</c>), which is
/// exactly the kind of claim that stops being true the day someone adds a third placeholder without
/// re-reading this file. A distinctive sentinel key, a capturing logger, and an assertion that the
/// sentinel never appears anywhere in what was captured turns that inspection into a gate.
/// </summary>
public sealed class LogStreamHealthTests
{
    private const string Connected = """[{"ev":"status","status":"connected","message":"Connected Successfully"}]""";
    private const string AuthSuccess = """[{"ev":"status","status":"auth_success","message":"authenticated"}]""";
    private const string SentinelApiKey = "SENTINEL-KEY-MUST-NOT-BE-LOGGED";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(MassiveStockStream Stream, FakeWebSocket First, FakeWebSocket Second)> ConnectAsync(IClock clock)
    {
        FakeWebSocket first = new() { AutoAcknowledgeSubscribes = true };
        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        FakeWebSocket second = new() { AutoAcknowledgeSubscribes = true };
        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        int created = 0;
        MassiveStreamClient client = new(
            new MassiveStreamOptions { ApiKey = SentinelApiKey, TopicBufferCapacity = 1 }, clock);
        MassiveStockStream stream = await client.ConnectStocksAsync(() => created++ == 0 ? first : second, Ct);

        return (stream, first, second);
    }

    [Fact]
    public async Task NeitherADropNorAReconnectEverLogsTheApiKey()
    {
        FakeClock clock = new(Instant.FromUnixTimeSeconds(0));
        (MassiveStockStream stream, FakeWebSocket first, FakeWebSocket second) = await ConnectAsync(clock);
        await using MassiveStockStream _ = stream;

        CapturingLoggerProvider capture = new();
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(capture);
        });
        ILogger logger = factory.CreateLogger("stream-health-test");

        stream.LogStreamHealth(logger);

        await stream.SubscribeTradesAsync(["AAPL"], Ct);

        // Force an overflow: capacity 1, three writes with nobody reading evicts the first two --
        // (a) below needs at least one drop warning to have actually fired.
        first.EnqueueText(
            """[{"ev":"T","sym":"AAPL","i":"1","p":1,"s":1,"t":1,"q":1},{"ev":"T","sym":"AAPL","i":"2","p":2,"s":1,"t":2,"q":2},{"ev":"T","sym":"AAPL","i":"3","p":3,"s":1,"t":3,"q":3}]""");

        // A widening subscribe, once acknowledged, guarantees every earlier frame -- including the
        // drops above -- has already been dispatched (same causality barrier StockStreamTests uses).
        await stream.SubscribeTradesAsync(["MSFT"], Ct);

        // Force a reconnect: (b) below needs the reconnect warning to have actually fired too.
        TaskCompletionSource reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        stream.Reconnected += _ => reconnected.TrySetResult();

        first.AbortNext();
        await reconnected.Task.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), Ct);

        string captured = capture.Text;

        // (a) a drop warning fired, carrying the correct delta. The throttle is edge-triggered
        // (StockStreamTests.DropObservedIsThrottledToAtMostOncePerSecond pins this in detail): the
        // burst causes two drops, but only the FIRST reaches DropObserved before the window closes
        // over the second -- so the one warning that fires carries a delta of 1, the running total
        // at that first drop, not 2.
        Assert.Contains("dropped 1 events on topic T", captured, StringComparison.Ordinal);

        // (b) the reconnect warning fired, carrying the count.
        Assert.Contains("reconnected (1 so far)", captured, StringComparison.Ordinal);

        // (c) the load-bearing assertion: the sentinel never appears anywhere captured -- no
        // message, no scope, no state value.
        Assert.DoesNotContain(SentinelApiKey, captured, StringComparison.Ordinal);
    }

    // F7 (Task 12 review round 1) moved the drop count onto the event so one LogStreamHealth call
    // covers every topic. That made the "how much is new since I last logged" delta a PER-TOPIC
    // question, and the fix is a Dictionary keyed by topic code. Nothing pinned it: the re-review
    // broke the dictionary back to a single scalar and all 796 tests stayed green, so the bug this
    // test describes could have returned unnoticed.
    //
    // With one shared scalar, the second topic's drop is measured against the FIRST topic's running
    // total, so `droppedCount > previouslyReported` is false and the quotes warning is silently
    // never logged -- a consumer watching the log would conclude quotes were keeping up while they
    // were being dropped. The clock is advanced between the two because DropObserved's throttle is
    // stream-wide, not per topic, so without it the quotes drop is suppressed for a reason that has
    // nothing to do with what this test is about.
    [Fact]
    public async Task DropsOnTwoTopicsAreCountedSeparatelyRatherThanContinuingEachOther()
    {
        FakeClock clock = new(Instant.FromUnixTimeSeconds(0));
        (MassiveStockStream stream, FakeWebSocket first, FakeWebSocket _) = await ConnectAsync(clock);
        await using MassiveStockStream owned = stream;

        CapturingLoggerProvider capture = new();
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(capture);
        });

        stream.LogStreamHealth(factory.CreateLogger("stream-health-test"));

        await stream.SubscribeTradesAsync(["AAPL"], Ct);
        await stream.SubscribeQuotesAsync(["AAPL"], Ct);

        // Capacity 1 with nobody reading: two trades evict one.
        first.EnqueueText(
            """[{"ev":"T","sym":"AAPL","i":"1","p":1,"s":1,"t":1,"q":1},{"ev":"T","sym":"AAPL","i":"2","p":2,"s":1,"t":2,"q":2}]""");
        await stream.SubscribeTradesAsync(["MSFT"], Ct);

        // Past the stream-wide throttle window, so the quotes drop below is reported on its own
        // merits rather than being swallowed as a repeat of the trades one.
        clock.Advance(Duration.FromSeconds(2));

        first.EnqueueText(
            """[{"ev":"Q","sym":"AAPL","bp":9,"ap":11,"t":1,"q":1},{"ev":"Q","sym":"AAPL","bp":8,"ap":12,"t":2,"q":2}]""");
        await stream.SubscribeQuotesAsync(["MSFT"], Ct);

        string captured = capture.Text;

        Assert.Contains("dropped 1 events on topic T", captured, StringComparison.Ordinal);
        Assert.Contains("dropped 1 events on topic Q", captured, StringComparison.Ordinal);
    }

    // Issue #52: a reconnect whose replay the server never acknowledges is reported as reconnected
    // by every other signal the SDK has, so the log is where a consumer wiring the SDK through DI
    // actually finds out. Without this bridge the event exists and nobody watching the logs would
    // ever see it -- D-W5's whole argument for why core's silence is narrowed here rather than
    // teaching core about logging (rule 8).
    [Fact]
    public async Task AReplayTheServerNeverAcknowledgesIsReportedOnTheLogger()
    {
        FakeWebSocket first = new() { AutoAcknowledgeSubscribes = true };
        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        // Connects and authenticates, then answers the replay with silence -- the server behaviour
        // D33 records, where an unrecognised topic is ignored rather than refused.
        FakeWebSocket second = new();
        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        int created = 0;
        MassiveStreamClient client = new(
            new MassiveStreamOptions
            {
                ApiKey = SentinelApiKey,
                HandshakeTimeout = Duration.FromMilliseconds(250),
            },
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await using MassiveStockStream stream =
            await client.ConnectStocksAsync(() => created++ == 0 ? first : second, Ct);

        CapturingLoggerProvider capture = new();
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(capture);
        });

        stream.LogStreamHealth(factory.CreateLogger("stream-health-test"));

        await stream.SubscribeTradesAsync(["AAPL"], Ct);

        TaskCompletionSource lost = new(TaskCreationOptions.RunContinuationsAsynchronously);
        stream.SubscriptionsLost += _ => lost.TrySetResult();

        first.AbortNext();
        await lost.Task.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), Ct);

        string captured = capture.Text;

        Assert.Contains("never acknowledged 1 of the subscriptions it replayed (T.AAPL)", captured, StringComparison.Ordinal);

        // The same rule this file exists for: a third placeholder must not become the one that
        // finally carries a key into a log (rule 11).
        Assert.DoesNotContain(SentinelApiKey, captured, StringComparison.Ordinal);
    }
}
