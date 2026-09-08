namespace MassiveDotNet.WebSocket;

/// <summary>The market a stream connects to, which is the path segment on a feed host.</summary>
public enum MassiveMarket
{
    /// <summary>US equities.</summary>
    Stocks,

    /// <summary>US options contracts.</summary>
    Options,

    /// <summary>Index values and aggregates.</summary>
    Indices,

    /// <summary>Foreign exchange pairs.</summary>
    Forex,

    /// <summary>Cryptocurrency pairs.</summary>
    Crypto,

    /// <summary>Futures contracts.</summary>
    Futures,
}

/// <summary>Extensions for <see cref="MassiveMarket"/>.</summary>
internal static class MassiveMarketExtensions
{
    /// <summary>Renders the market as the lowercase path segment a feed host expects.</summary>
    /// <param name="market">The market.</param>
    /// <returns>The path segment, such as <c>stocks</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The market is not a declared member.</exception>
    public static string ToPathSegment(this MassiveMarket market) => market switch
    {
        MassiveMarket.Stocks => "stocks",
        MassiveMarket.Options => "options",
        MassiveMarket.Indices => "indices",
        MassiveMarket.Forex => "forex",
        MassiveMarket.Crypto => "crypto",
        MassiveMarket.Futures => "futures",
        _ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
    };
}
