using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Exercises the filter surface against the live service.
/// </summary>
/// <remarks>
/// A fixture asserts the SDK produces the documented query string; it cannot say whether the
/// service accepts a literal comma in <c>any_of</c> or treats <c>gte</c> and <c>lte</c> as
/// inclusive, neither of which the OpenAPI description states. This is the one test that can.
/// </remarks>
public sealed class StocksDividendsLiveTests : LiveApiTest
{
    // A fixed historical window, so the assertions do not depend on when the suite is run. Both
    // tickers paid at least one dividend inside it.
    private static readonly LocalDate WindowStart = new(2025, 1, 1);
    private static readonly LocalDate WindowEnd = new(2025, 6, 30);
    private static readonly string[] Tickers = ["AAPL", "MSFT"];

    [Fact]
    public async Task HonoursADateRangeAndATickerSet()
    {
        Dividend[] dividends = (await Client.Stocks.ListDividendsAsync(
            ticker: SetFilter.AnyOf(Tickers),
            exDividendDate: RangeFilter.Between(WindowStart, WindowEnd),
            limit: 100,
            cancellationToken: Ct)).Results;

        Assert.NotEmpty(dividends);

        foreach (Dividend dividend in dividends)
        {
            Assert.Contains(dividend.Ticker, Tickers);
            Assert.NotNull(dividend.ExDividendDate);
            Assert.InRange(dividend.ExDividendDate.Value, WindowStart, WindowEnd);
        }
    }
}
