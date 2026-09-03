using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// The four <c>/stocks/financials/v1</c> routes against the real service: each is served, the
/// dates arrive in the ISO form the map binds them to, the server honours the filters the SDK
/// renders, and absent-versus-zero is what the wire actually does rather than what the published
/// samples suggest.
/// </summary>
/// <remarks>
/// Deserializing at all is the date assertion that matters. <c>period_end</c> and
/// <c>filing_date</c> declare <c>format: date</c> and the ratios' <c>date</c> declares no format
/// at all, and all three bind <see cref="LocalDate"/> through an ISO pattern — so a compact
/// <c>yyyyMMdd</c> body would throw here rather than pass. That is the check issue #44 is waiting
/// on: the SEC v1 filings family really does send the compact form, and this family does not.
/// </remarks>
public sealed class ReferenceFinancialStatementsLiveTests : LiveApiTest
{
    private const string Cik = "0000320193";

    /// <summary>
    /// Pins one fiscal period rather than taking the first page: the routes sort ascending by
    /// default, so a bare <c>limit: 1</c> lands on each statement's oldest filing, and the three
    /// series do not start in the same quarter.
    /// </summary>
    [Fact]
    public async Task TheThreeStatementsAgreeOnTheCompanyAndPeriod()
    {
        MassivePage<BalanceSheet> sheets = await Client.Reference.ListBalanceSheetsAsync(
            cik: Cik, fiscalYear: 2025, fiscalQuarter: 3, timeframe: "quarterly", limit: 1, cancellationToken: Ct);
        MassivePage<CashFlowStatement> flows = await Client.Reference.ListCashFlowStatementsAsync(
            cik: Cik, fiscalYear: 2025, fiscalQuarter: 3, timeframe: "quarterly", limit: 1, cancellationToken: Ct);
        MassivePage<IncomeStatement> incomes = await Client.Reference.ListIncomeStatementsAsync(
            cik: Cik, fiscalYear: 2025, fiscalQuarter: 3, timeframe: "quarterly", limit: 1, cancellationToken: Ct);

        BalanceSheet sheet = Assert.Single(sheets.Results);
        CashFlowStatement flow = Assert.Single(flows.Results);
        IncomeStatement income = Assert.Single(incomes.Results);

        Assert.Equal(Cik, sheet.Cik);
        Assert.Equal(Cik, flow.Cik);
        Assert.Equal(Cik, income.Cik);

        Assert.Equal(sheet.PeriodEnd, flow.PeriodEnd);
        Assert.Equal(sheet.PeriodEnd, income.PeriodEnd);
        Assert.Equal(new LocalDate(2025, 6, 28), sheet.PeriodEnd);
        Assert.Equal("quarterly", sheet.Timeframe);

        Assert.Contains("AAPL", sheet.Tickers ?? []);
        Assert.True(sheet.TotalAssets > 0);
        Assert.True(income.Revenue > 0);
        Assert.NotNull(flow.NetCashFromOperatingActivities);
    }

    /// <summary>
    /// What the statements actually do with a line item the filing does not report, which is not
    /// what their own published samples show: the balance sheet sample omits eight of its
    /// thirty-eight fields, and the live wire sent every one of them as <c>0</c> on 2026-09-03.
    /// </summary>
    /// <remarks>
    /// The properties stay nullable because the description requires none of them, and the
    /// description is the contract the SDK ships against (D21). This pins the behaviour so it
    /// flips the day Massive starts omitting what it currently zero-fills. The distinction is not
    /// decorative across the whole family — see
    /// <see cref="ARatioTheInputsDoNotSupportIsAbsentWhileARealZeroIsZero"/>, where the ratios
    /// route does omit, and means something by it.
    /// </remarks>
    [Fact]
    public async Task TheStatementsZeroFillEveryLineItemRatherThanOmittingIt()
    {
        MassivePage<BalanceSheet> page = await Client.Reference.ListBalanceSheetsAsync(
            cik: Cik, fiscalYear: 2025, fiscalQuarter: 3, timeframe: "quarterly", limit: 1, cancellationToken: Ct);

        BalanceSheet sheet = Assert.Single(page.Results);

        // Apple reports no goodwill, no preferred stock, and no treasury stock in this period, and
        // its published sample omits all three. The service sends them as zero instead.
        Assert.Equal(0d, sheet.Goodwill);
        Assert.Equal(0d, sheet.PreferredStock);
        Assert.Equal(0d, sheet.TreasuryStock);
        Assert.True(sheet.TotalLiabilities > 0);
    }

