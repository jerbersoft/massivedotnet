using System.Reflection;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The deprecated v2 tick endpoints, shipped marked <c>[Obsolete]</c> (rule 2). The description
/// marks fields required that its own examples omit and declares nanosecond timestamps as bare
/// integers; the map corrects both (D-G5), and these tests prove the unchanged published examples
/// deserialize as a result.
/// </summary>
public sealed class StocksHistoricTicksTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly LocalDate Session = new(2018, 2, 2);

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task TradesPathCarriesTheDateAndTheOffsetsRenderAsLongs()
    {
        StubHandler handler = new(Fixtures.StocksHistoricTrades);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListHistoricTradesAsync(
                "AAPL",
                Session,
                timestamp: 1517562000016036600,
                timestampLimit: 1517562000016038100,
                reverse: true,
                limit: 2,
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v2/ticks/stocks/trades/AAPL/2018-02-02?timestamp=1517562000016036600&timestampLimit=1517562000016038100&reverse=true&limit=2",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task TradesDeserializeThePublishedSampleWithTheFalselyRequiredFieldsNull()
    {
        StubHandler handler = new(Fixtures.StocksHistoricTrades);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        HistoricTrade[] trades;

        using (client)
        using (transport)
        {
            trades = await client.Stocks.ListHistoricTradesAsync("AAPL", Session, cancellationToken: Ct);
        }

        Assert.Equal(2, trades.Length);

        HistoricTrade first = trades[0];
        Assert.Equal("1", first.TradeId);
        Assert.Equal(171.55, first.Price);
        Assert.Equal(100d, first.Size);
        Assert.Equal(11, first.ExchangeId);
        Assert.Equal(1063L, first.SequenceNumber);
        Assert.Equal(3, first.Tape);
        Assert.Equal([12, 41], first.Conditions);
        Assert.Equal(1517562000016036600, first.SipTimestampNanoseconds);
        Assert.Equal(1517562000015577000, first.ParticipantTimestampNanoseconds);

        // The four the schema requires and the sample omits: a required modifier on any of them
        // would have made this a JsonException.
        Assert.Null(first.Ticker);
        Assert.Null(first.TrfTimestampNanoseconds);
        Assert.Null(first.CorrectionIndicator);
        Assert.Null(first.TrfId);

        Assert.Equal("2", trades[1].TradeId);
    }

    [Fact]
    public async Task QuotesPathCarriesTheDate()
    {
        StubHandler handler = new(Fixtures.StocksHistoricQuotes);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListHistoricQuotesAsync("AAPL", Session, limit: 2, cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v2/ticks/stocks/nbbo/AAPL/2018-02-02?limit=2", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task QuotesDeserializeThePublishedSampleWithTheFalselyRequiredFieldsNull()
    {
        StubHandler handler = new(Fixtures.StocksHistoricQuotes);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        HistoricQuote[] quotes;

        using (client)
        using (transport)
        {
            quotes = await client.Stocks.ListHistoricQuotesAsync("AAPL", Session, cancellationToken: Ct);
        }

        Assert.Equal(2, quotes.Length);

        HistoricQuote first = quotes[0];
        Assert.Equal(102.7, first.BidPrice);
        Assert.Equal(60, first.BidSize);
        Assert.Equal(11, first.BidExchangeId);
        Assert.Equal(0d, first.AskPrice);
        Assert.Equal(0, first.AskSize);
        Assert.Equal(0, first.AskExchangeId);
        Assert.Equal(2060L, first.SequenceNumber);
        Assert.Equal(3, first.Tape);
        Assert.Equal([1], first.Conditions);
        Assert.Equal(1517562000065700400, first.SipTimestampNanoseconds);
        Assert.Equal(1517562000065321200, first.ParticipantTimestampNanoseconds);

        Assert.Null(first.Ticker);
        Assert.Null(first.TrfTimestampNanoseconds);
        Assert.Null(first.Indicators);

        Assert.Equal(170d, quotes[1].BidPrice);
    }

    [Theory]
    [InlineData("ListHistoricTradesAsync", "Stocks.ListTradesAsync")]
    [InlineData("ListHistoricQuotesAsync", "Stocks.ListQuotesAsync")]
    public void TheObsoleteMessageNamesTheReplacement(string method, string replacement)
    {
        // EndpointCoverageTests checks that the attribute is present with the right id; this pins
        // the message, which is the part a consumer actually reads.
        ObsoleteAttribute? attribute = typeof(StocksGroup).GetMethod(method)?.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("MASSIVE0002", attribute.DiagnosticId);
        Assert.Equal($"Massive has deprecated this operation. Use {replacement} instead.", attribute.Message);
    }
}
