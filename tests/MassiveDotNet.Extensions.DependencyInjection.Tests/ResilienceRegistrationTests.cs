using System.Net;
using MassiveDotNet.Rest;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Extensions.DependencyInjection.Tests;

/// <summary>
/// The DI pipeline expresses D30's handler order through <c>AddHttpMessageHandler</c>, which is a
/// second statement of the order core's <c>CreateHandlerPipeline</c> already makes. These tests
/// assert the property that order exists for, so the two cannot drift apart silently.
/// </summary>
public sealed class ResilienceRegistrationTests
{
    private const string ApiKey = "test-key-abc123";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (ServiceProvider Provider, RecordingHandler Handler) Build(
        Action<MassiveClientOptions> configure,
        params HttpStatusCode[] statuses)
    {
        RecordingHandler handler = new() { Statuses = statuses.Length == 0 ? [HttpStatusCode.OK] : statuses };
        ServiceCollection services = new();

        services.AddMassive(configure).ConfigurePrimaryHttpMessageHandler(() => handler);

        return (services.BuildServiceProvider(), handler);
    }

    [Fact]
    public async Task SendsOnceOnAFailureWhenNoResilienceIsConfigured()
    {
        (ServiceProvider provider, RecordingHandler handler) =
            Build(options => options.ApiKey = ApiKey, HttpStatusCode.ServiceUnavailable);

        using (provider)
        {
            MassiveRestClient client = provider.GetRequiredService<MassiveRestClient>();

            await Assert.ThrowsAnyAsync<MassiveApiException>(
                () => client.Reference.ListTickersAsync(cancellationToken: Ct));
        }

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RetriesWhenConfiguredTo()
    {
        (ServiceProvider provider, RecordingHandler handler) = Build(
            options =>
            {
                options.ApiKey = ApiKey;
                options.Retry = new MassiveRetryOptions
                {
                    MaxAttempts = 3,
                    InitialBackoff = Duration.FromMilliseconds(1),
                    MaxBackoff = Duration.FromMilliseconds(2),
                };
            },
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK);

        using (provider)
        {
            MassiveRestClient client = provider.GetRequiredService<MassiveRestClient>();
            await client.Reference.ListTickersAsync(cancellationToken: Ct);
        }

        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task RegistersRetryInsideAuthenticationSoTheKeyIsAppendedOnce()
    {
        // The order pin for the DI path (D30). AddHttpMessageHandler appends outermost-first, so
        // authentication must be registered before retry. Registered the other way round, each
        // attempt re-authenticates a URI that already carries the key — and still succeeds, so
        // the key repeating in access logs is the only symptom (rule 11).
        (ServiceProvider provider, RecordingHandler handler) = Build(
            options =>
            {
                options.ApiKey = ApiKey;
                options.AuthenticationScheme = MassiveAuthenticationScheme.QueryString;
                options.Retry = new MassiveRetryOptions
                {
                    MaxAttempts = 3,
                    InitialBackoff = Duration.FromMilliseconds(1),
                    MaxBackoff = Duration.FromMilliseconds(2),
                };
            },
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK);

        using (provider)
        {
            MassiveRestClient client = provider.GetRequiredService<MassiveRestClient>();
            await client.Reference.ListTickersAsync(cancellationToken: Ct);
        }

        Assert.Equal(3, handler.SentUris.Count);

        foreach (string? uri in handler.SentUris)
        {
            int occurrences = (uri ?? string.Empty).Split("apiKey=", StringSplitOptions.None).Length - 1;
            Assert.True(occurrences == 1, $"Expected exactly one apiKey in '{uri}', found {occurrences}.");
        }
    }

    [Fact]
    public async Task SpendsAPermitOnEveryAttemptIncludingRetries()
    {
        // The limiter is registered last, so it sits innermost and every physical attempt spends
        // a permit — a retry is a request the server counts too.
        (ServiceProvider provider, RecordingHandler handler) = Build(
            options =>
            {
                options.ApiKey = ApiKey;
                options.Retry = new MassiveRetryOptions
                {
                    MaxAttempts = 2,
                    InitialBackoff = Duration.FromMilliseconds(1),
                    MaxBackoff = Duration.FromMilliseconds(2),
                };
                options.RateLimit = new MassiveRateLimitOptions
                {
                    PermitsPerWindow = 2,
                    Window = Duration.FromMinutes(10),
                    QueueLimit = 0,
                };
            },
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK);

        using (provider)
        {
            MassiveRestClient client = provider.GetRequiredService<MassiveRestClient>();

            await client.Reference.ListTickersAsync(cancellationToken: Ct);
            Assert.Equal(2, handler.Requests.Count);

            await Assert.ThrowsAsync<MassiveRateLimitExceededException>(
                () => client.Reference.ListTickersAsync(cancellationToken: Ct));
        }

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void RejectsAMisconfiguredRetryPolicyAtResolution()
    {
        // Options validation runs where every other MassiveClientOptions check runs, so a bad
        // value fails at resolution rather than on the first request that needed it.
        ServiceCollection services = new();
        services.AddMassive(options =>
        {
            options.ApiKey = ApiKey;
            options.Retry = new MassiveRetryOptions { MaxAttempts = 0 };
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.ThrowsAny<Exception>(provider.GetRequiredService<MassiveRestClient>);
    }
}
