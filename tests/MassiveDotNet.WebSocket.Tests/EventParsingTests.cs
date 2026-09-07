using System.Text;
using System.Text.Json;
using MassiveDotNet.WebSocket.Events;
using MassiveDotNet.WebSocket.Internal;
using NodaTime;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

public class EventParsingTests
{
    private static StockTrade ReadTrade(string json)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        reader.Read();  // [
        reader.Read();  // {

        return new StockTradeConverter(new TickerPool(16)).Read(ref reader, typeof(StockTrade), JsonSerializerOptions.Default);
    }

    [Fact]
    public void ThePublishedTradeSampleDeserializes()
    {
        StockTrade trade = ReadTrade(Fixtures.StockTrade);

        Assert.Equal("MSFT", trade.Ticker);
        Assert.Equal(4, trade.ExchangeId);
        Assert.Equal("12345", trade.TradeId);
        Assert.Equal(3, trade.Tape);
        Assert.Equal(114.125, trade.Price);
        Assert.Equal(100, trade.Size);
        Assert.Equal(3681328, trade.SequenceNumber);
        Assert.Equal([0, 12], trade.Conditions.AsSpan().ToArray());
    }

    // D5: the raw epoch is stored and the Instant computed on read. The streaming wire sends
    // milliseconds where REST v3 sends nanoseconds for the same conceptual field (D-W11).
    [Fact]
    public void TimestampsAreMillisecondsExposedAsInstants()
    {
        StockTrade trade = ReadTrade(Fixtures.StockTrade);

        Assert.Equal(1536036818784, trade.SipTimestampMilliseconds);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1536036818784), trade.SipTimestamp);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1536036818763), trade.ParticipantTimestamp);
    }

    // The published sample omits ds, trfi and trft, which is the documentation's own evidence
    // that a real message need not carry them.
    [Fact]
    public void OptionalFieldsAbsentFromTheSampleReadAsNull()
    {
        StockTrade trade = ReadTrade(Fixtures.StockTrade);

        Assert.Null(trade.DecimalSize);
        Assert.Null(trade.TrfId);
        Assert.Null(trade.TrfTimestampMilliseconds);
        Assert.Null(trade.TrfTimestamp);
    }

    [Fact]
    public void AnUnknownPropertyIsSkippedRatherThanThrowing()
    {
        StockTrade trade = ReadTrade("""[{"ev":"T","sym":"MSFT","p":1.0,"s":1,"i":"x","t":1,"q":1,"brandNew":{"nested":[1,2]}}]""");

        Assert.Equal("MSFT", trade.Ticker);
    }

    [Fact]
    public void ThePublishedQuoteSampleDeserializes()
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(Fixtures.StockQuote));
        reader.Read();
        reader.Read();

        StockQuote quote = new StockQuoteConverter(new TickerPool(16))
            .Read(ref reader, typeof(StockQuote), JsonSerializerOptions.Default);

        Assert.Equal("MSFT", quote.Ticker);
        Assert.Equal(4, quote.BidExchangeId);
        Assert.Equal(114.125, quote.BidPrice);
        Assert.Equal(100, quote.BidSize);
        Assert.Equal(7, quote.AskExchangeId);
        Assert.Equal(114.128, quote.AskPrice);
        Assert.Equal(160, quote.AskSize);
        Assert.Equal((int?)0, quote.Condition);
        Assert.Equal([604], quote.Indicators.AsSpan().ToArray());
        Assert.Equal(50385480, quote.SequenceNumber);
        Assert.Equal(3, quote.Tape);
    }

    [Fact]
    public void TheTickerIsPooledAcrossEvents()
    {
        TickerPool pool = new(16);
        StockTradeConverter converter = new(pool);

        StockTrade first = ReadWith(converter, """[{"ev":"T","sym":"AAPL","i":"1","p":1,"s":1,"t":1,"q":1}]""");
        StockTrade second = ReadWith(converter, """[{"ev":"T","sym":"AAPL","i":"2","p":2,"s":2,"t":2,"q":2}]""");

        Assert.Same(first.Ticker, second.Ticker);

        static StockTrade ReadWith(StockTradeConverter converter, string json)
        {
            Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
            reader.Read();
            reader.Read();
            return converter.Read(ref reader, typeof(StockTrade), JsonSerializerOptions.Default);
        }
    }

    // Never truncate: more conditions than the inline buffer holds spills to a heap array rather
    // than silently dropping codes, which is the data loss this SDK refuses everywhere else.
    [Fact]
    public void MoreConditionsThanFitInlineSpillWithoutLoss()
    {
        StockTrade trade = ReadTrade(
            """[{"ev":"T","sym":"MSFT","i":"1","p":1,"s":1,"t":1,"q":1,"c":[1,2,3,4,5,6,7,8,9,10]}]""");

        Assert.Equal(10, trade.Conditions.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], trade.Conditions.AsSpan().ToArray());
    }
}
