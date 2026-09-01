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
}
