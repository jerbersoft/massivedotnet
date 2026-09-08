using NodaTime;

namespace MassiveDotNet.WebSocket.Events;

/// <summary>One streamed NBBO stock quote: the <c>Q</c> topic.</summary>
/// <remarks>A tick-level type, so a struct (D4), with the raw epoch stored and the instant computed (D5).</remarks>
public readonly record struct StockQuote
{
    /// <summary>The ticker symbol, interned across events.</summary>
    public required string Ticker { get; init; }

    /// <summary>The bid exchange ID.</summary>
    public int BidExchangeId { get; init; }

    /// <summary>The bid price.</summary>
    public double BidPrice { get; init; }

    /// <summary>The number of shares buyers are bidding for at the bid price.</summary>
    public long BidSize { get; init; }

    /// <summary>The ask exchange ID.</summary>
    public int AskExchangeId { get; init; }

    /// <summary>The ask price.</summary>
    public double AskPrice { get; init; }

    /// <summary>The number of shares sellers are offering at the ask price.</summary>
    public long AskSize { get; init; }

    /// <summary>The quote condition. A single code, unlike a trade's set.</summary>
    public int? Condition { get; init; }

    /// <summary>The quote indicators.</summary>
    public ConditionSet Indicators { get; init; }

    /// <summary>The raw SIP timestamp, in Unix milliseconds.</summary>
    public long SipTimestampMilliseconds { get; init; }

    /// <summary>The sequence number, reset each trading session.</summary>
    public long SequenceNumber { get; init; }

    /// <summary>The tape: 1 = NYSE, 2 = AMEX, 3 = Nasdaq.</summary>
    public int? Tape { get; init; }

    /// <summary>When the SIP received the quote.</summary>
    public Instant SipTimestamp => Epoch.FromMilliseconds(SipTimestampMilliseconds);
}
