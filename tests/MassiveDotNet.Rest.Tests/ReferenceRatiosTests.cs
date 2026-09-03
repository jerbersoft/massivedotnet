using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Financial ratios: every ratio the row carries is also a filter, the three bare wire names
/// render unchanged under their renamed parameters, and the published sample deserializes with
/// its date read as a calendar date the description does not format.
/// </summary>
public sealed class ReferenceRatiosTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersEveryRatioFilterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceRatios);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListRatiosAsync(
                ticker: "AAPL",
                cik: SetFilter.AnyOf("320193"),
                price: RangeFilter.Gte(100d),
                averageVolume: RangeFilter.Gt(1000000d),
                marketCap: RangeFilter.Gte(1e12),
                earningsPerShare: RangeFilter.Gt(0d),
                priceToEarnings: RangeFilter.Lt(40d),
                priceToBook: RangeFilter.Lte(60d),
                priceToSales: RangeFilter.Lt(10d),
                priceToCashFlow: RangeFilter.Lt(35d),
                priceToFreeCashFlow: RangeFilter.Lt(40d),
                dividendYield: RangeFilter.Gt(0.001d),
                returnOnAssets: RangeFilter.Gte(0.2d),
                returnOnEquity: RangeFilter.Gte(1d),
                debtToEquity: RangeFilter.Lte(2d),
                currentRatio: RangeFilter.Gt(0.5d),
                quickRatio: RangeFilter.Gt(0.5d),
                cashRatio: RangeFilter.Gt(0.1d),
                evToSales: RangeFilter.Lt(12d),
                evToEbitda: RangeFilter.Lt(30d),
                enterpriseValue: RangeFilter.Gt(1e12),
                freeCashFlow: RangeFilter.Gt(0d),
                limit: 10,
                sort: "ticker.asc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/financials/v1/ratios"
                + "?ticker=AAPL&cik.any_of=320193"
                + "&price.gte=100&average_volume.gt=1000000&market_cap.gte=1000000000000"
                + "&earnings_per_share.gt=0&price_to_earnings.lt=40&price_to_book.lte=60"
                + "&price_to_sales.lt=10&price_to_cash_flow.lt=35&price_to_free_cash_flow.lt=40"
                + "&dividend_yield.gt=0.001&return_on_assets.gte=0.2&return_on_equity.gte=1"
                + "&debt_to_equity.lte=2&current.gt=0.5&quick.gt=0.5&cash.gt=0.1"
                + "&ev_to_sales.lt=12&ev_to_ebitda.lt=30&enterprise_value.gt=1000000000000"
                + "&free_cash_flow.gt=0&limit=10&sort=ticker.asc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesTheRatiosSample()
    {
        StubHandler handler = new(Fixtures.ReferenceRatios);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<FinancialRatios> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListRatiosAsync(ticker: "AAPL", cancellationToken: Ct);
        }

        FinancialRatios ratios = Assert.Single(page.Results);
        Assert.False(page.HasMore);
        Assert.Equal("AAPL", ratios.Ticker);
        Assert.Equal("320193", ratios.Cik);
        Assert.Equal(new LocalDate(2024, 9, 19), ratios.Date);
        Assert.Equal(228.87d, ratios.Price);
        Assert.Equal(34.84d, ratios.PriceToEarnings);
        Assert.Equal(0.68d, ratios.CurrentRatio);
        Assert.Equal(0.63d, ratios.QuickRatio);
        Assert.Equal(0.19d, ratios.CashRatio);
        Assert.Equal(26.98d, ratios.EvToEbitda);
    }

    [Fact]
    public async Task EnumerateWalksASinglePageOfRatios()
    {
        StubHandler handler = new(Fixtures.ReferenceRatios);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<LocalDate> dates = [];

        using (client)
        using (transport)
        {
            await foreach (FinancialRatios ratios in
                client.Reference.EnumerateRatiosAsync(ticker: "AAPL", cancellationToken: Ct))
            {
                dates.Add(ratios.Date);
            }
        }

        Assert.Equal(new LocalDate(2024, 9, 19), Assert.Single(dates));
    }
}
