using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="SnapshotMinute"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct SnapshotMinute
{
    /// <summary>The start of the minute window, converted from <see cref="TimestampMilliseconds"/>.</summary>
    /// <remarks>The conversion happens only when read (decision D5).</remarks>
    [JsonIgnore]
    public Instant Timestamp => Epoch.FromMilliseconds(TimestampMilliseconds);
}
