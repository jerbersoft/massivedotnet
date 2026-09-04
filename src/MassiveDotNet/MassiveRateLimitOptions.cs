using NodaTime;

namespace MassiveDotNet;

/// <summary>
/// Configures the SDK's client-side throttle. Assign an instance to
/// <see cref="MassiveClientOptions.RateLimit"/> to enable it; throttling is off while that
/// property is <see langword="null"/>.
/// </summary>
/// <remarks>
/// <para>
/// The throttle is opt-in because the SDK cannot know a caller's entitlement. A default tuned for
/// the free tier's five requests a minute would throttle a paid key to a fraction of its
/// allowance, and any other default would 429 the callers it was meant to protect.
/// </para>
/// <para>
/// Permits refill continuously rather than all at once on a window boundary, so a burst up to
/// <see cref="PermitsPerWindow"/> is allowed immediately and the requests after it are paced.
/// A fixed window would let a burst at the end of one window and another at the start of the next
/// land inside a single window on the server's own clock, which no client can observe.
/// </para>
/// </remarks>
public sealed class MassiveRateLimitOptions
{
    /// <summary>
    /// How many requests are permitted per <see cref="Window"/>. Defaults to <c>5</c>, the free
    /// tier's allowance; set it to match your own tier, which the SDK has no way to discover.
    /// </summary>
    public int PermitsPerWindow { get; set; } = 5;

    /// <summary>
    /// The period <see cref="PermitsPerWindow"/> is measured over. Defaults to one minute, which
    /// is how the platform states its own limits.
    /// </summary>
    public Duration Window { get; set; } = Duration.FromMinutes(1);

    /// <summary>
    /// How many requests may wait for a permit before further ones are rejected outright.
    /// Defaults to <see cref="int.MaxValue"/>, so a caller who enables throttling waits rather
    /// than fails.
    /// </summary>
    /// <remarks>
    /// Unbounded queueing is safe here in a way it would not be in a server: only requests the
    /// caller has actually issued can queue, so the depth is bounded by their own concurrency.
    /// Set it to <c>0</c> to fail fast instead — a rejected request throws
    /// <see cref="MassiveRateLimitExceededException"/> without reaching the network.
    /// </remarks>
    public int QueueLimit { get; set; } = int.MaxValue;

    internal void Validate()
    {
        if (PermitsPerWindow < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveRateLimitOptions)}.{nameof(PermitsPerWindow)} must be at least 1.");
        }

        if (Window <= Duration.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveRateLimitOptions)}.{nameof(Window)} must be greater than zero.");
        }

        if (QueueLimit < 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveRateLimitOptions)}.{nameof(QueueLimit)} must not be negative.");
        }
    }
}
