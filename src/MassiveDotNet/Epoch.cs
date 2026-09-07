using NodaTime;

namespace MassiveDotNet;

/// <summary>
/// Converts the raw epoch values wire DTOs store into instants, at the precision each wire field
/// carries.
/// </summary>
/// <remarks>
/// Models store the epoch value as a <see cref="long"/> and compute the <see cref="Instant"/> only
/// when read (decision D5), so this is called from computed properties, never at deserialization.
/// Nanosecond precision is kept: <see cref="Instant"/> resolves to the nanosecond and a
/// <see cref="Duration"/> built from nanoseconds loses nothing, whereas
/// <see cref="Instant.FromUnixTimeTicks"/> would truncate to 100 ns. It lives in core because both
/// the REST and WebSocket packages read wire epochs, and the two units do not agree: REST v3 sends
/// nanoseconds where the streaming wire sends milliseconds for the same conceptual field (D-W11).
/// </remarks>
public static class Epoch
{
    /// <summary>Converts Unix milliseconds, the unit of aggregate, indicator, and streaming timestamps.</summary>
    /// <param name="milliseconds">Milliseconds since the Unix epoch.</param>
    /// <returns>The instant.</returns>
    public static Instant FromMilliseconds(long milliseconds) => Instant.FromUnixTimeMilliseconds(milliseconds);

    /// <summary>Converts Unix nanoseconds, the unit of every REST tick-level timestamp.</summary>
    /// <param name="nanoseconds">Nanoseconds since the Unix epoch.</param>
    /// <returns>The instant, to the nanosecond.</returns>
    public static Instant FromNanoseconds(long nanoseconds) => NodaConstants.UnixEpoch + Duration.FromNanoseconds(nanoseconds);
}
