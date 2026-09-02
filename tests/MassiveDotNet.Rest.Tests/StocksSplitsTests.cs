using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Splits: a range-and-set filter on the ticker, a calendar-date range, and a plain-plus-any_of
/// set, each derived from the spec's suffix set (D15).
/// </summary>
public sealed class StocksSplitsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersEqualityRangeAndSetFiltersInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.StocksSplits);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListSplitsAsync(
                ticker: "AAPL",
                executionDate: RangeFilter.Gte(new LocalDate(2020, 1, 1)),
                adjustmentType: SetFilter.AnyOf("forward_split", "reverse_split"),
                limit: 10,
                sort: "execution_date.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/v1/splits"
                + "?ticker=AAPL"
                + "&execution_date.gte=2020-01-01"
                + "&adjustment_type.any_of=forward_split,reverse_split"
                + "&limit=10&sort=execution_date.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task AcceptsATickerSetAndAnAdjustmentTypeEquality()
    {
        StubHandler handler = new(Fixtures.StocksSplits);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListSplitsAsync(
                ticker: SetFilter.AnyOf("AAPL", "MSFT"),
                adjustmentType: "stock_dividend",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/v1/splits?ticker.any_of=AAPL,MSFT&adjustment_type=stock_dividend",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesTheCorrectedSample()
    {
        StubHandler handler = new(Fixtures.StocksSplits);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Split> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListSplitsAsync(ticker: "AAPL", cancellationToken: Ct);
        }

        Split split = Assert.Single(page.Results);
        Assert.Equal("AAPL", split.Ticker);
        Assert.Equal(new LocalDate(2005, 2, 28), split.ExecutionDate);
        Assert.Equal("forward_split", split.AdjustmentType);
        Assert.Equal(1d, split.SplitFrom);
        Assert.Equal(2d, split.SplitTo);
        Assert.Equal(0.017857, split.HistoricalAdjustmentFactor);
        Assert.Equal("E90a77bdf742661741ed7c8fc086415f0457c2816c45899d73aaa88bdc8ff6025", split.Id);
        Assert.False(page.HasMore);
        Assert.Equal("1", page.RequestId);
    }

    [Fact]
    public async Task EnumerateWalksTheSinglePage()
    {
        StubHandler handler = new(Fixtures.StocksSplits);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string?> ids = [];

        using (client)
        using (transport)
        {
            await foreach (Split split in client.Stocks.EnumerateSplitsAsync(ticker: "AAPL", cancellationToken: Ct))
            {
                ids.Add(split.Id);
            }
        }

        Assert.Equal("E90a77bdf742661741ed7c8fc086415f0457c2816c45899d73aaa88bdc8ff6025", Assert.Single(ids));
    }
}
