using System.Threading.RateLimiting;
using NodaTime;

namespace MassiveDotNet.Http;

/// <summary>
/// Paces outbound requests against a client-side allowance, so a caller stays inside their tier's
/// limit rather than discovering it through HTTP 429.
/// </summary>
/// <remarks>
/// <para>
/// Permits refill continuously rather than all at once on a window boundary. A burst up to
/// <see cref="MassiveRateLimitOptions.PermitsPerWindow"/> goes out immediately and everything
/// after it is paced, which keeps a small batch fast while never exceeding the configured rate
/// over a sustained run.
/// </para>
/// <para>
/// This handler belongs <em>innermost</em>, below any retry: a retried attempt is a request the
/// server counts too, so it must spend a permit like any other (D30).
/// </para>
/// </remarks>
public sealed class MassiveRateLimitHandler : DelegatingHandler
{
    private readonly TokenBucketRateLimiter _limiter;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="MassiveRateLimitHandler"/> class.</summary>
    /// <param name="options">The allowance to enforce.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="options"/> holds a value it cannot act on.</exception>
    public MassiveRateLimitHandler(MassiveRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        // One permit per slice of the window rather than the whole allowance at each boundary.
        // A client cannot see where the server's own window falls, so refilling in a block risks
        // a burst at the end of one landing in the same server window as the next block.
        Duration period = options.Window / options.PermitsPerWindow;

        // A period that rounds to nothing would make the limiter spin; one tick is the smallest
        // interval the timer can express, and at that rate the allowance is not the constraint.
        if (period < Duration.FromTicks(1))
        {
            period = Duration.FromTicks(1);
        }

        _limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = options.PermitsPerWindow,
            TokensPerPeriod = 1,

            // Boundary crossing (produce): converted inline, so the BCL type is never named.
            ReplenishmentPeriod = period.ToTimeSpan(),
            AutoReplenishment = true,
            QueueLimit = options.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    }

    /// <summary>
    /// How many requests may be sent right now without waiting. Exposed so a caller can report
    /// their own headroom; it is a sample, and may be stale by the time it is read.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The handler has been disposed.</exception>
    public int AvailablePermits
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return (int)_limiter.GetStatistics()!.CurrentAvailablePermits;
        }
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always. The SDK issues no synchronous requests.</exception>
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            $"{nameof(MassiveRateLimitHandler)} does not support synchronous sends: waiting for a permit " +
            "would have to block the calling thread. Use the asynchronous API.");

    /// <inheritdoc />
    /// <exception cref="MassiveRateLimitExceededException">
    /// The configured queue is full, so the request was rejected without being sent.
    /// </exception>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using RateLimitLease lease = await _limiter
            .AcquireAsync(permitCount: 1, cancellationToken)
            .ConfigureAwait(false);

        if (!lease.IsAcquired)
        {
            // Boundary crossing (consume): the pattern match keeps the temporary implicitly typed.
            Duration? retryAfter =
                lease.TryGetMetadata(MetadataName.RetryAfter, out var hint) && hint is { } delta
                    ? Duration.FromTimeSpan(delta)
                    : null;

            // Named as client-side because the server never saw this request. A caller reading
            // the message in a log otherwise has no way to tell the two apart.
            throw new MassiveRateLimitExceededException(
                "The request was rejected by the SDK's client-side rate limiter before it was sent, " +
                "because the configured queue is full. Raise MassiveRateLimitOptions.QueueLimit to " +
                "wait for a permit instead, or lower the request rate.",
                retryAfter);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            // The limiter owns a replenishment timer, so leaking one leaks a recurring callback
            // for the lifetime of the process.
            _disposed = true;
            _limiter.Dispose();
        }

        base.Dispose(disposing);
    }
}
