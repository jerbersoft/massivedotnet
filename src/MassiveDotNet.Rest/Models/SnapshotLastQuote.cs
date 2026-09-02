using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="SnapshotLastQuote"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct SnapshotLastQuote
{
    /// <summary>The moment the SIP received this quote, converted from <see cref="SipTimestampNanoseconds"/>.</summary>
    /// <remarks>The conversion happens only when read (decision D5).</remarks>
    [JsonIgnore]
    public Instant SipTimestamp => Epoch.FromNanoseconds(SipTimestampNanoseconds);
}
