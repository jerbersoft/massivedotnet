using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="Trade"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct Trade
{
    /// <summary>The moment the SIP received this trade, converted from <see cref="SipTimestampNanoseconds"/>.</summary>
    [JsonIgnore]
    public Instant SipTimestamp => Epoch.FromNanoseconds(SipTimestampNanoseconds);

    /// <summary>
    /// The moment the exchange generated this trade, converted from
    /// <see cref="ParticipantTimestampNanoseconds"/>, or <see langword="null"/> when the trade carries
    /// none, as an OTC trade reported through the FINRA ORF does.
    /// </summary>
    [JsonIgnore]
    public Instant? ParticipantTimestamp => ParticipantTimestampNanoseconds is { } nanoseconds ? Epoch.FromNanoseconds(nanoseconds) : null;

    /// <summary>
    /// The moment the trade reporting facility received this trade, converted from
    /// <see cref="TrfTimestampNanoseconds"/>, or <see langword="null"/> when the trade did not pass
    /// through one.
    /// </summary>
    [JsonIgnore]
    public Instant? TrfTimestamp => TrfTimestampNanoseconds is { } nanoseconds ? Epoch.FromNanoseconds(nanoseconds) : null;
}
