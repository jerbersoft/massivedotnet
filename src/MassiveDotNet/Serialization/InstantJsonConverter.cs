using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;
using NodaTime.Text;

namespace MassiveDotNet.Serialization;

/// <summary>
/// Reads and writes a moment on the global timeline in the RFC 3339 form the Massive API uses for
/// <c>format: date-time</c> fields, such as <c>2024-06-24T18:33:53Z</c>.
/// </summary>
/// <remarks>
/// <para>
/// Parses straight from the reader's UTF-8 bytes: no intermediate string, and none of the
/// <c>ParseResult</c> allocation NodaTime's pattern API incurs per value. A thousand-article news
/// page would otherwise allocate a thousand objects that are discarded immediately.
/// </para>
/// <para>
/// Accepts <c>YYYY-MM-DDTHH:MM:SS</c>, an optional fraction of one to nine digits, then <c>Z</c>
/// or a numeric <c>±HH:MM</c> offset. The <c>T</c> and <c>Z</c> designators may be lowercase, as
/// RFC 3339 permits. Anything else is a <see cref="JsonException"/> naming the value.
/// </para>
/// <para>
/// Registered once on the generated REST serialization context, so every <see cref="Instant"/>
/// property on every model uses it without a per-property attribute. A nullable
/// <c>Instant?</c> property resolves to this converter through the serializer's own nullable
/// wrapper.
/// </para>
/// </remarks>
public sealed class InstantJsonConverter : JsonConverter<Instant>
{
    // YYYY-MM-DDTHH:MM:SS is 19 bytes; the shortest valid value adds a Z.
    private const int DateTimeLength = 19;
    private const int MinLength = DateTimeLength + 1;

    // Longer than any escaped spelling of a timestamp. A raw value past this cannot be one, so it
    // is rejected before any buffer is sized from it.
    private const int MaxRawLength = 64;

    // Indexed by the number of fraction digits read, so the digits scale to nanoseconds.
    private static readonly long[] NanosecondScale =
        [1_000_000_000, 100_000_000, 10_000_000, 1_000_000, 100_000, 10_000, 1_000, 100, 10, 1];

    /// <inheritdoc />
    public override Instant Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected an RFC 3339 timestamp string, found a {reader.TokenType} token.");
        }

        // The fast path reads the bytes in place. A timestamp never needs an escape, and a value
        // split across buffer segments is rare, so the copying path below is for correctness only.
        if (!reader.HasValueSequence && !reader.ValueIsEscaped)
        {
            return Parse(reader.ValueSpan);
        }

        long rawLength = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;

        if (rawLength > MaxRawLength)
        {
            throw new JsonException("Expected an RFC 3339 timestamp string, found a longer value.");
        }

        Span<byte> buffer = stackalloc byte[MaxRawLength];
        int written = reader.CopyString(buffer);

        return Parse(buffer[..written]);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Instant value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(InstantPattern.ExtendedIso.Format(value));
    }

    /// <summary>
    /// Parses an RFC 3339 timestamp from a span of UTF-8 bytes.
    /// </summary>
    /// <param name="utf8">The bytes to parse.</param>
    /// <returns>The parsed instant.</returns>
    /// <exception cref="JsonException">The span does not contain a valid RFC 3339 timestamp.</exception>
    private static Instant Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length < MinLength
            || utf8[4] != (byte)'-'
            || utf8[7] != (byte)'-'
            || (utf8[10] | 0x20) != 't'
            || utf8[13] != (byte)':'
            || utf8[16] != (byte)':'
            || !TryDigits(utf8[..4], out int year)
            || !TryDigits(utf8.Slice(5, 2), out int month)
            || !TryDigits(utf8.Slice(8, 2), out int day)
            || !TryDigits(utf8.Slice(11, 2), out int hour)
            || !TryDigits(utf8.Slice(14, 2), out int minute)
            || !TryDigits(utf8.Slice(17, 2), out int second))
        {
            throw Malformed(utf8);
        }

        int position = DateTimeLength;
        long nanoseconds = 0;

        if (utf8[position] == (byte)'.')
        {
            int start = ++position;

            while (position < utf8.Length && utf8[position] is >= (byte)'0' and <= (byte)'9')
            {
                position++;
            }

            int digits = position - start;

            if (digits is < 1 or > 9 || !TryDigits(utf8.Slice(start, digits), out int fraction))
            {
                throw Malformed(utf8);
            }

            nanoseconds = fraction * NanosecondScale[digits];
        }

        if (position >= utf8.Length)
        {
            throw Malformed(utf8);
        }

        int offsetSeconds = 0;
        byte designator = utf8[position];

        if ((designator | 0x20) == 'z')
        {
            position++;
        }
        else if (designator is (byte)'+' or (byte)'-')
        {
            if (utf8.Length - position != 6
                || utf8[position + 3] != (byte)':'
                || !TryDigits(utf8.Slice(position + 1, 2), out int offsetHours)
                || !TryDigits(utf8.Slice(position + 4, 2), out int offsetMinutes))
            {
                throw Malformed(utf8);
            }

            offsetSeconds = ((offsetHours * 3600) + (offsetMinutes * 60)) * (designator == (byte)'-' ? -1 : 1);
            position += 6;
        }
        else
        {
            throw Malformed(utf8);
        }

        if (position != utf8.Length)
        {
            throw Malformed(utf8);
        }

        try
        {
            LocalDateTime local = new LocalDateTime(year, month, day, hour, minute, second).PlusNanoseconds(nanoseconds);
            return local.WithOffset(Offset.FromSeconds(offsetSeconds)).ToInstant();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new JsonException($"'{Encoding.UTF8.GetString(utf8)}' is not a valid timestamp.", ex);
        }
    }

    private static JsonException Malformed(ReadOnlySpan<byte> utf8) =>
        new($"Expected an RFC 3339 timestamp such as 2024-06-24T18:33:53Z, found '{Encoding.UTF8.GetString(utf8)}'.");

    /// <summary>
    /// Attempts to parse a decimal number from a span of at most nine UTF-8 digits.
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
