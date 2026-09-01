namespace MassiveDotNet;

/// <summary>
/// An asset class served by the Massive platform.
/// </summary>
public enum MarketType
{
    /// <summary>US equities.</summary>
    Stocks = 0,

    /// <summary>US options contracts.</summary>
    Options = 1,

    /// <summary>Global cryptocurrencies.</summary>
    Crypto = 2,

    /// <summary>Global foreign exchange.</summary>
    Fx = 3,

    /// <summary>Market indices.</summary>
    Indices = 4,

    /// <summary>US over-the-counter equities.</summary>
    Otc = 5,

    /// <summary>Futures contracts.</summary>
    Futures = 6,
}
