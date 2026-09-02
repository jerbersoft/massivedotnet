using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// One shape-asserting call each for grouped daily and previous close (D-G8), so a moved response
/// shape shows up on the next local run. Values are not asserted; the fixtures do that.
/// </summary>
public sealed class StocksBarsLiveTests : LiveApiTest
{
    // A fixed historical session, so the assertions do not depend on when the suite is run.
    private static readonly LocalDate Session = new(2024, 1, 16);

    [Fact]
    public async Task GroupedDailyReturnsABarPerTicker()
    {
        GroupedDailyBar[] bars = await Client.Stocks.ListGroupedDailyAsync(Session, adjusted: true, cancellationToken: Ct);

        Assert.NotEmpty(bars);
        Assert.Contains(bars, bar => bar.Ticker == "AAPL");

        foreach (GroupedDailyBar bar in bars)
        {
            Assert.False(string.IsNullOrEmpty(bar.Ticker));
            Assert.True(bar.High >= bar.Low, $"{bar.Ticker}: high {bar.High} was below low {bar.Low}.");
        }
    }

    [Fact]
    public async Task PreviousCloseReturnsOneRecentBar()
    {
        PreviousCloseBar[] bars = await Client.Stocks.ListPreviousCloseAsync("AAPL", cancellationToken: Ct);

        PreviousCloseBar bar = Assert.Single(bars);
        Assert.True(bar.Volume > 0, "A trading day should report volume.");
        Assert.True(bar.High >= bar.Low, $"High {bar.High} was below low {bar.Low}.");

        // Previous close is relative to today, so only recency is checkable; a week covers any
        // long weekend.
        Instant now = SystemClock.Instance.GetCurrentInstant();
        Assert.InRange(bar.Timestamp, now - Duration.FromDays(7), now);
    }
}
