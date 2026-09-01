using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="Agg"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct Agg
{
    /// <summary>
    /// The start of the aggregate window, converted from
    /// <see cref="TimestampMilliseconds"/>.
    /// </summary>
    /// <remarks>
    /// The raw <see cref="long"/> is what gets deserialized and stored; this conversion happens
    /// only when read, so a large series costs nothing until the value is actually wanted.
    /// </remarks>
    [JsonIgnore]
    public Instant Timestamp => Instant.FromUnixTimeMilliseconds(TimestampMilliseconds);
}
