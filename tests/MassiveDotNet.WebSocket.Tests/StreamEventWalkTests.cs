using System.Text;
using System.Text.Json;
using MassiveDotNet.WebSocket.Internal;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// The shared property walk, tested once rather than once per converter. Issue #21's whole
/// sequencing argument is that this shape gets extracted and proven before twenty-four converters
/// copy it, so these are the tests that stand in for all of them.
/// </summary>
public class StreamEventWalkTests
{
    // Reads the two known long properties out of one object, ignoring everything else. Whatever
    // sits between "a" and "b" is what each test varies: the walk must leave the reader positioned
    // so that "b" is still found, whatever shape the unrecognised property took.
    //
    // Wrapped in a one-element array with a trailing sentinel, matching how the wire actually
    // delivers events -- one JSON array of objects, walked by System.Text.Json's own array reader,
    // which resumes from wherever this converter leaves the reader. A bare top-level object has
    // nothing after its own EndObject: Utf8JsonReader.Read() called past it just returns false
    // without moving the token (checked against the real reader), so the position assertion below
    // could never fail against unwrapped input -- exactly the "guard nobody has seen fail" D31
    // refuses to ship. The sentinel gives an over-read somewhere real to land.
    private static (long A, long B) ReadPair(string json)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes($"[{json},0]"));
        reader.Read(); // StartArray
        reader.Read(); // the probed object's StartObject

        StreamEventWalk walk = new(ref reader, "Probe");
        long a = 0;
        long b = 0;

        while (walk.NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("a"u8))
            {
                a = walk.Int64(ref reader, "a");
            }
            else if (reader.ValueTextEquals("b"u8))
            {
                b = walk.Int64(ref reader, "b");
            }
        }

        // A JsonConverter.Read must leave the reader parked on its own EndObject: the wire delivers
        // a multi-event array, and the enclosing array reader resumes from wherever this walk
        // stops. One token short or one token past would silently misalign every event after it --
        // the same class of corruption a missed Skip() causes, one level up.
        Assert.Equal(JsonTokenType.EndObject, reader.TokenType);

        return (a, b);
    }

    // A missed Skip() does not throw. It leaves the reader mid-value, so the NEXT property is read
    // from the wrong token -- which deserializes wrong rather than failing. That is why every case
    // below asserts the value of a property that comes AFTER the unrecognised one, and why "does
    // not throw" would be a worthless assertion here.
    [Theory]
    [InlineData("""{"a":1,"unknown":42,"b":2}""")]
    [InlineData("""{"a":1,"unknown":"text","b":2}""")]
    [InlineData("""{"a":1,"unknown":null,"b":2}""")]
    [InlineData("""{"a":1,"unknown":true,"b":2}""")]
    [InlineData("""{"a":1,"unknown":{"x":1},"b":2}""")]
    [InlineData("""{"a":1,"unknown":[1,2,3],"b":2}""")]
    [InlineData("""{"a":1,"unknown":{"x":{"y":[1,{"z":2}]}},"b":2}""")]
    [InlineData("""{"a":1,"unknown":[[1,[2]],{"x":[3]}],"b":2}""")]
    public void APropertyTheConverterDoesNotConsumeIsSkippedWholesale(string json)
    {
        (long a, long b) = ReadPair(json);

        Assert.Equal(1, a);
        Assert.Equal(2, b);
    }

    [Fact]
    public void SeveralUnrecognisedPropertiesInARowAreEachSkipped()
    {
        (long a, long b) = ReadPair("""{"a":1,"p":{"q":1},"r":[1],"s":null,"t":"x","b":2}""");

        Assert.Equal(1, a);
        Assert.Equal(2, b);
    }

    [Fact]
    public void AnUnrecognisedPropertyBeforeEveryKnownOneIsSkipped()
    {
        (long a, long b) = ReadPair("""{"lead":{"deep":[1,2]},"a":1,"b":2}""");

        Assert.Equal(1, a);
        Assert.Equal(2, b);
    }

    [Fact]
    public void AnUnrecognisedTrailingPropertyEndsTheWalkCleanly()
    {
        (long a, long b) = ReadPair("""{"a":1,"b":2,"trailing":{"x":[1]}}""");

        Assert.Equal(1, a);
        Assert.Equal(2, b);
    }

    [Fact]
    public void AnObjectWithNoRecognisedPropertiesAtAllWalksToTheEnd()
    {
        (long a, long b) = ReadPair("""{"p":1,"q":{"r":[1,2]},"s":"x"}""");

        Assert.Equal(0, a);
        Assert.Equal(0, b);
    }

    [Fact]
    public void LastValueWinsWhenAPropertyRepeats()
    {
        (long a, long _) = ReadPair("""{"a":1,"a":7}""");

        Assert.Equal(7, a);
    }

    [Fact]
    public void MatchingIsOrdinalAndCaseSensitive()
    {
        (long a, long b) = ReadPair("""{"A":9,"a":1,"B":9,"b":2}""");

        Assert.Equal(1, a);
        Assert.Equal(2, b);
    }

    [Fact]
    public void AnEmptyObjectYieldsNoProperties()
    {
        (long a, long b) = ReadPair("{}");

        Assert.Equal(0, a);
        Assert.Equal(0, b);
    }

    // Every accessor delegates to JsonValueReader, so a malformed value is a JsonException naming
    // the model and property -- never Utf8JsonReader's own InvalidOperationException, which the
    // transport does not recognise as a malformed body.
    [Fact]
    public void AMalformedValueThrowsJsonExceptionNamingTheModelAndProperty()
    {
        JsonException error = Assert.Throws<JsonException>(() => ReadPair("""{"a":"not a number"}"""));

        // "a" alone is satisfied by the "a" in "Expected a number" or "a String token" -- neither
        // of which names the property -- so a regression that dropped the property name entirely
        // would still pass. Only the qualified "Probe.a" actually pins the name into the message.
        Assert.Contains("Probe.a", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWalkOverSomethingThatIsNotAnObjectThrows()
    {
        JsonException error = Assert.Throws<JsonException>(() =>
        {
            Utf8JsonReader reader = new("[1,2]"u8);
            reader.Read();

            // The constructor is what rejects a non-object reader -- there is no walk left to
            // drive once construction has thrown, so NextProperty is never called here.
            _ = new StreamEventWalk(ref reader, "Probe");
        });

        Assert.Contains("Probe", error.Message, StringComparison.Ordinal);
    }
}
