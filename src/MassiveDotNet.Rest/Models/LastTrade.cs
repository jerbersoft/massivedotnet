using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="LastTrade"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct LastTrade
{
    /// <summary>The moment the SIP received this trade, converted from <see cref="SipTimestampNanoseconds"/>.</summary>
    [JsonIgnore]
    public Instant SipTimestamp => Epoch.FromNanoseconds(SipTimestampNanoseconds);

    /// <summary>The moment the exchange generated this trade, converted from <see cref="ParticipantTimestampNanoseconds"/>.</summary>
    [JsonIgnore]
    public Instant ParticipantTimestamp => Epoch.FromNanoseconds(ParticipantTimestampNanoseconds);

    /// <summary>
    /// The moment the trade reporting facility received this trade, converted from
    /// <see cref="TrfTimestampNanoseconds"/>, or <see langword="null"/> when the trade did not pass
    /// through one.
    /// </summary>
    [JsonIgnore]
    public Instant? TrfTimestamp => TrfTimestampNanoseconds is { } nanoseconds ? Epoch.FromNanoseconds(nanoseconds) : null;
}
