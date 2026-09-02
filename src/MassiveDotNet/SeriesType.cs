namespace MassiveDotNet;

/// <summary>
/// The price in each aggregate that a technical indicator is calculated over.
/// </summary>
public enum SeriesType
{
    /// <summary>The open price of each aggregate.</summary>
    Open = 0,

    /// <summary>The high price of each aggregate.</summary>
    High = 1,

    /// <summary>The low price of each aggregate.</summary>
    Low = 2,

    /// <summary>The close price of each aggregate.</summary>
    Close = 3,
}
