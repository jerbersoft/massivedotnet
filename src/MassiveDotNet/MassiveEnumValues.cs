using NodaTime;
using NodaTime.Text;

namespace MassiveDotNet;

/// <summary>
/// Converts SDK enums to the literal strings the Massive platform API expects on the wire.
/// </summary>
/// <remarks>
/// These are plain switch expressions rather than reflection-driven conversion so that the
/// SDK stays fully Native AOT compatible.
/// </remarks>
public static class MassiveEnumValues
{
    /// <summary>Returns the wire representation of an <see cref="AggregateTimespan"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The literal accepted by the API, for example <c>"minute"</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined enum member.</exception>
    public static string ToWireValue(this AggregateTimespan value) => value switch
    {
        AggregateTimespan.Second => "second",
        AggregateTimespan.Minute => "minute",
        AggregateTimespan.Hour => "hour",
        AggregateTimespan.Day => "day",
        AggregateTimespan.Week => "week",
        AggregateTimespan.Month => "month",
        AggregateTimespan.Quarter => "quarter",
        AggregateTimespan.Year => "year",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Returns the wire representation of a <see cref="SortOrder"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>Either <c>"asc"</c> or <c>"desc"</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined enum member.</exception>
    public static string ToWireValue(this SortOrder value) => value switch
    {
        SortOrder.Ascending => "asc",
        SortOrder.Descending => "desc",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Returns the wire representation of a <see cref="MarketType"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The literal accepted by the API, for example <c>"stocks"</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined enum member.</exception>
    public static string ToWireValue(this MarketType value) => value switch
    {
        MarketType.Stocks => "stocks",
        MarketType.Options => "options",
        MarketType.Crypto => "crypto",
        MarketType.Fx => "fx",
        MarketType.Indices => "indices",
        MarketType.Otc => "otc",
        MarketType.Futures => "futures",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Returns the wire representation of a <see cref="SeriesType"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The literal accepted by the API, for example <c>"close"</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined enum member.</exception>
    public static string ToWireValue(this SeriesType value) => value switch
    {
        SeriesType.Open => "open",
        SeriesType.High => "high",
        SeriesType.Low => "low",
        SeriesType.Close => "close",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>Returns the wire representation of a calendar date.</summary>
    /// <param name="value">The date to convert.</param>
    /// <returns>The date in ISO <c>YYYY-MM-DD</c> form.</returns>
    public static string ToWireValue(this LocalDate value) => LocalDatePattern.Iso.Format(value);

    /// <summary>Returns the wire representation of an instant, as Unix milliseconds.</summary>
    /// <param name="value">The instant to convert.</param>
    /// <returns>Milliseconds since the Unix epoch.</returns>
    public static string ToWireValue(this Instant value) =>
        value.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Returns the wire representation of an instant, as Unix nanoseconds.</summary>
    /// <param name="value">The instant to convert.</param>
    /// <returns>Nanoseconds since the Unix epoch, used by the tick-level endpoints.</returns>
    public static string ToWireValueNanoseconds(this Instant value) =>
        value.ToUnixTimeTicks().ToString(System.Globalization.CultureInfo.InvariantCulture);
}
