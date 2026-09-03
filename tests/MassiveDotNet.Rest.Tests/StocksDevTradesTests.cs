using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The in-development trades route: the first operation marked experimental by a <c>dev</c>
/// segment rather than <c>vX</c> (D22). The service did not serve it when these were written,
/// so the fixture is hand-written from the description's schema and the live tier pins the 404;
/// what these prove is that the request renders and the declared shape binds.
/// </summary>
public sealed class StocksDevTradesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // 2018-02-02T09:00:00.016036600Z, the first SIP timestamp in the fixture, exactly.
    private static readonly Instant FirstSipTimestamp = NodaConstants.UnixEpoch + Duration.FromNanoseconds(1517562000016036600);

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersTheDevRouteWithANanosecondBound()
    {
        StubHandler handler = new(Fixtures.StocksDevTrades);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListDevTradesAsync(
                "AAPL",
                sipTimestamp: RangeFilter.Gte(DateOrNanoseconds.FromInstant(FirstSipTimestamp)),
                limit: 2,
                sort: "sip_timestamp.asc",
                cancellationToken: Ct);
        }

        // The field is sip_timestamp here, not the v3 route's timestamp, and the segment is dev
        // where a released route carries its version.
        Assert.Equal(
            "https://api.massive.com/stocks/dev/trades/AAPL?sip_timestamp.gte=1517562000016036600&limit=2&sort=sip_timestamp.asc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesTheDeclaredShape()
    {
        StubHandler handler = new(Fixtures.StocksDevTrades);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<DevTrade> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListDevTradesAsync("AAPL", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);
        Assert.Equal("3f1c9e2b7a5d4c6e8b0a1f2d3c4e5b6a", page.RequestId);

        // The shape differs from Trade in three places: the ticker rides on every row, size is
        // an integer with a separate fraction, and there is no decimal size string.
        DevTrade first = page.Results[0];
        Assert.Equal("AAPL", first.Ticker);
        Assert.Equal("1", first.TradeId);
        Assert.Equal(171.55, first.Price);
        Assert.Equal(100, first.Size);
        Assert.Equal(0L, first.SizeFraction);
        Assert.Equal(11, first.ExchangeId);
        Assert.Equal(1063L, first.SequenceNumber);
        Assert.Equal(3, first.Tape);
        Assert.Equal([12, 41], first.Conditions!);
        Assert.Null(first.CorrectionIndicator);
        Assert.Null(first.TrfId);
        Assert.Null(first.TrfTimestampNanoseconds);
        Assert.Null(first.TrfTimestamp);
        Assert.Equal(1517562000016036600, first.SipTimestampNanoseconds);
        Assert.Equal(FirstSipTimestamp, first.SipTimestamp);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1517562000015577000), first.ParticipantTimestamp);

        DevTrade second = page.Results[1];
        Assert.Equal("2", second.TradeId);
        Assert.Equal(250000000L, second.SizeFraction);
        Assert.Equal(0L, second.CorrectionIndicator);
        Assert.Equal(202, second.TrfId);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1517562000015000000), second.TrfTimestamp);
    }
}
