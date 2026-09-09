using System.Runtime.CompilerServices;
using System.Text;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Allocation ceilings for the paths the SDK's third design priority is about. A regression that
/// slips past every other test — same values, same wire format, more garbage — fails here.
/// </summary>
/// <remarks>
/// <para>
/// Each ceiling carries headroom over the figure measured when it was set, so a runtime patch
/// bump does not fail the build. Zero is asserted only where zero is the actual claim.
/// </para>
/// <para>
/// A ceiling is not a target. Lowering one when a path gets cheaper is part of the change that
/// made it cheaper; raising one is a decision that needs its reason written next to the number.
/// The figures behind them, and the naive baselines they are worth having against, live in
/// <c>docs/performance/</c> and are produced by <c>benchmarks/MassiveDotNet.Benchmarks</c>.
/// </para>
/// </remarks>
// AllocationTests and CursorTraversalTests are pinned to one collection so xunit never runs them
// concurrently. CursorTraversalTests measures GC.GetTotalMemory, which is process-wide, and
// AllocationTests deserializes 50,000 rows next door. That was survivable while those rows were
// garbage by the time the traversal measured; PooledArrayConverter (issue #47) makes the buffers
// behind them live in ArrayPool<T>.Shared, so a forced collection no longer clears them and the
// traversal read another class's pool as its own retention.
[Collection("Process memory")]
public sealed class AllocationTests
{
    /// <summary>
    /// What <see cref="BuildFilteredUri"/> allocated when this ceiling was set, on 2026-09-04.
    /// Named rather than inlined because the ceiling beside it is only meaningful as a margin.
    /// </summary>
    private const long MeasuredFilteredUri = 648;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------------------ group navigation

    /// <summary>
    /// D6 says navigating to a group allocates nothing. Groups are readonly structs over the
    /// shared transport precisely so that two-level access costs a copy of a reference and no
    /// more, and this is the one figure in this file that is exactly zero rather than a ceiling.
    /// </summary>
    [Fact]
    public void NavigatingToAGroupAllocatesNothing()
    {
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(new InlineStubHandler("{}"));

        using (client)
        using (transport)
        {
            long allocated = Allocation.Measure(() =>
            {
                for (int i = 0; i < 1_000; i++)
                {
                    Consume(client.Stocks);
                    Consume(client.Reference);
                }
            });

            Assert.True(
                allocated == 0,
                $"2,000 group navigations allocated {Allocation.Describe(allocated)}. D6 says this "
                    + $"costs nothing: a group is a readonly struct over the shared transport, so "
                    + $"anything here means one of them started boxing or capturing.");
        }
    }

    // ---------------------------------------------------------------------------- URI building

    /// <summary>
    /// The composed string is the only thing a request URI should cost: the builder writes into
    /// caller-supplied stack space and grows into pooled memory, so nothing else survives the call.
    /// </summary>
    [Fact]
    public void BuildingARequestUriCostsLittleMoreThanTheStringItReturns()
    {
        // Measured 200 B on 2026-09-04, which is the returned string and nothing else: 84
        // characters of UTF-16 plus a string's header and length. Anything much above that is a
        // buffer that escaped instead of going back to the pool.
        const long Ceiling = 256;

        long allocated = Allocation.Measure(() => Consume(BuildAggregatesUri()));

        Assert.True(
            allocated <= Ceiling,
            $"Building an aggregates request URI allocated {Allocation.Describe(allocated)}, "
                + $"over its {Allocation.Describe(Ceiling)} ceiling. The returned string is the only "
                + $"allocation this path is allowed; check that the pooled buffer is still returned.");
    }

