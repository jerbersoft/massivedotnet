using NodaTime;

namespace MassiveDotNet.WebSocket.Events;

/// <summary>One streamed net order imbalance: the <c>NOI</c> topic.</summary>
/// <remarks>
/// Auction imbalance updates, mostly at the opening and closing auctions but also during
/// ticker-specific halts and mini-auctions. A struct for consistency with every other event type
/// (D4), though this topic is far lower volume than trades or aggregates.
/// <para>
/// Two things differ from the trade, quote and aggregate topics. The ticker arrives as <c>T</c>
/// rather than <c>sym</c> — the same letter that is the trade topic's own wire code — and the
/// timestamp is Unix <b>nanoseconds</b> rather than milliseconds. Both are read from this topic's
/// own documentation, never inferred from a sibling.
/// </para>
/// </remarks>
public readonly record struct StockImbalance
{
    /// <summary>The ticker symbol, interned across events.</summary>
    public required string Ticker { get; init; }

    /// <summary>The raw event timestamp, in Unix nanoseconds.</summary>
    public long TimestampNanoseconds { get; init; }

    /// <summary>
    /// The raw auction time as the wire encodes it: <c>(hour × 100) + minutes</c> in Eastern time,
    /// so <c>930</c> is 09:30 and <c>1600</c> is 16:00.
    /// </summary>
    /// <remarks>
    /// Kept alongside <see cref="AuctionTime"/> so a code the wire sends that is not a wall clock
    /// is still readable, rather than being lost behind a <see langword="null"/>.
    /// </remarks>
    public int AuctionTimeCode { get; init; }

    /// <summary>
    /// The auction type: <c>O</c> early opening, <c>M</c> core opening, <c>H</c> reopening after a
    /// halt, <c>C</c> closing, <c>P</c> extreme closing imbalance, <c>R</c> regulatory closing
    /// imbalance.
    /// </summary>
    /// <remarks>
    /// A <see langword="string"/> rather than an enum, unlike <see cref="StockTopic"/>. That enum
    /// exists because the server silently ignores a topic code it does not recognise, so a caller's
    /// typo is unrecoverable and is made unrepresentable instead — an argument about a value the
    /// SDK <b>sends</b>. This is a value the SDK <b>receives</b>, where an enum could only throw on
    /// a code Massive adds later, failing a whole event over one field, or misfile it as an
    /// existing member. A string carries what arrived.
    /// </remarks>
    public string? AuctionType { get; init; }

    /// <summary>The symbol sequence number.</summary>
    public long SymbolSequence { get; init; }

    /// <summary>The exchange ID.</summary>
    public int ExchangeId { get; init; }

    /// <summary>The imbalance quantity.</summary>
    public long ImbalanceQuantity { get; init; }

    /// <summary>The paired quantity.</summary>
    public long PairedQuantity { get; init; }

    /// <summary>The book clearing price.</summary>
    public double BookClearingPrice { get; init; }

    /// <summary>When the imbalance was published.</summary>
    public Instant Timestamp => Epoch.FromNanoseconds(TimestampNanoseconds);

    /// <summary>
    /// The wall-clock time the auction is planned for, in Eastern time, or <see langword="null"/>
    /// when <see cref="AuctionTimeCode"/> is not a valid wall clock.
    /// </summary>
    /// <remarks>
    /// Nullable rather than throwing: a computed property must not fail on a value the server chose,
    /// and the raw code remains available for a caller who wants to see what actually arrived.
    /// </remarks>
    public LocalTime? AuctionTime =>
        AuctionTimeCode is >= 0 and <= 2359 && AuctionTimeCode % 100 < 60
            ? new LocalTime(AuctionTimeCode / 100, AuctionTimeCode % 100)
            : null;
}
