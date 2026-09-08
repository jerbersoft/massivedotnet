using System.Buffers;
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

    private static StockQuote ReadQuote(string json)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        reader.Read();  // [
        reader.Read();  // {

        return new StockQuoteConverter(new TickerPool(16)).Read(ref reader, typeof(StockQuote), JsonSerializerOptions.Default);
    }

    private static StockAggregate ReadAggregate(string json, string topicCode)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        reader.Read();  // [
        reader.Read();  // {

        return new StockAggregateConverter(new TickerPool(16), topicCode)
            .Read(ref reader, typeof(StockAggregate), JsonSerializerOptions.Default);
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

    // The converse of the above: a converter that ignored these fields entirely would also pass
    // "reads as null", so their presence is asserted separately here.
    [Fact]
    public void OptionalFieldsPresentInTheMessagePopulate()
    {
        StockTrade trade = ReadTrade(
            """[{"ev":"T","sym":"MSFT","i":"1","p":1,"s":1,"t":1,"q":1,"ds":"0.5","trfi":202,"trft":1536036818700}]""");

        Assert.Equal("0.5", trade.DecimalSize);
        Assert.Equal(202, trade.TrfId);
        Assert.Equal(1536036818700, trade.TrfTimestampMilliseconds);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1536036818700), trade.TrfTimestamp);
    }

    [Fact]
    public void AnUnknownPropertyIsSkippedRatherThanThrowing()
    {
        StockTrade trade = ReadTrade("""[{"ev":"T","sym":"MSFT","p":1.0,"s":1,"i":"x","t":1,"q":1,"brandNew":{"nested":[1,2]}}]""");

        Assert.Equal("MSFT", trade.Ticker);
    }

    // The existing unknown-property test uses a scalar, which a missed Skip() survives by accident:
    // the reader lands on the value and the next Read finds the following property name anyway. An
    // unknown OBJECT or ARRAY is what actually distinguishes a correct walk, so the assertion is on
    // a field that comes after it.
    [Theory]
    [InlineData("""{"ev":"T","sym":"MSFT","future":{"nested":[1,2]},"i":"12345","q":7}""")]
    [InlineData("""{"ev":"T","sym":"MSFT","future":[1,[2,{"x":3}]],"i":"12345","q":7}""")]
    public void AnUnknownNestedPropertyIsSkippedWholesale(string json)
    {
        StockTrade trade = ReadTrade($"[{json}]");

        Assert.Equal("MSFT", trade.Ticker);
        Assert.Equal("12345", trade.TradeId);
        Assert.Equal(7, trade.SequenceNumber);
    }

    [Fact]
    public void ThePublishedQuoteSampleDeserializes()
    {
        StockQuote quote = ReadQuote(Fixtures.StockQuote);

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

    // The spill boundary (F7): 7 and 8 stay inline, 9 spills. 0 covers an empty-but-present array.
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void ConditionCountsAroundTheSpillBoundaryReadCorrectly(int count)
    {
        int[] codes = new int[count];

        for (int i = 0; i < count; i++)
        {
            codes[i] = i + 1;
        }

        string codesJson = string.Join(",", codes);
        StockTrade trade = ReadTrade(
            $$"""[{"ev":"T","sym":"MSFT","i":"1","p":1,"s":1,"t":1,"q":1,"c":[{{codesJson}}]}]""");

        Assert.Equal(count, trade.Conditions.Count);
        Assert.Equal(codes, trade.Conditions.AsSpan().ToArray());
    }

    [Fact]
    public void ANullConditionsArrayReadsAsEmpty()
    {
        StockTrade trade = ReadTrade("""[{"ev":"T","sym":"MSFT","i":"1","p":1,"s":1,"t":1,"q":1,"c":null}]""");

        Assert.Equal(0, trade.Conditions.Count);
    }

    // F1: ConditionSet's [InlineArray] field defeats the runtime's default ValueType equality
    // outright (NotSupportedException) until IEquatable<ConditionSet> is implemented, which is
    // what StockTrade's and StockQuote's own record-struct equality (D4) delegates to.
    [Fact]
    public void TwoTradesParsedFromIdenticalJsonWithInlineConditionsAreEqual()
    {
        StockTrade a = ReadTrade(Fixtures.StockTrade);
        StockTrade b = ReadTrade(Fixtures.StockTrade);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void TwoTradesParsedFromIdenticalJsonWithSpilledConditionsAreEqual()
    {
        const string json = """[{"ev":"T","sym":"MSFT","i":"1","p":1,"s":1,"t":1,"q":1,"c":[1,2,3,4,5,6,7,8,9]}]""";

        StockTrade a = ReadTrade(json);
        StockTrade b = ReadTrade(json);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void TradesDifferingInOneConditionCodeAreUnequal()
    {
        StockTrade a = ReadTrade("""[{"ev":"T","sym":"MSFT","i":"1","p":1,"s":1,"t":1,"q":1,"c":[1,2]}]""");
        StockTrade b = ReadTrade("""[{"ev":"T","sym":"MSFT","i":"1","p":1,"s":1,"t":1,"q":1,"c":[1,99]}]""");

        Assert.NotEqual(a, b);
        Assert.False(a == b);
    }

    // The path the reviewer found goes silently wrong rather than throwing: a HashSet/dictionary
    // lookup depends on Equals and GetHashCode agreeing, not just Equals alone.
    [Fact]
    public void ConditionSetWorksAsAHashSetKey()
    {
        ConditionSet a = ReadTrade("""[{"ev":"T","sym":"MSFT","i":"1","p":1,"s":1,"t":1,"q":1,"c":[1,2,3]}]""").Conditions;
        ConditionSet b = ReadTrade("""[{"ev":"T","sym":"MSFT","i":"1","p":1,"s":1,"t":1,"q":1,"c":[1,2,3]}]""").Conditions;

        HashSet<ConditionSet> set = [a];

        Assert.Contains(b, set);
    }

    // F2: "sym" is read through TickerPool.Intern, which bypasses JsonValueReader for its pooling
    // fast path, so a malformed value must be checked explicitly or it escapes as the wrong
    // exception type (InvalidOperationException) instead of the JsonException every other field
    // throws.
    [Fact]
    public void ATradeWithANullSymbolThrowsJsonException()
    {
        JsonException exception = Assert.Throws<JsonException>(() =>
            ReadTrade("""[{"ev":"T","sym":null,"i":"1","p":1,"s":1,"t":1,"q":1}]"""));

        Assert.Contains("sym", exception.Message);
    }

    [Fact]
    public void ATradeWithANonStringSymbolThrowsJsonException()
    {
        Assert.Throws<JsonException>(() =>
            ReadTrade("""[{"ev":"T","sym":42,"i":"1","p":1,"s":1,"t":1,"q":1}]"""));
    }

    [Fact]
    public void AQuoteWithANullSymbolThrowsJsonException()
    {
        JsonException exception = Assert.Throws<JsonException>(() =>
            ReadQuote("""[{"ev":"Q","sym":null,"bp":1,"bs":1,"ap":1,"as":1,"t":1,"q":1}]"""));

        Assert.Contains("sym", exception.Message);
    }

    [Fact]
    public void AQuoteWithANonStringSymbolThrowsJsonException()
    {
        Assert.Throws<JsonException>(() =>
            ReadQuote("""[{"ev":"Q","sym":42,"bp":1,"bs":1,"ap":1,"as":1,"t":1,"q":1}]"""));
    }

    // F3: Write must round-trip every field a real message can carry, not just the ones the
    // brief's original Write happened to cover. This depends on ConditionSet equality (F1): the
    // trades compared here differ in Conditions along with everything else Write must preserve.
    [Fact]
    public void ATradeRoundTripsThroughWriteAndRead()
    {
        StockTrade original = ReadTrade(
            """[{"ev":"T","sym":"MSFT","x":4,"i":"12345","z":3,"p":114.125,"s":100,"ds":"0.5","c":[0,12],"t":1536036818784,"pt":1536036818763,"q":3681328,"trfi":202,"trft":1536036818700}]""");

        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            new StockTradeConverter(new TickerPool(16)).Write(writer, original, JsonSerializerOptions.Default);
        }

        Utf8JsonReader reader = new(buffer.WrittenSpan);
        reader.Read();
        StockTrade roundTripped = new StockTradeConverter(new TickerPool(16))
            .Read(ref reader, typeof(StockTrade), JsonSerializerOptions.Default);

        Assert.Equal(original, roundTripped);
    }

    // Also covers the case Write must NOT emit: an absent optional stays absent rather than
    // round-tripping to a written null, since this SDK never sends an optional as empty (D19's
    // rule for request parameters, held to on the response side here too).
    [Fact]
    public void ATradeWithNoOptionalFieldsRoundTripsThroughWriteAndRead()
    {
        StockTrade original = ReadTrade("""[{"ev":"T","sym":"MSFT","i":"1","p":1,"s":1,"t":1,"q":1}]""");

        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            new StockTradeConverter(new TickerPool(16)).Write(writer, original, JsonSerializerOptions.Default);
        }

        Utf8JsonReader reader = new(buffer.WrittenSpan);
        reader.Read();
        StockTrade roundTripped = new StockTradeConverter(new TickerPool(16))
            .Read(ref reader, typeof(StockTrade), JsonSerializerOptions.Default);

        Assert.Equal(original, roundTripped);
        Assert.Null(roundTripped.DecimalSize);
        Assert.Null(roundTripped.TrfId);
        Assert.Null(roundTripped.TrfTimestampMilliseconds);
    }

    // A swapped bid/ask, or a dropped condition/indicator field, would still pass field-by-field
    // assertions that happen not to look for the swap; a full-value round trip catches it without
    // anyone having to think to look.
    [Fact]
    public void AQuoteRoundTripsThroughWriteAndRead()
    {
        StockQuote original = ReadQuote(Fixtures.StockQuote);

        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            new StockQuoteConverter(new TickerPool(16)).Write(writer, original, JsonSerializerOptions.Default);
        }

        Utf8JsonReader reader = new(buffer.WrittenSpan);
        reader.Read();
        StockQuote roundTripped = new StockQuoteConverter(new TickerPool(16))
            .Read(ref reader, typeof(StockQuote), JsonSerializerOptions.Default);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void ThePublishedSecondAggregateSampleDeserializes()
    {
        StockAggregate bar = ReadAggregate(Fixtures.StockSecondAggregate, "A");

        Assert.Equal("SPCE", bar.Ticker);
        Assert.Equal(200, bar.Volume);
        Assert.Equal(8642007, bar.AccumulatedVolume);
        Assert.Equal(25.66, bar.OfficialOpenPrice);
        Assert.Equal(25.3981, bar.VolumeWeightedAveragePrice);
        Assert.Equal(25.39, bar.Open);
        Assert.Equal(25.39, bar.Close);
        Assert.Equal(25.39, bar.High);
        Assert.Equal(25.39, bar.Low);
        Assert.Equal(25.3714, bar.DailyVolumeWeightedAveragePrice);
        Assert.Equal(50, bar.AverageTradeSize);
    }

    [Fact]
    public void ThePublishedMinuteAggregateSampleDeserializesThroughTheSameModel()
    {
        StockAggregate bar = ReadAggregate(Fixtures.StockMinuteAggregate, "AM");

        Assert.Equal("GTE", bar.Ticker);
        Assert.Equal(4110, bar.Volume);
        Assert.Equal(685, bar.AverageTradeSize);
    }

    // D5, and the unit the A/AM topics document and send: milliseconds, unlike NOI and LULD, which
    // send nanoseconds for their own timestamp field.
    [Fact]
    public void AggregateWindowBoundsAreMillisecondsExposedAsInstants()
    {
        StockAggregate bar = ReadAggregate(Fixtures.StockSecondAggregate, "A");

        Assert.Equal(1610144868000, bar.StartTimestampMilliseconds);
        Assert.Equal(1610144869000, bar.EndTimestampMilliseconds);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1610144868000), bar.Start);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1610144869000), bar.End);
    }

    // A minute bar spans sixty seconds and a second bar one, which is how a caller tells the two
    // apart from one model (D-W14).
    [Fact]
    public void TheWindowLengthDistinguishesASecondBarFromAMinuteBar()
    {
        StockAggregate second = ReadAggregate(Fixtures.StockSecondAggregate, "A");
        StockAggregate minute = ReadAggregate(Fixtures.StockMinuteAggregate, "AM");

        Assert.Equal(Duration.FromSeconds(1), second.End - second.Start);
        Assert.Equal(Duration.FromMinutes(1), minute.End - minute.Start);
    }

    // The published sample omits all three, which is the documentation's own evidence that they
    // are optional. "otc" is documented as left off when false, so absent reads as false rather
    // than as an unknown.
    [Fact]
    public void AggregateFieldsAbsentFromTheSampleReadAsNullOrFalse()
    {
        StockAggregate bar = ReadAggregate(Fixtures.StockSecondAggregate, "A");

        Assert.Null(bar.DecimalVolume);
        Assert.Null(bar.DecimalAccumulatedVolume);
        Assert.False(bar.Otc);
    }

    // The live capture exists precisely because the published sample cannot exercise these.
    [Fact]
    public void TheLiveAggregateCaptureCarriesTheDecimalVolumesTheSampleOmits()
    {
        StockAggregate bar = ReadAggregate(Fixtures.StockSecondAggregateLive, "A");

        Assert.Equal("FCX", bar.Ticker);
        Assert.Equal("4989.0", bar.DecimalVolume);
        Assert.Equal("4332125.038360", bar.DecimalAccumulatedVolume);
    }

    [Fact]
    public void AnOtcAggregateReadsAsOtc()
    {
        StockAggregate bar = ReadAggregate(
            """[{"ev":"A","sym":"XYZ","v":1,"av":2,"op":1.0,"vw":1.0,"o":1.0,"c":1.0,"h":1.0,"l":1.0,"a":1.0,"z":1,"s":1,"e":2,"otc":true}]""",
            "A");

        Assert.True(bar.Otc);
    }

    [Fact]
    public void AnAggregateWithANonStringSymbolThrowsJsonException()
    {
        JsonException error = Assert.Throws<JsonException>(() =>
            ReadAggregate("""[{"ev":"A","sym":123,"v":1}]""", "A"));

        Assert.Contains("StockAggregate.sym", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAggregateCarryingNoSymbolThrowsJsonException()
    {
        JsonException error = Assert.Throws<JsonException>(() =>
            ReadAggregate("""[{"ev":"A","v":1}]""", "A"));

        Assert.Contains("StockAggregate", error.Message, StringComparison.Ordinal);
    }

    // The topic code the converter was built with is what it writes back, which is the whole reason
    // one model can serve two topics.
    [Theory]
    [InlineData("A")]
    [InlineData("AM")]
    public void AnAggregateRoundTripsThroughWriteAndReadUnderEitherTopicCode(string topicCode)
    {
        StockAggregate original = ReadAggregate(Fixtures.StockSecondAggregateLive, topicCode);

        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            new StockAggregateConverter(new TickerPool(16), topicCode)
                .Write(writer, original, JsonSerializerOptions.Default);
        }

        Assert.Contains(
            $"\"ev\":\"{topicCode}\"",
            Encoding.UTF8.GetString(buffer.WrittenSpan),
            StringComparison.Ordinal);

        Utf8JsonReader reader = new(buffer.WrittenSpan);
        reader.Read();
        StockAggregate roundTripped = new StockAggregateConverter(new TickerPool(16), topicCode)
            .Read(ref reader, typeof(StockAggregate), JsonSerializerOptions.Default);

        Assert.Equal(original, roundTripped);
    }
}
