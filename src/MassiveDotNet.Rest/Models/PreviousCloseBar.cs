using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="PreviousCloseBar"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct PreviousCloseBar
{
    /// <summary>The start of the previous trading day, converted from <see cref="TimestampMilliseconds"/>.</summary>
    /// <remarks>
    /// The raw <see cref="long"/> is what gets deserialized and stored; the conversion happens only
    /// when read (decision D5).
    /// </remarks>
    [JsonIgnore]
    public Instant Timestamp => Epoch.FromMilliseconds(TimestampMilliseconds);
}
