using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Exercises the aggregates endpoint against the live service.
/// </summary>
/// <remarks>
/// These assert what committed fixtures structurally cannot: that authentication is accepted by the
/// real service, that the wire format still matches what the SDK expects, and that error handling
/// matches the envelope the service actually returns rather than the one the OpenAPI description
/// declines to specify.
/// </remarks>
public sealed class StocksAggregatesLiveTests : LiveApiTest
{
    // A fixed historical window, so the assertions do not depend on when the suite is run.
    private static readonly LocalDate WindowStart = new(2026, 8, 3);
    private static readonly LocalDate WindowEnd = new(2026, 8, 7);

    [Fact]
    public async Task ReturnsDailyBarsForAKnownTicker()
    {
        Agg[] bars = (await Client.Stocks.ListAggregatesAsync(
            "AAPL", 1, AggregateTimespan.Day, WindowStart, WindowEnd, cancellationToken: Ct)).Results;

        Assert.NotEmpty(bars);

        foreach (Agg bar in bars)
        {
            Assert.True(bar.High >= bar.Low, $"High {bar.High} was below low {bar.Low}.");
            Assert.True(bar.Open > 0 && bar.Close > 0, "Prices should be positive.");
            Assert.True(bar.Volume > 0, "A trading day should report volume.");
            Assert.InRange(
                bar.Timestamp,
                WindowStart.AtMidnight().InUtc().ToInstant() - Duration.FromDays(1),
                WindowEnd.AtMidnight().InUtc().ToInstant() + Duration.FromDays(1));
        }
    }

    [Fact]
    public async Task HonoursTheLimitParameter()
    {
        Agg[] bars = (await Client.Stocks.ListAggregatesAsync(
            "MSFT", 1, AggregateTimespan.Day, WindowStart, WindowEnd, limit: 2, cancellationToken: Ct)).Results;

        Assert.True(bars.Length <= 2, $"Expected at most 2 bars, got {bars.Length}.");
    }

    [Fact]
    public async Task ReturnsNoBarsForAnUnknownTicker()
    {
        // Confirms the service answers 200-with-no-results rather than an error status, which is
        // what the SDK's "empty array" contract assumes.
        Agg[] bars = (await Client.Stocks.ListAggregatesAsync(
            "ZZZZNOTAREALTICKER", 1, AggregateTimespan.Day, WindowStart, WindowEnd, cancellationToken: Ct)).Results;

        Assert.Empty(bars);
    }
}
