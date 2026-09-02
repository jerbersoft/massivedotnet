using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Converts the raw epoch values wire DTOs store into instants, at the precision each wire field
/// carries.
/// </summary>
/// <remarks>
/// Models store the epoch value as a <see cref="long"/> and compute the <see cref="Instant"/> only
/// when read (decision D5), so this is called from computed properties, never at deserialization.
/// Nanosecond precision is kept: <see cref="Instant"/> resolves to the nanosecond and a
/// <see cref="Duration"/> built from nanoseconds loses nothing, whereas
/// <see cref="Instant.FromUnixTimeTicks"/> would truncate to 100 ns. Internal to the REST package
/// because core has no reason to know about wire epochs.
/// </remarks>
internal static class Epoch
{
    /// <summary>Converts Unix milliseconds, the unit of aggregate and indicator timestamps.</summary>
    /// <param name="milliseconds">Milliseconds since the Unix epoch.</param>
    /// <returns>The instant.</returns>
    public static Instant FromMilliseconds(long milliseconds) => Instant.FromUnixTimeMilliseconds(milliseconds);

    /// <summary>Converts Unix nanoseconds, the unit of every tick-level timestamp.</summary>
    /// <param name="nanoseconds">Nanoseconds since the Unix epoch.</param>
    /// <returns>The instant, to the nanosecond.</returns>
    public static Instant FromNanoseconds(long nanoseconds) => NodaConstants.UnixEpoch + Duration.FromNanoseconds(nanoseconds);
}
