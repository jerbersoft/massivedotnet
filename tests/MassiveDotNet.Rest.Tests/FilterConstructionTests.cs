using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Filters validate at construction, where the stack trace names the caller, rather than at
/// render time inside a generated method.
/// </summary>
public sealed class FilterConstructionTests
{
    [Fact]
    public void RangeFactoriesRejectNull()
    {
        Assert.Throws<ArgumentNullException>(() => RangeFilter.Gt<string>(null!));
        Assert.Throws<ArgumentNullException>(() => RangeFilter.Between<string>("a", null!));
    }

    [Fact]
    public void EqualityConversionRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => (RangeFilter<string>)(string)null!);
    }

    [Fact]
    public void ALowerBoundCannotBeSetTwice()
    {
        Assert.Throws<InvalidOperationException>(() => RangeFilter.Gt(1L).Gte(2L));
        Assert.Throws<InvalidOperationException>(() => RangeFilter.Gte(1L).Gt(2L));
    }

    [Fact]
    public void AnUpperBoundCannotBeSetTwice()
    {
        Assert.Throws<InvalidOperationException>(() => RangeFilter.Lt(1L).Lte(2L));
        Assert.Throws<InvalidOperationException>(() => RangeFilter.Lte(1L).Lt(2L));
    }

    [Fact]
    public void AnEqualityCannotTakeABound()
    {
        RangeFilter<long> equality = 5L;

        Assert.Throws<InvalidOperationException>(() => equality.Gt(1L));
        Assert.Throws<InvalidOperationException>(() => equality.Lte(9L));
    }

    [Fact]
    public void ChainingTheOtherSideIsAllowedInEitherOrder()
    {
        // Neither call throws: a lower bound may gain an upper bound and vice versa.
        _ = RangeFilter.Gte(1L).Lt(10L);
        _ = RangeFilter.Lt(10L).Gte(1L);
    }

    [Fact]
    public void BetweenDoesNotCheckOrdering()
    {
        // Deliberate: ordering needs a comparison constraint that string cannot honour sensibly,
        // and the server rejects an empty range on its own.
        _ = RangeFilter.Between(2L, 1L);
    }

    [Fact]
    public void SetFactoriesRejectAnEmptySet()
    {
        Assert.Throws<ArgumentException>(() => SetFilter.AnyOf<string>());
        Assert.Throws<ArgumentException>(() => ArrayFilter.AnyOf<string>());
        Assert.Throws<ArgumentException>(() => ArrayFilter.AllOf<string>());
    }

    [Fact]
    public void SetFactoriesRejectANullArray()
    {
        Assert.Throws<ArgumentNullException>(() => SetFilter.AnyOf<string>(null!));
        Assert.Throws<ArgumentNullException>(() => ArrayFilter.AllOf<string>(null!));
    }

    [Fact]
    public void SetFactoriesRejectANullElement()
    {
        Assert.Throws<ArgumentException>(() => SetFilter.AnyOf("AAPL", null!));
        Assert.Throws<ArgumentException>(() => ArrayFilter.AnyOf("AAPL", null!));
    }

    [Fact]
    public void SetAndArrayEqualityConversionsRejectNull()
    {
        Assert.Throws<ArgumentNullException>(() => (SetFilter<string>)(string)null!);
        Assert.Throws<ArgumentNullException>(() => (ArrayFilter<string>)(string)null!);
        Assert.Throws<ArgumentNullException>(() => (Filter<string>)(string)null!);
        Assert.Throws<ArgumentNullException>(() => ArrayFilter.Contains<string>(null!));
    }

    [Fact]
    public void FilterAcceptsARangeASetOrAValueByConversion()
    {
        // Compiles, and none of these throw: Filter<T> is only ever built by conversion.
        Filter<long> fromValue = 5L;
        Filter<long> fromRange = RangeFilter.Between(1L, 9L);
        Filter<long> fromSet = SetFilter.AnyOf(1L, 2L);

        _ = fromValue;
        _ = fromRange;
        _ = fromSet;
    }

    [Fact]
    public void ArrayFilterAcceptsASetByConversion()
    {
        ArrayFilter<string> fromSet = SetFilter.AnyOf("AAPL", "MSFT");
        ArrayFilter<string> fromValue = "AAPL";

        _ = fromSet;
        _ = fromValue;
    }
}
