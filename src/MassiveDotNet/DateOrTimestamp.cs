using System.Globalization;
using NodaTime;
using NodaTime.Text;

namespace MassiveDotNet;

/// <summary>
/// A point in time accepted by Massive endpoints that take "either a date with the format
/// YYYY-MM-DD or a millisecond timestamp".
/// </summary>
/// <remarks>
/// Implicit conversions exist from <see cref="LocalDate"/>, <see cref="Instant"/>,
/// <see cref="long"/>, and <see cref="string"/>, so callers can pass whichever form they
/// already have without converting by hand.
/// </remarks>
public readonly struct DateOrTimestamp : IEquatable<DateOrTimestamp>
{
    private readonly string? _literal;
    private readonly long _epochMilliseconds;

    private DateOrTimestamp(string literal)
    {
        _literal = literal;
        _epochMilliseconds = 0;
    }

    private DateOrTimestamp(long epochMilliseconds)
    {
        _literal = null;
        _epochMilliseconds = epochMilliseconds;
    }

    /// <summary>Creates a value from a calendar date, rendered as <c>YYYY-MM-DD</c>.</summary>
    /// <param name="value">The calendar date.</param>
    /// <returns>The wrapped value.</returns>
    public static DateOrTimestamp FromDate(LocalDate value) =>
        new(LocalDatePattern.Iso.Format(value));

    /// <summary>Creates a value from an instant, rendered as Unix milliseconds.</summary>
    /// <param name="value">The instant.</param>
    /// <returns>The wrapped value.</returns>
    public static DateOrTimestamp FromInstant(Instant value) =>
        new(value.ToUnixTimeMilliseconds());

    /// <summary>Creates a value from a Unix millisecond timestamp.</summary>
    /// <param name="epochMilliseconds">Milliseconds since the Unix epoch.</param>
    /// <returns>The wrapped value.</returns>
    public static DateOrTimestamp FromUnixMilliseconds(long epochMilliseconds) =>
        new(epochMilliseconds);

    /// <summary>Creates a value from a literal already in a form the API accepts.</summary>
    /// <param name="value">The literal value, such as <c>"2026-01-15"</c>.</param>
    /// <returns>The wrapped value.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null or whitespace.</exception>
    public static DateOrTimestamp FromLiteral(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new DateOrTimestamp(value);
    }

    /// <summary>Converts a calendar date.</summary>
    /// <param name="value">The calendar date.</param>
    public static implicit operator DateOrTimestamp(LocalDate value) => FromDate(value);

    /// <summary>Converts an instant.</summary>
    /// <param name="value">The instant.</param>
    public static implicit operator DateOrTimestamp(Instant value) => FromInstant(value);

    /// <summary>Converts a Unix millisecond timestamp.</summary>
    /// <param name="value">Milliseconds since the Unix epoch.</param>
    public static implicit operator DateOrTimestamp(long value) => FromUnixMilliseconds(value);

    /// <summary>Converts a literal value.</summary>
    /// <param name="value">The literal value.</param>
    public static implicit operator DateOrTimestamp(string value) => FromLiteral(value);

    /// <summary>Renders the value in the form the API expects.</summary>
    /// <returns>Either a <c>YYYY-MM-DD</c> date or a Unix millisecond timestamp.</returns>
    public override string ToString() =>
        _literal ?? _epochMilliseconds.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public bool Equals(DateOrTimestamp other) =>
        _literal == other._literal && _epochMilliseconds == other._epochMilliseconds;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DateOrTimestamp other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_literal, _epochMilliseconds);

    /// <summary>Compares two values for equality.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    public static bool operator ==(DateOrTimestamp left, DateOrTimestamp right) => left.Equals(right);

    /// <summary>Compares two values for inequality.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true"/> when the values differ.</returns>
    public static bool operator !=(DateOrTimestamp left, DateOrTimestamp right) => !left.Equals(right);
}
