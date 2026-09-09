using System.Buffers;
using System.Globalization;
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

    private static StockImbalance ReadImbalance(string json)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        reader.Read();  // [
        reader.Read();  // {

        return new StockImbalanceConverter(new TickerPool(16))
            .Read(ref reader, typeof(StockImbalance), JsonSerializerOptions.Default);
    }

    private static StockLimitUpLimitDown ReadLimitUpLimitDown(string json)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        reader.Read();  // [
        reader.Read();  // {

        return new StockLimitUpLimitDownConverter(new TickerPool(16))
            .Read(ref reader, typeof(StockLimitUpLimitDown), JsonSerializerOptions.Default);
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

        Assert.Equal(0.5m, trade.DecimalSize);
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

    // ThePublishedSecondAggregateSampleDeserializes above cannot catch a transposed OHLC binding:
    // the SPCE sample carries 25.39 for all four of Open/High/Low/Close, so any consistent
    // relabeling among them still satisfies that test. The live FCX capture carries four distinct
    // values, so this test pins each one by its literal number.
    [Fact]
    public void TheLiveAggregateCaptureDistinguishesOpenHighLowClose()
    {
        StockAggregate bar = ReadAggregate(Fixtures.StockSecondAggregateLive, "A");

        Assert.Equal(78.1, bar.Open);
        Assert.Equal(78.125, bar.High);
        Assert.Equal(78.07, bar.Low);
        Assert.Equal(78.08, bar.Close);
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
        Assert.Equal(4989.0m, bar.DecimalVolume);
        Assert.Equal(4332125.038360m, bar.DecimalAccumulatedVolume);
    }

    // decimal compares by value, so the assertion above passes whether or not the scale survived.
    // The scale is the reason the binding is decimal rather than double (D38), so it gets an
    // assertion that can actually see it.
    [Fact]
    public void TheDecimalVolumesKeepTheScaleTheServiceWrote()
    {
        StockAggregate bar = ReadAggregate(Fixtures.StockSecondAggregateLive, "A");

        Assert.Equal("4989.0", bar.DecimalVolume?.ToString(CultureInfo.InvariantCulture));
        Assert.Equal("4332125.038360", bar.DecimalAccumulatedVolume?.ToString(CultureInfo.InvariantCulture));
    }

    // The round-trip guard reaches the streaming wire too: an over-precise value is refused rather
    // than silently rounded, which is the hazard the decimal binding was decided against.
    [Fact]
    public void AnAggregateWhoseDecimalVolumeCannotBeHeldExactlyThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => ReadAggregate(
            """[{"ev":"A","sym":"MSFT","v":1,"av":2,"op":1.0,"vw":1.0,"o":1.0,"c":1.0,"h":1.0,"l":1.0,"a":1.0,"z":1,"s":1,"e":2,"dv":"8827.8140001491929689677164477"}]""",
            "A"));
    }

    [Fact]
    public void AnOtcAggregateReadsAsOtc()
    {
        StockAggregate bar = ReadAggregate(
            """[{"ev":"A","sym":"XYZ","v":1,"av":2,"op":1.0,"vw":1.0,"o":1.0,"c":1.0,"h":1.0,"l":1.0,"a":1.0,"z":1,"s":1,"e":2,"otc":true}]""",
            "A");

        Assert.True(bar.Otc);
    }

    // #23's whole-branch review: every other optional field on this branch tolerates an explicit
    // JSON null (dv/dav via walk.String, z/trfi/trft via the NullableInt32/64 accessors), but "otc"
    // read through walk.Boolean, which throws on null instead of treating it the way absence is
    // already treated -- a parse failure over one field the wire is documented to omit, which
    // TopicSink.Write cannot recover from (it terminates every topic on the stream, not just this
    // one). An explicit null carries the same "not OTC" meaning the wire's own omit-when-false
    // convention already gives absence, so it reads as false rather than as a third, unknown state.
    [Fact]
    public void AnExplicitNullOtcReadsAsFalse()
    {
        StockAggregate bar = ReadAggregate(
            """[{"ev":"A","sym":"FCX","v":1,"av":2,"op":1.0,"vw":1.0,"o":1.0,"c":1.0,"h":1.0,"l":1.0,"a":1.0,"z":1,"s":1,"e":2,"otc":null}]""",
            "A");

        Assert.False(bar.Otc);
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

    [Fact]
    public void ThePublishedImbalanceSampleDeserializes()
    {
        StockImbalance imbalance = ReadImbalance(Fixtures.StockImbalance);

        Assert.Equal("NTEST.Q", imbalance.Ticker);
        Assert.Equal("M", imbalance.AuctionType);
        Assert.Equal(44, imbalance.SymbolSequence);
        Assert.Equal(10, imbalance.ExchangeId);
        Assert.Equal(480, imbalance.ImbalanceQuantity);
        Assert.Equal(440, imbalance.PairedQuantity);
        Assert.Equal(25.03, imbalance.BookClearingPrice);
    }

    // This topic sends the ticker as "T", not "sym" -- the same letter that is the trade topic's
    // own wire code. A converter that assumed "sym" would find no ticker and throw on every event.
    [Fact]
    public void TheImbalanceTickerIsReadFromTheCapitalTProperty()
    {
        StockImbalance imbalance = ReadImbalance(Fixtures.StockImbalance);

        Assert.Equal("NTEST.Q", imbalance.Ticker);
    }

    // Nanoseconds, documented and sampled that way -- unlike the aggregate topics, which send
    // milliseconds. Read as milliseconds this instant would land roughly fifty million years out.
    [Fact]
    public void TheImbalanceTimestampIsNanosecondsExposedAsAnInstant()
    {
        StockImbalance imbalance = ReadImbalance(Fixtures.StockImbalance);

        Assert.Equal(1601318039223013600, imbalance.TimestampNanoseconds);
        Assert.Equal(Epoch.FromNanoseconds(1601318039223013600), imbalance.Timestamp);
        Assert.Equal(2020, imbalance.Timestamp.InUtc().Year);
    }

    // D5's shape applied to a wall clock rather than an epoch: the wire's own (hour x 100) + minutes
    // encoding is stored raw and the NodaTime type computed on read (rule 12's vocabulary).
    [Theory]
    [InlineData(930, 9, 30)]
    [InlineData(1600, 16, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(2359, 23, 59)]
    public void TheAuctionTimeCodeIsExposedAsALocalTime(int code, int hour, int minute)
    {
        StockImbalance imbalance = ReadImbalance(
            $$"""[{"ev":"NOI","T":"AAPL","t":1,"at":{{code}},"a":"M","i":1,"x":1,"o":1,"p":1,"b":1.0}]""");

        Assert.Equal(code, imbalance.AuctionTimeCode);
        Assert.Equal(new LocalTime(hour, minute), imbalance.AuctionTime);
    }

    // A computed property must not throw on a value the server chose, so a code that is not a wall
    // clock reads as null and the raw code stays available.
    [Theory]
    [InlineData(2400)]
    [InlineData(999)]
    [InlineData(-1)]
    [InlineData(1275)]
    public void AnAuctionTimeCodeThatIsNotAWallClockReadsAsNull(int code)
    {
        StockImbalance imbalance = ReadImbalance(
            $$"""[{"ev":"NOI","T":"AAPL","t":1,"at":{{code}},"a":"M","i":1,"x":1,"o":1,"p":1,"b":1.0}]""");

        Assert.Null(imbalance.AuctionTime);
        Assert.Equal(code, imbalance.AuctionTimeCode);
    }

    [Fact]
    public void AnImbalanceWithANonStringTickerThrowsJsonException()
    {
        JsonException error = Assert.Throws<JsonException>(() =>
            ReadImbalance("""[{"ev":"NOI","T":123,"t":1}]"""));

        Assert.Contains("StockImbalance.T", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnImbalanceCarryingNoTickerThrowsJsonException()
    {
        JsonException error = Assert.Throws<JsonException>(() =>
            ReadImbalance("""[{"ev":"NOI","t":1}]"""));

        Assert.Contains("StockImbalance", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnImbalanceRoundTripsThroughWriteAndRead()
    {
        StockImbalance original = ReadImbalance(Fixtures.StockImbalance);

        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            new StockImbalanceConverter(new TickerPool(16))
                .Write(writer, original, JsonSerializerOptions.Default);
        }

        Utf8JsonReader reader = new(buffer.WrittenSpan);
        reader.Read();
        StockImbalance roundTripped = new StockImbalanceConverter(new TickerPool(16))
            .Read(ref reader, typeof(StockImbalance), JsonSerializerOptions.Default);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void ThePublishedLimitUpLimitDownSampleDeserializes()
    {
        StockLimitUpLimitDown band = ReadLimitUpLimitDown(Fixtures.StockLimitUpLimitDown);

        Assert.Equal("MSFT", band.Ticker);
        Assert.Equal(492.99, band.HighPrice);
        Assert.Equal(446.04, band.LowPrice);
        Assert.Equal([16], band.Indicators.AsSpan().ToArray());
        Assert.Equal(3, band.Tape);
        Assert.Equal(5925769, band.SequenceNumber);
    }

    // Like NOI and unlike every other stock topic, the ticker arrives as "T".
    [Fact]
    public void TheLimitUpLimitDownTickerIsReadFromTheCapitalTProperty()
    {
        StockLimitUpLimitDown band = ReadLimitUpLimitDown(Fixtures.StockLimitUpLimitDownLive);

        Assert.Equal("ATHR", band.Ticker);
    }

    // D-W15. Massive's documentation says this field is milliseconds; its own sample and the live
    // wire both say nanoseconds. The assertion is on the resulting YEAR rather than on the raw
    // long, because that is what distinguishes the two readings: as milliseconds these values land
    // roughly fifty-six million years out, which no equality check on the stored long would catch.
    [Fact]
    public void TheLimitUpLimitDownTimestampIsNanosecondsNotTheDocumentedMilliseconds()
    {
        StockLimitUpLimitDown published = ReadLimitUpLimitDown(Fixtures.StockLimitUpLimitDown);
        StockLimitUpLimitDown live = ReadLimitUpLimitDown(Fixtures.StockLimitUpLimitDownLive);

        Assert.Equal(1764086430905642800, published.TimestampNanoseconds);
        Assert.Equal(Epoch.FromNanoseconds(1764086430905642800), published.Timestamp);
        Assert.Equal(2025, published.Timestamp.InUtc().Year);
        Assert.Equal(2026, live.Timestamp.InUtc().Year);
    }

    [Fact]
    public void LimitUpLimitDownIndicatorsReadAsAConditionSet()
    {
        StockLimitUpLimitDown band = ReadLimitUpLimitDown(
            """[{"ev":"LULD","T":"AAPL","h":1.0,"l":0.5,"i":[9,10,11],"z":1,"t":1,"q":1}]""");

        Assert.Equal([9, 10, 11], band.Indicators.AsSpan().ToArray());
    }

    [Fact]
    public void ALimitUpLimitDownWithNoIndicatorsReadsAsAnEmptySet()
    {
        StockLimitUpLimitDown band = ReadLimitUpLimitDown(
            """[{"ev":"LULD","T":"AAPL","h":1.0,"l":0.5,"z":1,"t":1,"q":1}]""");

        Assert.Equal(0, band.Indicators.Count);
    }

    [Fact]
    public void ALimitUpLimitDownWithoutATapeReadsAsNull()
    {
        StockLimitUpLimitDown band = ReadLimitUpLimitDown(
            """[{"ev":"LULD","T":"AAPL","h":1.0,"l":0.5,"i":[16],"t":1,"q":1}]""");

        Assert.Null(band.Tape);
    }

    [Fact]
    public void ALimitUpLimitDownWithANonStringTickerThrowsJsonException()
    {
        JsonException error = Assert.Throws<JsonException>(() =>
            ReadLimitUpLimitDown("""[{"ev":"LULD","T":123,"t":1}]"""));

        Assert.Contains("StockLimitUpLimitDown.T", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALimitUpLimitDownCarryingNoTickerThrowsJsonException()
    {
        JsonException error = Assert.Throws<JsonException>(() =>
            ReadLimitUpLimitDown("""[{"ev":"LULD","t":1}]"""));

        Assert.Contains("StockLimitUpLimitDown", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALimitUpLimitDownRoundTripsThroughWriteAndRead()
    {
        StockLimitUpLimitDown original = ReadLimitUpLimitDown(Fixtures.StockLimitUpLimitDownLive);

        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            new StockLimitUpLimitDownConverter(new TickerPool(16))
                .Write(writer, original, JsonSerializerOptions.Default);
        }

        Utf8JsonReader reader = new(buffer.WrittenSpan);
        reader.Read();
        StockLimitUpLimitDown roundTripped = new StockLimitUpLimitDownConverter(new TickerPool(16))
            .Read(ref reader, typeof(StockLimitUpLimitDown), JsonSerializerOptions.Default);

        Assert.Equal(original, roundTripped);
    }
}
