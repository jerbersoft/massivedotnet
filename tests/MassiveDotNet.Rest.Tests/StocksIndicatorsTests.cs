using System.Net;
using System.Text.Json;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The first paginated singular result: one object under <c>results</c> whose <c>values</c>
/// continue across pages while its <c>underlying</c> belongs to each page (D-S2).
/// </summary>
public sealed class StocksIndicatorsTests
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
        StubHandler handler = new(Fixtures.StocksSma);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListSmaAsync(
                "AAPL",
                timestamp: RangeFilter.Between(
                    DateOrTimestamp.FromDate(new LocalDate(2024, 1, 1)),
                    DateOrTimestamp.FromDate(new LocalDate(2024, 6, 30))),
                timespan: AggregateTimespan.Day,
                adjusted: true,
                window: 10,
                seriesType: SeriesType.Close,
                expandUnderlying: true,
                order: SortOrder.Descending,
                limit: 2,
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v1/indicators/sma/AAPL"
                + "?timestamp.gte=2024-01-01&timestamp.lte=2024-06-30"
                + "&timespan=day&adjusted=true&window=10&series_type=close&expand_underlying=true&order=desc&limit=2",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSampleAsOnePageWithBothHalves()
    {
        StubHandler handler = new(Fixtures.StocksSma);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePagedResult<IndicatorSeries> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListSmaAsync("AAPL", cancellationToken: Ct);
        }

        Assert.NotNull(page.Result.Values);
        IndicatorValue value = Assert.Single(page.Result.Values);
        Assert.Equal(1517562000016, value.TimestampMilliseconds);
        Assert.Equal(140.139, value.Value);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1517562000016), value.Timestamp);

        Assert.NotNull(page.Result.Underlying);
        Assert.Equal("https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2003-01-01/2022-07-25", page.Result.Underlying.Url);
        Assert.NotNull(page.Result.Underlying.Aggregates);
        Assert.Equal(2, page.Result.Underlying.Aggregates.Length);
        Assert.Equal(75.0875, page.Result.Underlying.Aggregates[0].Close);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1577941200000), page.Result.Underlying.Aggregates[0].Timestamp);

        Assert.True(page.HasMore);
        Assert.Equal("a47d1beb8c11b6ae897ab76cdbbf35a3", page.RequestId);
    }

    [Fact]
    public async Task ReportsNoFurtherPagesFromAPageWithoutACursor()
    {
        StubHandler handler = new(Fixtures.StocksSmaLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePagedResult<IndicatorSeries> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListSmaAsync("AAPL", cancellationToken: Ct);
        }

        Assert.False(page.HasMore);
        Assert.NotNull(page.Result.Underlying);
        Assert.Null(page.Result.Underlying.Aggregates);
    }

    [Fact]
    public async Task EnumerateYieldsEveryValueAcrossPagesRequestingEachOnlyWhenNeeded()
    {
        PagingStubHandler handler = new(Fixtures.StocksSma, Fixtures.StocksSmaLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<long> timestamps = [];

        using (client)
        using (transport)
        {
            await foreach (IndicatorValue value in client.Stocks.EnumerateSmaAsync("AAPL", limit: 1, cancellationToken: Ct))
            {
                timestamps.Add(value.TimestampMilliseconds);

                // One page in flight: the second request is issued only after the first page's
                // single value has been consumed, never ahead of it.
                Assert.Equal(timestamps.Count, handler.Requests.Count);
            }
        }

        Assert.Equal([1517562000016, 1517475600016], timestamps);
        Assert.Equal(2, handler.Requests.Count);

        // The cursor is followed verbatim (D14): the second request must match the fixture's own
        // next_url exactly, not merely start with its cursor.
        using JsonDocument firstPage = JsonDocument.Parse(Fixtures.StocksSma);
        Uri nextUrl = new(firstPage.RootElement.GetProperty("next_url").GetString()!);

        Assert.Equal(nextUrl.PathAndQuery, handler.Requests[1].PathAndQuery);
    }

    [Fact]
    public async Task ASuccessWithoutItsPayloadThrowsWithTheRequestId()
    {
        StubHandler handler = new(Fixtures.SingularWithoutResults);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Stocks.ListSmaAsync("AAPL", cancellationToken: Ct));

            Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
            Assert.Equal("r", exception.RequestId);
            Assert.Contains("carried no 'results' payload", exception.Message, StringComparison.Ordinal);
            Assert.Contains("/v1/indicators/sma/AAPL", exception.Message, StringComparison.Ordinal);
        }
    }
}