    /// <summary>
    /// Filters render through the same builder, so no comparator form may cost a string of its
    /// own. This is the shape D15 chose over 1,182 flat parameters, and rendering lives in one
    /// place precisely so it can be measured once rather than in 93 generated files.
    /// </summary>
    /// <remarks>
    /// Six forms across five parameters — <c>.gt</c>, <c>.lte</c>, <c>.any_of</c>, <c>.all_of</c>,
    /// and two equalities. The ceiling is deliberately close to the measured figure: a per-form
    /// intermediate string is worth about 70 bytes, so a ceiling carrying a form's worth of slack
    /// would let through exactly the regression this test exists for. An earlier version rendered
    /// three forms under a 704-byte ceiling and did not fail when comparator rendering was
    /// deliberately changed to concatenate a string per form.
    /// </remarks>
    [Fact]
    public void RenderingComparatorFiltersStaysOnTheBuilder()
    {
        const long Ceiling = MeasuredFilteredUri + 96;

        long allocated = Allocation.Measure(() => Consume(BuildFilteredUri()));

        Assert.True(
            allocated <= Ceiling,
            $"Rendering six comparator forms allocated {Allocation.Describe(allocated)}, over its "
                + $"{Allocation.Describe(Ceiling)} ceiling. Rendering happens on the builder (D15); "
                + $"an intermediate string per form would show up exactly here.");
    }

    // ------------------------------------------------------------------------- deserialization

