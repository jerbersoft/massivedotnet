using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="MacdValue"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct MacdValue
{
    /// <summary>
    /// The moment of the last aggregate used to compute this point, converted from
    /// <see cref="TimestampMilliseconds"/>.
    /// </summary>
    /// <remarks>The conversion happens only when read (decision D5).</remarks>
    [JsonIgnore]
    public Instant Timestamp => Epoch.FromMilliseconds(TimestampMilliseconds);
}
