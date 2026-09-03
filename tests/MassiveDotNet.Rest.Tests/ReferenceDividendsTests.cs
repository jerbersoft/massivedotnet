using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The v3 dividends: four calendar-date ranges and a cash-amount range, all derived from the
/// spec's suffix sets (D15), on a model separate from the stocks/v1 <c>Dividend</c> (D-R7).
/// </summary>
public sealed class ReferenceDividendsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersEveryFilterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListDividendsAsync(
                ticker: "AAPL",
                exDividendDate: RangeFilter.Between(new LocalDate(2021, 1, 1), new LocalDate(2021, 12, 31)),
                recordDate: RangeFilter.Gt(new LocalDate(2021, 1, 1)),
                declarationDate: RangeFilter.Lt(new LocalDate(2022, 1, 1)),
                payDate: new LocalDate(2021, 11, 11),
                frequency: 4,
                cashAmount: RangeFilter.Gte(0.2),
                dividendType: "CD",
                order: SortOrder.Descending,
                limit: 2,
                sort: "ex_dividend_date",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v3/reference/dividends"
                + "?ticker=AAPL"
                + "&ex_dividend_date.gte=2021-01-01&ex_dividend_date.lte=2021-12-31"
                + "&record_date.gt=2021-01-01"
                + "&declaration_date.lt=2022-01-01"
                + "&pay_date=2021-11-11"
                + "&frequency=4"
                + "&cash_amount.gte=0.2"
                + "&dividend_type=CD&order=desc&limit=2&sort=ex_dividend_date",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<ReferenceDividend> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListDividendsAsync(ticker: "AAPL", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);

        ReferenceDividend dividend = page.Results[0];
        Assert.Equal("AAPL", dividend.Ticker);
        Assert.Equal("E8e3c4f794613e9205e2f178a36c53fcc57cdabb55e1988c87b33f9e52e221444", dividend.Id);
        Assert.Equal(0.22, dividend.CashAmount);
        Assert.Equal("CD", dividend.DividendType);
        Assert.Equal(4, dividend.Frequency);
        Assert.Equal(new LocalDate(2021, 10, 28), dividend.DeclarationDate);
        Assert.Equal(new LocalDate(2021, 11, 5), dividend.ExDividendDate);
        Assert.Equal(new LocalDate(2021, 11, 8), dividend.RecordDate);
        Assert.Equal(new LocalDate(2021, 11, 11), dividend.PayDate);
        Assert.Null(dividend.Currency);

        Assert.Equal(new LocalDate(2021, 8, 6), page.Results[1].ExDividendDate);
        Assert.True(page.HasMore);
        Assert.Equal("6a7e466379af0a71039d60cc78e72282", page.RequestId);
    }

    [Fact]
    public async Task EnumerateFollowsTheSampleCursorThenStops()
    {
        // The sample's cursor points at the same origin, so the traversal follows it verbatim
        // (D14); the stub serves the same page again, which has a cursor too, so the test stops
        // the traversal itself after the seam it set out to cross.
        PagingStubHandler handler = new(Fixtures.ReferenceDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string> ids = [];

        using (client)
        using (transport)
        {
            await foreach (ReferenceDividend dividend in client.Reference.EnumerateDividendsAsync(ticker: "AAPL", cancellationToken: Ct))
            {
                ids.Add(dividend.Id);

                if (ids.Count == 3)
                {
                    break;
                }
            }
        }

        Assert.Equal(3, ids.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.StartsWith("https://api.massive.com/v3/reference/dividends/AAPL?cursor=", handler.Requests[1].ToString(), StringComparison.Ordinal);
    }
}
