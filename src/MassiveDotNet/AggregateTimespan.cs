namespace MassiveDotNet;

/// <summary>
/// The size of the time window each aggregate bar covers.
/// </summary>
public enum AggregateTimespan
{
    /// <summary>One second per bar.</summary>
    Second = 0,

    /// <summary>One minute per bar.</summary>
    Minute = 1,

    /// <summary>One hour per bar.</summary>
    Hour = 2,

    /// <summary>One day per bar.</summary>
    Day = 3,

    /// <summary>One week per bar.</summary>
    Week = 4,

    /// <summary>One month per bar.</summary>
    Month = 5,

    /// <summary>One quarter per bar.</summary>
    Quarter = 6,

    /// <summary>One year per bar.</summary>
    Year = 7,
}
