using NodaTime;

namespace MassiveDotNet;

/// <summary>
/// Configures the SDK's bounded retry. Assign an instance to
/// <see cref="MassiveClientOptions.Retry"/> to enable it; retry is off while that property is
/// <see langword="null"/>.
/// </summary>
/// <remarks>
/// <para>
/// Only HTTP 429 and 5xx are retried. Every other 4xx describes a request that will fail again
/// however many times it is sent, so retrying one spends quota to reach the same answer.
/// </para>
/// <para>
/// A request carrying content is never retried, since re-sending a consumed body is not
/// guaranteed to succeed. Every route the SDK exposes is a GET, so this costs nothing today.
/// </para>
/// </remarks>
public sealed class MassiveRetryOptions
{
    /// <summary>
    /// The total number of attempts, including the first. Defaults to <c>3</c>, so a failing
    /// request is sent three times rather than four.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// The backoff before the second attempt, doubled by <see cref="BackoffMultiplier"/> for each
    /// attempt after that and capped at <see cref="MaxBackoff"/>. Defaults to 500 milliseconds.
    /// </summary>
    public Duration InitialBackoff { get; set; } = Duration.FromMilliseconds(500);

    /// <summary>
    /// The ceiling on a single backoff. Defaults to 30 seconds.
    /// </summary>
    /// <remarks>
    /// This also bounds how long a server's <c>Retry-After</c> is honoured. A hint longer than
    /// this is not waited out: the 429 surfaces with
    /// <see cref="MassiveRateLimitExceededException.RetryAfter"/> intact, so the caller decides
    /// whether to wait rather than having their task held for a period the SDK did not choose.
    /// </remarks>
    public Duration MaxBackoff { get; set; } = Duration.FromSeconds(30);

    /// <summary>
    /// The factor each backoff is multiplied by for the following attempt. Defaults to <c>2.0</c>.
    /// Must be at least <c>1.0</c>, since a shrinking backoff retries hardest when the server is
    /// least able to answer.
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Whether to spread each backoff randomly across the upper half of its computed value.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Without jitter, every client throttled by the same outage retries in lockstep and
    /// reconstructs the spike that caused it. The lower half of the interval is kept as a floor
    /// so a jittered backoff can still never approach zero.
    /// </remarks>
    public bool UseJitter { get; set; } = true;

    internal void Validate()
    {
        if (MaxAttempts < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveRetryOptions)}.{nameof(MaxAttempts)} must be at least 1.");
        }

        if (InitialBackoff < Duration.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveRetryOptions)}.{nameof(InitialBackoff)} must not be negative.");
        }

        if (MaxBackoff < InitialBackoff)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveRetryOptions)}.{nameof(MaxBackoff)} must not be less than " +
                $"{nameof(InitialBackoff)}.");
        }

        if (BackoffMultiplier < 1.0 || double.IsNaN(BackoffMultiplier))
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveRetryOptions)}.{nameof(BackoffMultiplier)} must be at least 1.0.");
        }
    }
}
