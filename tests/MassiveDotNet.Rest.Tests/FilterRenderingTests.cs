using MassiveDotNet.Http;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Wire forms of the four filter types, asserted on the builder directly. The builder is public
/// API, so these are not tests of internals; they pin the exact strings every generated endpoint
/// will produce, for every element type in the closed set.
/// </summary>
public sealed class FilterRenderingTests
{
    private static string RenderRange<T>(RangeFilter<T>? filter)
    {
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery("f", filter);
        return builder.ToUriString();
    }

    private static string RenderSet<T>(SetFilter<T>? filter, bool hasExactForm = true)
    {
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery("f", filter, hasExactForm);
        return builder.ToUriString();
    }

    private static string RenderFilter<T>(Filter<T>? filter)
    {
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery("f", filter);
        return builder.ToUriString();
    }

    private static string RenderArray<T>(ArrayFilter<T>? filter)
    {
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery("f", filter);
        return builder.ToUriString();
    }

    [Fact]
    public void EqualityRendersThePlainField()
    {
        Assert.Equal("/x?f=AAPL", RenderRange<string>("AAPL"));
        Assert.Equal("/x?f=AAPL", RenderSet<string>("AAPL"));
        Assert.Equal("/x?f=AAPL", RenderFilter<string>("AAPL"));
        Assert.Equal("/x?f=AAPL", RenderArray<string>("AAPL"));
    }

    [Fact]
    public void EachBoundRendersItsOwnSuffix()
    {
        Assert.Equal("/x?f.gt=5", RenderRange<long>(RangeFilter.Gt(5L)));
        Assert.Equal("/x?f.gte=5", RenderRange<long>(RangeFilter.Gte(5L)));
        Assert.Equal("/x?f.lt=5", RenderRange<long>(RangeFilter.Lt(5L)));
        Assert.Equal("/x?f.lte=5", RenderRange<long>(RangeFilter.Lte(5L)));
    }

    [Fact]
    public void BetweenRendersInclusiveBounds()
    {
        Assert.Equal(
            "/x?f.gte=2026-01-01&f.lte=2026-01-31",
            RenderRange<LocalDate>(RangeFilter.Between(new LocalDate(2026, 1, 1), new LocalDate(2026, 1, 31))));
    }

    [Fact]
    public void BoundsRenderInFixedOrderRegardlessOfChainingOrder()
    {
        Assert.Equal("/x?f.gte=1&f.lt=10", RenderRange<long>(RangeFilter.Gte(1L).Lt(10L)));
        Assert.Equal("/x?f.gte=1&f.lt=10", RenderRange<long>(RangeFilter.Lt(10L).Gte(1L)));
    }

    [Fact]
    public void EveryElementTypeRendersItsWireForm()
    {
        Assert.Equal("/x?f.gt=3", RenderRange<int>(RangeFilter.Gt(3)));
        Assert.Equal("/x?f.gt=3", RenderRange<long>(RangeFilter.Gt(3L)));
        Assert.Equal("/x?f.gt=0.5", RenderRange<double>(RangeFilter.Gt(0.5)));
        Assert.Equal("/x?f.gt=1E-07", RenderRange<double>(RangeFilter.Gt(0.0000001)));
        Assert.Equal("/x?f.gt=2026-01-01", RenderRange<LocalDate>(RangeFilter.Gt(new LocalDate(2026, 1, 1))));
        Assert.Equal("/x?f.gte=1578114000000", RenderRange<DateOrTimestamp>(RangeFilter.Gte<DateOrTimestamp>(1578114000000L)));
        Assert.Equal("/x?f.lte=2020-01-10", RenderRange<DateOrTimestamp>(RangeFilter.Lte<DateOrTimestamp>(new LocalDate(2020, 1, 10))));
    }

    [Fact]
    public void APositiveExponentEscapesThePlusSign()
    {
        // Shortest round-trip formatting emits a raw `+` for a large enough magnitude
        // (`1E+17`), and most servers decode a literal `+` in a query string as a space.
        Assert.Equal("/x?f.gte=1E%2B17", RenderRange<double>(RangeFilter.Gte(1E17)));
    }

    [Fact]
    public void StringElementsArePercentEscaped()
    {
        Assert.Equal("/x?f=BRK%2FB", RenderRange<string>("BRK/B"));
        Assert.Equal("/x?f.gte=a%20b", RenderRange<string>(RangeFilter.Gte("a b")));
    }

    [Fact]
    public void DateOrTimestampLiteralsArePercentEscapedLikeAnyOtherCallerSuppliedValue()
    {
        // DateOrTimestamp.FromLiteral accepts any non-whitespace string and ToString() returns it
        // unchanged, so an unescaped render would let a crafted literal inject a second query
        // parameter. Uri.EscapeDataString is the identity on ordinary dates and timestamps, so
        // this does not change EveryElementTypeRendersItsWireForm's assertions.
        Assert.Equal(
            "/x?f.gte=2026-01-01%26limit%3D50000",
            RenderRange<DateOrTimestamp>(RangeFilter.Gte<DateOrTimestamp>("2026-01-01&limit=50000")));
    }

