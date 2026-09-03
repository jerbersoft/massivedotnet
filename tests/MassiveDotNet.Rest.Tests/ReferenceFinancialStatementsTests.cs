using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The three <c>/stocks/financials/v1</c> statements: every filter shape the family declares
/// renders, each operation keeps its own declaration order, and the published samples deserialize
/// with the line items they omit left null rather than zero.
/// </summary>
public sealed class ReferenceFinancialStatementsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersEveryBalanceSheetFilterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceBalanceSheets);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListBalanceSheetsAsync(
                cik: SetFilter.AnyOf("0000320193", "0000789019"),
                tickers: ArrayFilter.AllOf("AAPL", "MSFT"),
                periodEnd: RangeFilter.Between(new LocalDate(2025, 1, 1), new LocalDate(2025, 6, 30)),
                filingDate: RangeFilter.Gt(new LocalDate(2025, 7, 1)),
                fiscalYear: 2025,
                fiscalQuarter: RangeFilter.Gte(2L),
                timeframe: "quarterly",
                limit: 5,
                sort: "period_end.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/financials/v1/balance-sheets"
                + "?cik.any_of=0000320193,0000789019"
                + "&tickers.all_of=AAPL,MSFT"
                + "&period_end.gte=2025-01-01&period_end.lte=2025-06-30"
                + "&filing_date.gt=2025-07-01"
                + "&fiscal_year=2025&fiscal_quarter.gte=2"
                + "&timeframe=quarterly&limit=5&sort=period_end.desc",
            handler.LastRequestUri?.ToString());
    }

    /// <summary>
    /// The cash flow statement declares <c>tickers</c> after the two dates where its two siblings
    /// declare it before them. Parameter order follows each operation's own declaration, so this
    /// pins the difference rather than a canonical order the description does not have.
    /// </summary>
    [Fact]
    public async Task RendersTheCashFlowStatementFiltersInItsOwnDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceCashFlowStatements);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListCashFlowStatementsAsync(
                cik: "0000320193",
                periodEnd: RangeFilter.Lte(new LocalDate(2025, 6, 28)),
                filingDate: new LocalDate(2025, 8, 1),
                tickers: ArrayFilter.Contains("AAPL"),
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/financials/v1/cash-flow-statements"
                + "?cik=0000320193&period_end.lte=2025-06-28&filing_date=2025-08-01&tickers=AAPL",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesTheBalanceSheetSample()
    {
        StubHandler handler = new(Fixtures.ReferenceBalanceSheets);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<BalanceSheet> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListBalanceSheetsAsync(tickers: "AAPL", cancellationToken: Ct);
        }

        BalanceSheet sheet = Assert.Single(page.Results);
        Assert.False(page.HasMore);
        Assert.Equal("0000320193", sheet.Cik);
        Assert.Equal(["AAPL"], sheet.Tickers!);
        Assert.Equal("quarterly", sheet.Timeframe);
        Assert.Equal(new LocalDate(2025, 6, 28), sheet.PeriodEnd);
        Assert.Equal(new LocalDate(2025, 8, 1), sheet.FilingDate);
        Assert.Equal(2025, sheet.FiscalYear);
        Assert.Equal(3, sheet.FiscalQuarter);
        Assert.Equal(331495000000d, sheet.TotalAssets);
        Assert.Equal(sheet.TotalAssets, sheet.TotalLiabilitiesAndEquity);
    }

    /// <summary>
    /// The acceptance the issue asked for, against the shape Massive publishes: a line item the
    /// sample omits deserializes to null, and one it reports as zero deserializes to <c>0</c>.
    /// The sample carries both, so the distinction is testable without hand-editing the fixture.
    /// </summary>
    /// <remarks>
    /// The live service does not currently produce the null half on this route — it zero-fills
    /// every field, including the eight this sample omits — which
    /// <c>ReferenceFinancialStatementsLiveTests</c> pins. What this test asserts is still worth
    /// asserting: the model draws the distinction the description permits, so the SDK reads a
    /// sparse body correctly if and when one arrives.
    /// </remarks>
    [Fact]
    public async Task AbsentLineItemsAreNullWhereReportedZeroesAreZero()
    {
        StubHandler handler = new(Fixtures.ReferenceBalanceSheets);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<BalanceSheet> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListBalanceSheetsAsync(tickers: "AAPL", cancellationToken: Ct);
        }

        BalanceSheet sheet = Assert.Single(page.Results);
        Assert.Equal(0d, sheet.OtherEquity);
        Assert.Null(sheet.Goodwill);
        Assert.Null(sheet.AdditionalPaidInCapital);
        Assert.Null(sheet.CommitmentsAndContingencies);
        Assert.Null(sheet.IntangibleAssetsNet);
        Assert.Null(sheet.NoncontrollingInterest);
        Assert.Null(sheet.PreferredStock);
        Assert.Null(sheet.ShortTermInvestments);
        Assert.Null(sheet.TreasuryStock);
    }

    [Fact]
    public async Task DeserializesTheCashFlowStatementSample()
    {
        StubHandler handler = new(Fixtures.ReferenceCashFlowStatements);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<CashFlowStatement> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListCashFlowStatementsAsync(tickers: "AAPL", cancellationToken: Ct);
        }

        CashFlowStatement statement = Assert.Single(page.Results);
        Assert.Equal(new LocalDate(2025, 6, 28), statement.PeriodEnd);
        Assert.Equal("quarterly", statement.Timeframe);
        Assert.Equal(27867000000d, statement.NetCashFromOperatingActivities);
        Assert.Equal(-3462000000d, statement.PurchaseOfPropertyPlantAndEquipment);
        Assert.Null(statement.OtherCashAdjustments);
        Assert.Null(statement.EffectOfCurrencyExchangeRate);
    }

    [Fact]
    public async Task DeserializesTheIncomeStatementSample()
    {
        StubHandler handler = new(Fixtures.ReferenceIncomeStatements);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<IncomeStatement> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListIncomeStatementsAsync(tickers: "AAPL", cancellationToken: Ct);
        }

        IncomeStatement statement = Assert.Single(page.Results);
        Assert.Equal(94036000000d, statement.Revenue);
        Assert.Equal(1.57d, statement.BasicEarningsPerShare);
        Assert.Equal(0d, statement.OtherOperatingExpenses);
        Assert.Null(statement.InterestExpense);
        Assert.Null(statement.ExtraordinaryItems);
    }

    /// <summary>
    /// <c>timeframe</c> is required on the balance sheet and optional on the other two, which is
    /// the description's own asymmetry rather than the map's. The income statement sample happens
    /// to send it, so this asserts the property is the nullable one.
    /// </summary>
    [Fact]
    public async Task TimeframeIsOptionalOnTheStatementsThatDoNotRequireIt()
    {
        StubHandler handler = new(Fixtures.ReferenceIncomeStatements);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<IncomeStatement> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListIncomeStatementsAsync(tickers: "AAPL", cancellationToken: Ct);
        }

        string? timeframe = Assert.Single(page.Results).Timeframe;
        Assert.Equal("quarterly", timeframe);
    }

    [Fact]
    public async Task EnumerateWalksASinglePageOfIncomeStatements()
    {
        StubHandler handler = new(Fixtures.ReferenceIncomeStatements);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<double?> revenues = [];

        using (client)
        using (transport)
        {
            await foreach (IncomeStatement statement in
                client.Reference.EnumerateIncomeStatementsAsync(tickers: "AAPL", cancellationToken: Ct))
            {
                revenues.Add(statement.Revenue);
            }
        }

        Assert.Equal(94036000000d, Assert.Single(revenues));
    }
}
