using System.Buffers;
using System.Text;
using System.Text.Json;
using MassiveDotNet.Serialization;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Driven directly through <see cref="Utf8JsonReader"/> for the same reason as
/// <see cref="LocalDateJsonConverterTests"/>: reflection-based serialization is disabled here, so
/// there is no <c>JsonSerializer.Deserialize&lt;Instant&gt;</c> to call without a context. The
/// end-to-end path through a generated model is covered in <c>ReferenceNewsTests</c>.
/// </summary>
public sealed class InstantJsonConverterTests
{
    private static readonly InstantJsonConverter Converter = new();
    private static readonly JsonSerializerOptions Options = new();
    private static readonly Instant Published = Instant.FromUtc(2024, 6, 24, 18, 33, 53);

    private static Instant Read(string json)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        Assert.True(reader.Read(), "The test JSON should contain one token.");
        return Converter.Read(ref reader, typeof(Instant), Options);
    }

    private static string Write(Instant value)
    {
        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            Converter.Write(writer, value, Options);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    [Fact]
    public void ReadsAZuluTimestamp()
    {
        Assert.Equal(Published, Read("\"2024-06-24T18:33:53Z\""));
    }

    [Fact]
    public void ReadsAZeroOffsetAsZulu()
    {
        Assert.Equal(Published, Read("\"2024-06-24T18:33:53+00:00\""));
    }

    [Fact]
    public void AppliesANegativeOffset()
    {
        Assert.Equal(Instant.FromUtc(2024, 6, 24, 22, 33, 53), Read("\"2024-06-24T18:33:53-04:00\""));
    }

    [Fact]
    public void AppliesAPositiveOffsetWithMinutes()
    {
        Assert.Equal(Instant.FromUtc(2024, 6, 24, 13, 3, 53), Read("\"2024-06-24T18:33:53+05:30\""));
    }

    [Fact]
    public void ReadsOneFractionDigit()
    {
        Assert.Equal(Published + Duration.FromMilliseconds(500), Read("\"2024-06-24T18:33:53.5Z\""));
    }

    [Fact]
    public void ReadsNineFractionDigits()
    {
        Assert.Equal(Published + Duration.FromNanoseconds(123456789), Read("\"2024-06-24T18:33:53.123456789Z\""));
    }

    [Fact]
    public void AcceptsLowercaseDesignators()
    {
        // RFC 3339 permits lowercase t and z; the service emits uppercase.
        Assert.Equal(Published, Read("\"2024-06-24t18:33:53z\""));
    }

    [Fact]
    public void ReadsAnEscapedTimestampThroughTheSlowPath()
    {
        // - is a hyphen. An escaped value forces the copy-and-unescape path, which the
        // unescaped fast path never exercises.
        Assert.Equal(Published, Read("\"2024\\u002D06-24T18:33:53Z\""));
    }

    [Theory]
    [InlineData("\"2024-06-24\"")]
    [InlineData("\"2024-06-24T18:33:53\"")]
    [InlineData("\"2024-06-24 18:33:53Z\"")]
    [InlineData("\"2024-06-24T18:33:53.Z\"")]
    [InlineData("\"2024-06-24T18:33:53.1234567890Z\"")]
    [InlineData("\"2024-06-24T18:33:53+0400\"")]
    [InlineData("\"2024-06-24T18:33:53+19:00\"")]
    [InlineData("\"2024-06-24T18:33:53+05:99\"")]
    [InlineData("\"2024-13-24T18:33:53Z\"")]
    [InlineData("\"2024-06-24T24:00:00Z\"")]
    [InlineData("\"2024-06-24T18:33:53ZZ\"")]
    [InlineData("1719253200000")]
    [InlineData("null")]
    public void RejectsAnythingThatIsNotAnRfc3339Timestamp(string json)
    {
        Assert.Throws<JsonException>(() => Read(json));
    }

    [Fact]
    public void RejectsAnOffsetThatOverflowsTheSupportedInstantRange()
    {
        // The local date/time is itself the maximum NodaTime's calendar supports; subtracting a
        // further 18 hours of offset pushes the instant past what NodaTime's Instant can
        // represent, which surfaces as OverflowException rather than the ArgumentOutOfRangeException
        // an out-of-range local date/time (a bad month, an hour of 24) throws.
        Assert.Throws<JsonException>(() => Read("\"9999-12-31T23:59:59-18:00\""));
    }

    [Fact]
    public void WritesTheExtendedIsoForm()
    {
        Assert.Equal("\"2024-06-24T18:33:53Z\"", Write(Published));
    }

    [Fact]
    public void WritesTheFractionOnlyWhenPresent()
    {
        Assert.Equal("\"2024-06-24T18:33:53.5Z\"", Write(Published + Duration.FromMilliseconds(500)));
    }
}
