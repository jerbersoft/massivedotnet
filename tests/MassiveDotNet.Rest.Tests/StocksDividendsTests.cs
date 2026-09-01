using System.Text.Json;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The generated filter surface, end to end: the first mapped operation with comparator fields,
/// and the first whose result carries calendar dates.
/// </summary>
public sealed class StocksDividendsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(StubHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task BuildsTheDocumentedRequestPathWithNoFilters()
    {
        StubHandler handler = new(Fixtures.StocksDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListDividendsAsync(cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/stocks/v1/dividends", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task ANullTickerVariableOmitsTheFilterRatherThanThrowing()
    {
        // ticker is a null *reference* variable, not a `null` literal: this is the shape that
        // hits the implicit T -> Filter<T> conversion directly, since C# only lifts a
        // user-defined conversion for a nullable value-type source. It must produce the plain
        // URL with no query, like any other omitted optional parameter, not throw
        // ArgumentNullException for a parameter name ("value") the caller never wrote.
        //
        // The `!` below only silences the compiler's static nullable check -- this project
        // builds with warnings as errors, which an ordinary consumer project need not -- it does
        // not change the runtime value: `ticker` is still null when the operator runs.
        string? ticker = null;

        StubHandler handler = new(Fixtures.StocksDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListDividendsAsync(ticker: ticker!, cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/stocks/v1/dividends", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task RendersEqualityRangeAndSetFiltersInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.StocksDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListDividendsAsync(
                ticker: "AAPL",
                exDividendDate: RangeFilter.Between(new LocalDate(2025, 1, 1), new LocalDate(2025, 12, 31)),
                frequency: RangeFilter.Gte(4L),
                distributionType: SetFilter.AnyOf("recurring", "special"),
                limit: 50,
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/v1/dividends"
                + "?ticker=AAPL"
                + "&ex_dividend_date.gte=2025-01-01&ex_dividend_date.lte=2025-12-31"
                + "&frequency.gte=4"
                + "&distribution_type.any_of=recurring,special"
                + "&limit=50",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task AcceptsASetOnAFieldThatAlsoTakesARange()
    {
        StubHandler handler = new(Fixtures.StocksDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListDividendsAsync(ticker: SetFilter.AnyOf("AAPL", "MSFT"), cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/v1/dividends?ticker.any_of=AAPL,MSFT",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task AcceptsAHalfOpenDateWindow()
    {
        StubHandler handler = new(Fixtures.StocksDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListDividendsAsync(
                exDividendDate: RangeFilter.Gte(new LocalDate(2025, 1, 1)).Lt(new LocalDate(2025, 7, 1)),
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/v1/dividends?ex_dividend_date.gte=2025-01-01&ex_dividend_date.lt=2025-07-01",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.StocksDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Dividend> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListDividendsAsync(ticker: "AAPL", cancellationToken: Ct);
        }

        Dividend dividend = Assert.Single(page.Results);

        Assert.Equal("AAPL", dividend.Ticker);
        Assert.Equal(new LocalDate(2025, 8, 11), dividend.ExDividendDate);
        Assert.Equal(new LocalDate(2025, 7, 31), dividend.DeclarationDate);
        Assert.Equal(new LocalDate(2025, 8, 11), dividend.RecordDate);
        Assert.Equal(new LocalDate(2025, 8, 14), dividend.PayDate);
        Assert.Equal(0.26, dividend.CashAmount);
        Assert.Equal(0.26, dividend.SplitAdjustedCashAmount);
        Assert.Equal(0.997899, dividend.HistoricalAdjustmentFactor);
        Assert.Equal("USD", dividend.Currency);
        Assert.Equal(4L, dividend.Frequency);
        Assert.Equal("recurring", dividend.DistributionType);
        Assert.Equal("Ed2c9da60abda1e3f0e99a43f6465863c137b671e1f5cd3f833d1fcb4f4eb27fe", dividend.Id);
        Assert.False(page.HasMore);
        Assert.Equal("1", page.RequestId);
    }

    [Fact]
    public async Task SurfacesAMalformedDateAsAnApiException()
    {
        // The sample with its ex-dividend and record dates (both 2025-08-11) made non-ISO.
        string body = Fixtures.StocksDividends.Replace("\"2025-08-11\"", "\"2025-8-11\"", StringComparison.Ordinal);
        StubHandler handler = new(body);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Stocks.ListDividendsAsync(cancellationToken: Ct));

            Assert.IsType<JsonException>(exception.InnerException);
        }
    }

    [Fact]
    public async Task AResponseMissingASchemaRequiredFieldIsRejected()
    {
        // distribution_type is required by the schema, which is why Dividend.DistributionType is
        // a non-nullable string rather than string?. Stripping it from the one result item should
        // surface as a JsonException wrapped in a MassiveApiException, not a silently-defaulted
        // value or a null reference discovered later by the caller.
        string body = Fixtures.StocksDividends.Replace("\"distribution_type\": \"recurring\",", "", StringComparison.Ordinal);
        StubHandler handler = new(body);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Stocks.ListDividendsAsync(cancellationToken: Ct));

            Assert.IsType<JsonException>(exception.InnerException);
        }
    }

    [Fact]
    public async Task EnumerateWalksTheSinglePage()
    {
        StubHandler handler = new(Fixtures.StocksDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string?> tickers = [];

        using (client)
        using (transport)
        {
            await foreach (Dividend dividend in client.Stocks.EnumerateDividendsAsync(ticker: "AAPL", cancellationToken: Ct))
            {
                tickers.Add(dividend.Ticker);
            }
        }

        Assert.Equal("AAPL", Assert.Single(tickers));
    }
}