    [Fact]
    public void AnElementTypeOutsideTheClosedSetThrows()
    {
        Assert.Throws<NotSupportedException>(() => RenderRange<decimal>(RangeFilter.Gt(1m)));
    }

    [Fact]
    public void NullAndUnsetFiltersRenderNothing()
    {
        Assert.Equal("/x", RenderRange<string>(null));
        Assert.Equal("/x", RenderRange<string>(default(RangeFilter<string>)));
        Assert.Equal("/x", RenderSet<string>(null));
        Assert.Equal("/x", RenderSet<string>(default(SetFilter<string>)));
        Assert.Equal("/x", RenderFilter<string>(null));
        Assert.Equal("/x", RenderFilter<string>(default(Filter<string>)));
        Assert.Equal("/x", RenderArray<string>(null));
        Assert.Equal("/x", RenderArray<string>(default(ArrayFilter<string>)));
    }

    [Fact]
    public void NullReferenceConversionsRenderNothing()
    {
        // Exercises the T -> X<T> implicit conversion with a genuine null *reference* assigned
        // to each filter-typed variable -- the same shape as a generated method whose parameter
        // is one of these four types receiving a null `string?` argument. Each operator's
        // parameter is annotated `T?`, so a plain `string?` local needs no null-forgiving
        // operator here: the signature itself now states what the operator does. This is what
        // proves the conversion produces an unset filter rather than throwing:
        // FilterConstructionTests can only assert that the conversion does not throw, since
        // Mode is internal and this assembly has no InternalsVisibleTo grant to read it directly.
        string? ticker = null;

        RangeFilter<string>? rangeFilter = ticker;
        SetFilter<string>? setFilter = ticker;
        Filter<string>? filterFilter = ticker;
        ArrayFilter<string>? arrayFilter = ticker;

        Assert.Equal("/x", RenderRange(rangeFilter));
        Assert.Equal("/x", RenderSet(setFilter));
        Assert.Equal("/x", RenderFilter(filterFilter));
        Assert.Equal("/x", RenderArray(arrayFilter));
    }

    [Fact]
    public void AnyOfJoinsWithALiteralCommaAndEscapesEachElement()
    {
        Assert.Equal("/x?f.any_of=BRK%2FB,AAPL", RenderSet<string>(SetFilter.AnyOf("BRK/B", "AAPL")));
        Assert.Equal("/x?f.any_of=1,2,3", RenderSet<long>(SetFilter.AnyOf(1L, 2L, 3L)));
    }

    [Fact]
    public void SetEqualityWithoutAnExactFormRendersAOneElementSet()
    {
        // The one base-less group in the spec (/v1/summaries ticker.any_of): no plain `ticker`
        // exists, so equality has to travel as a one-element any_of.
        Assert.Equal("/x?f.any_of=AAPL", RenderSet<string>("AAPL", hasExactForm: false));
        Assert.Equal("/x?f.any_of=AAPL,MSFT", RenderSet<string>(SetFilter.AnyOf("AAPL", "MSFT"), hasExactForm: false));
    }

    [Fact]
    public void FilterRendersWhateverItWasBuiltFrom()
    {
        Assert.Equal("/x?f=5", RenderFilter<long>(5L));
        Assert.Equal("/x?f.gte=1&f.lte=9", RenderFilter<long>(RangeFilter.Between(1L, 9L)));
        Assert.Equal("/x?f.any_of=1,2", RenderFilter<long>(SetFilter.AnyOf(1L, 2L)));
    }

    [Fact]
    public void ArrayFilterRendersContainsAnyOfAndAllOf()
    {
        Assert.Equal("/x?f=AAPL", RenderArray<string>(ArrayFilter.Contains("AAPL")));
        Assert.Equal("/x?f.any_of=A,B", RenderArray<string>(ArrayFilter.AnyOf("A", "B")));
        Assert.Equal("/x?f.all_of=A,B", RenderArray<string>(ArrayFilter.AllOf("A", "B")));
        Assert.Equal("/x?f.any_of=A", RenderArray<string>(SetFilter.AnyOf("A")));
    }

    [Fact]
    public void FiltersShareTheSeparatorWithOtherParameters()
    {
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery("a", "1");
        builder.AppendQuery<long>("f", RangeFilter.Between(1L, 2L));
        builder.AppendQuery("z", 3);

        Assert.Equal("/x?a=1&f.gte=1&f.lte=2&z=3", builder.ToUriString());
    }

    [Fact]
    public void PlainDoubleParameterRendersInvariantAndSkipsNull()
    {
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery("d", (double?)0.25);
        builder.AppendQuery("e", (double?)null);

        Assert.Equal("/x?d=0.25", builder.ToUriString());
    }

    [Fact]
    public void PlainDoubleParameterEscapesAPositiveExponentsPlusSign()
    {
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery("d", (double?)1E17);

        Assert.Equal("/x?d=1E%2B17", builder.ToUriString());
    }
}
