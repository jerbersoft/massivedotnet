using System.Buffers;
using System.Text;
using System.Text.Json;
using MassiveDotNet.Serialization;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The converter is driven directly through <see cref="Utf8JsonReader"/> because the test
/// project, like the shipped code, has reflection-based serialization disabled and so cannot call
/// <c>JsonSerializer.Deserialize&lt;LocalDate&gt;</c> without a context of its own. The end-to-end
/// path through a generated model is covered in <c>StocksDividendsTests</c>.
/// </summary>
public sealed class LocalDateJsonConverterTests
{
    private static readonly LocalDateJsonConverter Converter = new();
    private static readonly JsonSerializerOptions Options = new();

    private static LocalDate Read(string json)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        Assert.True(reader.Read(), "The test JSON should contain one token.");
        return Converter.Read(ref reader, typeof(LocalDate), Options);
    }

    [Fact]
    public void ReadsAnIsoDate()
    {
        Assert.Equal(new LocalDate(2025, 8, 11), Read("\"2025-08-11\""));
    }

    [Fact]
    public void ReadsAnEscapedIsoDateThroughTheSlowPath()
    {
        // - is a hyphen. An escaped value forces the copy-and-unescape path, which the
        // unescaped fast path never exercises.
        Assert.Equal(new LocalDate(2025, 8, 11), Read("\"2025\\u002D08-11\""));
    }

    [Theory]
    [InlineData("\"2025-8-11\"")]
    [InlineData("\"20250811\"")]
    [InlineData("\"2025-13-01\"")]
    [InlineData("\"2025-02-30\"")]
    [InlineData("\"abcd-ef-gh\"")]
    [InlineData("\"2025-08-11T00:00:00Z\"")]
    [InlineData("20250811")]
    [InlineData("null")]
    public void RejectsAnythingThatIsNotAnIsoDate(string json)
    {
        Assert.Throws<JsonException>(() => Read(json));
    }

    [Fact]
    public void WritesTheIsoForm()
    {
        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            Converter.Write(writer, new LocalDate(2025, 8, 11), Options);
        }

        Assert.Equal("\"2025-08-11\"", Encoding.UTF8.GetString(buffer.WrittenSpan));
    }
}
