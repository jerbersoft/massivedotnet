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
}
