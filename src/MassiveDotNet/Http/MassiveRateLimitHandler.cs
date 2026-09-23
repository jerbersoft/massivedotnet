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
    // How often the handler asks the limiter to replenish. This sets only how promptly a queued
    // request is released; it is not the rate, which the limiter derives from the replenishment
    // period below at tick precision (D43). Both are private constants rather than
    // MassiveRateLimitOptions properties for D40's reason: that would be public surface under
    // rules 10 and 14 for numbers no consumer can tune.
    //
    // The floor is 5ms because System.Threading.Timer is given whole milliseconds and stores a
    // period of 0 as "fire once", so anything under 1ms would stop the ticker after one firing --
    // the defect this replaced, arriving by a second route. Two hundred wakeups a second is noise
    // beside the thousand-plus requests a second a caller who configured that rate is issuing.
    private static readonly Duration MinTickInterval = Duration.FromMilliseconds(5);

    // The cap keeps a slow allowance releasing promptly. At five a minute the period is twelve
    // seconds, so without it a permit that misses a tick waits another six -- measured at 42.0s
    // against an ideal 36.0. The cost is one wakeup a second while idle, which buys that back.
    private static readonly Duration MaxTickInterval = Duration.FromSeconds(1);

    private readonly TokenBucketRateLimiter _limiter;
    private readonly Timer _ticker;
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

        // Reachable only above roughly 600 million permits a minute, where the slice rounds away
        // entirely and the limiter would refuse a period of zero. One tick is the smallest it can
        // hold, and at ten million requests a second the allowance is not the constraint.
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

            // Replenishment is driven by the ticker below rather than by the limiter's own timer,
            // and the difference is arithmetic rather than scheduling. The automatic path adds a
            // flat TokensPerPeriod on every firing and trusts the timer to be close enough, so
            // the rate becomes whatever the timer can express: truncated to whole milliseconds,
            // stopped after one firing below a millisecond, and permanently a permit short for
            // every firing a pause delayed. The manual path adds the fill rate times the time
            // that actually elapsed, computed from this period's ticks, so a fractional period is
            // honoured exactly and a late tick catches up (D43).
            AutoReplenishment = false,
            QueueLimit = options.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });

        Duration half = period / 2;
        Duration interval =
            half < MinTickInterval ? MinTickInterval :
            half > MaxTickInterval ? MaxTickInterval :
            half;

        // What the clamp buys is a bound on lateness, not a rate. The limiter ignores a replenish
        // arriving before a full period has elapsed and dates the next one from the moment it
        // accepted rather than from when it was due, so a tick scheduled exactly one period after
        // the last one lands a hair short from time to time and is skipped; the permit then waits
        // for the tick after that. One interval is therefore the most a permit can be late, which
        // is why the interval is clamped at both ends rather than simply tracking the period.
        // Ticking early is free -- the limiter carries the time a skipped tick did not spend --
        // so the only cost of a shorter interval is the wakeups.
        //
        // Measured on the free-tier default, whose period is 12 seconds, permits 6/7/8 arrived at
        // 12.0/24.0/37.0s against an ideal 12/24/36: one interval late, once. The same run with
        // the cap removed (a 6s interval) gave 42.0s, and with no clamp at all (12s) it happened
        // to give 36.0s. The clamped rule is the one whose worst case is stated rather than lucky.
        //
        // Boundary crossing (produce): converted inline, so the BCL type is never named.
        _ticker = new Timer(
            static state => ((TokenBucketRateLimiter)state!).TryReplenish(),
            _limiter,
            interval.ToTimeSpan(),
            interval.ToTimeSpan());
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
            // Leaking either of these leaks a recurring callback for the lifetime of the process.
            // The ticker goes first, and a firing already under way when it does is harmless:
            // TryReplenish on a disposed limiter returns without touching anything, so the race
            // cannot put an unhandled exception on a thread-pool thread.
            _disposed = true;
            _ticker.Dispose();
            _limiter.Dispose();
        }

        base.Dispose(disposing);
    }
}
