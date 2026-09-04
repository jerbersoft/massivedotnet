using System.Net;
using MassiveDotNet.Http;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The options surface for D30 and the pipeline the transport composes from it. Both features
/// are off unless the caller sets their option, and the order the pipeline composes them in is
/// load-bearing rather than incidental.
/// </summary>
public sealed class ResilienceOptionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MassiveClientOptions Valid() => new() { ApiKey = "test-key" };

    [Fact]
    public void LeavesBothFeaturesOffByDefault()
    {
        MassiveClientOptions options = Valid();

        Assert.Null(options.RateLimit);
        Assert.Null(options.Retry);
    }

    [Fact]
    public async Task SendsOnceOnAFailureWhenRetryIsNotConfigured()
    {
        // The acceptance criterion for #5: nothing changes for a consumer who does not opt in.
        ScriptedHandler inner = new(HttpStatusCode.ServiceUnavailable);

        using HttpMessageHandler pipeline = MassiveHttpTransport.CreateHandlerPipeline(Valid(), inner);
        using HttpClient httpClient = new(pipeline, disposeHandler: false)
        {
            BaseAddress = MassiveEndpoints.Production,
        };

        using MassiveHttpTransport transport = new(httpClient);
        using MassiveRestClient client = new(transport);

        await Assert.ThrowsAsync<MassiveApiException>(
            () => client.Reference.ListTickersAsync(cancellationToken: Ct));

        Assert.Equal(1, inner.RequestCount);
    }

    [Fact]
    public async Task ComposesAuthOutsideRetrySoTheKeyIsAppendedOnce()
    {
        // The pipeline is the one place the order is expressed for core (D30). Asserted through
        // behaviour rather than by walking InnerHandler, so a reordering cannot pass by
        // preserving the shape while breaking the property the shape exists for.
        ScriptedHandler inner = new(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK);

        MassiveClientOptions options = new()
        {
            ApiKey = "test-key",
            AuthenticationScheme = MassiveAuthenticationScheme.QueryString,
            Retry = new MassiveRetryOptions
            {
                MaxAttempts = 2,
                InitialBackoff = Duration.FromMilliseconds(1),
                MaxBackoff = Duration.FromMilliseconds(2),
            },
        };

        using HttpMessageHandler pipeline = MassiveHttpTransport.CreateHandlerPipeline(options, inner);
        using HttpClient httpClient = new(pipeline, disposeHandler: false)
        {
            BaseAddress = MassiveEndpoints.Production,
        };

        using MassiveHttpTransport transport = new(httpClient);
        using MassiveRestClient client = new(transport);

        await client.Reference.ListTickersAsync(cancellationToken: Ct);

        Assert.Equal(2, inner.RequestCount);

        foreach (Uri? uri in inner.RequestUris)
        {
            int occurrences = (uri?.Query ?? string.Empty).Split("apiKey=", StringSplitOptions.None).Length - 1;
            Assert.True(occurrences == 1, $"Expected one apiKey in '{uri?.Query}', found {occurrences}.");
        }
    }

    [Fact]
    public async Task SpendsAPermitOnEveryAttemptIncludingRetries()
    {
        // The limiter sits innermost because a retry is a request the server counts too. Two
        // permits and two attempts must exhaust the allowance exactly.
        ScriptedHandler inner = new(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);

        MassiveClientOptions options = new()
        {
            ApiKey = "test-key",
            Retry = new MassiveRetryOptions
            {
                MaxAttempts = 2,
                InitialBackoff = Duration.FromMilliseconds(1),
                MaxBackoff = Duration.FromMilliseconds(2),
            },
            RateLimit = new MassiveRateLimitOptions
            {
                PermitsPerWindow = 2,
                Window = Duration.FromMinutes(10),
                QueueLimit = 0,
            },
        };

        using HttpMessageHandler pipeline = MassiveHttpTransport.CreateHandlerPipeline(options, inner);
        using HttpClient httpClient = new(pipeline, disposeHandler: false)
        {
            BaseAddress = MassiveEndpoints.Production,
        };

        using MassiveHttpTransport transport = new(httpClient);
        using MassiveRestClient client = new(transport);

        await client.Reference.ListTickersAsync(cancellationToken: Ct);
        Assert.Equal(2, inner.RequestCount);

        // Both permits went to the two attempts above, so the next call has none left.
        await Assert.ThrowsAsync<MassiveRateLimitExceededException>(
            () => client.Reference.ListTickersAsync(cancellationToken: Ct));

        Assert.Equal(2, inner.RequestCount);
    }

    [Fact]
    public async Task ThrottlesEveryPageOfATraversalIndependently()
    {
        // A traversal is N requests, not one, so each page must spend its own permit. This is the
        // one place the SDK's two long-running features meet: EnumerateAsync holds an async
        // iterator open across every page, and the limiter awaits inside it.
        PagingStubHandler pages = new(
            """{"results":[1,2],"next_url":"https://api.massive.com/v3/reference/things?cursor=a","status":"OK"}""",
            """{"results":[3,4],"next_url":"https://api.massive.com/v3/reference/things?cursor=b","status":"OK"}""",
            """{"results":[5,6],"status":"OK"}""");

        MassiveClientOptions options = new()
        {
            ApiKey = "test-key",
            RateLimit = new MassiveRateLimitOptions
            {
                PermitsPerWindow = 3,
                Window = Duration.FromMinutes(10),
                QueueLimit = 0,
            },
        };

        using HttpMessageHandler pipeline = MassiveHttpTransport.CreateHandlerPipeline(options, pages);
        using HttpClient httpClient = new(pipeline, disposeHandler: false)
        {
            BaseAddress = MassiveEndpoints.Production,
        };

        using MassiveHttpTransport transport = new(httpClient);

        List<int> seen = [];
        await foreach (int value in transport.EnumerateAsync<FakePage, int>(
            "/v3/reference/things?limit=2", TestJsonContext.Default.FakePage, Ct))
        {
            seen.Add(value);
        }

        Assert.Equal([1, 2, 3, 4, 5, 6], seen);
        Assert.Equal(3, pages.Requests.Count);

        // Three pages spent the three permits, so a fourth request has none left. A traversal
        // that somehow shared one permit across its pages would leave headroom here.
        await Assert.ThrowsAsync<MassiveRateLimitExceededException>(
            () => transport.GetAsync("/v3/reference/things", TestJsonContext.Default.FakePage, Ct));
    }

    [Fact]
    public void DisposingThePipelineReachesTheLimiterNestedInsideIt()
    {
        // The limiter owns a replenishment timer, so one left alive keeps a recurring callback for
        // the lifetime of the process. The options constructor hands its pipeline to an HttpClient
        // with disposeHandler: true and relies on disposal walking the whole chain from the
        // outermost handler down — this is that walk, asserted rather than assumed.
        MassiveRateLimitOptions limit = new()
        {
            PermitsPerWindow = 1,
            Window = Duration.FromMinutes(1),
        };

        MassiveRateLimitHandler limiter = new(limit)
        {
            InnerHandler = new ScriptedHandler(HttpStatusCode.OK),
        };

        // Passed as the innermost handler, so the pipeline wraps this exact instance rather than
        // building a second limiter whose disposal nothing here could observe.
        HttpMessageHandler pipeline = MassiveHttpTransport.CreateHandlerPipeline(Valid(), limiter);

        Assert.Equal(1, limiter.AvailablePermits);

        pipeline.Dispose();

        Assert.Throws<ObjectDisposedException>(() => limiter.AvailablePermits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsANonPositiveAttemptCap(int maxAttempts)
    {
        MassiveClientOptions options = Valid();
        options.Retry = new MassiveRetryOptions { MaxAttempts = maxAttempts };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(MassiveRetryOptions.MaxAttempts), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsABackoffCeilingBelowTheInitialBackoff()
    {
        MassiveClientOptions options = Valid();
        options.Retry = new MassiveRetryOptions
        {
            InitialBackoff = Duration.FromSeconds(10),
            MaxBackoff = Duration.FromSeconds(1),
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(MassiveRetryOptions.MaxBackoff), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsANegativeInitialBackoff()
    {
        MassiveClientOptions options = Valid();
        options.Retry = new MassiveRetryOptions { InitialBackoff = Duration.FromSeconds(-1) };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(MassiveRetryOptions.InitialBackoff), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsABackoffMultiplierBelowOne()
    {
        MassiveClientOptions options = Valid();
        options.Retry = new MassiveRetryOptions { BackoffMultiplier = 0.5 };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(MassiveRetryOptions.BackoffMultiplier), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsANonPositivePermitAllowance(int permits)
    {
        MassiveClientOptions options = Valid();
        options.RateLimit = new MassiveRateLimitOptions { PermitsPerWindow = permits };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(MassiveRateLimitOptions.PermitsPerWindow), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsANonPositiveWindow()
    {
        MassiveClientOptions options = Valid();
        options.RateLimit = new MassiveRateLimitOptions { Window = Duration.Zero };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(MassiveRateLimitOptions.Window), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsANegativeQueueLimit()
    {
        MassiveClientOptions options = Valid();
        options.RateLimit = new MassiveRateLimitOptions { QueueLimit = -1 };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(MassiveRateLimitOptions.QueueLimit), exception.Message, StringComparison.Ordinal);
    }
}
