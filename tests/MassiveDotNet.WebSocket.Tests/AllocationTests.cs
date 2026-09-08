using System.Text;
using System.Text.Json;
using MassiveDotNet.Rest.Tests;
using MassiveDotNet.WebSocket.Events;
using MassiveDotNet.WebSocket.Internal;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// Allocation ceilings for the streaming hot path. A regression that slips past every other test —
/// same events, same wire format, more garbage — fails here.
/// </summary>
/// <remarks>
/// Each ceiling carries roughly 20% headroom over the figure measured when it was set, recorded
/// beside it. A ceiling is not a target: lowering one when the path gets cheaper is part of the
/// change that made it cheaper, and raising one needs its reason written next to the number. The
/// exception is a claim of "allocates nothing", asserted with strict equality rather than headroom,
/// because headroom there would defeat the point (D31).
/// </remarks>
[Collection("Process memory")]
public sealed class AllocationTests
{
    /// <summary>
    /// Bytes 1,000 repeat interns of an already-pooled ticker allocated on 2026-09-08. This is the
    /// ticker intern figure specifically — a whole trade parse, which also allocates the trade id
    /// string, is measured separately below. The whole point of <see cref="TickerPool"/> is that a
    /// repeat symbol costs nothing: it is found through the span-keyed alternate lookup, so no
    /// string is allocated to ask the question.
    /// </summary>
    private const long MeasuredRepeatIntern = 0;

    /// <summary>
    /// Bytes reading a three-code condition array — within <see cref="ConditionSet.InlineCapacity"/>
    /// — allocated on 2026-09-08. The codes land in the set's inline buffer, so nothing spills to
    /// the heap.
    /// </summary>
    private const long MeasuredInlineConditions = 0;

    private static byte[] TradeFrame(int count)
    {
        StringBuilder json = new("[");

        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                json.Append(',');
            }

