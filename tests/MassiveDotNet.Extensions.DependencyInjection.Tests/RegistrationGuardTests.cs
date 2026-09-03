using MassiveDotNet.Rest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace MassiveDotNet.Extensions.DependencyInjection.Tests;

/// <summary>
/// The two ways a plausible registration goes wrong: incomplete options, and being registered
/// twice. Neither fails loudly on its own, so both are pinned here.
/// </summary>
public sealed class RegistrationGuardTests
{
    private const string ApiKey = "test-key-abc123";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void AMissingApiKeyFailsValidationWithoutNamingAnyValue()
    {
        ServiceCollection services = new();
        services.AddMassive(options => options.BaseAddress = new Uri("https://sandbox.massive.test/"));

        using ServiceProvider provider = services.BuildServiceProvider();

        OptionsValidationException error = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<MassiveRestClient>);

        Assert.Contains(nameof(MassiveClientOptions.ApiKey), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARegisteredKeyNeverAppearsInAValidationFailure()
    {
        // Rule 11: an API key is never echoed in an exception message. A base address that is not
        // absolute fails validation while the key is present, which is the case that could leak.
        ServiceCollection services = new();
        services.AddMassive(options =>
        {
            options.ApiKey = ApiKey;
            options.BaseAddress = new Uri("/relative", UriKind.Relative);
        });

        using ServiceProvider provider = services.BuildServiceProvider();

        OptionsValidationException error = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<MassiveRestClient>);

        Assert.DoesNotContain(ApiKey, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisteringTwiceAttachesTheKeyOnlyOnce()
    {
        // AddHttpMessageHandler is additive, so without the registration marker a second call
        // would stack a second authentication handler. Under the query scheme that appends the
        // key twice — visibly wrong — while under Bearer the second write silently wins.
        RecordingHandler handler = new();
        ServiceCollection services = new();

        void Configure(MassiveClientOptions options)
        {
            options.ApiKey = ApiKey;
            options.AuthenticationScheme = MassiveAuthenticationScheme.QueryString;
        }

        services.AddMassive(Configure);
        services.AddMassive(Configure).ConfigurePrimaryHttpMessageHandler(() => handler);

        using (ServiceProvider provider = services.BuildServiceProvider())
        {
            await provider.GetRequiredService<MassiveRestClient>()
                .Reference.ListTickersAsync(limit: 1, cancellationToken: Ct);
        }

        string uri = handler.Last.RequestUri!.ToString();

        Assert.Equal(1, uri.Split($"apiKey={ApiKey}").Length - 1);
    }

    [Fact]
    public void ASecondCallStillLayersItsOptionsDelegate()
    {
        // The marker suppresses the pipeline, not configuration: a second call is how a consumer
        // overrides one value without restating the rest.
        ServiceCollection services = new();
        services.AddMassive(ApiKey);
        services.AddMassive(options => options.UserAgent = "contoso-trading/2.1");

        using ServiceProvider provider = services.BuildServiceProvider();
        MassiveClientOptions options = provider.GetRequiredService<IOptions<MassiveClientOptions>>().Value;

        Assert.Equal(ApiKey, options.ApiKey);
        Assert.Equal("contoso-trading/2.1", options.UserAgent);
    }
}
