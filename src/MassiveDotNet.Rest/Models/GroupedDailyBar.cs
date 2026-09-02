using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="GroupedDailyBar"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct GroupedDailyBar
{
    /// <summary>The start of the trading day, converted from <see cref="TimestampMilliseconds"/>.</summary>
    /// <remarks>
    /// The raw <see cref="long"/> is what gets deserialized and stored; this conversion happens
    /// only when read, so a whole-market response costs nothing until a value is actually wanted.
    /// </remarks>
    [JsonIgnore]
    public Instant Timestamp => Epoch.FromMilliseconds(TimestampMilliseconds);
}
