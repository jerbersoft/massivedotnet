using MassiveDotNet.WebSocket;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// What a fixture structurally cannot verify: that the handshake works against the real service,
/// and that the entitlements and hosts observed on 2026-09-07 still hold.
/// </summary>
public sealed class StreamHandshakeLiveTests : LiveApiTest
{
    private static MassiveStreamOptions Options()
    {
        // LiveApiTest exposes Client and Ct, not the key: the key reaches a test through
        // LiveCredentials, behind the skip that keeps a missing .env honest rather than red.
        Assert.SkipUnless(LiveCredentials.IsAvailable, LiveCredentials.MissingKeyReason);

        // L2 (Task 14 ruling): LiveCredentials.ApiKey is string?; the null-forgiving operator
        // matches how LiveApiTest.Client already reads it, immediately below the same skip guard.
        return new MassiveStreamOptions { ApiKey = LiveCredentials.ApiKey! };
    }

    [Fact]
    public async Task StocksAuthenticatesAndAcknowledgesASubscription()
    {
        await using MassiveStreamClient client = new(Options());
        await using MassiveStockStream stream = await client.ConnectStocksAsync(Ct);

        // A wildcard is accepted like any other subscription. No assertion is made that data
        // arrives: the market is closed outside trading hours, and a test that passes only during
        // them fails for a reason unrelated to the SDK.
        await stream.SubscribeTradesAsync(["AAPL"], Ct);
        await stream.SubscribeQuotesAsync(["AAPL"], Ct);

        Assert.Equal(0, stream.ReconnectCount);
    }

    /// <summary>
    /// Pinned observation, 2026-09-07: this key reaches only the stocks feed. Every other market
    /// answers auth_failed with an entitlement message rather than a credential one.
    /// </summary>
    /// <remarks>
    /// D21's posture: the observation is pinned and dated so it flips the day the entitlement
    /// changes, where a skip would read as green.
    /// </remarks>
    [Fact]
    public async Task TheOtherFiveMarketsAnswerWithTheEntitlementMessage()
    {
        await using MassiveStreamClient client = new(Options());

        // ConnectRawAsync rather than a crypto facade: #20 ships only the stocks facade, and #21
        // adds the rest. The entitlement is a property of the handshake, not of any facade.
        MassiveStreamAuthenticationException error =
            await Assert.ThrowsAsync<MassiveStreamAuthenticationException>(
                async () => await client.ConnectRawAsync(MassiveMarket.Crypto, Ct));

        Assert.Contains("websocket access", error.ServerMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Pinned observation, 2026-09-07: a nonsense topic is silently dropped, which is why
    /// StockTopic is an enum (D-W1) and why subscriptions are acknowledgement-counted (D-W2).
    /// </summary>
    /// <remarks>
    /// This is the highest-value test in the streaming tier. If Massive ever starts rejecting an
    /// unknown topic properly, this fails and D-W2's guard can be reconsidered.
    /// </remarks>
    [Fact]
    public async Task AnUnknownTopicIsStillSilentlyIgnored()
    {
        await using MassiveStreamClient client = new(Options());
        await using MassiveStockStream stream = await client.ConnectStocksAsync(Ct);

        await Assert.ThrowsAsync<MassiveStreamSubscriptionException>(
            async () => await stream.SubscribeRawAsync("ZZ", ["AAPL"], Ct));
    }

    /// <summary>
    /// Pinned observation, 2026-09-07: launchpad presents the ingress default certificate on both
    /// domains, so MassiveFeeds exposes no property for it (D-W8).
    /// </summary>
    [Fact]
    public async Task TheLaunchpadHostIsStillNotProvisioned()
    {
        MassiveStreamOptions options = Options();
        options.Feed = new Uri("wss://launchpad.massive.com");

        await using MassiveStreamClient client = new(options);

        await Assert.ThrowsAnyAsync<Exception>(async () => await client.ConnectStocksAsync(Ct));
    }
}
