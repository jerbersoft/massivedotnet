using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Verifies the paginated singular shape against the real service. A fixture proves the SDK reads
/// a recording; only a live traversal proves the indicator endpoints still page over
/// <c>results.values</c> and that the cursor contract holds across a real boundary.
/// </summary>
public sealed class StocksIndicatorsLiveTests : LiveApiTest
{
    // A fixed historical window, so the assertions do not depend on when the suite is run.
    private static readonly LocalDate WindowStart = new(2024, 1, 1);
    private static readonly LocalDate WindowEnd = new(2024, 3, 31);

    [Fact]
    public async Task CrossesRealPageBoundaries()
    {
        // limit is per page, so five values at two per page is at least three round trips.
        List<IndicatorValue> values = [];

        await foreach (IndicatorValue value in Client.Stocks.EnumerateSmaAsync(
            "AAPL",
            timestamp: RangeFilter.Between(DateOrTimestamp.FromDate(WindowStart), DateOrTimestamp.FromDate(WindowEnd)),
            timespan: AggregateTimespan.Day,
            window: 10,
            limit: 2,
            cancellationToken: Ct))
        {
            values.Add(value);

            if (values.Count >= 5)
            {
                break;
            }
        }

        Assert.Equal(5, values.Count);

        // Values must be strictly ordered across the page seams, which is where an incorrectly
        // rebuilt cursor would show up as repeated or skipped points. The endpoint's default
        // order is descending.
        Assert.Equal(
            values.Select(v => v.TimestampMilliseconds).OrderDescending(),
            values.Select(v => v.TimestampMilliseconds));
        Assert.Equal(5, values.Select(v => v.TimestampMilliseconds).Distinct().Count());
    }

    [Fact]
    public async Task ListReportsMorePagesAndCarriesTheUnderlying()
    {
        MassivePagedResult<IndicatorSeries> page = await Client.Stocks.ListSmaAsync(
            "AAPL",
            timestamp: RangeFilter.Between(DateOrTimestamp.FromDate(WindowStart), DateOrTimestamp.FromDate(WindowEnd)),
            timespan: AggregateTimespan.Day,
            window: 10,
            expandUnderlying: true,
            limit: 2,
            cancellationToken: Ct);

        Assert.True(page.HasMore);
        Assert.NotNull(page.RequestId);
        Assert.NotNull(page.Result.Values);
        Assert.Equal(2, page.Result.Values.Length);
        Assert.NotNull(page.Result.Underlying);
        Assert.NotNull(page.Result.Underlying.Aggregates);
        Assert.NotEmpty(page.Result.Underlying.Aggregates);

        // The underlying URL is a plain aggregates request and must never carry a key (rule 11).
        Assert.DoesNotContain("apiKey", page.Result.Underlying.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmaReturnsValuesInTheWindow()
    {
        MassivePagedResult<IndicatorSeries> page = await Client.Stocks.ListEmaAsync(
            "AAPL",
            timestamp: RangeFilter.Between(DateOrTimestamp.FromDate(WindowStart), DateOrTimestamp.FromDate(WindowEnd)),
            timespan: AggregateTimespan.Day,
            window: 10,
            limit: 2,
            cancellationToken: Ct);

        Assert.NotNull(page.Result.Values);
        Assert.Equal(2, page.Result.Values.Length);
        Assert.All(page.Result.Values, value => Assert.True(value.Value > 0));
    }

    [Fact]
    public async Task RsiReturnsValuesBetweenZeroAndOneHundred()
    {
        MassivePagedResult<IndicatorSeries> page = await Client.Stocks.ListRsiAsync(
            "AAPL",
            timestamp: RangeFilter.Between(DateOrTimestamp.FromDate(WindowStart), DateOrTimestamp.FromDate(WindowEnd)),
            timespan: AggregateTimespan.Day,
            window: 14,
            limit: 2,
            cancellationToken: Ct);

        Assert.NotNull(page.Result.Values);
        Assert.Equal(2, page.Result.Values.Length);
        Assert.All(page.Result.Values, value => Assert.InRange(value.Value, 0, 100));
    }

    [Fact]
    public async Task MacdReturnsAHistogramThatIsTheLineMinusTheSignal()
    {
        MassivePagedResult<MacdSeries> page = await Client.Stocks.ListMacdAsync(
            "AAPL",
            timestamp: RangeFilter.Between(DateOrTimestamp.FromDate(WindowStart), DateOrTimestamp.FromDate(WindowEnd)),
            timespan: AggregateTimespan.Day,
            shortWindow: 12,
            longWindow: 26,
            signalWindow: 9,
            limit: 2,
            cancellationToken: Ct);

        Assert.NotNull(page.Result.Values);
        Assert.Equal(2, page.Result.Values.Length);

        // The histogram is defined as the MACD line minus its signal, so the three members of
        // every point must agree with each other whatever their values are.
        foreach (MacdValue value in page.Result.Values)
        {
            Assert.Equal(value.Value - value.Signal, value.Histogram, precision: 6);
        }
    }
}
