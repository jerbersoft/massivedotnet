using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Financials: the plain and searched company names render under their wire names, the
/// calendar-date ranges the description types render, and the published sample deserializes
/// four dictionaries of one data point model with every line item reachable by its key (D-R11).
/// </summary>
/// <remarks>
/// The published sample omits <c>tickers</c>, which the description makes optional, so
/// <see cref="DeserializesFourDictionariesOfDataPoints"/> asserts it null; the live tier asserts
/// it arrives. The sample also omits the required <c>timeframe</c>, which the fixture supplies
/// (D-R12), because a required property that never arrives fails deserialization.
/// </remarks>
public sealed class ReferenceFinancialsTests
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
        StubHandler handler = new(Fixtures.ReferenceFinancials);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListFinancialsAsync(
                ticker: "SITE",
                cik: "0001650729",
                companyName: "SiteOne",
                sic: "5070",
                filingDate: RangeFilter.Between(new LocalDate(2022, 1, 1), new LocalDate(2022, 12, 31)),
                periodOfReportDate: RangeFilter.Gte(new LocalDate(2022, 1, 1)),
                timeframe: "quarterly",
                includeSources: true,
                companyNameSearch: "Site",
                order: SortOrder.Descending,
                limit: 1,
                sort: "filing_date",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/vX/reference/financials"
                + "?ticker=SITE&cik=0001650729&company_name=SiteOne&sic=5070"
                + "&filing_date.gte=2022-01-01&filing_date.lte=2022-12-31"
                + "&period_of_report_date.gte=2022-01-01"
                + "&timeframe=quarterly&include_sources=true&company_name.search=Site"
                + "&order=desc&limit=1&sort=filing_date",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesFourDictionariesOfDataPoints()
    {
        StubHandler handler = new(Fixtures.ReferenceFinancials);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<FinancialReport> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListFinancialsAsync(ticker: "SITE", limit: 1, cancellationToken: Ct);
        }

        FinancialReport report = Assert.Single(page.Results);
        Assert.True(page.HasMore);
        Assert.Equal("55eb92ed43b25568ab0cce159830ea34", page.RequestId);

        Assert.Equal("0001650729", report.Cik);
        Assert.Equal("SiteOne Landscape Supply, Inc.", report.CompanyName);
        Assert.Equal("Q1", report.FiscalPeriod);
        Assert.Equal("2022", report.FiscalYear);
        Assert.Equal(new LocalDate(2022, 1, 3), report.StartDate);
        Assert.Equal(new LocalDate(2022, 4, 3), report.EndDate);
        Assert.Equal(new LocalDate(2022, 5, 4), report.FilingDate);
        Assert.Equal("quarterly", report.Timeframe);
        Assert.Null(report.Tickers);
        Assert.Null(report.AcceptanceTimestamp);
        Assert.Equal("https://api.massive.com/v1/reference/sec/filings/0001650729-22-000010", report.SourceFilingUrl);

        FinancialStatements statements = report.Financials;
        Assert.NotNull(statements.BalanceSheet);
        Assert.NotNull(statements.CashFlowStatement);
        Assert.NotNull(statements.ComprehensiveIncome);
        Assert.NotNull(statements.IncomeStatement);
        Assert.Equal(10, statements.BalanceSheet!.Count);
        Assert.Equal(9, statements.CashFlowStatement!.Count);
        Assert.Equal(5, statements.ComprehensiveIncome!.Count);
        Assert.Equal(19, statements.IncomeStatement!.Count);

        FinancialDataPoint assets = statements.BalanceSheet["assets"];
        Assert.Equal("Assets", assets.Label);
        Assert.Equal(2407400000d, assets.Value);
        Assert.Equal("USD", assets.Unit);
        Assert.Equal(100, assets.Order);
        Assert.Null(assets.Source);
        Assert.Null(assets.Formula);
        Assert.Null(assets.XPath);
        Assert.Null(assets.DerivedFrom);

        Assert.Equal(0.72, statements.IncomeStatement["basic_earnings_per_share"].Value);
        Assert.Equal("USD / shares", statements.IncomeStatement["basic_earnings_per_share"].Unit);
        Assert.Equal(-8600000d, statements.CashFlowStatement["net_cash_flow"].Value);
        Assert.Equal(40500000d, statements.ComprehensiveIncome["comprehensive_income_loss"].Value);
    }

    [Fact]
    public async Task EnumerateFollowsTheSampleCursorThenStops()
    {
        // The sample's cursor is the route with an empty query, which is still a same-origin URI
        // the traversal follows verbatim (D14); the stub serves the same page again, so the test
        // stops the traversal itself after the seam it set out to cross.
        PagingStubHandler handler = new(Fixtures.ReferenceFinancials);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string> periods = [];

        using (client)
        using (transport)
        {
            await foreach (FinancialReport report in client.Reference.EnumerateFinancialsAsync(ticker: "SITE", limit: 1, cancellationToken: Ct))
            {
                periods.Add(report.FiscalPeriod);

                if (periods.Count == 2)
                {
                    break;
                }
            }
        }

        Assert.Equal(["Q1", "Q1"], periods);
        Assert.Equal(2, handler.Requests.Count);
        Assert.StartsWith("https://api.massive.com/vX/reference/financials", handler.Requests[1].ToString(), StringComparison.Ordinal);
    }
}
