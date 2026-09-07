using NodaTime;

namespace MassiveDotNet.WebSocket;

/// <summary>Backoff for reconnecting a dropped stream.</summary>
public sealed class MassiveStreamReconnectOptions
{
    /// <summary>How long to wait before the first reconnect attempt. Defaults to 500 ms.</summary>
    public Duration InitialBackoff { get; set; } = Duration.FromMilliseconds(500);

    /// <summary>The ceiling on the backoff delay. Defaults to 30 seconds.</summary>
    public Duration MaxBackoff { get; set; } = Duration.FromSeconds(30);

    /// <summary>The factor each successive delay is multiplied by. Defaults to 2.0.</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// The proportion of random jitter applied to each delay, so a fleet of clients reconnecting
    /// after the same outage does not synchronise. Defaults to 0.2, meaning plus or minus 20%.
    /// </summary>
    public double Jitter { get; set; } = 0.2;

    /// <summary>Throws if the options are not in a usable state.</summary>
    /// <exception cref="InvalidOperationException">A value cannot be acted on.</exception>
    public void Validate()
    {
        if (InitialBackoff <= Duration.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamReconnectOptions)}.{nameof(InitialBackoff)} must be positive.");
        }

        if (MaxBackoff < InitialBackoff)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamReconnectOptions)}.{nameof(MaxBackoff)} must be at least "
                + $"{nameof(InitialBackoff)}.");
        }

        if (BackoffMultiplier < 1.0)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamReconnectOptions)}.{nameof(BackoffMultiplier)} must be at "
                + "least 1.0, or the delay shrinks on every attempt.");
        }

        if (Jitter is < 0.0 or > 1.0)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamReconnectOptions)}.{nameof(Jitter)} must be between 0 and 1.");
        }
    }
}
