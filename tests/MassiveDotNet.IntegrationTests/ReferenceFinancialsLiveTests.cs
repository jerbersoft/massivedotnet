using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Financials against the real service: the four dictionaries deserialize with their line items
/// reachable by key, the required fields the published sample omits do arrive, and asking for
/// sources adds the attributes it promises (D-R11, D-R13).
/// </summary>
public sealed class ReferenceFinancialsLiveTests : LiveApiTest
{
    [Fact]
    public async Task AnAnnualReportCarriesFourStatementsReachableByKey()
    {
        MassivePage<FinancialReport> page = await Client.Reference.ListFinancialsAsync(
            ticker: "AAPL",
            timeframe: "annual",
            limit: 1,
            cancellationToken: Ct);

        FinancialReport report = Assert.Single(page.Results);

        Assert.Equal("0000320193", report.Cik);
        Assert.Contains("Apple", report.CompanyName, StringComparison.Ordinal);
        Assert.Equal("annual", report.Timeframe);
        Assert.False(string.IsNullOrEmpty(report.FiscalPeriod));
        Assert.NotNull(report.StartDate);
        Assert.NotNull(report.EndDate);
        Assert.NotNull(report.FilingDate);
        Assert.True(report.StartDate < report.EndDate);

        // The published sample omits tickers, so the fixture test asserts it null and this one
        // asserts it arrives; the acceptance timestamp is absent from the sample too, and the
        // wire sent RFC 3339 here where the SEC v1 filings send the compact form, which is why
        // the property is a string on both (D-R9).
        Assert.Contains("AAPL", report.Tickers ?? []);
        Assert.NotNull(report.AcceptanceTimestamp);

        FinancialStatements statements = report.Financials;
        Assert.NotNull(statements.BalanceSheet);
        Assert.NotNull(statements.CashFlowStatement);
        Assert.NotNull(statements.ComprehensiveIncome);
        Assert.NotNull(statements.IncomeStatement);

        FinancialDataPoint assets = statements.BalanceSheet["assets"];
        Assert.Equal("Assets", assets.Label);
        Assert.Equal("USD", assets.Unit);
        Assert.True(assets.Value > 0);
        Assert.True(assets.Order > 0);

        Assert.True(statements.IncomeStatement["revenues"].Value > 0);
    }

    [Fact]
    public async Task AskingForSourcesAddsTheXPathAndFormula()
    {
        // include_sources is the only parameter whose effect is visible in the payload, so it is
        // the one thing here a fixture genuinely cannot check.
        MassivePage<FinancialReport> page = await Client.Reference.ListFinancialsAsync(
            ticker: "AAPL",
            timeframe: "annual",
            includeSources: true,
            limit: 1,
            cancellationToken: Ct);

        FinancialReport report = Assert.Single(page.Results);
        Assert.NotNull(report.Financials.BalanceSheet);

        Assert.Contains(
            report.Financials.BalanceSheet.Values,
            point => !string.IsNullOrEmpty(point.XPath) || !string.IsNullOrEmpty(point.Formula));

        Assert.Contains(report.Financials.BalanceSheet.Values, point => !string.IsNullOrEmpty(point.Source));
    }

    [Fact]
    public async Task FinancialsCrossAPageBoundary()
    {
        List<string> periods = [];

        await foreach (FinancialReport report in Client.Reference.EnumerateFinancialsAsync(
            ticker: "AAPL",
            timeframe: "quarterly",
            order: SortOrder.Descending,
            sort: "period_of_report_date",
            limit: 2,
            cancellationToken: Ct))
        {
            periods.Add($"{report.FiscalYear} {report.FiscalPeriod}");

            if (periods.Count == 5)
            {
                break;
            }
        }

        Assert.Equal(5, periods.Count);
        Assert.Equal(5, periods.Distinct(StringComparer.Ordinal).Count());
    }
}
