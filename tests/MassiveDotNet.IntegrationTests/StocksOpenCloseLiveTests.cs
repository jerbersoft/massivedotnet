using System.Net;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Verifies the body-object shape against the real service, on the endpoint whose published
/// sample is likeliest to have drifted, and confirms the 404 the singular contract rests on.
/// </summary>
public sealed class StocksOpenCloseLiveTests : LiveApiTest
{
    // A fixed historical session, so the assertions do not depend on when the suite is run.
    private static readonly LocalDate Session = new(2024, 1, 5);

    [Fact]
    public async Task ReturnsTheSessionForAKnownTickerAndDate()
    {
        DailyOpenClose day = await Client.Stocks.GetDailyOpenCloseAsync("AAPL", Session, cancellationToken: Ct);

        Assert.Equal("AAPL", day.Symbol);
        Assert.Equal(Session, day.From);
        Assert.Equal("OK", day.Status);
        Assert.True(day.High >= day.Low, $"High {day.High} was below low {day.Low}.");
        Assert.True(day.Open > 0 && day.Close > 0, "Prices should be positive.");
        Assert.True(day.Volume > 0, "A trading day should report volume.");
    }

    [Fact]
    public async Task ADateWithNoSessionIsANotFoundError()
    {
        // The live probe behind decision D17: a 200 always carries its payload, and a day with no
        // session is a 404. This is the assumption the "Get returns T, never T?" contract rests on.
        MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(() =>
            Client.Stocks.GetDailyOpenCloseAsync("AAPL", new LocalDate(2024, 1, 6), cancellationToken: Ct));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }
}
