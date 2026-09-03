using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// The v3 dividends and splits against the real service, on windows whose contents are a matter
/// of record, so the bare-string dates the map binds to <see cref="LocalDate"/> (D-R9) are proven
/// on the way in and the way out.
/// </summary>
public sealed class ReferenceCorporateActionsLiveTests : LiveApiTest
{
    // Apple paid four quarterly dividends in 2021 and split 4-for-1 on 2020-08-31.
    private static readonly LocalDate DividendWindowStart = new(2021, 1, 1);
    private static readonly LocalDate DividendWindowEnd = new(2021, 12, 31);
    private static readonly LocalDate SplitWindowStart = new(2020, 1, 1);
    private static readonly LocalDate SplitWindowEnd = new(2020, 12, 31);

    [Fact]
    public async Task DividendsHonourATickerAndAnExDateRange()
    {
        MassivePage<ReferenceDividend> page = await Client.Reference.ListDividendsAsync(
            ticker: "AAPL",
            exDividendDate: RangeFilter.Between(DividendWindowStart, DividendWindowEnd),
            order: SortOrder.Ascending,
            cancellationToken: Ct);

        Assert.Equal(4, page.Results.Length);

        foreach (ReferenceDividend dividend in page.Results)
        {
            Assert.Equal("AAPL", dividend.Ticker);
            Assert.Equal("CD", dividend.DividendType);
            Assert.InRange(dividend.ExDividendDate, DividendWindowStart, DividendWindowEnd);
            Assert.True(dividend.CashAmount > 0);
        }
    }

    [Fact]
    public async Task SplitsReturnTheAppleSplit()
    {
        MassivePage<ReferenceSplit> page = await Client.Reference.ListSplitsAsync(
            ticker: "AAPL",
            executionDate: RangeFilter.Between(SplitWindowStart, SplitWindowEnd),
            cancellationToken: Ct);

        ReferenceSplit split = Assert.Single(page.Results);
        Assert.Equal(new LocalDate(2020, 8, 31), split.ExecutionDate);
        Assert.Equal(1d, split.SplitFrom);
        Assert.Equal(4d, split.SplitTo);
    }
}