    /// <summary>
    /// The acceptance the issue asked for, on the one route where the service exercises it: a
    /// ratio whose inputs do not support it is absent, while a ratio that is genuinely nothing is
    /// <c>0</c>, and both arrive on the same row.
    /// </summary>
    /// <remarks>
    /// Price-to-earnings is documented as computed only where earnings per share is positive, so
    /// a loss-making company has none; a company that pays no dividend has a yield of zero. Of
    /// the first fifty rows on 2026-09-03, 24 carried a null price-to-earnings, 22 a zero
    /// dividend yield, and 16 carried both — so this asserts against the page rather than against
    /// one company's financial fortunes.
    /// </remarks>
    [Fact]
    public async Task ARatioTheInputsDoNotSupportIsAbsentWhileARealZeroIsZero()
    {
        MassivePage<FinancialRatios> page = await Client.Reference.ListRatiosAsync(
            limit: 50, cancellationToken: Ct);

        Assert.Contains(page.Results, r => r.PriceToEarnings is null && r.DividendYield == 0d);
    }

    /// <summary>
    /// A fixture proves the filters render; only the service proves it accepts what they render.
    /// The dotted comparator suffixes and the calendar dates go out together here.
    /// </summary>
    [Fact]
    public async Task TheServerHonoursTheFiscalPeriodAndDateFilters()
    {
        MassivePage<IncomeStatement> page = await Client.Reference.ListIncomeStatementsAsync(
            cik: Cik,
            periodEnd: RangeFilter.Between(new LocalDate(2023, 1, 1), new LocalDate(2023, 12, 31)),
            fiscalYear: 2023,
            timeframe: "quarterly",
            limit: 10,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (IncomeStatement statement in page.Results)
        {
            Assert.Equal(2023, statement.FiscalYear);
            Assert.NotNull(statement.PeriodEnd);
            Assert.InRange(statement.PeriodEnd!.Value, new LocalDate(2023, 1, 1), new LocalDate(2023, 12, 31));
            Assert.Equal("quarterly", statement.Timeframe);
        }
    }

    [Fact]
    public async Task RatiosServeTheRequiredFieldsForOneTicker()
    {
        MassivePage<FinancialRatios> page = await Client.Reference.ListRatiosAsync(
            ticker: "AAPL", limit: 1, cancellationToken: Ct);

        FinancialRatios ratios = Assert.Single(page.Results);

        Assert.Equal("AAPL", ratios.Ticker);
        Assert.True(ratios.Price > 0);
        Assert.True(ratios.Date > new LocalDate(2000, 1, 1));
        Assert.False(string.IsNullOrEmpty(ratios.Cik));
    }

    /// <summary>
    /// A ratio is both a response field and a filter, so the server can be asked to screen on the
    /// same number it reports. Nothing offline can show the screen actually applied.
    /// </summary>
    [Fact]
    public async Task TheServerScreensOnARatioItAlsoReports()
    {
        MassivePage<FinancialRatios> page = await Client.Reference.ListRatiosAsync(
            marketCap: RangeFilter.Gte(1e12),
            priceToEarnings: RangeFilter.Lt(100d),
            limit: 5,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (FinancialRatios ratios in page.Results)
        {
            Assert.True(ratios.MarketCap >= 1e12);
            Assert.True(ratios.PriceToEarnings < 100d);
        }
    }
}
