using NodaTime;

namespace MassiveDotNet.WebSocket.Events;

/// <summary>One streamed stock trade: the <c>T</c> topic.</summary>
/// <remarks>
/// A tick-level type, so a struct (D4). Timestamps are stored as raw Unix **millisecond** values
/// and exposed as <see cref="Instant"/> only when read (D5). The unit differs from REST v3, which
/// sends nanoseconds for the same conceptual field (D-W11).
/// </remarks>
public readonly record struct StockTrade
{
    /// <summary>The ticker symbol, interned across events.</summary>
    public required string Ticker { get; init; }

    /// <summary>The trade ID, unique per ticker, exchange, and TRF combination.</summary>
    public required string TradeId { get; init; }

    /// <summary>The exchange ID.</summary>
    public int ExchangeId { get; init; }

    /// <summary>The tape: 1 = NYSE, 2 = AMEX, 3 = Nasdaq.</summary>
    public int? Tape { get; init; }

    /// <summary>The price per share.</summary>
    public double Price { get; init; }

    /// <summary>The trade size.</summary>
    public long Size { get; init; }

    /// <summary>The trade size including fractional shares, as the wire's decimal string.</summary>
    public string? DecimalSize { get; init; }

    /// <summary>The trade conditions.</summary>
    public ConditionSet Conditions { get; init; }

    /// <summary>The raw SIP timestamp, in Unix milliseconds.</summary>
    public long SipTimestampMilliseconds { get; init; }

    /// <summary>The raw participant timestamp, in Unix milliseconds, never after the SIP timestamp.</summary>
    public long? ParticipantTimestampMilliseconds { get; init; }

    /// <summary>The sequence number, increasing and unique per ticker but not contiguous.</summary>
    public long SequenceNumber { get; init; }

    /// <summary>The Trade Reporting Facility ID, when the trade was reported through one.</summary>
    public int? TrfId { get; init; }

    /// <summary>The raw TRF timestamp, in Unix milliseconds.</summary>
    public long? TrfTimestampMilliseconds { get; init; }

    /// <summary>When the SIP received the trade.</summary>
    public Instant SipTimestamp => Epoch.FromMilliseconds(SipTimestampMilliseconds);

    /// <summary>When the trade occurred at the exchange or TRF.</summary>
    public Instant? ParticipantTimestamp =>
        ParticipantTimestampMilliseconds is { } milliseconds ? Epoch.FromMilliseconds(milliseconds) : null;

    /// <summary>When the Trade Reporting Facility received the trade.</summary>
    public Instant? TrfTimestamp =>
        TrfTimestampMilliseconds is { } milliseconds ? Epoch.FromMilliseconds(milliseconds) : null;
}
