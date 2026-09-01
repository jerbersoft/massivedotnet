using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Verifies cursor traversal against the real service. Fixtures cannot cover this: they assert
/// the SDK agrees with a recording, whereas crossing a real page boundary asserts the cursor
/// contract still holds today.
/// </summary>
public sealed class PaginationLiveTests : LiveApiTest
{
    [Fact]
    public async Task CrossesRealPageBoundaries()
    {
        // limit is per page, so a small limit over a wide window forces several round trips.
        List<Agg> bars = [];

        await foreach (Agg bar in Client.Stocks.EnumerateAggregatesAsync(
            "AAPL",
            1,
            AggregateTimespan.Day,
            new LocalDate(2024, 1, 1),
            new LocalDate(2024, 6, 30),
            limit: 5,
            cancellationToken: Ct))
        {
            bars.Add(bar);

            // Enough to prove several pages were crossed without walking the whole window.
            if (bars.Count >= 40)
            {
                break;
            }
        }

        Assert.Equal(40, bars.Count);

        // Bars must be strictly ordered across the page seam, which is where an incorrectly
        // rebuilt cursor would show up as repeated or skipped windows.
        Assert.Equal(
            bars.Select(b => b.TimestampMilliseconds).Order(),
            bars.Select(b => b.TimestampMilliseconds));

        Assert.Equal(
            bars.Select(b => b.TimestampMilliseconds).Distinct().Count(),
            bars.Count);
    }

    [Fact]
    public async Task ListReportsThatMorePagesExist()
    {
        MassivePage<Agg> page = await Client.Stocks.ListAggregatesAsync(
            "AAPL",
            1,
            AggregateTimespan.Day,
            new LocalDate(2024, 1, 1),
            new LocalDate(2024, 6, 30),
            limit: 5,
            cancellationToken: Ct);

        Assert.Equal(5, page.Results.Length);
        Assert.True(page.HasMore);
        Assert.NotNull(page.RequestId);
    }
}
