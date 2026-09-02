using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// The tick endpoints against the real service: a traversal across a real page boundary on the
/// v3 trades envelope, the nanosecond bound that <see cref="DateOrNanoseconds"/> exists for (D20),
/// and one call each for quotes, the last quote, and the deprecated v2 pair (D-G8).
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
    public async Task TheDeprecatedTradesEndpointStillAnswers()
    {
        // Included so the suite reports the day Massive retires the v2 tick endpoints (D-G8).
        HistoricTrade[] trades = await Client.Stocks.ListHistoricTradesAsync("AAPL", Session, limit: 2, cancellationToken: Ct);

        Assert.Equal(2, trades.Length);
        Assert.All(trades, trade => Assert.True(trade.Price > 0));
        Assert.All(trades, trade => Assert.True(trade.SipTimestampNanoseconds > 0));
    }

    [Fact]
    public async Task TheDeprecatedQuotesEndpointStillAnswers()
    {
        HistoricQuote[] quotes = await Client.Stocks.ListHistoricQuotesAsync("AAPL", Session, limit: 2, cancellationToken: Ct);

        Assert.Equal(2, quotes.Length);
        Assert.All(quotes, quote => Assert.True(quote.SipTimestampNanoseconds > 0));
    }
}
