using System.Globalization;
using System.Text.Json;

namespace MassiveDotNet.Serialization;

/// <summary>
/// Writes a scalar whose .NET type and wire form disagree, so that a model this SDK deserializes
/// serializes back to the shape the service sent.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="decimal"/> needs this today. Every other scalar the generated converters carry
/// writes through a <see cref="Utf8JsonWriter"/> method directly, because its .NET type and its
/// JSON token already agree; the fractional-share family does not, since it is a number the
/// description declares as a string (D38).
/// </para>
/// <para>
/// The counterpart to <see cref="JsonValueReader"/>, and here for the same reason: one
/// implementation, tested once, rather than the same formatting emitted into seventeen generated
/// converters and hand-written into the streaming ones, where it could drift between them.
/// </para>
/// </remarks>
public static class JsonValueWriter
{
    // 29 digits, a sign, and a point is 31 bytes, so no decimal formats longer than this.
    private const int MaxLength = 64;

    /// <summary>Writes a decimal property as the JSON string the wire uses.</summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="property">The wire property name.</param>
    /// <param name="value">The value to write.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Formatted straight into UTF-8, so writing a page of rows allocates no strings -- the same
    /// concern that moved this family off <see cref="string"/> in the first place. The invariant
    /// culture is not a detail: a culture using <c>,</c> as its decimal separator would emit a value
    /// the service, and this SDK's own reader, would refuse.
    /// </remarks>
    public static void WriteDecimalString(Utf8JsonWriter writer, string property, decimal value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Span<byte> formatted = stackalloc byte[MaxLength];

        // A decimal always fits, so a failure here would be a bug in this constant rather than
        // anything a caller can cause; there is no honest fallback to write instead.
        if (!value.TryFormat(formatted, out int written, provider: CultureInfo.InvariantCulture))
        {
            throw new JsonException($"A decimal did not fit {MaxLength} bytes when writing {property}.");
        }

        writer.WriteString(property, formatted[..written]);
    }
}
