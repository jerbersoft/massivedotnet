using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="TickerSnapshot"/>, alongside the generated wire properties.
/// </summary>
public sealed partial record TickerSnapshot
{
    /// <summary>
    /// The moment this snapshot was last updated, converted from <see cref="UpdatedNanoseconds"/>,
    /// or <see langword="null"/> when the service sent none.
    /// </summary>
    /// <remarks>The conversion happens only when read (decision D5).</remarks>
    [JsonIgnore]
    public Instant? Updated => UpdatedNanoseconds is { } nanoseconds ? Epoch.FromNanoseconds(nanoseconds) : null;
}
