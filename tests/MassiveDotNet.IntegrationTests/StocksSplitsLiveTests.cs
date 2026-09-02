using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// One call for splits (D-G8): a ticker and a calendar-date range around a split whose ratio and
/// date are a matter of record.
/// </summary>
public sealed class StocksSplitsLiveTests : LiveApiTest
{
    // AAPL's 4-for-1 split on 2020-08-31 is the only AAPL split in this window.
    private static readonly LocalDate WindowStart = new(2020, 1, 1);
    private static readonly LocalDate WindowEnd = new(2020, 12, 31);

    [Fact]
    public async Task HonoursATickerAndADateRange()
    {
        MassivePage<Split> page = await Client.Stocks.ListSplitsAsync(
            ticker: "AAPL",
            executionDate: RangeFilter.Between(WindowStart, WindowEnd),
            cancellationToken: Ct);

        Split split = Assert.Single(page.Results);
        Assert.Equal("AAPL", split.Ticker);
        Assert.Equal(new LocalDate(2020, 8, 31), split.ExecutionDate);
        Assert.Equal(1d, split.SplitFrom);
        Assert.Equal(4d, split.SplitTo);
    }
}
