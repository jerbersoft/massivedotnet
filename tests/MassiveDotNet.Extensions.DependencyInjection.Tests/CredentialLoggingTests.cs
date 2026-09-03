using MassiveDotNet.Rest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MassiveDotNet.Extensions.DependencyInjection.Tests;

/// <summary>
/// Rule 11 against the one thing this package adds that core does not have: a logger.
/// </summary>
/// <remarks>
/// <c>IHttpClientFactory</c> logs every request, and at <see cref="LogLevel.Trace"/> it logs the
/// headers too. Core's transport writes nothing anywhere, so registering through the factory is
/// the first time an API key is anywhere near a log sink — under Bearer in a header, and under the
/// query scheme in the URI. Both are asserted here at the most verbose level a consumer can set.
/// </remarks>
public sealed class CredentialLoggingTests
{
    private const string ApiKey = "test-key-abc123";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string> CaptureLogsAsync(
        MassiveAuthenticationScheme scheme,
        Action<IHttpClientBuilder>? customize = null)
    {
        CapturingLoggerProvider capture = new();
        ServiceCollection services = new();

        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(capture);
        });

        services.AddMassive(options =>
        {
            options.ApiKey = ApiKey;
            options.AuthenticationScheme = scheme;
        })
        .ConfigurePrimaryHttpMessageHandler(static () => new RecordingHandler());

        customize?.Invoke(services.AddHttpClient(MassiveServiceCollectionExtensions.HttpClientName));

        using (ServiceProvider provider = services.BuildServiceProvider())
        {
            await provider.GetRequiredService<MassiveRestClient>()
                .Reference.ListTickersAsync(limit: 1, cancellationToken: Ct);
        }

        return capture.Text;
    }

    [Fact]
    public async Task TheBearerTokenNeverReachesALogSink()
    {
        string logs = await CaptureLogsAsync(MassiveAuthenticationScheme.BearerToken);

        Assert.Contains("Authorization", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBearerTokenStaysRedactedWhenTheConsumerNarrowsRedaction()
    {
        // RedactLoggedHeaders replaces the predicate rather than adding to it, so a consumer who
        // asks to see one of their own headers turns every other header's value on — including the
        // one carrying the key. Rule 11 says never, not "not by default", so the registration
        // forces this one back off after every consumer delegate has run.
        string logs = await CaptureLogsAsync(
            MassiveAuthenticationScheme.BearerToken,
            static builder => builder.RedactLoggedHeaders(["X-Correlation-Id"]));

        Assert.DoesNotContain(ApiKey, logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheQueryStringKeyNeverReachesALogSink()
    {
        string logs = await CaptureLogsAsync(MassiveAuthenticationScheme.QueryString);

        Assert.DoesNotContain(ApiKey, logs, StringComparison.Ordinal);
    }
}
