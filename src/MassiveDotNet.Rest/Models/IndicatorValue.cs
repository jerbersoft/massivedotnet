using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="IndicatorValue"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct IndicatorValue
{
    /// <summary>
    /// The moment of the last aggregate used to compute this value, converted from
    /// <see cref="TimestampMilliseconds"/>.
    /// </summary>
    /// <remarks>
    /// The raw <see cref="long"/> is what gets deserialized and stored; this conversion happens
    /// only when read, so a long series costs nothing until the value is actually wanted.
    /// </remarks>
    [JsonIgnore]
    public Instant Timestamp => Epoch.FromMilliseconds(TimestampMilliseconds);
}
