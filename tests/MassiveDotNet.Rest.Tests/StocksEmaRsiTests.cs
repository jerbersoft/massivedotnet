using System.Text.Json;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// EMA and RSI are SMA property for property, so they reuse <see cref="IndicatorSeries"/> and
/// the generator verifies the reuse at each site (D16). These tests pin the two routes and one
/// traversal; the page shape itself is covered by <see cref="StocksIndicatorsTests"/>.
/// </summary>
public sealed class StocksEmaRsiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task EmaBuildsTheDocumentedRequestWithEveryParameter()
    {
        StubHandler handler = new(Fixtures.StocksEma);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListEmaAsync(
                "AAPL",
                timestamp: RangeFilter.Between(
                    DateOrTimestamp.FromDate(new LocalDate(2024, 1, 1)),
                    DateOrTimestamp.FromDate(new LocalDate(2024, 6, 30))),
                timespan: AggregateTimespan.Day,
                adjusted: true,
                window: 50,
                seriesType: SeriesType.Close,
                expandUnderlying: false,
                order: SortOrder.Ascending,
                limit: 1,
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v1/indicators/ema/AAPL"
                + "?timestamp.gte=2024-01-01&timestamp.lte=2024-06-30"
                + "&timespan=day&adjusted=true&window=50&series_type=close&expand_underlying=false&order=asc&limit=1",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task EmaDeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.StocksEma);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePagedResult<IndicatorSeries> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListEmaAsync("AAPL", cancellationToken: Ct);
        }

        Assert.NotNull(page.Result.Values);
        IndicatorValue value = Assert.Single(page.Result.Values);
        Assert.Equal(140.139, value.Value);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1517562000016), value.Timestamp);
        Assert.NotNull(page.Result.Underlying);
        Assert.Null(page.Result.Underlying.Aggregates);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task EmaEnumerateCrossesThePageBoundary()
    {
        // The SMA last page is the same envelope shape, which is the point of the reuse.
        PagingStubHandler handler = new(Fixtures.StocksEma, Fixtures.StocksSmaLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<long> timestamps = [];

        using (client)
        using (transport)
        {
            await foreach (IndicatorValue value in client.Stocks.EnumerateEmaAsync("AAPL", limit: 1, cancellationToken: Ct))
            {
                timestamps.Add(value.TimestampMilliseconds);
            }
        }

        Assert.Equal([1517562000016, 1517475600016], timestamps);
        Assert.Equal(2, handler.Requests.Count);

        using JsonDocument firstPage = JsonDocument.Parse(Fixtures.StocksEma);
        Uri nextUrl = new(firstPage.RootElement.GetProperty("next_url").GetString()!);
        Assert.Equal(nextUrl.PathAndQuery, handler.Requests[1].PathAndQuery);
    }

    [Fact]
    public async Task RsiBuildsTheDocumentedRequestPath()
    {
        StubHandler handler = new(Fixtures.StocksRsi);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListRsiAsync("AAPL", window: 14, timespan: AggregateTimespan.Day, cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v1/indicators/rsi/AAPL?timespan=day&window=14", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task RsiDeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.StocksRsi);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePagedResult<IndicatorSeries> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListRsiAsync("AAPL", cancellationToken: Ct);
        }

        Assert.NotNull(page.Result.Values);
        Assert.Equal(82.19, Assert.Single(page.Result.Values).Value);
        Assert.True(page.HasMore);
    }
}
