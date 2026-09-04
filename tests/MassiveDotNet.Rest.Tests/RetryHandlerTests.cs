using System.Diagnostics;
using System.Net;
using MassiveDotNet.Http;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The opt-in bounded retry (D30). Backoffs are set to a millisecond throughout except where a
/// test is about the delay itself, so the suite stays fast and asserts on attempt counts rather
/// than on wall-clock timing it cannot control.
/// </summary>
public sealed class RetryHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MassiveRetryOptions Fast(int maxAttempts = 3) => new()
    {
        MaxAttempts = maxAttempts,
        InitialBackoff = Duration.FromMilliseconds(1),
        MaxBackoff = Duration.FromMilliseconds(4),
    };

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(
        HttpMessageHandler inner,
        MassiveRetryOptions retry)
    {
        MassiveRetryHandler handler = new(retry) { InnerHandler = inner };
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task SendsOnceWhenTheFirstAttemptSucceeds()
    {
        ScriptedHandler inner = new(HttpStatusCode.OK);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(inner, Fast());

        using (client)
        using (transport)
        {
            await client.Reference.ListTickersAsync(cancellationToken: Ct);
        }

        Assert.Equal(1, inner.RequestCount);
    }

    [Fact]
    public async Task StopsRetryingOnceAnAttemptSucceeds()
    {
        ScriptedHandler inner = new(
            HttpStatusCode.InternalServerError,
            HttpStatusCode.OK);

        (MassiveRestClient client, MassiveHttpTransport transport) = Create(inner, Fast());

        using (client)
        using (transport)
        {
            await client.Reference.ListTickersAsync(cancellationToken: Ct);
        }

        Assert.Equal(2, inner.RequestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task RetriesToTheAttemptCapAndThenSurfacesTheFailure(HttpStatusCode status)
    {
        ScriptedHandler inner = new(status);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(inner, Fast(maxAttempts: 3));

        using (client)
        using (transport)
        {
            // Any MassiveApiException: 429 surfaces as the derived
            // MassiveRateLimitExceededException, which carries the server's hint.
            await Assert.ThrowsAnyAsync<MassiveApiException>(
                () => client.Reference.ListTickersAsync(cancellationToken: Ct));
        }

        // MaxAttempts counts attempts, not retries: three requests, not four.
        Assert.Equal(3, inner.RequestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task NeverRetriesAClientErrorOtherThanTooManyRequests(HttpStatusCode status)
    {
        ScriptedHandler inner = new(status);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(inner, Fast());

        using (client)
        using (transport)
        {
            await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Reference.ListTickersAsync(cancellationToken: Ct));
        }

        Assert.Equal(1, inner.RequestCount);
    }

    [Fact]
    public async Task WaitsTheServerRetryAfterRatherThanItsOwnBackoff()
    {
        ScriptedHandler inner = new(
        [
            (HttpStatusCode.TooManyRequests, Duration.FromMilliseconds(600)),
            (HttpStatusCode.OK, null),
        ]);

        MassiveRetryOptions retry = new()
        {
            MaxAttempts = 2,
            InitialBackoff = Duration.FromMilliseconds(1),
            MaxBackoff = Duration.FromSeconds(5),
        };

        (MassiveRestClient client, MassiveHttpTransport transport) = Create(inner, retry);

        long start = Stopwatch.GetTimestamp();

        using (client)
        using (transport)
        {
            await client.Reference.ListTickersAsync(cancellationToken: Ct);
        }

        // Boundary crossing (consume): the pattern match keeps the temporary implicitly typed.
        Duration elapsed = Stopwatch.GetElapsedTime(start) is { } measured
            ? Duration.FromTimeSpan(measured)
            : Duration.Zero;

        Assert.Equal(2, inner.RequestCount);

        // The computed backoff is a millisecond, so anything near the header's 600ms proves the
        // header won. The bound is loose because a timer only guarantees "at least".
        Assert.True(
            elapsed >= Duration.FromMilliseconds(500),
            $"Expected the 600ms Retry-After to be honoured, but only {elapsed} elapsed.");
    }

    [Fact]
    public async Task SurfacesImmediatelyWhenRetryAfterExceedsTheBackoffCeiling()
    {
        ScriptedHandler inner = new(
        [
            (HttpStatusCode.TooManyRequests, Duration.FromHours(1)),
        ]);

        MassiveRetryOptions retry = new()
        {
            MaxAttempts = 3,
            InitialBackoff = Duration.FromMilliseconds(1),
            MaxBackoff = Duration.FromSeconds(2),
        };

        (MassiveRestClient client, MassiveHttpTransport transport) = Create(inner, retry);

        MassiveRateLimitExceededException exception;

        using (client)
        using (transport)
        {
            exception = await Assert.ThrowsAsync<MassiveRateLimitExceededException>(
                () => client.Reference.ListTickersAsync(cancellationToken: Ct));
        }

        // Parking the caller's task for an hour on a number the server chose is worse than
        // failing, so the 429 surfaces with its hint intact and the caller decides (D30).
        Assert.Equal(1, inner.RequestCount);
        Assert.Equal(Duration.FromHours(1), exception.RetryAfter);
    }

    [Fact]
    public async Task ObservesCancellationDuringTheBackoff()
    {
        ScriptedHandler inner = new(HttpStatusCode.TooManyRequests);

        MassiveRetryOptions retry = new()
        {
            MaxAttempts = 5,
            InitialBackoff = Duration.FromSeconds(30),
            MaxBackoff = Duration.FromSeconds(30),
        };

        (MassiveRestClient client, MassiveHttpTransport transport) = Create(inner, retry);
        using CancellationTokenSource cts = new();

        using (client)
        using (transport)
        {
            Task pending = client.Reference.ListTickersAsync(cancellationToken: cts.Token);
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }

        // Cancelled inside the backoff, so the second attempt was never sent.
        Assert.Equal(1, inner.RequestCount);
    }

    [Fact]
    public async Task DoesNotRetryARequestCarryingContent()
    {
        // Re-sending a request with a consumed body is not guaranteed to work, so retry declines
        // rather than failing obscurely on the second attempt (D30). Every SDK route is a GET,
        // so this guard is what keeps that assumption true rather than merely current.
        ScriptedHandler inner = new(HttpStatusCode.ServiceUnavailable);
        MassiveRetryHandler handler = new(Fast()) { InnerHandler = inner };
        using HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };

        using HttpRequestMessage request = new(HttpMethod.Post, "/v3/reference/tickers")
        {
            Content = new StringContent("{}"),
        };

        using HttpResponseMessage response = await httpClient.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, inner.RequestCount);
    }

    [Fact]
    public async Task AppendsTheApiKeyExactlyOnceAcrossRetriesUnderTheQueryScheme()
    {
        // The ordering pin (D30). MassiveAuthenticationHandler mutates the request URI under the
        // query scheme, so retry must sit INSIDE it. Placed outside, each attempt appends the key
        // again and the request still succeeds — the key reaching the access log three times is
        // the only symptom, which is why this is asserted rather than left to review.
        ScriptedHandler inner = new(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK);

        MassiveRetryHandler retry = new(Fast()) { InnerHandler = inner };
        MassiveAuthenticationHandler auth =
            new("secret-key", MassiveAuthenticationScheme.QueryString) { InnerHandler = retry };

        using HttpClient httpClient = new(auth) { BaseAddress = MassiveEndpoints.Production };
        using MassiveHttpTransport transport = new(httpClient);
        using MassiveRestClient client = new(transport);

        await client.Reference.ListTickersAsync(cancellationToken: Ct);

        Assert.Equal(3, inner.RequestCount);

        foreach (Uri? uri in inner.RequestUris)
        {
            string query = uri?.Query ?? string.Empty;
            int occurrences = query.Split("apiKey=", StringSplitOptions.None).Length - 1;

            Assert.True(occurrences == 1, $"Expected exactly one apiKey in '{query}', found {occurrences}.");
        }
    }
}
