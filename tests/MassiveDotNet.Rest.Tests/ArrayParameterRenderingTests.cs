using MassiveDotNet.Http;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Wire form of a bare array-typed query parameter, such as the snapshot operations'
/// <c>tickers</c>, asserted on the builder directly. Unlike a <see cref="SetFilter{T}"/>, the
/// field itself is the list: it renders as <c>name=a,b</c> with no comparator suffix (D19).
/// </summary>
public sealed class ArrayParameterRenderingTests
{
    private static string Render<T>(T[]? values)
    {
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery("tickers", values);
        return builder.ToUriString();
    }

    [Fact]
    public void ElementsJoinWithALiteralComma()
    {
        Assert.Equal("/x?tickers=AAPL,MSFT", Render<string>(["AAPL", "MSFT"]));
    }

    [Fact]
    public void NullAndEmptyArraysRenderNothing()
    {
        // A null local typed exactly as a generated optional parameter is, so this also proves
        // the emitted call shape `builder.AppendQuery("tickers", tickers)` compiles.
        string[]? tickers = null;

        Assert.Equal("/x", Render(tickers));
        Assert.Equal("/x", Render<string>([]));
    }

    // The three tests below pin behaviour the overload inherits from the filter set path rather
    // than adding any: each passed on first run, and exists so a change to that shared path is
    // caught here, where an array parameter is named, as well as in FilterRenderingTests.

    [Fact]
    public void EachElementIsEscapedSoAnEmbeddedCommaStaysDistinctFromTheSeparator()
    {
        Assert.Equal("/x?tickers=BRK%2FB,A%2CB", Render<string>(["BRK/B", "A,B"]));
    }

    [Fact]
    public void ElementsFollowTheFilterDispatch()
    {
        Assert.Equal("/x?tickers=1,2", Render<long>([1L, 2L]));
    }

    [Fact]
    public void AnUnsupportedElementTypeThrows()
    {
        Assert.Throws<NotSupportedException>(() => Render<decimal>([1m]));
    }

    [Fact]
    public void AnArrayParameterSeparatesFromItsNeighbours()
    {
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery("a", 1);
        builder.AppendQuery<string>("tickers", ["AAPL", "MSFT"]);
        builder.AppendQuery("z", (bool?)true);

        Assert.Equal("/x?a=1&tickers=AAPL,MSFT&z=true", builder.ToUriString());
    }
}
