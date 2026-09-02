using System.Globalization;
using NodaTime;
using NodaTime.Text;

namespace MassiveDotNet;

/// <summary>
/// A point in time accepted by the tick-level Massive endpoints that take "either a date with the
/// format YYYY-MM-DD or a nanosecond timestamp".
/// </summary>
/// <remarks>
/// <para>
/// This is <see cref="DateOrTimestamp"/> with the unit changed. The two are separate types with
/// the unit in the name because an <see cref="Instant"/> converts implicitly to either, and a
/// millisecond render on a nanosecond endpoint would compile and ask for a moment in 1970
/// (decision D20). The map chooses the type each endpoint documents.
/// </para>
/// <para>
/// Implicit conversions exist from <see cref="LocalDate"/>, <see cref="Instant"/>,
/// <see cref="long"/>, and <see cref="string"/>, so callers can pass whichever form they
/// already have without converting by hand.
/// </para>
/// </remarks>
public readonly struct DateOrNanoseconds : IEquatable<DateOrNanoseconds>
{
    private readonly string? _literal;
    private readonly long _epochNanoseconds;

    private DateOrNanoseconds(string literal)
    {
        _literal = literal;
        _epochNanoseconds = 0;
    }

    private DateOrNanoseconds(long epochNanoseconds)
    {
        _literal = null;
        _epochNanoseconds = epochNanoseconds;
    }

    /// <summary>Creates a value from a calendar date, rendered as <c>YYYY-MM-DD</c>.</summary>
    /// <param name="value">The calendar date.</param>
    /// <returns>The wrapped value.</returns>
    public static DateOrNanoseconds FromDate(LocalDate value) =>
        new(LocalDatePattern.Iso.Format(value));

    /// <summary>Creates a value from an instant, rendered as Unix nanoseconds.</summary>
    /// <param name="value">The instant.</param>
    /// <returns>The wrapped value.</returns>
    public static DateOrNanoseconds FromInstant(Instant value) =>
        new((value - NodaConstants.UnixEpoch).ToInt64Nanoseconds());

    /// <summary>Creates a value from a Unix nanosecond timestamp.</summary>
    /// <param name="epochNanoseconds">Nanoseconds since the Unix epoch.</param>
    /// <returns>The wrapped value.</returns>
    public static DateOrNanoseconds FromUnixNanoseconds(long epochNanoseconds) =>
        new(epochNanoseconds);

    /// <summary>Creates a value from a literal already in a form the API accepts.</summary>
    /// <param name="value">The literal value, such as <c>"2026-01-15"</c>.</param>
    /// <returns>The wrapped value.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null or whitespace.</exception>
    public static DateOrNanoseconds FromLiteral(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new DateOrNanoseconds(value);
    }

    /// <summary>Converts a calendar date.</summary>
    /// <param name="value">The calendar date.</param>
    public static implicit operator DateOrNanoseconds(LocalDate value) => FromDate(value);

    /// <summary>Converts an instant.</summary>
    /// <param name="value">The instant.</param>
    public static implicit operator DateOrNanoseconds(Instant value) => FromInstant(value);

    /// <summary>Converts a Unix nanosecond timestamp.</summary>
    /// <param name="value">Nanoseconds since the Unix epoch.</param>
    public static implicit operator DateOrNanoseconds(long value) => FromUnixNanoseconds(value);

    /// <summary>Converts a literal value.</summary>
    /// <param name="value">The literal value.</param>
    public static implicit operator DateOrNanoseconds(string value) => FromLiteral(value);

    /// <summary>Renders the value in the form the API expects.</summary>
    /// <returns>Either a <c>YYYY-MM-DD</c> date or a Unix nanosecond timestamp.</returns>
    public override string ToString() =>
        _literal ?? _epochNanoseconds.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public bool Equals(DateOrNanoseconds other) =>
        _literal == other._literal && _epochNanoseconds == other._epochNanoseconds;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DateOrNanoseconds other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_literal, _epochNanoseconds);

    /// <summary>Compares two values for equality.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    public static bool operator ==(DateOrNanoseconds left, DateOrNanoseconds right) => left.Equals(right);

    /// <summary>Compares two values for inequality.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true"/> when the values differ.</returns>
    public static bool operator !=(DateOrNanoseconds left, DateOrNanoseconds right) => !left.Equals(right);
}
