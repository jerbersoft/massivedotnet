using System.Text;
using System.Text.Json;
using MassiveDotNet.WebSocket.Internal;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

public class TickerPoolTests
{
    [Fact]
    public void TheSameTickerInternsToTheSameInstance()
    {
        TickerPool pool = new(capacity: 16);

        string first = pool.Intern("AAPL");
        string second = pool.Intern("AAPL");

        Assert.Equal("AAPL", first);
        Assert.Same(first, second);
    }

    [Fact]
    public void DifferentTickersAreDistinct()
    {
        TickerPool pool = new(capacity: 16);

        Assert.NotSame(pool.Intern("AAPL"), pool.Intern("MSFT"));
        Assert.Equal(2, pool.Count);
    }

    // An uncapped intern table is an unbounded cache wearing a helpful hat. Past the cap the pool
    // degrades to allocating, which is bounded and correct, rather than growing forever.
    [Fact]
    public void PastTheCapItStopsInterningButKeepsReturningCorrectValues()
    {
        TickerPool pool = new(capacity: 2);

        pool.Intern("AAA");
        pool.Intern("BBB");

        string first = pool.Intern("CCC");
        string second = pool.Intern("CCC");

        Assert.Equal("CCC", first);
        Assert.Equal("CCC", second);
        Assert.NotSame(first, second);
        Assert.Equal(2, pool.Count);
    }

    [Fact]
    public void ItInternsStraightOffTheReaderWithoutAProbeString()
    {
        TickerPool pool = new(capacity: 16);
        byte[] json = Encoding.UTF8.GetBytes("""{"sym":"AAPL"}""");

        Utf8JsonReader reader = new(json);
        reader.Read();              // {
        reader.Read();              // "sym"
        reader.Read();              // "AAPL"

        Assert.Equal("AAPL", pool.Intern(ref reader));
        Assert.Same(pool.Intern("AAPL"), pool.Intern("AAPL"));
    }

    // A value longer than the stack buffer must still be correct; it simply does not intern.
    [Fact]
    public void AnOverlongValueFallsBackToAllocating()
    {
        TickerPool pool = new(capacity: 16);
        string overlong = new('A', 64);
        byte[] json = Encoding.UTF8.GetBytes($$"""{"sym":"{{overlong}}"}""");

        Utf8JsonReader reader = new(json);
        reader.Read();
        reader.Read();
        reader.Read();

        Assert.Equal(overlong, pool.Intern(ref reader));
        Assert.Equal(0, pool.Count);
    }

    // Reading ValueSpan raw is the tempting shortcut, and it is wrong: a JSON string carrying an
    // escape sequence comes back with the backslashes still in it, so the ticker is silently
    // corrupted rather than failing. CopyString unescapes. Nothing else in the suite would notice
    // a regression to the raw span, because no other fixture contains an escape.
    [Fact]
    public void AnEscapedValueIsUnescapedRatherThanReadRaw()
    {
        TickerPool pool = new(capacity: 16);
        byte[] json = Encoding.UTF8.GetBytes("""{"sym":"X:BTC\/USD"}""");

        Utf8JsonReader reader = new(json);
        reader.Read();
        reader.Read();
        reader.Read();

        Assert.Equal("X:BTC/USD", pool.Intern(ref reader));
    }
}
