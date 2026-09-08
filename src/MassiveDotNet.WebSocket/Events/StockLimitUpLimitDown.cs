using NodaTime;

namespace MassiveDotNet.WebSocket.Events;

/// <summary>One streamed limit up-limit down band update: the <c>LULD</c> topic.</summary>
/// <remarks>
/// Price band updates, and the pauses, halts and resumptions that follow a breach. High volume
/// during regular hours, so a struct (D4).
/// <para>
/// The ticker arrives as <c>T</c> rather than <c>sym</c>, as it does on the imbalance topic.
/// </para>
/// <para>
/// <b>The timestamp is nanoseconds, and Massive's documentation says milliseconds.</b> That page
/// contradicts itself: its own published sample carries a nineteen-digit value, and a frame
/// captured live from <c>wss://socket.massive.com/stocks</c> on 2026-09-08 carried
/// <c>1788877046310003385</c>. Read as milliseconds either value lands roughly fifty-six million
/// years in the future — a binding that compiles, deserializes without error, and is silently
/// wrong, which is what D20 introduced <c>DateOrNanoseconds</c> to prevent on the request side.
/// The observation is pinned by a live test and flips the day either the wire or the documentation
/// moves (D-W15).
/// </para>
/// </remarks>
public readonly record struct StockLimitUpLimitDown
{
    /// <summary>The ticker symbol, interned across events.</summary>
    public required string Ticker { get; init; }

    /// <summary>The limit up price band.</summary>
    public double HighPrice { get; init; }

    /// <summary>The limit down price band.</summary>
    public double LowPrice { get; init; }

    /// <summary>
    /// The LULD indicators. The same wire shape as a trade's conditions and a quote's indicators —
    /// an array of integer codes — so it uses the same inline-capacity set.
    /// </summary>
    public ConditionSet Indicators { get; init; }

    /// <summary>The tape: 1 = NYSE, 2 = AMEX, 3 = Nasdaq.</summary>
    public int? Tape { get; init; }

    /// <summary>The raw event timestamp, in Unix nanoseconds — not the milliseconds the docs claim.</summary>
    public long TimestampNanoseconds { get; init; }

    /// <summary>The sequence number, increasing and unique per ticker but not contiguous.</summary>
    public long SequenceNumber { get; init; }

    /// <summary>When the band update was published.</summary>
    public Instant Timestamp => Epoch.FromNanoseconds(TimestampNanoseconds);
}
