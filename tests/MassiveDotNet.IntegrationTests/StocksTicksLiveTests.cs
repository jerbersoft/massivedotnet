using System.Net;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// The tick endpoints against the real service: a traversal across a real page boundary on the
/// v3 trades envelope, the nanosecond bound that <see cref="DateOrNanoseconds"/> exists for (D20),
/// one call each for quotes and the last quote, and pins on the retired v2 pair and the unserved
/// dev route (D21, D22).
/// </summary>
public sealed class StocksTicksLiveTests : LiveApiTest
{
    // A fixed historical session, so the assertions do not depend on when the suite is run.
    private static readonly LocalDate Session = new(2024, 1, 16);

    // 10:00 Eastern on that session, which is 15:00 UTC in January.
    private static readonly Instant MidMorning = Instant.FromUtc(2024, 1, 16, 15, 0);

    [Fact]
    public async Task TradesCrossARealPageBoundary()
    {
        // limit is per page, so five trades at two per page is at least three round trips, and
        // the seams are where a rebuilt cursor would repeat or skip.
        List<Trade> trades = [];

        await foreach (Trade trade in Client.Stocks.EnumerateTradesAsync(
            "AAPL",
            timestamp: DateOrNanoseconds.FromDate(Session),
            order: SortOrder.Ascending,
            limit: 2,
            cancellationToken: Ct))
        {
            trades.Add(trade);

            if (trades.Count >= 5)
            {
                break;
            }
        }

        Assert.Equal(5, trades.Count);
        Assert.Equal(
            trades.Select(t => t.SipTimestampNanoseconds).Order(),
            trades.Select(t => t.SipTimestampNanoseconds));
        Assert.Equal(5, trades.Select(t => t.SequenceNumber).Distinct().Count());

        foreach (Trade trade in trades)
        {
            // An Eastern session runs from 09:00 to 01:00 UTC the next day.
            Assert.InRange(trade.SipTimestamp.InUtc().Date, Session, Session.PlusDays(1));
            Assert.True(trade.Price > 0);
        }
    }

    [Fact]
    public async Task ANanosecondBoundIsHonoured()
    {
        // A millisecond render of the same instant would name a moment in 1970, and the service
        // would answer with the oldest trades it holds rather than an error. Every trade on or
        // after the bound proves the nineteen-digit form was read as intended.
        MassivePage<Trade> page = await Client.Stocks.ListTradesAsync(
            "AAPL",
            timestamp: RangeFilter.Gte(DateOrNanoseconds.FromInstant(MidMorning)),
            order: SortOrder.Ascending,
            limit: 3,
            cancellationToken: Ct);

        Assert.Equal(3, page.Results.Length);
        Assert.True(page.HasMore);

        foreach (Trade trade in page.Results)
        {
            Assert.True(trade.SipTimestamp >= MidMorning, $"Trade at {trade.SipTimestamp} precedes the bound {MidMorning}.");
            Assert.Equal(Session, trade.SipTimestamp.InUtc().Date);
        }
    }

    /// <summary>
    /// Every <c>decimal_size</c> on a real page is a form the round-trip guard accepts.
    /// </summary>
    /// <remarks>
    /// The one claim D38 rests on that a fixture structurally cannot make. The guard refuses a
    /// decimal string that does not format back byte for byte, which means a leading <c>+</c>, a
    /// redundant zero, a separator, or exponent notation throws at deserialization -- so if
    /// Massive's wire is ever not canonical, this is where it surfaces, and a committed recording
    /// of the past could never say so. Deserializing the page at all is the assertion; the loop
    /// below is what keeps that from being vacuous, since a page of zeroes would also have parsed.
    /// Observed canonical on 2026-09-09.
    /// </remarks>
    [Fact]
    public async Task EveryRealTradeCarriesADecimalSizeTheGuardAccepts()
    {
        MassivePage<Trade> page = await Client.Stocks.ListTradesAsync(
            "AAPL",
            timestamp: DateOrNanoseconds.FromDate(Session),
            limit: 50,
            cancellationToken: Ct);

        Assert.Equal(50, page.Results.Length);

        foreach (Trade trade in page.Results)
        {
            // Size truncates the fractional part, so the decimal form is never the smaller of the
            // two. Comparing them is what proves a real number arrived rather than a default.
            Assert.True(
                trade.DecimalSize >= (decimal)trade.Size && trade.DecimalSize > 0m,
                $"Trade {trade.TradeId} reported a decimal size of {trade.DecimalSize} against a size of {trade.Size}.");
        }
    }

    [Fact]
    public async Task QuotesReturnAPage()
    {
        MassivePage<Quote> page = await Client.Stocks.ListQuotesAsync(
            "AAPL",
            timestamp: DateOrNanoseconds.FromDate(Session),
            limit: 2,
            cancellationToken: Ct);

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);

        foreach (Quote quote in page.Results)
        {
            Assert.InRange(quote.SipTimestamp.InUtc().Date, Session, Session.PlusDays(1));
            Assert.True(quote.BidPrice > 0 || quote.AskPrice > 0, "A quote should carry at least one side.");
        }
    }

    [Fact]
    public async Task LastQuoteReturnsTheTicker()
    {
        LastQuote quote = await Client.Stocks.GetLastQuoteAsync("AAPL", Ct);

        Assert.Equal("AAPL", quote.Ticker);
        Assert.True(quote.SipTimestamp > Instant.FromUtc(2024, 1, 1, 0, 0), "The last quote should be recent.");
    }

    [Fact]
    public async Task TheDevTradesRouteAnswersNotFound()
    {
        // The description declares GET /stocks/dev/trades/{ticker} and its dev segment marks it
        // experimental (D22), but the service answered a plain-text 404 for it, not an API error
        // envelope (observed 2026-09-03). D21 keeps it mapped as the description declares it;
        // this pin flips the day the route is served, at which point it becomes a shape
        // assertion and the hand-written fixture gets replaced by a capture.
        MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(() =>
            Client.Stocks.ListDevTradesAsync("AAPL", limit: 2, cancellationToken: Ct));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task TheRetiredHistoricTradesRouteAnswersNotFound()
    {
        // Massive retired the v2 historic trades route server-side (observed 2026-09-02; the
        // error body points callers at the v3 trades feed). The description still declares it,
        // so D21 keeps the SDK shipping this operation as [Obsolete], naming ListTradesAsync as
        // the replacement, until the nightly sync drops the route. This test exists so the day
        // the route answers again, someone upgrades it back to a shape assertion instead of
        // leaving a skip that would read as green.
        MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(() =>
            Client.Stocks.ListHistoricTradesAsync("AAPL", Session, limit: 2, cancellationToken: Ct));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task TheRetiredHistoricQuotesRouteAnswersNotFound()
    {
        // Same retirement as the trades route, on the v2 historic quotes route (observed
        // 2026-09-02); the SDK keeps ListHistoricQuotesAsync mapped and [Obsolete] under D21,
        // naming ListQuotesAsync as the replacement.
        MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(() =>
            Client.Stocks.ListHistoricQuotesAsync("AAPL", Session, limit: 2, cancellationToken: Ct));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }
}
