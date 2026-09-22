using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MassiveDotNet.Serialization;

/// <summary>
/// Reads a scalar from a <see cref="Utf8JsonReader"/> positioned on its value token, reporting a
/// malformed value as a <see cref="JsonException"/>.
/// </summary>
/// <remarks>
/// <para>
/// The generated model converters are built from these. <see cref="Utf8JsonReader"/>'s own
/// accessors are the wrong shape for that job: <see cref="Utf8JsonReader.GetInt32"/> throws
/// <see cref="InvalidOperationException"/> on a token of the wrong kind and
/// <see cref="FormatException"/> on a number outside the target's range. Neither is a
/// <see cref="JsonException"/>, so neither is recognised as a malformed body by the transport, and
/// both would surface from an ordinary REST call as an exception type nothing documents.
/// </para>
/// <para>
/// One implementation rather than the same token check emitted into every generated converter,
/// for the reason decision D15 keeps filter rendering in <c>RequestUriBuilder</c>: it is tested
/// once here instead of once per generated file, and it cannot drift between them.
/// </para>
/// <para>
/// Every method leaves the reader on the value it read, so the caller's next
/// <see cref="Utf8JsonReader.Read"/> advances to the following property name.
/// </para>
/// <para>
/// Exactly one failure message echoes the value that caused it: the range failure raised when a
/// token is a number the target type cannot hold. This paragraph used to say that no method echoes
/// anything, and issue #65 is what reversed it. A streaming <c>StockAggregate.z</c> was refused in
/// production and the log said only that the number did not fit -- which cannot distinguish a
/// non-integral value from an integral one written as <c>12.0</c> from one genuinely past the
/// range. Those are three different causes with three different fixes, and the value is the only
/// thing that tells them apart.
/// </para>
/// <para>
/// The echo is confined to that one helper, and the confinement IS the rule 11 argument: it is
/// reachable only after its caller has checked
/// <c>reader.TokenType == JsonTokenType.Number</c>, which every caller does and no other path
/// reaches it, so the bytes it quotes are provably a JSON number -- and an API key is not a JSON
/// number. The safety is structural rather than a matter of care taken at each site. The
/// wrong-token-type failure names a token type and has no value in hand; the decimal round-trip
/// failure reads a <see cref="JsonTokenType.String"/>, where that proof does not hold and the class
/// of value it could quote back is unbounded. Neither echoes, and neither is to be changed to
/// (D-W21).
/// </para>
/// </remarks>
public static class JsonValueReader
{
    /// <summary>Reads a string, or <see langword="null"/> from a JSON null.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The string, or <see langword="null"/>.</returns>
    /// <exception cref="JsonException">The token is neither a string nor null.</exception>
    public static string? ReadString(ref Utf8JsonReader reader, string model, string property) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            _ => throw Expected("a string", reader.TokenType, model, property),
        };

    /// <summary>Reads a boolean.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The boolean.</returns>
    /// <exception cref="JsonException">The token is not <c>true</c> or <c>false</c>.</exception>
    public static bool ReadBoolean(ref Utf8JsonReader reader, string model, string property) =>
        reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            _ => throw Expected("a boolean", reader.TokenType, model, property),
        };

    /// <summary>Reads a boolean, or <see langword="null"/> from a JSON null.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The boolean, or <see langword="null"/>.</returns>
    /// <exception cref="JsonException">The token is neither a boolean nor null.</exception>
    public static bool? ReadNullableBoolean(ref Utf8JsonReader reader, string model, string property) =>
        reader.TokenType == JsonTokenType.Null ? null : ReadBoolean(ref reader, model, property);

    /// <summary>Reads a 32-bit integer.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The integer.</returns>
    /// <exception cref="JsonException">The token is not a number, or does not fit an <see cref="int"/>.</exception>
    public static int ReadInt32(ref Utf8JsonReader reader, string model, string property)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw Expected("a number", reader.TokenType, model, property);
        }

        return reader.TryGetInt32(out int value)
            ? value
            : throw OutOfRange("a 32-bit integer", ref reader, model, property);
    }

    /// <summary>Reads a 32-bit integer, or <see langword="null"/> from a JSON null.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The integer, or <see langword="null"/>.</returns>
    /// <exception cref="JsonException">The token is neither a fitting number nor null.</exception>
    public static int? ReadNullableInt32(ref Utf8JsonReader reader, string model, string property) =>
        reader.TokenType == JsonTokenType.Null ? null : ReadInt32(ref reader, model, property);

    /// <summary>Reads a 64-bit integer.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The integer.</returns>
    /// <exception cref="JsonException">The token is not a number, or does not fit a <see cref="long"/>.</exception>
    /// <remarks>
    /// The range check matters more here than anywhere else in this file: nanosecond timestamps are
    /// stored raw as <see cref="long"/> (decision D5), and a value one past the range silently
    /// becoming a <see cref="double"/> would hand a caller a wrong instant with nothing to notice it.
    /// </remarks>
    public static long ReadInt64(ref Utf8JsonReader reader, string model, string property)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw Expected("a number", reader.TokenType, model, property);
        }

        return reader.TryGetInt64(out long value)
            ? value
            : throw OutOfRange("a 64-bit integer", ref reader, model, property);
    }

    /// <summary>Reads a 64-bit integer, or <see langword="null"/> from a JSON null.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The integer, or <see langword="null"/>.</returns>
    /// <exception cref="JsonException">The token is neither a fitting number nor null.</exception>
    public static long? ReadNullableInt64(ref Utf8JsonReader reader, string model, string property) =>
        reader.TokenType == JsonTokenType.Null ? null : ReadInt64(ref reader, model, property);

    /// <summary>Reads a double.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The double.</returns>
    /// <exception cref="JsonException">The token is not a number, or does not fit a <see cref="double"/>.</exception>
    public static double ReadDouble(ref Utf8JsonReader reader, string model, string property)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw Expected("a number", reader.TokenType, model, property);
        }

        return reader.TryGetDouble(out double value)
            ? value
            : throw OutOfRange("a double", ref reader, model, property);
    }

    /// <summary>Reads a double, or <see langword="null"/> from a JSON null.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The double, or <see langword="null"/>.</returns>
    /// <exception cref="JsonException">The token is neither a fitting number nor null.</exception>
    public static double? ReadNullableDouble(ref Utf8JsonReader reader, string model, string property) =>
        reader.TokenType == JsonTokenType.Null ? null : ReadDouble(ref reader, model, property);

    /// <summary>
    /// Reads a decimal the wire sent as a string, refusing any value that does not round-trip.
    /// </summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The decimal, with the scale the wire wrote.</returns>
    /// <exception cref="JsonException">
    /// The token is not a string, or the string is not a decimal this type can carry exactly.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The fractional-share family -- <c>dv</c>, <c>dav</c>, <c>ds</c>, <c>decimal_size</c>,
    /// <c>decimal_volume</c> -- is declared <c>type: string</c> at all 31 of its sites, so this
    /// reads a <see cref="JsonTokenType.String"/>. A JSON number is refused, exactly as it was when
    /// the family bound to <see cref="string"/>.
    /// </para>
    /// <para>
    /// The parse is verified by formatting the result back and comparing bytes, rather than
    /// predicted by counting digits. <see cref="decimal"/> rounds silently and returns
    /// <see langword="true"/> when a value's scaled integer overflows its 96-bit mantissa --
    /// <c>8827.8140001491929689677164477</c> does at 29 digits while
    /// <c>12345678901234567890123456789</c> does not -- so digit count does not predict it, and a
    /// threshold set at 29 leaked silent roundings across a randomized corpus. Round-tripping is
    /// exact in both directions because the value carries its own scale, which is the same property
    /// that lets <c>4989.0</c> keep its trailing zero (D38).
    /// </para>
    /// <para>
    /// The guard also enforces canonical spelling, since a leading <c>+</c>, a redundant zero, a
    /// thousands separator, surrounding whitespace, or exponent notation all format back
    /// differently. That is stricter than "never rounds" and is accepted deliberately: Massive's
    /// wire is canonical, and a refusal is visible where a silent normalisation is not.
    /// </para>
    /// </remarks>
    public static decimal ReadDecimal(ref Utf8JsonReader reader, string model, string property)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw Expected("a decimal string", reader.TokenType, model, property);
        }

        // The fast path judges the bytes in place. These values need no escape and rarely straddle
        // a buffer boundary, so the copy below is for correctness rather than for the common case.
        if (!reader.HasValueSequence && !reader.ValueIsEscaped)
        {
            return TryReadExact(reader.ValueSpan, out decimal value)
                ? value
                : throw NotExact(model, property);
        }

        long rawLength = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;

        if (rawLength > MaxDecimalRawLength)
        {
            throw NotExact(model, property);
        }

        Span<byte> buffer = stackalloc byte[MaxDecimalRawLength];
        int written = reader.CopyString(buffer);

        return TryReadExact(buffer[..written], out decimal copied) ? copied : throw NotExact(model, property);
    }

    /// <summary>Reads a decimal string, or <see langword="null"/> from a JSON null.</summary>
    /// <param name="reader">A reader positioned on the value token.</param>
    /// <param name="model">The model being read, named in the failure message.</param>
    /// <param name="property">The property being read, named in the failure message.</param>
    /// <returns>The decimal, or <see langword="null"/>.</returns>
    /// <exception cref="JsonException">The token is neither an exact decimal string nor null.</exception>
    public static decimal? ReadNullableDecimal(ref Utf8JsonReader reader, string model, string property) =>
        reader.TokenType == JsonTokenType.Null ? null : ReadDecimal(ref reader, model, property);

    /// <summary>
    /// Longer than any decimal this type can hold: 29 digits, a sign, and a point is 31 bytes. A
    /// raw value past this cannot round-trip, so it is refused before a buffer is sized from it.
    /// </summary>
    private const int MaxDecimalRawLength = 64;

    /// <summary>Parses a decimal, and proves the parse lost nothing by formatting it back.</summary>
    /// <param name="utf8">The decoded value.</param>
    /// <param name="value">The parsed decimal, or zero.</param>
    /// <returns><see langword="true"/> when the parse round-trips byte for byte.</returns>
    private static bool TryReadExact(ReadOnlySpan<byte> utf8, out decimal value)
    {
        if (!decimal.TryParse(utf8, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        Span<byte> formatted = stackalloc byte[MaxDecimalRawLength];

        return value.TryFormat(formatted, out int written, provider: CultureInfo.InvariantCulture)
            && formatted[..written].SequenceEqual(utf8);
    }

    private static JsonException Expected(string expected, JsonTokenType found, string model, string property) =>
        new($"Expected {expected} for {model}.{property}, but the response carried a {found} token.");

    /// <summary>
    /// How much of an offending number reaches a range failure's message. A JSON number token has
    /// no length limit, so without a cap a pathological value could produce an exception message
    /// thousands of digits long -- and the DI package bridges these messages to a logger.
    /// </summary>
    private const int MaxEchoedTokenLength = 32;

    private static JsonException OutOfRange(
        string expected, ref Utf8JsonReader reader, string model, string property) =>
        new($"The number in {model}.{property} ({EchoNumber(ref reader)}) does not fit {expected}.");

    /// <summary>Renders the number token the reader is on, capped, for a failure message.</summary>
    /// <param name="reader">A reader positioned on a <see cref="JsonTokenType.Number"/> token.</param>
    /// <returns>The token's text, with a trailing ellipsis when it was longer than the cap.</returns>
    /// <remarks>
    /// Called only from <see cref="OutOfRange"/>, whose every caller has already refused a
    /// non-number token, so every byte here comes from the JSON number grammar and is therefore
    /// ASCII. That is what makes both the decode and the truncation safe: there is no multi-byte
    /// character for a cut at <see cref="MaxEchoedTokenLength"/> to split in half. Do not call this
    /// from anywhere that has not made that check (D-W21, rule 11).
    /// </remarks>
    private static string EchoNumber(ref Utf8JsonReader reader)
    {
        long length = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
        int copied = (int)Math.Min(length, MaxEchoedTokenLength);
        Span<byte> token = stackalloc byte[MaxEchoedTokenLength];

        if (reader.HasValueSequence)
        {
            reader.ValueSequence.Slice(0, copied).CopyTo(token);
        }
        else
        {
            reader.ValueSpan[..copied].CopyTo(token);
        }

        string text = Encoding.UTF8.GetString(token[..copied]);

        return length > MaxEchoedTokenLength ? text + "..." : text;
    }

    private static JsonException NotExact(string model, string property) =>
        new($"The value in {model}.{property} is not a decimal that round-trips exactly. It must be "
            + "written plainly -- no exponent, no leading sign beyond '-', no redundant zeros, no "
            + "separators or whitespace -- and must fit decimal's 96-bit range.");
}
