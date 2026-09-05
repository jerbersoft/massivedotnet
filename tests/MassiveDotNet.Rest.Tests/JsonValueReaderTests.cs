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
