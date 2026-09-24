using System.Diagnostics;
using System.Net;
using MassiveDotNet.Http;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The opt-in client-side throttle (D30). The free tier allows five requests a minute, so the
/// limiter is the difference between an SDK that works on that tier and one that 429s on the
/// sixth call — but it cannot be the default, since the SDK never learns the caller's tier.
/// </summary>
public sealed class RateLimitHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(
        HttpMessageHandler inner,
        MassiveRateLimitOptions limit)
    {
        MassiveRateLimitHandler handler = new(limit) { InnerHandler = inner };
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task LetsTheInitialBurstThroughWithoutWaiting()
    {
        // The bucket starts full, so a caller's first N calls are not paced. Only what exceeds
        // the allowance waits, which is what makes a modest burst usable.
        ScriptedHandler inner = new(HttpStatusCode.OK);
        MassiveRateLimitOptions limit = new()
        {
            PermitsPerWindow = 5,
            Window = Duration.FromMinutes(1),
        };

        (MassiveRestClient client, MassiveHttpTransport transport) = Create(inner, limit);

        long start = Stopwatch.GetTimestamp();

        using (client)
        using (transport)
        {
            for (int i = 0; i < 5; i++)
            {
                await client.Reference.ListTickersAsync(cancellationToken: Ct);
            }
        }

        Duration elapsed = Stopwatch.GetElapsedTime(start) is { } measured
            ? Duration.FromTimeSpan(measured)
            : Duration.Zero;

        Assert.Equal(5, inner.RequestCount);
        Assert.True(elapsed < Duration.FromSeconds(5), $"The initial burst was paced: {elapsed}.");
    }

    [Fact]
    public async Task PacesTheRequestThatExceedsTheAllowance()
    {
        // Two permits a second replenishes one every 500ms, so the third request waits for a
        // token rather than being sent or rejected.
        ScriptedHandler inner = new(HttpStatusCode.OK);
        MassiveRateLimitOptions limit = new()
        {
            PermitsPerWindow = 2,
            Window = Duration.FromSeconds(1),
        };

        (MassiveRestClient client, MassiveHttpTransport transport) = Create(inner, limit);

        long start = Stopwatch.GetTimestamp();

        using (client)
        using (transport)
        {
            for (int i = 0; i < 3; i++)
            {
                await client.Reference.ListTickersAsync(cancellationToken: Ct);
            }
        }

        Duration elapsed = Stopwatch.GetElapsedTime(start) is { } measured
            ? Duration.FromTimeSpan(measured)
            : Duration.Zero;

        Assert.Equal(3, inner.RequestCount);
        Assert.True(
            elapsed >= Duration.FromMilliseconds(400),
            $"The third request was not paced; only {elapsed} elapsed.");
    }

    [Fact]
    public async Task RejectsRatherThanQueueingWhenTheQueueIsFull()
    {
        // QueueLimit 0 is the fail-fast posture: a caller who would rather handle the throttle
        // themselves than have the SDK hold their task gets an exception instead of a wait.
        ScriptedHandler inner = new(HttpStatusCode.OK);
        MassiveRateLimitOptions limit = new()
        {
            PermitsPerWindow = 1,
            Window = Duration.FromMinutes(5),
            QueueLimit = 0,
        };

        (MassiveRestClient client, MassiveHttpTransport transport) = Create(inner, limit);

        using (client)
        using (transport)
        {
            await client.Reference.ListTickersAsync(cancellationToken: Ct);

            MassiveRateLimitExceededException exception =
                await Assert.ThrowsAsync<MassiveRateLimitExceededException>(
                    () => client.Reference.ListTickersAsync(cancellationToken: Ct));

            // The failure is local, so it must say so: a caller reading this in a log needs to
            // know the server never saw the request.
            Assert.Contains("client-side", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(1, inner.RequestCount);
    }

    [Fact]
    public async Task ObservesCancellationWhileWaitingForAPermit()
    {
        ScriptedHandler inner = new(HttpStatusCode.OK);
        MassiveRateLimitOptions limit = new()
        {
            PermitsPerWindow = 1,
            Window = Duration.FromMinutes(10),
        };

        (MassiveRestClient client, MassiveHttpTransport transport) = Create(inner, limit);
        using CancellationTokenSource cts = new();

        using (client)
        using (transport)
        {
            await client.Reference.ListTickersAsync(cancellationToken: Ct);

            Task pending = client.Reference.ListTickersAsync(cancellationToken: cts.Token);
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }

        Assert.Equal(1, inner.RequestCount);
    }

    [Fact]
    public void DisposesItsLimiterWithTheHandler()
    {
        // The limiter owns a replenishment timer, so leaking one leaks a callback for the
        // process lifetime. The handler owns what it created.
        MassiveRateLimitOptions limit = new()
        {
            PermitsPerWindow = 1,
            Window = Duration.FromMinutes(1),
        };

        MassiveRateLimitHandler handler = new(limit) { InnerHandler = new ScriptedHandler(HttpStatusCode.OK) };
        handler.Dispose();

        Assert.Throws<ObjectDisposedException>(() => handler.AvailablePermits);
    }

    [Fact]
    public void ReportsTheAllowanceItWasConfiguredWith()
    {
        MassiveRateLimitOptions limit = new()
        {
            PermitsPerWindow = 4,
            Window = Duration.FromMinutes(1),
        };

        using MassiveRateLimitHandler handler = new(limit)
        {
            InnerHandler = new ScriptedHandler(HttpStatusCode.OK),
        };

        Assert.Equal(4, handler.AvailablePermits);
    }

    [Fact]
    public async Task KeepsReleasingWhenTheRefillPeriodIsUnderAMillisecond()
    {
        // 1,500 a second is a 0.667ms period. The BCL timer is given whole milliseconds and
        // stores a period of 0 as "fire once", so the limiter refilled once at construction --
        // while the bucket was still full -- and never again. Every request after the initial
        // burst then waited for a permit that was never coming, and QueueLimit's int.MaxValue
        // default means it waited forever with nothing reporting it.
        //
        // All-or-nothing on purpose, which is what lets a wall-clock assertion sit in the
        // offline tier under D31: the broken tree releases essentially nothing here, so the
        // gap being asserted is the whole rate, not a tolerance around it.
        ScriptedHandler inner = new(HttpStatusCode.OK);
        MassiveRateLimitOptions limit = new()
        {
            PermitsPerWindow = 1_500,
            Window = Duration.FromSeconds(1),
        };

        using MassiveRateLimitHandler handler = new(limit) { InnerHandler = inner };
        using HttpMessageInvoker invoker = new(handler);

        await SpendTheBurstAsync(invoker, limit.PermitsPerWindow);

        int released = await CountReleasedAsync(invoker, Duration.FromSeconds(2), queued: 4_000);

        Assert.True(
            released >= 500,
            $"The limiter stopped refilling below a one-millisecond period: {released} requests " +
            "were released in two seconds at a configured 1,500 a second.");
    }

    [Fact]
    public async Task DoesNotExceedTheConfiguredRateWhenTheRefillPeriodIsNotAWholeMillisecond()
    {
        // 400 a second is a 2.5ms period, which the BCL timer truncated to 2ms -- a quarter more
        // requests than the caller asked for, sustained for as long as they kept sending. Every
        // period that is not a whole number of milliseconds over-delivered this way.
        //
        // The assertion is one-sided, and that is the point: a loaded or slow runner can only
        // release FEWER requests than configured, never more, so scheduling noise cannot fail
        // this. It is an upper bound, not the two-sided rate tolerance D31 keeps out of the
        // offline tier.
        ScriptedHandler inner = new(HttpStatusCode.OK);
        MassiveRateLimitOptions limit = new()
        {
            PermitsPerWindow = 400,
            Window = Duration.FromSeconds(1),
        };

        using MassiveRateLimitHandler handler = new(limit) { InnerHandler = inner };
        using HttpMessageInvoker invoker = new(handler);

        await SpendTheBurstAsync(invoker, limit.PermitsPerWindow);

        long start = Stopwatch.GetTimestamp();
        int released = await CountReleasedAsync(invoker, Duration.FromSeconds(2), queued: 2_000);

        Duration elapsed = Stopwatch.GetElapsedTime(start) is { } measured
            ? Duration.FromTimeSpan(measured)
            : Duration.Zero;

        double configured = limit.PermitsPerWindow / limit.Window.TotalSeconds;
        double rate = released / elapsed.TotalSeconds;

        Assert.True(
            rate <= configured * 1.05,
            $"The limiter released {rate:N0} a second against a configured {configured:N0}.");
    }

    [Fact]
    public async Task DoesNotReleaseASecondBurstAfterSittingIdle()
    {
        // The limiter's clock freezes while the bucket is full: TryReplenish returns before it
        // dates itself, so time spent idle stays banked and pays out in one lump the moment a
        // request drains the bucket. The caller then gets their burst, and the whole allowance
        // again immediately after it -- on the free tier, ten requests inside one second against
        // a configured five a minute.
        //
        // Every gate written for #70 spends the burst before it measures anything, so the bucket
        // is never left full and stale and this path went entirely unexercised. This one idles
        // first, which is the whole difference.
        //
        // One-sided like its #70 siblings: an upper bound on what the limiter hands back, which
        // load and jitter can only lower.
        ScriptedHandler inner = new(HttpStatusCode.OK);
        MassiveRateLimitOptions limit = new()
        {
            PermitsPerWindow = 40,
            Window = Duration.FromSeconds(1),
        };

        using MassiveRateLimitHandler handler = new(limit) { InnerHandler = inner };
        using HttpMessageInvoker invoker = new(handler);

        // Longer than the window, with the bucket full and untouched throughout. That is what
        // banks the credit: sixty periods' worth, against a bucket that holds forty.
        //
        // Boundary crossing (produce): converted inline, so the BCL type is never named.
        await Task.Delay(Duration.FromMilliseconds(1_500).ToTimeSpan(), Ct);

        await SpendTheBurstAsync(invoker, limit.PermitsPerWindow);

        // Four tick intervals. The period is 25ms, so honest replenishment owes at most two
        // permits over this window; the regression pays the whole forty-permit allowance on the
        // first tick after the drain.
        await Task.Delay(Duration.FromMilliseconds(50).ToTimeSpan(), Ct);

        int available = handler.AvailablePermits;

        Assert.True(
            available <= 8,
            $"The limiter released a second burst after sitting idle: {available} permits were " +
            $"available 50ms after the initial burst of {limit.PermitsPerWindow} was spent.");
    }

    [Fact]
    public async Task KeepsTheWholeBurstAvailableAfterSittingIdle()
    {
        // The fix above must not be paid for out of the burst, and the obvious form of it is.
        // Nudging the limiter on every tick also stops the second burst, but it spends a permit
        // each time whether or not the credit to replace it has accrued, so a slow allowance sits
        // permanently one short -- measured at four of five on the free tier's own parameters,
        // which is a 20% haircut on the thing the throttle exists to make usable. The nudge is
        // therefore gated on a full period's credit already being banked, which is exactly the
        // condition that makes it net-zero.
        //
        // A gauge read rather than a timing assertion, so a loaded runner has nothing to fail
        // here: the bucket cannot exceed its limit, and nothing is consuming from it.
        MassiveRateLimitOptions limit = new()
        {
            PermitsPerWindow = 40,
            Window = Duration.FromSeconds(1),
        };

        using MassiveRateLimitHandler handler = new(limit)
        {
            InnerHandler = new ScriptedHandler(HttpStatusCode.OK),
        };

        // Boundary crossing (produce): converted inline, so the BCL type is never named.
        await Task.Delay(Duration.FromMilliseconds(1_500).ToTimeSpan(), Ct);

        Assert.Equal(limit.PermitsPerWindow, handler.AvailablePermits);
    }

    /// <summary>
    /// Spends the initial burst. The bucket starts full at
    /// <see cref="MassiveRateLimitOptions.PermitsPerWindow"/>, so the sustained rate is only
    /// observable once those permits are gone.
    /// </summary>
    private static Task SpendTheBurstAsync(HttpMessageInvoker invoker, int permits) =>
        Task.WhenAll(Enumerable
            .Range(0, permits)
            .Select(_ => SendAsync(invoker, CancellationToken.None)));

    /// <summary>
    /// Queues far more requests than <paramref name="window"/> could release and counts how many
    /// the limiter let through before the window closed.
    /// </summary>
    private static async Task<int> CountReleasedAsync(HttpMessageInvoker invoker, Duration window, int queued)
    {
        using CancellationTokenSource expiry = new();

        // Boundary crossing (produce): converted inline, so the BCL type is never named.
        expiry.CancelAfter(window.ToTimeSpan());

        int released = 0;

        await Task.WhenAll(Enumerable.Range(0, queued).Select(async _ =>
        {
            try
            {
                await SendAsync(invoker, expiry.Token);
                Interlocked.Increment(ref released);
            }
            catch (OperationCanceledException)
            {
                // The window closed while this one was still queued, which is the normal way
                // for all but the released requests to end.
            }
        }));

        return released;
    }

    /// <summary>
    /// Drives the handler directly rather than through <see cref="MassiveRestClient"/>: these two
    /// tests spend a burst of hundreds of permits before they measure anything, and a URI build
    /// per request would dominate what is meant to be a timing measurement.
    /// </summary>
    private static async Task SendAsync(HttpMessageInvoker invoker, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, MassiveEndpoints.Production);
        using HttpResponseMessage response = await invoker.SendAsync(request, cancellationToken);
    }
}
