using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The grouped daily endpoint: a calendar date in the path, and a bar per ticker whose wide
/// integers the description declares without a format.
/// </summary>
public sealed class StocksGroupedDailyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task BuildsTheDocumentedRequestWithEveryParameter()
    {
        StubHandler handler = new(Fixtures.StocksGroupedDaily);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListGroupedDailyAsync(new LocalDate(2020, 10, 14), adjusted: true, includeOtc: true, cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v2/aggs/grouped/locale/us/market/stocks/2020-10-14?adjusted=true&include_otc=true",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task OmitsTheOptionalParametersWhenNull()
    {
        StubHandler handler = new(Fixtures.StocksGroupedDaily);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListGroupedDailyAsync(new LocalDate(2020, 10, 14), cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v2/aggs/grouped/locale/us/market/stocks/2020-10-14", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesTheCorrectedSample()
    {
        StubHandler handler = new(Fixtures.StocksGroupedDaily);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        GroupedDailyBar[] bars;

        using (client)
        using (transport)
        {
            bars = await client.Stocks.ListGroupedDailyAsync(new LocalDate(2020, 10, 14), cancellationToken: Ct);
        }

        Assert.Equal(3, bars.Length);

        GroupedDailyBar first = bars[0];
        Assert.Equal("KIMpL", first.Ticker);
        Assert.Equal(26.07, first.Open);
        Assert.Equal(26.25, first.High);
        Assert.Equal(25.91, first.Low);
        Assert.Equal(25.9102, first.Close);
        Assert.Equal(4369d, first.Volume);
        Assert.Equal(26.0407, first.VolumeWeightedAveragePrice);
        Assert.Equal(74L, first.TransactionCount);
        Assert.False(first.IsOtc);

        // 1602705600000 ms is 2020-10-14T20:00:00Z, which overflows int32 by five orders of
        // magnitude: the description declares a bare integer, and the map corrects it to long.
        Assert.Equal(1602705600000, first.TimestampMilliseconds);
        Assert.Equal(Instant.FromUtc(2020, 10, 14, 20, 0), first.Timestamp);

        Assert.Equal("TANH", bars[1].Ticker);
        Assert.Equal(25933.6, bars[1].Volume);
        Assert.Equal("VSAT", bars[2].Ticker);
    }

    [Fact]
    public async Task AnEmptyResultsArrayIsAnEmptyArray()
    {
        StubHandler handler = new("""{ "adjusted": true, "queryCount": 0, "request_id": "r", "results": [], "resultsCount": 0, "status": "OK" }""");
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            Assert.Empty(await client.Stocks.ListGroupedDailyAsync(new LocalDate(2020, 10, 11), cancellationToken: Ct));
        }
    }
}
