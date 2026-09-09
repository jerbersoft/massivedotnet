using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MassiveDotNet.Serialization;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The write half of the decimal-string binding (D38).
/// </summary>
/// <remarks>
/// The fractional-share family arrives as a JSON string and has to leave as one: the property is a
/// <see cref="decimal"/> on the model, but writing it with <c>WriteNumber</c> would emit a shape
/// the service never sends and this SDK's own reader refuses. One implementation, here, rather than
/// the same formatting emitted into every generated converter and hand-written into the streaming
/// ones -- the argument <c>JsonValueReader</c> makes for the read half.
/// </remarks>
public sealed class JsonValueWriterTests
{
    private static string Write(decimal value)
    {
        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            JsonValueWriter.WriteDecimalString(writer, "ds", value);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    [Theory]
    [InlineData("4989.0")]
    [InlineData("4332125.038360")]
    [InlineData("0")]
    [InlineData("-1.5")]
    [InlineData("79228162514264337593543950335")]
    public void WritesADecimalAsTheStringTheWireUses(string literal)
    {
        decimal value = decimal.Parse(literal, CultureInfo.InvariantCulture);

        Assert.Equal($"{{\"ds\":\"{literal}\"}}", Write(value));
    }

    /// <summary>
    /// The pair has to close: whatever the writer emits, the reader must accept and return
    /// unchanged, or a caller who serializes a model cannot deserialize it back.
    /// </summary>
    [Theory]
    [InlineData("4989.0")]
    [InlineData("4332125.038360")]
    [InlineData("-0.000001")]
    [InlineData("79228162514264337593543950335")]
    public void RoundTripsThroughTheReader(string literal)
    {
        decimal value = decimal.Parse(literal, CultureInfo.InvariantCulture);

        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(Write(value)));
        Assert.True(reader.Read(), "start object");
        Assert.True(reader.Read(), "property name");
        Assert.True(reader.Read(), "value");

        decimal read = JsonValueReader.ReadDecimal(ref reader, "Row", "ds");

        Assert.Equal(value, read);
        Assert.Equal(literal, read.ToString(CultureInfo.InvariantCulture));
    }
}
