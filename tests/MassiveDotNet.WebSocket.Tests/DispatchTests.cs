using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MassiveDotNet.WebSocket.Events;
using MassiveDotNet.WebSocket.Internal;
using NodaTime;
using NodaTime.Testing;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// Covers the dispatch that routes an incoming event to its topic sink by <c>ev</c> code, and the
/// three defects Task 10's review found in it: the mixed status/data frame that used to lose ticks
/// (T1), the sink table's data race (T3), and reading <c>ev</c> the way every other field in this
/// codebase is read (T4). BackpressureTests covers TopicSink/MassiveTopicSubscription in isolation;
/// this file covers MassiveStreamConnection's routing around them.
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

    // RULING T3. AddSink runs on the caller's thread while DispatchAsync reads the sink table on
    // the read-loop thread StartReading began -- a genuine race on a plain Dictionary, not a
    // theoretical one. This registers many sinks -- distinct keys, so the table actually grows and
    // rehashes rather than just replacing a value -- concurrently with the read loop actively
    // dispatching a stream of frames, and checks both that nothing corrupts and that a sink
    // registered mid-stream starts receiving what arrives after it goes live.
    [Fact]
    public async Task RegisteringASinkWhileDispatchIsRunningIsSafeAndItStartsReceiving()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        TopicSink<StockTrade> tradeSink = new("T", capacity: 4096, new StockTradeConverter(new TickerPool(16)));

        // Real OS threads running tight, unyielding loops for a shared wall-clock window, rather
        // than Task.Run plus periodic Task.Yield: cooperative yielding onto the shared ThreadPool
        // did not produce enough genuinely simultaneous access to the field to serve as evidence
        // either way. Stopwatch.ElapsedMilliseconds is a long, not a TimeSpan (rule 12).
        Stopwatch clock = Stopwatch.StartNew();
        const long RunMilliseconds = 500;

        // Producer thread: keeps the socket fed for the whole run, so the read loop's DispatchAsync
        // -- the reader side of the race -- never runs dry waiting for the next frame.
        Thread producer = new(() =>
        {
            int i = 0;

            while (clock.ElapsedMilliseconds < RunMilliseconds)
            {
                socket.EnqueueText($$"""[{"ev":"T","sym":"AAPL","i":"{{i}}","p":1,"s":1,"t":1,"q":{{i}}}]""");
                i++;
            }
        });

        // Writer thread: rebuilds the sink table as fast as it can for the same window -- distinct
        // keys each time, so the table genuinely grows and rehashes rather than just replacing a
        // value -- while the read loop, already running via ConnectAsync's StartReading, is
        // concurrently calling DispatchAsync on its own thread.
        Thread writer = new(() =>
        {
            int i = 0;

            while (clock.ElapsedMilliseconds < RunMilliseconds)
            {
                connection.AddSink(new TopicSink<StockTrade>($"decoy{i}", capacity: 1, new StockTradeConverter(new TickerPool(1))));
                i++;
            }

            connection.AddSink(tradeSink);
        });

        producer.Start();
        writer.Start();
        producer.Join();
        writer.Join();

        // Drain whatever reaches the sink once it is live, bounded rather than sleeping a fixed
        // amount: exactly how much arrives after registration depends on how the race above
        // interleaved, which is the point -- only that at least one event does, and nothing throws
        // along the way.
        List<StockTrade> received = [];

        using CancellationTokenSource cts =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(5000);

        try
        {
            await foreach (StockTrade trade in tradeSink.Subscription.WithCancellation(cts.Token))
            {
                received.Add(trade);

                if (received.Count >= 50)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Only reached if fewer than 50 arrived before the bound above -- the assertion below
            // still catches that case.
        }

        Assert.False(connection.ReadLoopTask.IsFaulted, connection.ReadLoopTask.Exception?.ToString());
        Assert.NotEmpty(received);
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
}
