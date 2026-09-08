namespace MassiveDotNet.WebSocket;

/// <summary>A stock streaming topic.</summary>
/// <remarks>
/// An enum rather than a string because the server **silently ignores** a topic code it does not
/// recognise: no acknowledgement, no error, and no data ever after. A caller who mistypes a string
/// would see a healthy connection producing nothing, indefinitely. The mistake is made
/// unrepresentable instead of validated (D-W1).
/// </remarks>
public enum StockTopic
{
    /// <summary>Tick-level trades, wire code <c>T</c>.</summary>
    Trades,

    /// <summary>NBBO quotes, wire code <c>Q</c>.</summary>
    Quotes,

    /// <summary>Second-by-second OHLC aggregate bars, wire code <c>A</c>.</summary>
    SecondAggregates,

    /// <summary>Minute-by-minute OHLC aggregate bars, wire code <c>AM</c>.</summary>
    MinuteAggregates,

    /// <summary>Net order imbalance auction events, wire code <c>NOI</c>.</summary>
    Imbalances,

    /// <summary>Limit up-limit down price band events, wire code <c>LULD</c>.</summary>
    LimitUpLimitDown,
}

/// <summary>Extensions for <see cref="StockTopic"/>.</summary>
internal static class StockTopicExtensions
{
    /// <summary>Renders the topic as the wire code the subscribe/unsubscribe action expects.</summary>
    /// <param name="topic">The topic.</param>
    /// <returns>The wire code, such as <c>T</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The topic is not a declared member.</exception>
    public static string ToCode(this StockTopic topic) => topic switch
    {
        StockTopic.Trades => "T",
        StockTopic.Quotes => "Q",
        StockTopic.SecondAggregates => "A",
        StockTopic.MinuteAggregates => "AM",
        StockTopic.Imbalances => "NOI",
        StockTopic.LimitUpLimitDown => "LULD",
        _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, null),
    };
}