            json.Append(
                $$"""{"ev":"T","sym":"MSFT","x":4,"i":"{{i}}","z":3,"p":114.125,"s":100,"c":[0,12],"t":1536036818784,"pt":1536036818763,"q":{{i}}}""");
        }

        return Encoding.UTF8.GetBytes(json.Append(']').ToString());
    }

    /// <summary>
    /// The claim D-W10 makes: after the ticker is interned and the conditions are inline, a parsed
    /// trade costs nothing on the heap. The trade id is the one field that still allocates, so it
    /// is held constant here and measured separately below.
    /// </summary>
    /// <remarks>
    /// Regressed on 2026-09-08 by replacing the pooled <c>tickers.Intern(ref reader)</c> call with
    /// <c>reader.GetString()</c>: measured allocation doubled from 32 B to 64 B, the extra 32 B
    /// being a fresh, unpooled "MSFT" string. Confirmed to fail under the regression before this
    /// ceiling was committed.
    /// </remarks>
    [Fact]
    public void ParsingATradeAllocatesNothingBeyondItsTradeId()
    {
        // Measured 32 B on 2026-09-08: one string for the trade id, which is unique per trade and
        // therefore unpoolable. The ceiling below carries headroom rounded up to the next size
        // class rather than a flat 20%, since 20% of 32 B is under one allocator granularity step.
        // The GetString() regression above measures 64 B, comfortably over this ceiling.
        const long Ceiling = 40;

        TickerPool pool = new(16);
        StockTradeConverter converter = new(pool);
        byte[] frame = Encoding.UTF8.GetBytes(
            """[{"ev":"T","sym":"MSFT","x":4,"i":"same","z":3,"p":114.125,"s":100,"c":[0,12],"t":1536036818784,"pt":1536036818763,"q":1}]""");

        long allocated = Allocation.Measure(() =>
        {
            Utf8JsonReader reader = new(frame);
            reader.Read();
            reader.Read();
            _ = converter.Read(ref reader, typeof(StockTrade), JsonSerializerOptions.Default);
        });

        // One string for the trade id, which is unique per trade and therefore unpoolable.
        Assert.True(
            allocated <= Ceiling,
            $"Parsing one trade allocated {Allocation.Describe(allocated)}, over its "
                + $"{Allocation.Describe(Ceiling)} ceiling. The ticker is pooled and "
                + "the conditions are inline, so only the trade id should remain.");
    }

    /// <summary>
    /// The ticker pool's whole purpose. Without it this figure grows linearly with event count.
    /// </summary>
    /// <remarks>
    /// Regressed on 2026-09-08 by probing with <c>_pool.TryGetValue(new string(ticker), …)</c>
    /// instead of the span-keyed alternate lookup: 1,000 repeat interns measured 32,000 B, 32 B
    /// per call for the probe string the regression allocates just to ask the dictionary a
    /// question the real code answers for free. Confirmed to fail under the regression before this
    /// ceiling was committed.
    /// </remarks>
    [Fact]
    public void TheTickerCostsNothingAfterTheFirstEvent()
    {
        TickerPool pool = new(16);
        pool.Intern("MSFT");

        long allocated = Allocation.Measure(() =>
        {
            for (int i = 0; i < 1_000; i++)
            {
                _ = pool.Intern("MSFT");
            }
        });

        Assert.True(
            allocated == MeasuredRepeatIntern,
            $"1,000 repeat interns allocated {Allocation.Describe(allocated)}, and the whole point of "
                + "the pool is that a repeat symbol costs nothing.");
    }

    /// <summary>
    /// Conditions within the inline capacity must not touch the heap; this is what an
    /// <c>int[]</c> per trade would have cost.
    /// </summary>
    /// <remarks>
    /// Regressed on 2026-09-08 by making <see cref="ConditionSetSerialization.Read"/> spill to the
    /// heap for every code instead of only past <see cref="ConditionSet.InlineCapacity"/>: a
    /// three-code array, well within that capacity, measured 320 B instead of 0. Confirmed to fail
    /// under the regression before this ceiling was committed.
    /// </remarks>
    [Fact]
    public void ConditionsWithinTheInlineCapacityAllocateNothing()
    {
        byte[] codes = Encoding.UTF8.GetBytes("[0,12,37]");

        long allocated = Allocation.Measure(() =>
        {
            Utf8JsonReader reader = new(codes);
            reader.Read();
            _ = ConditionSetSerialization.Read(ref reader, "StockTrade", "c");
        });

        Assert.True(
            allocated == MeasuredInlineConditions,
            $"An inline condition set allocated {Allocation.Describe(allocated)}.");
    }

    /// <summary>
    /// #20's acceptance criterion, stated so it can fail a build: what a stream holds must not
    /// grow with how many events have passed through it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The buffer is bounded and the oldest event is evicted, so a consumer that never reads still
    /// costs a fixed amount. This is the streaming counterpart of
    /// <c>RetainsNoMemoryProportionalToThePagesTraversed</c>, and like it, it was confirmed to go
    /// red under an unbounded channel before being committed.
    /// </para>
    /// <para>
    /// The 256 KB bound is deliberately loose, the same reasoning
    /// <c>CursorTraversalTests.RetainsNoMemoryProportionalToThePagesTraversed</c> uses for its own
    /// 8 MB bound: <see cref="GC.GetTotalMemory(bool)"/> is process-wide, not a per-thread
    /// allocation delta, so it carries noise from whatever else lives on the heap. Measured on
    /// 2026-09-08, the correct bounded-channel implementation grew by roughly 92,000-111,000 B
    /// between the small and large pass depending on what else was resident; regressed to an
    /// unbounded channel (<c>Channel.CreateUnbounded&lt;T&gt;()</c>), the same test grew by
    /// 7,007,240 B — 26.7x the bound. The gap between "genuine, bounded growth" and "unbounded
    /// accumulation" is wide enough that a loose bound is still a meaningful one.
    /// </para>
    /// </remarks>
    [Fact]
    public void RetainsNoMemoryProportionalToTheEventsReceived()
    {
        TopicSink<StockTrade> sink = new("T", capacity: 8, new StockTradeConverter(new TickerPool(64)));

        long Retained(int events)
        {
            byte[] frame = TradeFrame(events);

            for (int i = 0; i < 20; i++)
            {
                Utf8JsonReader reader = new(frame);
                reader.Read();

                while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
                {
                    sink.Write(ref reader);
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            return GC.GetTotalMemory(forceFullCollection: true);
        }

        long small = Retained(10);
        long large = Retained(1_000);

        Assert.True(
            large - small < 256 * 1024,
            $"Retention grew by {Allocation.Describe(large - small)} between 200 and 20,000 events. "
                + "A bounded buffer that drops the oldest must cost the same either way.");
    }

    /// <summary>
    /// An aggregate bar carrying the wire's two decimal-volume strings. Those two are the only
    /// fields on this model that can allocate: the ticker is pooled, and every other field is a
    /// number or a bool read straight off the tokens.
    /// </summary>
    /// <remarks>
    /// Regressed on 2026-09-08 by replacing the pooled <c>walk.Ticker(…)</c> call with
    /// <c>walk.String(…)</c>: measured allocation rose from 64 B to 96 B, the difference being a
    /// fresh, unpooled "MSFT". Confirmed to fail under the regression before this ceiling was
    /// committed.
    /// </remarks>
    [Fact]
    public void ParsingAnAggregateAllocatesNothingBeyondItsDecimalVolumes()
    {
        // Measured 64 B on 2026-09-08: two strings, "1.0" and "2.0", for dv and dav.
        const long Ceiling = 80;

        TickerPool pool = new(16);
        StockAggregateConverter converter = new(pool, "A");
        byte[] frame = Encoding.UTF8.GetBytes(
            """[{"ev":"A","sym":"MSFT","v":1,"av":2,"op":1.0,"vw":1.0,"o":1.0,"c":1.0,"h":1.0,"l":1.0,"a":1.0,"z":1,"s":1,"e":2,"dv":"1.0","dav":"2.0"}]""");

        long allocated = Allocation.Measure(() =>
        {
            Utf8JsonReader reader = new(frame);
            reader.Read();
            reader.Read();
            _ = converter.Read(ref reader, typeof(StockAggregate), JsonSerializerOptions.Default);
        });

        Assert.True(
            allocated <= Ceiling,
            $"Parsing one aggregate allocated {Allocation.Describe(allocated)}, over its "
                + $"{Allocation.Describe(Ceiling)} ceiling. The ticker is pooled, so only the two "
                + "decimal-volume strings should remain.");
    }

    /// <summary>
    /// The same bar without <c>dv</c> and <c>dav</c>, which is the shape the published sample and
    /// most live frames carry. Nothing on it can allocate at all.
    /// </summary>
    /// <remarks>
    /// Regressed on 2026-09-08 by the same unpooled-ticker change as above, which takes this path
    /// from 0 B to 32 B. Confirmed to fail under the regression before this was committed.
    /// </remarks>
    [Fact]
    public void ParsingAnAggregateWithoutDecimalVolumesAllocatesNothing()
    {
        TickerPool pool = new(16);
        StockAggregateConverter converter = new(pool, "A");
        byte[] frame = Encoding.UTF8.GetBytes(
            """[{"ev":"A","sym":"MSFT","v":1,"av":2,"op":1.0,"vw":1.0,"o":1.0,"c":1.0,"h":1.0,"l":1.0,"a":1.0,"z":1,"s":1,"e":2}]""");

        Utf8JsonReader warm = new(frame);
        warm.Read();
        warm.Read();
        _ = converter.Read(ref warm, typeof(StockAggregate), JsonSerializerOptions.Default);

        long allocated = Allocation.Measure(() =>
        {
            Utf8JsonReader reader = new(frame);
            reader.Read();
            reader.Read();
            _ = converter.Read(ref reader, typeof(StockAggregate), JsonSerializerOptions.Default);
        });

        // Strict equality, not a ceiling: "allocates nothing" is the claim, and headroom on a zero
        // would defeat the point (D31).
        Assert.True(
            allocated == 0,
            $"Parsing one aggregate without decimal volumes allocated {Allocation.Describe(allocated)}, "
                + "and every field on that shape is a pooled ticker or a number.");
    }

    /// <summary>
    /// A limit up-limit down band. Its ticker is pooled and its indicators fit inline, so nothing
    /// on the hot path touches the heap — the strongest claim of the three new topics.
    /// </summary>
    /// <remarks>
    /// Regressed on 2026-09-08 by forcing <c>ConditionSetSerialization.Read</c> to spill instead of
    /// using the inline buffer: this path went from 0 B to 144 B, a <c>List&lt;int&gt;</c>
    /// and its backing array. Confirmed to fail under the regression before this was committed.
    /// </remarks>
    [Fact]
    public void ParsingALimitUpLimitDownBandAllocatesNothing()
    {
        TickerPool pool = new(16);
        StockLimitUpLimitDownConverter converter = new(pool);
        byte[] frame = Encoding.UTF8.GetBytes(
            """[{"ev":"LULD","T":"MSFT","h":1.0,"l":0.5,"i":[16],"z":3,"t":1,"q":1}]""");

        Utf8JsonReader warm = new(frame);
        warm.Read();
        warm.Read();
        _ = converter.Read(ref warm, typeof(StockLimitUpLimitDown), JsonSerializerOptions.Default);

        long allocated = Allocation.Measure(() =>
        {
            Utf8JsonReader reader = new(frame);
            reader.Read();
            reader.Read();
            _ = converter.Read(ref reader, typeof(StockLimitUpLimitDown), JsonSerializerOptions.Default);
        });

        Assert.True(
            allocated == 0,
            $"Parsing one limit up-limit down band allocated {Allocation.Describe(allocated)}, and its "
                + "ticker is pooled while its indicators fit inline.");
    }

    /// <summary>
    /// An imbalance. Its one-character auction type is the only field that can allocate — the
    /// ticker is pooled and everything else is a number.
    /// </summary>
    /// <remarks>
    /// Regressed on 2026-09-08 by replacing the pooled <c>walk.Ticker(…)</c> call with
    /// <c>walk.String(…)</c>: measured allocation rose from 24 B to 56 B.
    /// Confirmed to fail under the regression before this ceiling was committed.
    /// </remarks>
    [Fact]
    public void ParsingAnImbalanceAllocatesNothingBeyondItsAuctionType()
    {
        // Measured 24 B on 2026-09-08: one string, "M", for the auction type.
        const long Ceiling = 32;

        TickerPool pool = new(16);
        StockImbalanceConverter converter = new(pool);
        byte[] frame = Encoding.UTF8.GetBytes(
            """[{"ev":"NOI","T":"MSFT","t":1,"at":930,"a":"M","i":1,"x":10,"o":480,"p":440,"b":25.03}]""");

        Utf8JsonReader warm = new(frame);
        warm.Read();
        warm.Read();
        _ = converter.Read(ref warm, typeof(StockImbalance), JsonSerializerOptions.Default);

        long allocated = Allocation.Measure(() =>
        {
            Utf8JsonReader reader = new(frame);
            reader.Read();
            reader.Read();
            _ = converter.Read(ref reader, typeof(StockImbalance), JsonSerializerOptions.Default);
        });

        Assert.True(
            allocated <= Ceiling,
            $"Parsing one imbalance allocated {Allocation.Describe(allocated)}, over its "
                + $"{Allocation.Describe(Ceiling)} ceiling. The ticker is pooled, so only the "
                + "auction-type string should remain.");
    }
}
