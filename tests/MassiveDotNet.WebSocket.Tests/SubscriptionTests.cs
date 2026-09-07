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

    [Fact]
    public async Task ItSendsOneCommaSeparatedSubscribeAndWaitsForEveryAcknowledgement()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        socket.EnqueueText(
            """[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"},{"ev":"status","status":"success","message":"subscribed to: T.MSFT"}]""");

        await connection.SubscribeAsync("T", ["AAPL", "MSFT"], TestContext.Current.CancellationToken);

        Assert.Contains("""{"action":"subscribe","params":"T.AAPL,T.MSFT"}""", socket.Sent);
    }

    // The finding that shaped the design: the server drops an unrecognised pair in silence, so a
    // subscription that does not exist is indistinguishable from a quiet market (D-W2).
    [Fact]
    public async Task AShortfallOfAcknowledgementsThrows()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        socket.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""");

        MassiveStreamSubscriptionException error =
            await Assert.ThrowsAsync<MassiveStreamSubscriptionException>(async () =>
                await connection.SubscribeAsync("T", ["AAPL", "MSFT"], TestContext.Current.CancellationToken));

        Assert.Equal(1, error.Unacknowledged);
        Assert.Contains("T.AAPL,T.MSFT", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WildcardsSubscribeLikeAnyOtherTicker()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        socket.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: T.*"}]""");

        await connection.SubscribeAsync("T", ["*"], TestContext.Current.CancellationToken);

        Assert.Contains("""{"action":"subscribe","params":"T.*"}""", socket.Sent);
    }

    [Fact]
    public async Task TheRegistryRemembersWhatReconnectMustReplay()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        socket.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""");
        await connection.SubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        socket.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: Q.MSFT"}]""");
        await connection.SubscribeAsync("Q", ["MSFT"], TestContext.Current.CancellationToken);

        Assert.Equal(["Q.MSFT", "T.AAPL"], connection.Registry.Parameters.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task UnsubscribeSendsTheActionAndForgetsThePair()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        socket.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""");
        await connection.SubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        await connection.UnsubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        Assert.Contains("""{"action":"unsubscribe","params":"T.AAPL"}""", socket.Sent);
        Assert.Empty(connection.Registry.Parameters);
    }

    [Theory]
    [InlineData(StockTopic.Trades, "T")]
    [InlineData(StockTopic.Quotes, "Q")]
    public void TopicsRenderAsTheirWireCodes(StockTopic topic, string expected) =>
        Assert.Equal(expected, topic.ToCode());
}
