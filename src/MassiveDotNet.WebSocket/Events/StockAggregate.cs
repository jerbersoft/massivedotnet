using NodaTime;

namespace MassiveDotNet.WebSocket.Events;

/// <summary>One streamed stock aggregate bar: the <c>A</c> and <c>AM</c> topics.</summary>
/// <remarks>
/// One model serves both topics because their wire shapes are field-for-field identical, differing
/// only in the event code and the length of the window (D-W14). Which window produced a bar is
/// readable from <see cref="Start"/> and <see cref="End"/>: one second apart, or sixty.
/// <para>
/// A high-volume type, so a struct (D4). Window bounds are stored as raw Unix <b>millisecond</b>
/// values and exposed as <see cref="Instant"/> only when read (D5). The unit is this topic's own —
/// the imbalance and limit up-limit down topics send nanoseconds for their timestamp field, and
/// REST v3 sends nanoseconds for the conceptually similar tick timestamp.
/// </para>
/// </remarks>
public readonly record struct StockAggregate
{
    /// <summary>The ticker symbol, interned across events.</summary>
    public required string Ticker { get; init; }

    /// <summary>The tick volume within this window.</summary>
    public long Volume { get; init; }

    /// <summary>The tick volume including fractional shares, as the wire's decimal string.</summary>
    public string? DecimalVolume { get; init; }

    /// <summary>Today's accumulated volume.</summary>
    public long AccumulatedVolume { get; init; }

    /// <summary>Today's accumulated volume including fractional shares, as the wire's decimal string.</summary>
    public string? DecimalAccumulatedVolume { get; init; }

    /// <summary>Today's official opening price.</summary>
    public double OfficialOpenPrice { get; init; }

    /// <summary>This window's volume weighted average price.</summary>
    public double VolumeWeightedAveragePrice { get; init; }

    /// <summary>The opening tick price for this window.</summary>
    public double Open { get; init; }

    /// <summary>The closing tick price for this window.</summary>
    public double Close { get; init; }

    /// <summary>The highest tick price for this window.</summary>
    public double High { get; init; }

    /// <summary>The lowest tick price for this window.</summary>
    public double Low { get; init; }

    /// <summary>Today's volume weighted average price.</summary>
    public double DailyVolumeWeightedAveragePrice { get; init; }

    /// <summary>The average trade size within this window.</summary>
    public long AverageTradeSize { get; init; }

    /// <summary>The raw start of this window, in Unix milliseconds.</summary>
    public long StartTimestampMilliseconds { get; init; }

    /// <summary>The raw end of this window, in Unix milliseconds.</summary>
    public long EndTimestampMilliseconds { get; init; }

    /// <summary>Whether this bar is for an OTC ticker. The wire omits the field when false.</summary>
    public bool Otc { get; init; }

    /// <summary>When this window opened.</summary>
    public Instant Start => Epoch.FromMilliseconds(StartTimestampMilliseconds);

    /// <summary>When this window closed.</summary>
    public Instant End => Epoch.FromMilliseconds(EndTimestampMilliseconds);
}
