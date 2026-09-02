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
    public Instant SipTimestamp => FromNanoseconds(SipTimestampNanoseconds);

    /// <summary>The moment the exchange generated this trade, converted from <see cref="ParticipantTimestampNanoseconds"/>.</summary>
    [JsonIgnore]
    public Instant ParticipantTimestamp => FromNanoseconds(ParticipantTimestampNanoseconds);

    /// <summary>
    /// The moment the trade reporting facility received this trade, converted from
    /// <see cref="TrfTimestampNanoseconds"/>, or <see langword="null"/> when the trade did not pass
    /// through one.
    /// </summary>
    [JsonIgnore]
    public Instant? TrfTimestamp => TrfTimestampNanoseconds is { } nanoseconds ? FromNanoseconds(nanoseconds) : null;

    // Nanosecond precision is kept: Instant resolves to the nanosecond and a Duration built from
    // nanoseconds loses nothing, whereas Instant.FromUnixTimeTicks would truncate to 100 ns. The
    // conversion happens only when read (decision D5).
    private static Instant FromNanoseconds(long nanoseconds) =>
        NodaConstants.UnixEpoch + Duration.FromNanoseconds(nanoseconds);
}
