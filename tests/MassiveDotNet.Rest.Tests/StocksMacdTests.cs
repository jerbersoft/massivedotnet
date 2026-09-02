using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// MACD: the indicator whose values carry a signal and a histogram beside the line, and whose
/// window is three parameters rather than one.
/// </summary>
public sealed class StocksMacdTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersTheThreeWindows()
    {
        StubHandler handler = new(Fixtures.StocksMacd);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListMacdAsync(
                "AAPL",
                timespan: AggregateTimespan.Day,
                adjusted: true,
                shortWindow: 12,
                longWindow: 26,
                signalWindow: 9,
                seriesType: SeriesType.Close,
                order: SortOrder.Descending,
                limit: 2,
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v1/indicators/macd/AAPL"
                + "?timespan=day&adjusted=true&short_window=12&long_window=26&signal_window=9&series_type=close&order=desc&limit=2",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSampleWithSignalAndHistogram()
    {
        StubHandler handler = new(Fixtures.StocksMacd);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePagedResult<MacdSeries> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListMacdAsync("AAPL", cancellationToken: Ct);
        }

        Assert.NotNull(page.Result.Values);
        Assert.Equal(2, page.Result.Values.Length);

        MacdValue first = page.Result.Values[0];
        Assert.Equal(145.3613333333, first.Value);
        Assert.Equal(106.9811666667, first.Signal);
        Assert.Equal(38.3801666667, first.Histogram);
        Assert.Equal(1517562000016, first.TimestampMilliseconds);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1517562000016), first.Timestamp);

        Assert.Equal(1517562001016, page.Result.Values[1].TimestampMilliseconds);

        Assert.NotNull(page.Result.Underlying);
        Assert.Equal("https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2003-01-01/2022-07-25", page.Result.Underlying.Url);
        Assert.True(page.HasMore);
        Assert.Equal("a47d1beb8c11b6ae897ab76cdbbf35a3", page.RequestId);
    }

    [Fact]
    public async Task EnumerateYieldsEveryValueAcrossPages()
    {
        PagingStubHandler handler = new(Fixtures.StocksMacd, Fixtures.StocksMacdLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<double> histograms = [];

        using (client)
        using (transport)
        {
            await foreach (MacdValue value in client.Stocks.EnumerateMacdAsync("AAPL", limit: 2, cancellationToken: Ct))
            {
                histograms.Add(value.Histogram);
            }
        }

        Assert.Equal([38.3801666667, 41.098859136, 40.1], histograms);
        Assert.Equal(2, handler.Requests.Count);
    }
}
