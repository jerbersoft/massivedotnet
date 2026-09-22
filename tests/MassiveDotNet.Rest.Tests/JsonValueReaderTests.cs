using System.Globalization;
using System.Text;
using System.Text.Json;
using MassiveDotNet.Serialization;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The scalar readers the generated model converters are built from.
/// </summary>
/// <remarks>
/// <para>
/// These exist because <see cref="Utf8JsonReader"/>'s own accessors are the wrong shape for a
/// converter. <c>GetInt32</c> on a string token throws <see cref="InvalidOperationException"/> and
/// on an out-of-range number throws <see cref="FormatException"/> -- neither of which is a
/// <see cref="JsonException"/>, so neither is caught by the transport's malformed-body handler and
/// both would escape a REST call as something no caller could reasonably be asked to catch.
/// </para>
/// <para>
/// One implementation, tested here once, rather than the same four-line token check emitted into
/// seventeen generated converters -- the argument D15 makes for keeping filter rendering in
/// <c>RequestUriBuilder</c>.
/// </para>
/// </remarks>
public sealed class JsonValueReaderTests
{
    private const string Model = "Row";
    private const string Property = "field";

    /// <summary>Positions a reader on the single value in <paramref name="json"/>.</summary>
    private static Utf8JsonReader At(string json)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        Assert.True(reader.Read(), "The test JSON should contain a token.");
        return reader;
    }

    /// <summary>
    /// A reader call under test. Needed because <see cref="Utf8JsonReader"/> is a
    /// <see langword="ref"/> struct, so it cannot be captured by the lambda
    /// <see cref="Assert.Throws{T}(Action)"/> would otherwise take.
    /// </summary>
    private delegate void ReaderCall(ref Utf8JsonReader reader);

    private static JsonException Rejects(string json, ReaderCall read)
    {
        Utf8JsonReader reader = At(json);

        try
        {
            read(ref reader);
        }
        catch (JsonException expected)
        {
            return expected;
        }

        Assert.Fail($"Reading {json} should have thrown a JsonException, but it succeeded.");
        return null!;
    }

    [Theory]
    [InlineData("\"hello\"", "hello")]
    [InlineData("\"\"", "")]
    [InlineData("\"a\\u0062c\"", "abc")]
    [InlineData("null", null)]
    public void ReadsAString(string json, string? expected)
    {
        Utf8JsonReader reader = At(json);
        Assert.Equal(expected, JsonValueReader.ReadString(ref reader, Model, Property));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void RejectsANonStringWhereAStringIsExpected(string json) =>
        Rejects(json, (ref Utf8JsonReader reader) => JsonValueReader.ReadString(ref reader, Model, Property));

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void ReadsABoolean(string json, bool expected)
    {
        Utf8JsonReader reader = At(json);
        Assert.Equal(expected, JsonValueReader.ReadBoolean(ref reader, Model, Property));
    }

    [Fact]
    public void ReadsANullBooleanAsNull()
    {
        Utf8JsonReader reader = At("null");
        Assert.Null(JsonValueReader.ReadNullableBoolean(ref reader, Model, Property));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"true\"")]
    [InlineData("1")]
    public void RejectsANonBooleanWhereABooleanIsExpected(string json) =>
        Rejects(json, (ref Utf8JsonReader reader) => JsonValueReader.ReadBoolean(ref reader, Model, Property));

    [Theory]
    [InlineData("0", 0)]
    [InlineData("-7", -7)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("-2147483648", int.MinValue)]
    public void ReadsAnInt32(string json, int expected)
    {
        Utf8JsonReader reader = At(json);
        Assert.Equal(expected, JsonValueReader.ReadInt32(ref reader, Model, Property));
    }

    /// <summary>
    /// Out of range and fractional are the two failures <c>GetInt32</c> reports as a
    /// <see cref="FormatException"/>, which would otherwise escape the SDK unwrapped.
    /// </summary>
    [Theory]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("1.5")]
    [InlineData("1e3")]
    [InlineData("null")]
    [InlineData("\"3\"")]
    public void RejectsAValueThatIsNotAnInt32(string json) =>
        Rejects(json, (ref Utf8JsonReader reader) => JsonValueReader.ReadInt32(ref reader, Model, Property));

    [Theory]
    [InlineData("null", null)]
    [InlineData("3", 3)]
    public void ReadsANullableInt32(string json, int? expected)
    {
        Utf8JsonReader reader = At(json);
        Assert.Equal(expected, JsonValueReader.ReadNullableInt32(ref reader, Model, Property));
    }

    [Theory]
    [InlineData("0", 0L)]
    [InlineData("1517562000016036600", 1517562000016036600L)]
    [InlineData("-9223372036854775808", long.MinValue)]
    public void ReadsAnInt64(string json, long expected)
    {
        Utf8JsonReader reader = At(json);
        Assert.Equal(expected, JsonValueReader.ReadInt64(ref reader, Model, Property));
    }

    /// <summary>
    /// A nanosecond timestamp one past <see cref="long.MaxValue"/> must fail rather than silently
    /// becoming a double, which is the failure mode that would land a wrong instant in a caller's
    /// hands with nothing to notice it (D5 stores these raw).
    /// </summary>
    [Theory]
    [InlineData("9223372036854775808")]
    [InlineData("1.5")]
    [InlineData("null")]
    [InlineData("\"5\"")]
    public void RejectsAValueThatIsNotAnInt64(string json) =>
        Rejects(json, (ref Utf8JsonReader reader) => JsonValueReader.ReadInt64(ref reader, Model, Property));

    [Theory]
    [InlineData("null", null)]
    [InlineData("5", 5L)]
    public void ReadsANullableInt64(string json, long? expected)
    {
        Utf8JsonReader reader = At(json);
        Assert.Equal(expected, JsonValueReader.ReadNullableInt64(ref reader, Model, Property));
    }

    // Issue #65. A production log said only "The number in StockAggregate.z does not fit a 64-bit
    // integer". Utf8JsonReader.TryGetInt64 returns false for three unrelated reasons and that
    // message distinguishes none of them: a non-integral value (the vendor violating its own
    // schema), an integral value written in a non-integer JSON FORM -- 12.0, 1e3, both valid JSON
    // numbers, both whole, both refused because the token is not an integer token -- and a value
    // genuinely past long's range. Three causes, three different fixes, and the value itself is the
    // only thing that tells them apart (D-W21).
    [Theory]
    [InlineData("12.0")]
    [InlineData("1e3")]
    [InlineData("123.45")]
    [InlineData("9223372036854775808")]
    public void AnOutOfRangeInt64NamesTheValueItRefused(string json)
    {
        JsonException thrown = Rejects(
            json, (ref Utf8JsonReader reader) => JsonValueReader.ReadInt64(ref reader, Model, Property));

        Assert.Contains(json, thrown.Message, StringComparison.Ordinal);
        Assert.Contains(Model, thrown.Message, StringComparison.Ordinal);
        Assert.Contains(Property, thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOutOfRangeInt32NamesTheValueItRefused()
    {
        JsonException thrown = Rejects(
            "2147483648", (ref Utf8JsonReader reader) => JsonValueReader.ReadInt32(ref reader, Model, Property));

        Assert.Contains("2147483648", thrown.Message, StringComparison.Ordinal);
    }

    // A JSON number token has no length limit, so an unbounded echo would put a value of any size
    // into an exception message -- and from there, through the DI package's log bridge, into a log
    // line. Capped at 32 bytes with a trailing ellipsis.
    [Fact]
    public void ALongNumberIsEchoedOnlyUpToTheCap()
    {
        JsonException thrown = Rejects(
            new string('9', 120),
            (ref Utf8JsonReader reader) => JsonValueReader.ReadInt64(ref reader, Model, Property));

        Assert.Contains(new string('9', 32) + "...", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('9', 33), thrown.Message, StringComparison.Ordinal);
    }

    // The other two failure messages deliberately do NOT echo, for two different reasons, and both
    // are rule 11's argument rather than an oversight. The token-type failure names a token TYPE
    // and has no value in hand at all. The decimal round-trip failure reads a String token, where
    // "the bytes are provably a JSON number" does not hold and the class of value it could quote
    // back is unbounded -- which is exactly the class an API key belongs to (D-W21).
    [Fact]
    public void AWrongTokenTypeDoesNotEchoTheValue()
    {
        JsonException thrown = Rejects(
            "\"secret-value\"",
            (ref Utf8JsonReader reader) => JsonValueReader.ReadInt64(ref reader, Model, Property));

        Assert.DoesNotContain("secret-value", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedDecimalDoesNotEchoTheValue()
    {
        JsonException thrown = Rejects(
            "\"1e5\"",
            (ref Utf8JsonReader reader) => JsonValueReader.ReadDecimal(ref reader, Model, Property));

        Assert.DoesNotContain("1e5", thrown.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("170.15", 170.15)]
    [InlineData("2", 2.0)]
    [InlineData("-0.5", -0.5)]
    [InlineData("1e3", 1000.0)]
    public void ReadsADouble(string json, double expected)
    {
        Utf8JsonReader reader = At(json);
        Assert.Equal(expected, JsonValueReader.ReadDouble(ref reader, Model, Property));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"170.15\"")]
    [InlineData("true")]
    public void RejectsAValueThatIsNotADouble(string json) =>
        Rejects(json, (ref Utf8JsonReader reader) => JsonValueReader.ReadDouble(ref reader, Model, Property));

    [Theory]
    [InlineData("null", null)]
    [InlineData("1.25", 1.25)]
    public void ReadsANullableDouble(string json, double? expected)
    {
        Utf8JsonReader reader = At(json);
        Assert.Equal(expected, JsonValueReader.ReadNullableDouble(ref reader, Model, Property));
    }

    /// <summary>
    /// The wire sends this family as a string, so the scale it wrote is part of the value and has
    /// to survive: <c>decimal</c> carries scale, which is the property that makes the binding
    /// possible at all (D38).
    /// </summary>
    [Theory]
    [InlineData("\"4989.0\"", "4989.0")]
    [InlineData("\"4332125.038360\"", "4332125.038360")]
    [InlineData("\"0\"", "0")]
    [InlineData("\"-1.5\"", "-1.5")]
    [InlineData("\"79228162514264337593543950335\"", "79228162514264337593543950335")]
    [InlineData("\"12345678901234567890123456789\"", "12345678901234567890123456789")]
    public void ReadsADecimalStringKeepingTheScaleTheWireWrote(string json, string expected)
    {
        Utf8JsonReader reader = At(json);
        decimal value = JsonValueReader.ReadDecimal(ref reader, Model, Property);

        Assert.Equal(expected, value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The hazard the binding was decided against, and the reason the reader round-trips rather
    /// than counts digits: <c>decimal.TryParse</c> drops a digit and returns <see langword="true"/>
    /// on the first two of these, so nothing downstream could notice the number changed.
    /// </summary>
    [Theory]
    [InlineData("\"8827.8140001491929689677164477\"")]
    [InlineData("\"1.2345678901234567890123456789012\"")]
    [InlineData("\"79228162514264337593543950336\"")]
    public void RefusesADecimalItCannotCarryExactly(string json) =>
        Rejects(json, (ref Utf8JsonReader reader) => JsonValueReader.ReadDecimal(ref reader, Model, Property));

    /// <summary>
    /// The round-trip guard enforces canonical spelling as a side effect, which is a stricter
    /// contract than "never rounds" and is accepted deliberately (D38): Massive's wire is canonical,
    /// and a refusal is visible where a silent normalisation is not.
    /// </summary>
    [Theory]
    [InlineData("\".5\"")]
    [InlineData("\"+4989.0\"")]
    [InlineData("\"004989.0\"")]
    [InlineData("\"4989.\"")]
    [InlineData("\"1e5\"")]
    [InlineData("\" 4989.0\"")]
    [InlineData("\"1,000.5\"")]
    [InlineData("\"\"")]
    [InlineData("\"nope\"")]
    public void RefusesANonCanonicalDecimalString(string json) =>
        Rejects(json, (ref Utf8JsonReader reader) => JsonValueReader.ReadDecimal(ref reader, Model, Property));

    /// <summary>
    /// A JSON number is refused exactly as it was when this family bound to <c>string</c>: the
    /// description declares these fields strings, and reading a number here would accept a form the
    /// service does not send.
    /// </summary>
    [Theory]
    [InlineData("4989.0")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[]")]
    public void RejectsAValueThatIsNotADecimalString(string json) =>
        Rejects(json, (ref Utf8JsonReader reader) => JsonValueReader.ReadDecimal(ref reader, Model, Property));

    [Theory]
    [InlineData("null", null)]
    [InlineData("\"4989.0\"", "4989.0")]
    public void ReadsANullableDecimal(string json, string? expected)
    {
        Utf8JsonReader reader = At(json);
        decimal? value = JsonValueReader.ReadNullableDecimal(ref reader, Model, Property);

        Assert.Equal(expected, value?.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Escaping is a transport detail, not part of the value, so the copying path decodes first and
    /// judges the decoded text -- accepting a canonical value however it was spelled on the wire,
    /// and refusing a non-canonical one that escaping might otherwise have hidden.
    /// </summary>
    [Theory]
    [InlineData("\"\\u0034989.0\"", "4989.0")]
    [InlineData("\"4989.\\u0030\"", "4989.0")]
    public void ReadsADecimalThroughTheCopyingPath(string json, string expected)
    {
        Utf8JsonReader reader = At(json);

        Assert.Equal(expected, JsonValueReader.ReadDecimal(ref reader, Model, Property).ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void RefusesANonCanonicalDecimalThroughTheCopyingPath() =>
        Rejects("\"\\u002B4989.0\"", (ref Utf8JsonReader reader) => JsonValueReader.ReadDecimal(ref reader, Model, Property));

    /// <summary>
    /// A refused decimal names its model and property like every other failure here. The value
    /// itself is not echoed: the response body is not ours to quote back, and the two names are
    /// enough to find it.
    /// </summary>
    [Fact]
    public void NamesTheModelAndPropertyWhenADecimalIsRefused()
    {
        JsonException thrown = Rejects(
            "\"1e5\"",
            (ref Utf8JsonReader reader) => JsonValueReader.ReadDecimal(ref reader, "Trade", "decimal_size"));

        Assert.Contains("Trade", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("decimal_size", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("1e5", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The message names the model and the property, because a failure inside a fifty-thousand row
    /// page is otherwise indistinguishable from any other row's.
    /// </summary>
    [Fact]
    public void NamesTheModelAndPropertyInTheMessage()
    {
        JsonException thrown = Rejects("\"nope\"", (ref Utf8JsonReader reader) => JsonValueReader.ReadDouble(ref reader, "Trade", "price"));

        Assert.Contains("Trade", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("price", thrown.Message, StringComparison.Ordinal);
    }
}
