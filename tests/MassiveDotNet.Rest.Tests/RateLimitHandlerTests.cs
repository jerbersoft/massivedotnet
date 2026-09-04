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
}
