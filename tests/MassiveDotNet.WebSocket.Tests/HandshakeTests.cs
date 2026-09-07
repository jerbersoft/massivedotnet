using MassiveDotNet.WebSocket.Internal;
using NodaTime;
using NodaTime.Testing;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

public class HandshakeTests
{
    private const string Connected = """[{"ev":"status","status":"connected","message":"Connected Successfully"}]""";
    private const string AuthSuccess = """[{"ev":"status","status":"auth_success","message":"authenticated"}]""";

    private static MassiveStreamConnection CreateConnection(
        FakeWebSocket socket,
        string apiKey = "test-key",
        MassiveMarket market = MassiveMarket.Stocks)
    {
        MassiveStreamOptions options = new() { ApiKey = apiKey };

        return new MassiveStreamConnection(options, market, () => socket, new FakeClock(Instant.FromUnixTimeSeconds(0)));
    }

    [Fact]
    public async Task ItAuthenticatesWithTheKeyAsAMessage()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        await using MassiveStreamConnection connection = CreateConnection(socket);
        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["""{"action":"auth","params":"test-key"}"""], socket.Sent);
    }

    [Fact]
    public async Task ABadKeyThrowsCarryingTheServerMessageVerbatim()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText("""[{"ev":"status","status":"auth_failed","message":"authentication failed"}]""");

        await using MassiveStreamConnection connection = CreateConnection(socket);

        MassiveStreamAuthenticationException error =
            await Assert.ThrowsAsync<MassiveStreamAuthenticationException>(
                async () => await connection.ConnectAsync(TestContext.Current.CancellationToken));

        Assert.Equal("authentication failed", error.ServerMessage);
    }

    // After auth_failed the server closes without a close handshake, so a next ReceiveAsync would
    // throw rather than report a Close frame (AbortNext reproduces that). The handshake never
    // issues that next read once the auth_failed status has been parsed, so the abrupt drop the
    // server performs afterward must never surface as anything but the authentication failure.
    [Fact]
    public async Task AuthFailedSurfacesEvenThoughTheServerDropsTheConnectionAfterwards()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText("""[{"ev":"status","status":"auth_failed","message":"authentication failed"}]""");
        socket.AbortNext();

        await using MassiveStreamConnection connection = CreateConnection(socket);

        MassiveStreamAuthenticationException error =
            await Assert.ThrowsAsync<MassiveStreamAuthenticationException>(
                async () => await connection.ConnectAsync(TestContext.Current.CancellationToken));

        Assert.Equal("authentication failed", error.ServerMessage);
    }

    // auth_failed carries two unrelated failures apart from the prose, and a caller's response to
    // each differs completely. The SDK reports what the server said rather than inventing a
    // category it cannot determine (D-W6).
    [Fact]
    public async Task AnUnentitledMarketThrowsTheSameTypeWithItsOwnMessage()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(
            """[{"ev":"status","status":"auth_failed","message":"Your plan doesn't include websocket access. Visit https://massive.com/pricing to upgrade."}]""");

        await using MassiveStreamConnection connection = CreateConnection(socket, market: MassiveMarket.Crypto);

        MassiveStreamAuthenticationException error =
            await Assert.ThrowsAsync<MassiveStreamAuthenticationException>(
                async () => await connection.ConnectAsync(TestContext.Current.CancellationToken));

        Assert.Contains("doesn't include websocket access", error.ServerMessage, StringComparison.Ordinal);
        Assert.Contains("doesn't include websocket access", error.Message, StringComparison.Ordinal);
    }

    // Rule 11, and the reason D-W9 exists: under REST the key lived in a header the SDK never
    // rendered. Here it is a frame body, one ToString() away from a log file.
    [Fact]
    public async Task NoExceptionEverNamesTheKey()
    {
        const string Key = "sk-live-do-not-leak-me";

        await using FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText("""[{"ev":"status","status":"auth_failed","message":"authentication failed"}]""");

        await using MassiveStreamConnection connection = CreateConnection(socket, apiKey: Key);

        MassiveStreamAuthenticationException error =
            await Assert.ThrowsAsync<MassiveStreamAuthenticationException>(
                async () => await connection.ConnectAsync(TestContext.Current.CancellationToken));

        Assert.DoesNotContain(Key, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItConnectsToTheMarketPathOnTheConfiguredFeed()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        await using MassiveStreamConnection connection = CreateConnection(socket, market: MassiveMarket.Options);
        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("wss://socket.massive.com/options"), connection.Endpoint);
    }

    [Fact]
    public async Task AnUnexpectedFirstMessageThrowsRatherThanProceeding()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText("""[{"ev":"status","status":"disconnected","message":"go away"}]""");

        await using MassiveStreamConnection connection = CreateConnection(socket);

        await Assert.ThrowsAsync<MassiveStreamException>(
            async () => await connection.ConnectAsync(TestContext.Current.CancellationToken));
    }

    // A frame can carry more than one status event (a subscribe to two topics acknowledges both in
    // one frame). Silently keeping only the first `destination.Length` of them would drop the rest
    // without a trace, which is the class of data loss this SDK refuses elsewhere (D29) -- so this
    // is refused rather than truncated.
    [Fact]
    public void ParseRefusesToSilentlyDropEventsThatDoNotFitTheDestination()
    {
        // Span<T> is a ref struct, so it cannot be captured by a lambda -- Assert.Throws is not
        // usable here, and the call is made directly instead.
        ReadOnlySpan<byte> payload =
            """[{"ev":"status","status":"connected","message":"a"},{"ev":"status","status":"auth_success","message":"b"}]"""u8;
        Span<StatusMessage> destination = new StatusMessage[1];

        MassiveStreamException? error = null;

        try
        {
            StatusMessage.Parse(payload, destination);
        }
        catch (MassiveStreamException caught)
        {
            error = caught;
        }

        Assert.NotNull(error);
    }
}
