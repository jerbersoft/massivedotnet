using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

public class FeedTests
{
    public static TheoryData<Uri, string> ProductionFeeds() => new()
    {
        { MassiveFeeds.RealTime, "wss://socket.massive.com/" },
        { MassiveFeeds.Delayed, "wss://delayed.massive.com/" },
        { MassiveFeeds.Business, "wss://business.massive.com/" },
        { MassiveFeeds.DelayedBusiness, "wss://delayed-business.massive.com/" },
        { MassiveFeeds.PolyFeed, "wss://polyfeed.massive.com/" },
        { MassiveFeeds.PolyFeedPlus, "wss://polyfeedplus.massive.com/" },
        { MassiveFeeds.NasdaqFeed, "wss://nasdaqfeed.massive.com/" },
        { MassiveFeeds.StarterFeed, "wss://starterfeed.massive.com/" },
    };

    [Theory]
    [MemberData(nameof(ProductionFeeds))]
    public void ProductionFeedsAreAbsoluteWebSocketUris(Uri feed, string expected)
    {
        Assert.True(feed.IsAbsoluteUri);
        Assert.Equal("wss", feed.Scheme);
        Assert.Equal(expected, feed.ToString());
    }

    [Fact]
    public void LegacyFeedsMirrorProductionOnThePolygonDomain()
    {
        Assert.Equal("wss://socket.polygon.io/", MassiveFeeds.Legacy.RealTime.ToString());
        Assert.Equal("wss://delayed-business.polygon.io/", MassiveFeeds.Legacy.DelayedBusiness.ToString());
    }

    // launchpad is named in issue #20 but presents the ingress default certificate on both
    // domains (2026-09-07), so shipping a property for it would hand a caller a TLS failure
    // from a name the SDK told them was real. This test is the reminder, not a formality.
    [Fact]
    public void NoLaunchpadFeedIsExposed()
    {
        Assert.DoesNotContain(
            typeof(MassiveFeeds).GetProperties(),
            property => property.Name.Contains("Launchpad", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(MassiveMarket.Stocks, "stocks")]
    [InlineData(MassiveMarket.Options, "options")]
    [InlineData(MassiveMarket.Indices, "indices")]
    [InlineData(MassiveMarket.Forex, "forex")]
    [InlineData(MassiveMarket.Crypto, "crypto")]
    [InlineData(MassiveMarket.Futures, "futures")]
    public void MarketsRenderAsLowercasePathSegments(MassiveMarket market, string expected) =>
        Assert.Equal(expected, market.ToPathSegment());
}
