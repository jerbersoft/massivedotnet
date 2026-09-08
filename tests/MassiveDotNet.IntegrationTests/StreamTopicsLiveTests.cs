using MassiveDotNet.WebSocket;
using MassiveDotNet.WebSocket.Events;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// What a fixture structurally cannot verify about the four topics issue #21 added: that the wire
/// codes are the ones the service actually serves, and that the entitlements and units observed on
/// 2026-09-08 still hold.
/// </summary>
/// <remarks>
/// An acknowledgement is the strongest evidence available that a topic code is right, because the
/// server answers a code it does not recognise with silence — no acknowledgement and no error
/// (D33). A subscribe that returns rather than throwing is therefore a positive result, not the
/// absence of a negative one.
/// </remarks>
public sealed class StreamTopicsLiveTests : LiveApiTest
{
    private static MassiveStreamOptions Options()
    {
        Assert.SkipUnless(LiveCredentials.IsAvailable, LiveCredentials.MissingKeyReason);

        return new MassiveStreamOptions { ApiKey = LiveCredentials.ApiKey! };
    }

    /// <summary>
    /// Pinned observation, 2026-09-08: the service acknowledges <c>A</c>, <c>AM</c> and
    /// <c>LULD</c>. No assertion is made that data arrives — the market is closed outside trading
    /// hours, and a test that passes only during them fails for a reason unrelated to the SDK.
    /// </summary>
    [Fact]
    public async Task TheAggregateAndBandTopicCodesAreAcknowledged()
    {
        await using MassiveStreamClient client = new(Options());
        await using MassiveStockStream stream = await client.ConnectStocksAsync(Ct);

        await stream.SubscribeSecondAggregatesAsync(["AAPL"], Ct);
        await stream.SubscribeMinuteAggregatesAsync(["AAPL"], Ct);
        await stream.SubscribeLimitUpLimitDownAsync(["AAPL"], Ct);

        Assert.Equal(0, stream.ReconnectCount);
    }

    /// <summary>
    /// Pinned observation, 2026-09-08: this key is not entitled to the imbalance topic. The server
    /// answers <c>{"ev":"status","status":"error","message":"not authorized"}</c> rather than the
    /// <c>success</c> a subscribe expects, so the acknowledgement count falls short and the
    /// subscribe throws.
    /// </summary>
    /// <remarks>
    /// D21's posture: the observation is pinned and dated so it flips the day the entitlement
    /// changes, where a skip would read as green.
    /// <para>
    /// The exception's <b>message</b> is deliberately not asserted here. It currently says the
    /// server "ignores a topic code it does not recognise", which is not what happened — the code
    /// was recognised and the plan was not entitled. That is issue #60, and it will change this
    /// message. Pinning the type and the parameter rather than the prose means #60 has to move
    /// this test deliberately without it failing for the wrong reason first.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheImbalanceTopicIsStillNotAuthorizedOnThisKey()
    {
        await using MassiveStreamClient client = new(Options());
        await using MassiveStockStream stream = await client.ConnectStocksAsync(Ct);

        MassiveStreamSubscriptionException error =
            await Assert.ThrowsAsync<MassiveStreamSubscriptionException>(() =>
                stream.SubscribeImbalancesAsync(["AAPL"], Ct));

        Assert.Equal("NOI.AAPL", error.Parameters);
        Assert.Equal(1, error.Unacknowledged);
    }

    /// <summary>
    /// Pinned observation, 2026-09-08: <c>LULD</c>'s <c>t</c> is Unix nanoseconds, against the
    /// documentation's own prose claiming milliseconds (D-W15). This is the test that flips the day
    /// either the wire or the documentation moves.
    /// </summary>
    /// <remarks>
    /// Band updates flow continuously during regular hours and not at all outside them, so this
    /// skips rather than fails when no event arrives within the window — an honest report to a
    /// person reading the output, not a false signal, since a run outside market hours proves
    /// nothing either way.
    /// </remarks>
    [Fact]
    public async Task ALiveBandUpdateCarriesANanosecondTimestamp()
    {
        await using MassiveStreamClient client = new(Options());
        await using MassiveStockStream stream = await client.ConnectStocksAsync(Ct);

        MassiveTopicSubscription<StockLimitUpLimitDown> bands =
            await stream.SubscribeLimitUpLimitDownAsync(["*"], Ct);

        using CancellationTokenSource window = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        window.CancelAfter(NodaTime.Duration.FromSeconds(30).ToTimeSpan());

        StockLimitUpLimitDown? observed = null;

        try
        {
            await foreach (StockLimitUpLimitDown band in bands.WithCancellation(window.Token))
            {
                observed = band;
                break;
            }
        }
        catch (OperationCanceledException)
        {
            // Falls through to the skip below.
        }

        Assert.SkipWhen(
            observed is null,
            "No limit up-limit down event arrived within 30 seconds. Band updates flow only during "
                + "regular trading hours, so this proves nothing outside them.");

        // Read as milliseconds, a nanosecond value of this magnitude lands roughly fifty-six
        // million years out, so the year is what distinguishes the two readings.
        int year = observed!.Value.Timestamp.InUtc().Year;

        Assert.InRange(year, 2020, 2100);
    }
}
