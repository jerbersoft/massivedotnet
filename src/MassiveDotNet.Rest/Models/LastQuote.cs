using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="LastQuote"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct LastQuote
{
    /// <summary>The moment the SIP received this quote, converted from <see cref="SipTimestampNanoseconds"/>.</summary>
    [JsonIgnore]
    public Instant SipTimestamp => Epoch.FromNanoseconds(SipTimestampNanoseconds);

    /// <summary>The moment the exchange generated this quote, converted from <see cref="ParticipantTimestampNanoseconds"/>.</summary>
    [JsonIgnore]
    public Instant ParticipantTimestamp => Epoch.FromNanoseconds(ParticipantTimestampNanoseconds);

    /// <summary>
    /// The moment the trade reporting facility received this quote, converted from
    /// <see cref="TrfTimestampNanoseconds"/>, or <see langword="null"/> when the quote did not pass
    /// through one.
    /// </summary>
    [JsonIgnore]
    public Instant? TrfTimestamp => TrfTimestampNanoseconds is { } nanoseconds ? Epoch.FromNanoseconds(nanoseconds) : null;
}
