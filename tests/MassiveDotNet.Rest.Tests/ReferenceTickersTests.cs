using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The tickers list and the ticker types: a <see cref="MarketType"/> on a query parameter, a
/// calendar date, RFC 3339 timestamps on the model, a two-page traversal, and a captured
/// fixture for the one operation the description gives no JSON example (D-R12).
/// </summary>
public sealed class ReferenceTickersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Cursor =
        "https://api.massive.com/v3/reference/tickers?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy";

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersEveryParameterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceTickers);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListTickersAsync(
                ticker: RangeFilter.Gte("A"),
                type: "CS",
                market: MarketType.Stocks,
                exchange: "XNYS",
                cusip: "00846U101",
                cik: "0001090872",
                date: new LocalDate(2024, 1, 16),
                search: "agilent",
                active: true,
                order: SortOrder.Ascending,
                limit: 2,
                sort: "ticker",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v3/reference/tickers"
                + "?ticker.gte=A&type=CS&market=stocks&exchange=XNYS&cusip=00846U101&cik=0001090872"
                + "&date=2024-01-16&search=agilent&active=true&order=asc&limit=2&sort=ticker",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task OmitsEveryParameterByDefault()
    {
        StubHandler handler = new(Fixtures.ReferenceTickers);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListTickersAsync(cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v3/reference/tickers", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceTickers);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<TickerSummary> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListTickersAsync(cancellationToken: Ct);
        }

        TickerSummary ticker = Assert.Single(page.Results);
        Assert.Equal("A", ticker.Ticker);
        Assert.Equal("Agilent Technologies Inc.", ticker.Name);
        Assert.Equal("stocks", ticker.Market);
        Assert.Equal("us", ticker.Locale);
        Assert.Equal("XNYS", ticker.PrimaryExchange);
        Assert.Equal("CS", ticker.Type);
        Assert.True(ticker.IsActive);
        Assert.Equal("usd", ticker.CurrencyName);
        Assert.Equal("0001090872", ticker.Cik);
        Assert.Equal("BBG000BWQYZ5", ticker.CompositeFigi);
        Assert.Equal("BBG001SCTQY4", ticker.ShareClassFigi);
        Assert.Equal(Instant.FromUtc(2021, 4, 25, 0, 0), ticker.LastUpdatedUtc);
        Assert.Null(ticker.DelistedUtc);
        Assert.Null(ticker.CurrencySymbol);
        Assert.True(page.HasMore);
        Assert.Equal("e70013d92930de90e089dc8fa098888e", page.RequestId);
    }

    [Fact]
    public async Task EnumerateTraversesTwoPagesFollowingTheCursorVerbatim()
    {
        PagingStubHandler handler = new(Fixtures.ReferenceTickers, Fixtures.ReferenceTickersLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string> tickers = [];

        using (client)
        using (transport)
        {
            await foreach (TickerSummary ticker in client.Reference.EnumerateTickersAsync(market: MarketType.Stocks, cancellationToken: Ct))
            {
                tickers.Add(ticker.Ticker);
            }
        }

        Assert.Equal(["A", "AAL"], tickers);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("https://api.massive.com/v3/reference/tickers?market=stocks", handler.Requests[0].ToString());
        Assert.Equal(Cursor, handler.Requests[1].ToString());
    }

    [Fact]
    public async Task TickerTypesRenderTheAssetClassAndLocale()
    {
        StubHandler handler = new(Fixtures.ReferenceTickerTypes);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListTickerTypesAsync(assetClass: MarketType.Stocks, locale: "us", cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v3/reference/tickers/types?asset_class=stocks&locale=us", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task TickerTypesDeserializeTheCapturedResponse()
    {
        StubHandler handler = new(Fixtures.ReferenceTickerTypes);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        TickerType[] types;

        using (client)
        using (transport)
        {
            types = await client.Reference.ListTickerTypesAsync(cancellationToken: Ct);
        }

        Assert.Equal(24, types.Length);

        TickerType common = types[0];
        Assert.Equal("CS", common.Code);
        Assert.Equal("Common Stock", common.Description);
        Assert.Equal("stocks", common.AssetClass);
        Assert.Equal("us", common.Locale);

        Assert.Contains(types, type => type.Code == "ETF" && type.Description == "Exchange Traded Fund");
    }
}
