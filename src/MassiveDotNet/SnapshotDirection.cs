namespace MassiveDotNet;

/// <summary>
/// Which end of the market a snapshot of the day's movers describes.
/// </summary>
public enum SnapshotDirection
{
    /// <summary>The tickers with the largest percentage gain today.</summary>
    Gainers = 0,

    /// <summary>The tickers with the largest percentage loss today.</summary>
    Losers = 1,
}
