using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;
using NodaTime.Text;

namespace MassiveDotNet.Serialization;

/// <summary>
/// Reads and writes a calendar date in the <c>YYYY-MM-DD</c> form the Massive API uses.
/// </summary>
/// <remarks>
/// <para>
/// Parses straight from the reader's UTF-8 bytes: no intermediate string, and none of the
/// <c>ParseResult</c> allocation NodaTime's pattern API incurs per value. A reference model with
/// four date fields over a thousand-row page would otherwise allocate eight thousand objects
/// that are discarded immediately.
/// </para>
/// <para>
/// Registered once on the generated REST serialization context, so every <see cref="LocalDate"/>
/// property on every model uses it without a per-property attribute. A nullable
/// <c>LocalDate?</c> property resolves to this converter through the serializer's own nullable
/// wrapper.
/// </para>
/// </remarks>
public sealed class LocalDateJsonConverter : JsonConverter<LocalDate>
{
    private const int IsoLength = 10;

    // Longer than any escaped spelling of a ten-character date. A raw value past this cannot be a
    // date, so it is rejected before any buffer is sized from it.
    private const int MaxRawLength = 64;

    /// <inheritdoc />
    public override LocalDate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a YYYY-MM-DD string for a date, found a {reader.TokenType} token.");
        }

        // The fast path reads the bytes in place. A date never needs an escape, and a value split
        // across buffer segments is rare, so the copying path below is for correctness only.
        if (!reader.HasValueSequence && !reader.ValueIsEscaped)
        {
            return Parse(reader.ValueSpan);
        }

        long rawLength = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;

        if (rawLength > MaxRawLength)
        {
            throw new JsonException("Expected a YYYY-MM-DD string for a date, found a longer value.");
        }

        Span<byte> buffer = stackalloc byte[MaxRawLength];
        int written = reader.CopyString(buffer);

        return Parse(buffer[..written]);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LocalDate value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(LocalDatePattern.Iso.Format(value));
    }

    /// <summary>
    /// Parses a YYYY-MM-DD date from a span of UTF-8 bytes.
    /// </summary>
    /// <param name="utf8">The bytes to parse.</param>
    /// <returns>The parsed date.</returns>
    /// <exception cref="JsonException">The span does not contain a valid ISO date.</exception>
    private static LocalDate Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length != IsoLength
            || utf8[4] != (byte)'-'
            || utf8[7] != (byte)'-'
            || !TryDigits(utf8[..4], out int year)
            || !TryDigits(utf8.Slice(5, 2), out int month)
            || !TryDigits(utf8.Slice(8, 2), out int day))
        {
            throw new JsonException($"Expected a YYYY-MM-DD date, found '{Encoding.UTF8.GetString(utf8)}'.");
        }

        try
        {
            return new LocalDate(year, month, day);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new JsonException($"'{Encoding.UTF8.GetString(utf8)}' is not a valid calendar date.", ex);
        }
    }

    /// <summary>
    /// Attempts to parse a decimal number from a span of UTF-8 bytes.
    /// </summary>
    /// <param name="utf8">The bytes to parse as digits.</param>
    /// <param name="value">The parsed value if successful; otherwise zero.</param>
    /// <returns><c>true</c> if all bytes are ASCII digits; otherwise <c>false</c>.</returns>
    private static bool TryDigits(ReadOnlySpan<byte> utf8, out int value)
    {
        value = 0;

        foreach (byte b in utf8)
        {
            if (b is < (byte)'0' or > (byte)'9')
            {
                return false;
            }

            value = (value * 10) + (b - '0');
        }

        return true;
    }
}