    /// <summary>
    /// What a caller pays to read a page of bars, measured through the public API over a handler
    /// that answers inline — which is the only honest place to measure it, since the JSON context
    /// is internal and a direct serializer call would skip the transport a consumer cannot skip.
    /// </summary>
    /// <param name="rows">Rows in the response body.</param>
    /// <param name="ceiling">Bytes this may allocate, with headroom over the measured figure.</param>
    /// <remarks>
    /// The ceilings are roughly 20% above what was measured on 2026-09-04, after issue #47 was
    /// fixed in both halves: 90,864 B for 1,000 rows, 882,864 B for 10,000, and 4,402,864 B for
    /// 50,000. Each is the array it returns plus a fixed overhead of about 2,864 B that does not
    /// grow with the row count — the request, the response, and the envelope — so the ratio to the
    /// floor improves with size rather than degrading: 1.03x at 1,000 rows and 1.0007x at 50,000.
    /// Before the fix the same three paths cost 727,040 B, 8,326,496 B, and 38,737,296 B, between
    /// eight and ten times the array. The gap between those two rows is the whole of #47.
    /// </remarks>
    [Theory]
    [InlineData(1_000, 109_000L)]
    [InlineData(10_000, 1_059_000L)]
    [InlineData(50_000, 5_283_000L)]
    public void DeserializingAggregateRowsStaysUnderItsCeiling(int rows, long ceiling)
    {
        InlineStubHandler handler = new(AggregatesBody(rows));
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            long allocated = Allocation.MeasureTask(async () =>
            {
                MassivePage<Agg> page = await client.Stocks.ListAggregatesAsync(
                    "AAPL", 1, AggregateTimespan.Day,
                    new LocalDate(2024, 1, 1), new LocalDate(2024, 3, 1),
                    cancellationToken: Ct);

                Assert.Equal(rows, page.Results.Length);
            });

            long floor = (long)rows * Unsafe.SizeOf<Agg>();

            Assert.True(
                allocated <= ceiling,
                $"Deserializing {rows:N0} aggregate rows allocated {Allocation.Describe(allocated)} "
                    + $"to return a {Allocation.Describe(floor)} array — "
                    + $"{(double)allocated / floor:N1}x its floor — over the "
                    + $"{Allocation.Describe(ceiling)} ceiling.");
        }
    }

    /// <summary>
    /// The same measurement for a page of trades, which is the shape a decimal-typed
    /// <c>decimal_size</c> was decided for (D38).
    /// </summary>
    /// <param name="rows">Rows in the response body.</param>
    /// <param name="ceiling">Bytes this may allocate, with headroom over the measured figure.</param>
    /// <remarks>
    /// <para>
    /// A trades page cannot reach the aggregates floor: <c>id</c> is unique per trade, so every row
    /// allocates one string that nothing can pool away. What it can do is allocate exactly one --
    /// <c>decimal_size</c> was a second until it became a <see cref="decimal"/> that lives in the
    /// row. There was no ceiling on this path at all before then, which is why it is measured with
    /// the change rather than after it.
    /// </para>
    /// <para>
    /// Measured on 2026-09-09: 154,424 B for 1,000 rows, 1,522,432 B for 10,000, and 7,922,432 B
    /// for 50,000. The headroom is 10%, not the 20% the aggregates ceilings above carry, and the
    /// difference is deliberate: the regression these exist to catch is one more string per row,
    /// which on this shape is itself about 20%, so a 20% ceiling could not see it. Confirmed by
    /// putting <c>decimal_size</c> back to <c>string</c> and regenerating: 9,122,432 B at 50,000
    /// rows, and all three go red. That comparison is worth reading in full, because the row itself
    /// gets bigger: <c>Trade</c> grows from 112 to 120 bytes when an 8-byte reference becomes a
    /// 16-byte value, so the array gains 400,000 B and the heap still loses 1,200,000 B along with
    /// 50,000 objects the collector no longer has to track.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(1_000, 170_000L)]
    [InlineData(10_000, 1_675_000L)]
    [InlineData(50_000, 8_715_000L)]
    public void DeserializingTradeRowsStaysUnderItsCeiling(int rows, long ceiling)
    {
        InlineStubHandler handler = new(TradesBody(rows));
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            long allocated = Allocation.MeasureTask(async () =>
            {
                MassivePage<Trade> page = await client.Stocks.ListTradesAsync("AAPL", limit: rows, cancellationToken: Ct);

                Assert.Equal(rows, page.Results.Length);
            });

            long floor = (long)rows * Unsafe.SizeOf<Trade>();

            Assert.True(
                allocated <= ceiling,
                $"Deserializing {rows:N0} trade rows allocated {Allocation.Describe(allocated)} "
                    + $"to return a {Allocation.Describe(floor)} array — "
                    + $"{(double)allocated / floor:N1}x its floor — over the "
                    + $"{Allocation.Describe(ceiling)} ceiling. Only the trade id should allocate "
                    + "per row.");
        }
    }

    // ------------------------------------------------------------------------------- traversal

    /// <summary>
    /// A traversal's cost must be proportional to the pages walked, not to the square of them.
    /// </summary>
    /// <remarks>
    /// <c>CursorTraversalTests.RetainsNoMemoryProportionalToThePagesTraversed</c> asserts that
    /// pages do not accumulate in memory; this asserts that walking them does not accumulate work,
    /// which is not the same claim. Changing the traversal to rebuild an accumulated array on
    /// every page left that test **passing** — the accumulator is released before its measurement
    /// is taken — while this one went from 10,274 to 54,738 bytes per page.
    /// </remarks>
    [Fact]
    public void TraversingPagesAllocatesABoundedAmountPerPage()
    {
        const int Pages = 100;
        const int RowsPerPage = 10;
        // Measured 2,909 B per page on 2026-09-04, of which 880 B is the ten bars themselves. It
        // was 10,274 B before issue #47, and 7,469 B with only the first half of the fix in place.
        const long PerPageCeiling = 3_490L;

        InlineStubHandler handler = new(CursorScript(Pages, RowsPerPage));
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            int seen = 0;

            long allocated = Allocation.MeasureTask(async () =>
            {
                seen = 0;
                await foreach (Agg _ in client.Stocks.EnumerateAggregatesAsync(
                    "AAPL", 1, AggregateTimespan.Day,
                    new LocalDate(2024, 1, 1), new LocalDate(2024, 3, 1),
                    cancellationToken: Ct))
                {
                    seen++;
                }
            });

            Assert.Equal(Pages * RowsPerPage, seen);

            long perPage = allocated / Pages;

            Assert.True(
                perPage <= PerPageCeiling,
                $"Traversing {Pages} pages allocated {Allocation.Describe(allocated)}, "
                    + $"{Allocation.Describe(perPage)} per page, over the "
                    + $"{Allocation.Describe(PerPageCeiling)} ceiling. A per-page figure that climbs "
                    + $"with the page count means the traversal is accumulating rather than streaming.");
        }
    }

    // --------------------------------------------------------------------------------- helpers

    /// <summary>
    /// Swallows a value without boxing it, so the JIT cannot delete the work that produced it.
    /// A static sink field would do the same job but would then be assigned and never read, which
    /// the repository's IDE0052 floor rejects.
    /// </summary>
    /// <typeparam name="T">The value's type, left open so a struct is not boxed on the way in.</typeparam>
    /// <param name="value">The value to discard.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Consume<T>(T value) => _ = value;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(InlineStubHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    /// <summary>Rebuilds what the generated aggregates endpoint builds, which is private.</summary>
    private static string BuildAggregatesUri()
    {
        RequestUriBuilder builder = new(stackalloc char[256]);

        builder.AppendPathLiteral("/v2/aggs/ticker/");
        builder.AppendPathSegment("AAPL");
        builder.AppendPathLiteral("/range/");
        builder.AppendPathSegment(1);
        builder.AppendPathLiteral("/day/2024-01-01/2024-03-01");
        builder.AppendQuery("adjusted", true);
        builder.AppendQuery("sort", "asc");
        builder.AppendQuery("limit", 50_000);

        return builder.ToUriString();
    }

    /// <summary>
    /// Built once, outside every measured region. The set factories take a <c>params</c> array,
    /// and constructing a caller's argument is not what rendering it costs.
    /// </summary>
    private static readonly SetFilter<string>? Tickers = SetFilter.AnyOf("AAPL", "MSFT", "NVDA");

    private static readonly RangeFilter<LocalDate>? ExDividendDates =
        RangeFilter.Gt(new LocalDate(2024, 1, 1)).Lte(new LocalDate(2024, 12, 31));

    private static readonly ArrayFilter<string>? Tags = ArrayFilter.AllOf("earnings", "guidance");

    private static readonly Filter<int>? Frequency = 4;

    private static string BuildFilteredUri()
    {
        RequestUriBuilder builder = new(stackalloc char[256]);

        builder.AppendPathLiteral("/v3/reference/dividends");
        builder.AppendQuery("ticker", Tickers);
        builder.AppendQuery("ex_dividend_date", ExDividendDates);
        builder.AppendQuery("tags", Tags);
        builder.AppendQuery("frequency", Frequency);
        builder.AppendQuery("limit", 1_000);

        return builder.ToUriString();
    }

    /// <summary>A trades envelope carrying <paramref name="rows"/> trades and no cursor.</summary>
    private static string TradesBody(int rows)
    {
        StringBuilder body = new(rows * 160);

        body.Append("""{"status":"OK","request_id":"alloc","results":[""");

        for (int i = 0; i < rows; i++)
        {
            if (i > 0)
            {
                body.Append(',');
            }

            body.Append("{\"id\":\"t").Append(i)
                .Append("\",\"sip_timestamp\":").Append(1517562000016036600L + i)
                .Append(",\"participant_timestamp\":").Append(1517562000015577000L + i)
                .Append(",\"sequence_number\":").Append(1000 + i)
                .Append(",\"price\":170.15,\"size\":2,\"decimal_size\":\"2.0\",\"exchange\":11,\"tape\":3}");
        }

        body.Append("]}");

        return body.ToString();
    }

    /// <summary>An aggregates envelope carrying <paramref name="rows"/> bars and no cursor.</summary>
    private static string AggregatesBody(int rows) => Envelope(rows, offset: 0, nextUrl: null);

    /// <summary>
    /// A cursor chain of <paramref name="pages"/> envelopes, each naming the next on the same
    /// origin as the configured base address, which is what D14 requires before one is followed.
    /// </summary>
    private static string[] CursorScript(int pages, int rowsPerPage)
    {
        string[] script = new string[pages];

        for (int i = 0; i < pages; i++)
        {
            script[i] = Envelope(
                rowsPerPage,
                offset: i * rowsPerPage,
                nextUrl: i == pages - 1
                    ? null
                    : $"https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2024-01-01/2024-03-01?cursor={i + 1}");
        }

        return script;
    }

    private static string Envelope(int rows, int offset, string? nextUrl)
    {
        StringBuilder body = new(rows * 96);

        body.Append("""{"ticker":"AAPL","adjusted":true,"status":"OK","request_id":"alloc","results":[""");

        for (int i = 0; i < rows; i++)
        {
            if (i > 0)
            {
                body.Append(',');
            }

            long timestamp = 1704085200000L + ((offset + i) * 60_000L);

            body.Append("{\"v\":").Append(1_000 + i)
                .Append(",\"vw\":190.5,\"o\":190.1,\"c\":190.9,\"h\":191.2,\"l\":189.8,\"t\":")
                .Append(timestamp)
                .Append(",\"n\":42}");
        }

        body.Append(']');

        if (nextUrl is not null)
        {
            body.Append(",\"next_url\":\"").Append(nextUrl).Append('"');
        }

        body.Append('}');

        return body.ToString();
    }
}
