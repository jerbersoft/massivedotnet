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
    /// <see cref="GC.GetTotalMemory(bool)"/> is process-wide, not a per-thread allocation delta, so
    /// it carries noise from whatever else lives on the heap -- and under <c>dotnet test</c>
    /// specifically (never the native runner, which measured clean across ten unfiltered runs) that
    /// noise is not small jitter but a repeatable ~1,037,880 B step, observed 2026-09-08 landing on
    /// one side or the other of a single small/large pair often enough to fail roughly one run in
    /// three at 226 tests -- a step this project's own xunit-level isolation
    /// (<c>ProcessMemoryTests.cs</c>, <c>[Collection("Process memory")]</c>) cannot reach, because
    /// it does not come from another xunit collection: it persisted even fully serial. Switching to
    /// <see cref="GC.GetAllocatedBytesForCurrentThread"/>, immune to that step, was tried first and
    /// rejected on its own measured evidence: it reported 4,800 B for 200 events against 638,400 B
    /// for 20,000 -- proportional to events processed, not flat, because every trade's id string is
    /// genuine, unpooled, per-event garbage (D-W10) that this instrument counts as allocated whether
    /// or not the bounded buffer ever retains it. It cannot express a retention claim here.
    /// </para>
    /// <para>
    /// So the probe stays process-wide, and is made noise-robust instead: eight independent
    /// small/large samples, taking <b>the minimum of each side independently</b> --
    /// <c>min(large_i) - min(small_i)</c> -- and subtracting those two minima. This replaced an
    /// earlier version that instead took <c>min(large_i - small_i)</c>, the minimum of each
    /// sample's own difference, found on whole-branch review (#23) to be backwards: the step is
    /// one-sided positive on each raw reading, so noise landing on the SMALL side of a pair
    /// *subtracts* from that pair's difference, making min-of-difference actively prefer the most
    /// contaminated sample rather than the cleanest one. Measured on 2026-09-08, the flawed
    /// estimator selected a delta between -1,350,736 B and -1,338,968 B against the (unmoved)
    /// 256 KB bound -- deeply negative, and nowhere near the ~+100 KB order of magnitude a clean
    /// delta should be. Against that ceiling, a negative baseline is roughly 1.6 MB of headroom
    /// before the delta could climb high enough to fail, next to the ~145 KB of headroom the
    /// earlier single-sample instrument had against the same bound (256 KB against a highest
    /// observed clean reading of 91,912-110,640 B) -- an ~11x loosening, and precisely the
    /// excess-headroom failure D31 names. Taking each minimum independently is correct because the
    /// step still only ever ADDS to a single raw reading, never subtracts: the smallest of eight
    /// small-side readings is genuinely the small-side reading the step happened not to land on,
    /// the smallest of eight large-side readings is the same for the large side, and each is
    /// therefore a lower bound on its own true value, never an underestimate of the other.
    /// Subtracting two independently-clean numbers is what makes the difference clean; subtracting
    /// two numbers chosen to minimize their OWN difference is not the same thing.
    /// </para>
    /// <para>
    /// Re-measured 2026-09-08 with the corrected estimator, across repeated runs: <c>min(large)</c>
    /// and <c>min(small)</c> both land on the same values every time this was run --
    /// 759,768 B and 652,632 B respectively, a delta of 107,136 B -- comfortably under the 256 KB
    /// bound with headroom running the right direction. <b>Corrected on further re-review: this is
    /// NOT, as an earlier version of this remark said, roughly the retention-only figure this test
    /// was originally written against.</b> Both raw minima are large mostly because
    /// <c>TradeFrame</c>'s return value is a live local at every <c>GC.GetTotalMemory</c> call in
    /// this method, and it IS counted: the earlier version nulled <c>frame</c> before measuring, saw
    /// the reading unchanged, and wrongly concluded it was not counted -- that result is exactly
    /// what a live-and-counted array also produces (a null assignment a few instructions earlier
    /// does not make it unreachable under this JIT/config; <c>GC.KeepAlive(frame)</c> gives the same
    /// figure). The variant that actually distinguishes the two hypotheses -- never allocating the
    /// array at all -- reads 121,808 B lower, and an isolated no-retention reproduction of this same
    /// 8-sample estimator (no sink, no channel, just the fixture arrays) reports a delta of
    /// 120,600 B, matching <c>Frame(1,000) - Frame(10)</c> to the byte. So the 107,136 B above is,
    /// to within that gap, almost entirely fixture-array artefact: the 256 KB bound is a bound on
    /// artefact plus retention together, not on retention alone. See
    /// <c>docs/performance/2026-09-07-streaming-allocation-figures.md</c> for the full measurement.
    /// </para>
    /// <para>
    /// One more limitation found on that same re-review: the process heap floor tends to drift
    /// upward within a run, so several of the eight samples routinely tie or nearly tie and the
    /// estimator is effectively decided by far fewer than eight independent draws (in the isolated
    /// no-retention reproduction, <c>min(small)</c> and <c>min(large)</c> both land on sample 0
    /// every run; on this test specifically, <c>large</c> ties across all 8 samples and
    /// <c>small</c> ties across samples 1-7, with sample 0 the one elevated outlier). Still strictly
    /// better than <c>min(large_i - small_i)</c>, which did not merely leave samples unused -- it
    /// actively selected the most contaminated one (see above). Not a reason to reopen the
    /// estimator; recorded so nobody "simplifies" eight samples down to one or two believing that
    /// would be equivalent.
    /// </para>
    /// <para>
    /// <b>Inflating <c>capacity</c> alone does not regress the flawed estimator</b> -- tried first
    /// against <c>min(large_i - small_i)</c>, and rejected on its own measured evidence: with a
    /// huge but still-bounded capacity, later samples in the same run land near-zero, because the
    /// channel's backing storage grows to accommodate one large burst and later bursts of the same
    /// size no longer make it grow further, so the minimum of eight differences hides exactly the
    /// regression it exists to catch. Re-tried after the estimator fix above, as a cheap
    /// confirmation the fix is real rather than merely differently-shaped: with the same inflated,
    /// still-bounded capacity, the corrected estimator now DOES falsify, measuring a delta of
    /// 6,531,416 B (6.23 MB), 25x the 256 KB bound -- min(large) and min(small) are no longer
    /// forced through the same subtraction that let a near-zero late sample mask the regression.
    /// Reverted to <c>Channel.CreateUnbounded&lt;T&gt;()</c> -- the scenario this test was written
    /// against -- the corrected estimator measured a delta of 7,187,416 B (6.85 MB), 27x the bound.
    /// The ceiling itself is unchanged from when this test was first written: the fix is what is
    /// measured, not how much slack it gets.
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

        const int Samples = 8;
        long minSmall = long.MaxValue;
        long minLarge = long.MaxValue;

        for (int sample = 0; sample < Samples; sample++)
        {
            long small = Retained(10);
            long large = Retained(1_000);

            if (small < minSmall)
            {
                minSmall = small;
            }

            if (large < minLarge)
            {
                minLarge = large;
            }
        }

        // min(large) - min(small), NOT min(large - small) -- see this method's own <remarks> for
        // why the two are not interchangeable. The former's minimum is the cleanest of each raw
        // reading, taken independently; the latter's minimum actively prefers whichever pairing
        // happened to have the noise land on the SMALL side, since that is what makes the
        // subtraction smallest -- selecting the most contaminated sample rather than the cleanest.
        long delta = minLarge - minSmall;

        Assert.True(
            delta < 256 * 1024,
            $"Retention grew by at least {Allocation.Describe(delta)} -- min(large) - min(small) "
                + $"across {Samples} independent samples -- between 200 and 20,000 events. A bounded "
                + "buffer that drops the oldest must cost the same either way; host noise only ever "
                + "adds to a single raw reading, so each minimum, taken independently, is the "
                + "cleanest reading of its own side.");
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
