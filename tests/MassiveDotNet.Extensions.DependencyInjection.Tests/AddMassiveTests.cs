using MassiveDotNet.Http;
using MassiveDotNet.Rest;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Extensions.DependencyInjection.Tests;

/// <summary>
/// The registration's observable contract: what resolves, with what lifetime, and what the
/// resulting pipeline puts on the wire.
/// </summary>
/// <remarks>
/// Everything is asserted through the public surface. The wire assertions install a recording
/// primary handler through the <c>IHttpClientBuilder</c> that <c>AddMassive</c> returns, which is
/// the same extension point a consumer uses to add resilience or logging.
/// </remarks>
public sealed class AddMassiveTests
{
    private const string ApiKey = "test-key-abc123";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (ServiceProvider Provider, RecordingHandler Handler) Build(
        Action<MassiveClientOptions> configure)
    {
        RecordingHandler handler = new();
        ServiceCollection services = new();

        services.AddMassive(configure).ConfigurePrimaryHttpMessageHandler(() => handler);

        return (services.BuildServiceProvider(), handler);
    }

    [Fact]
    public void RegistersAResolvableClient()
    {
        ServiceCollection services = new();
        services.AddMassive(ApiKey);

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<MassiveRestClient>());
    }

    [Fact]
    public void TheClientAndTransportAreSingletons()
    {
        // D28: the typed-client pattern would register MassiveRestClient transient, and it is
        // IDisposable, so the root container would accumulate one instance per resolution.
        ServiceCollection services = new();
        services.AddMassive(ApiKey);

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<MassiveRestClient>(),
            provider.GetRequiredService<MassiveRestClient>());
        Assert.Same(
            provider.GetRequiredService<MassiveHttpTransport>(),
            provider.GetRequiredService<MassiveHttpTransport>());
    }

    [Fact]
    public async Task TheDefaultSchemePresentsTheKeyAsABearerToken()
    {
        (ServiceProvider provider, RecordingHandler handler) = Build(options => options.ApiKey = ApiKey);

        using (provider)
        {
            await provider.GetRequiredService<MassiveRestClient>()
                .Reference.ListTickersAsync(limit: 1, cancellationToken: Ct);
        }

        Assert.Equal("Bearer", handler.Last.Headers.Authorization?.Scheme);
        Assert.Equal(ApiKey, handler.Last.Headers.Authorization?.Parameter);
        Assert.DoesNotContain(ApiKey, handler.Last.RequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheQuerySchemePresentsTheKeyInTheUri()
    {
        (ServiceProvider provider, RecordingHandler handler) = Build(options =>
        {
            options.ApiKey = ApiKey;
            options.AuthenticationScheme = MassiveAuthenticationScheme.QueryString;
        });

        using (provider)
        {
            await provider.GetRequiredService<MassiveRestClient>()
                .Reference.ListTickersAsync(limit: 1, cancellationToken: Ct);
        }

        Assert.Null(handler.Last.Headers.Authorization);
        Assert.Contains($"apiKey={ApiKey}", handler.Last.RequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBaseAddressReachesTheRequest()
    {
        // Cursor traversal resolves next_url against the client's BaseAddress and throws when it
        // is unset (D14), so a registration that forgot it would break pagination, not requests.
        Uri sandbox = new("https://sandbox.massive.test/");

        (ServiceProvider provider, RecordingHandler handler) = Build(options =>
        {
            options.ApiKey = ApiKey;
            options.BaseAddress = sandbox;
        });

        using (provider)
        {
            await provider.GetRequiredService<MassiveRestClient>()
                .Reference.ListTickersAsync(limit: 1, cancellationToken: Ct);
        }

        Assert.Equal(sandbox, new Uri(handler.Last.RequestUri!.GetLeftPart(UriPartial.Authority) + "/"));
    }

    [Fact]
    public async Task TheUserAgentReachesTheRequest()
    {
        (ServiceProvider provider, RecordingHandler handler) = Build(options =>
        {
            options.ApiKey = ApiKey;
            options.UserAgent = "contoso-trading/2.1";
        });

        using (provider)
        {
            await provider.GetRequiredService<MassiveRestClient>()
                .Reference.ListTickersAsync(limit: 1, cancellationToken: Ct);
        }

        Assert.Contains("contoso-trading/2.1", handler.Last.Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheTimeoutReachesTheHttpClient()
    {
        ServiceCollection services = new();
        services.AddMassive(options =>
        {
            options.ApiKey = ApiKey;
            options.Timeout = Duration.FromSeconds(7);
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        HttpClient http = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(MassiveServiceCollectionExtensions.HttpClientName);

        // Consumed from a BCL surface, so the temporary stays implicitly typed (rule 12).
        Assert.Equal(Duration.FromSeconds(7), Duration.FromTimeSpan(http.Timeout));
    }
}
