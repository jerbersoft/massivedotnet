using System.Net;
using MassiveDotNet;
using MassiveDotNet.Extensions.DependencyInjection;
using MassiveDotNet.Http;
using MassiveDotNet.Rest;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the Massive REST client in a service collection, wired through
/// <see cref="IHttpClientFactory"/>.
/// </summary>
public static class MassiveServiceCollectionExtensions
{
    /// <summary>
    /// The name of the <see cref="HttpClient"/> registration the SDK issues requests on. Use it to
    /// reach the same pipeline from <see cref="IHttpClientFactory"/> directly.
    /// </summary>
    public const string HttpClientName = "MassiveDotNet";

    /// <summary>
    /// Registers the Massive REST client authenticated with an API key.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="apiKey">The Massive API key.</param>
    /// <returns>
    /// The builder for the underlying named <see cref="HttpClient"/>, so resilience, logging, or
    /// any other handler can be added to the same pipeline.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> is null or whitespace.</exception>
    public static IHttpClientBuilder AddMassive(this IServiceCollection services, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        return services.AddMassive(options => options.ApiKey = apiKey);
    }

    /// <summary>
    /// Registers the Massive REST client from a full set of options.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Configures the client options.</param>
    /// <returns>
    /// The builder for the underlying named <see cref="HttpClient"/>, so resilience, logging, or
    /// any other handler can be added to the same pipeline.
    /// </returns>
    /// <remarks>
    /// <para>
    /// There is deliberately no overload binding an <c>IConfiguration</c>
    /// section. <see cref="MassiveClientOptions.Timeout"/> is a NodaTime <see cref="Duration"/>,
    /// which the configuration binder cannot convert: a bound section compiles clean, raises no
    /// warning, and silently leaves the default in place. Read the values yourself instead —
    /// <c>options.ApiKey = configuration["Massive:ApiKey"]</c> — which is also free of reflection
    /// and so stays Native AOT clean (D27).
    /// </para>
    /// <para>
    /// Calling this more than once layers an additional options delegate but builds the HTTP
    /// pipeline only on the first call, so the API key is never attached twice.
    /// </para>
    /// <para>
    /// Unlike core's transport, the registered pipeline logs. The <c>Authorization</c> header's
    /// value is redacted unconditionally, so no consumer logging configuration can expose the key
    /// (rule 11). The default Bearer scheme keeps the key out of the request URI as well, which is
    /// the reason it is the default (D2).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static IHttpClientBuilder AddMassive(
        this IServiceCollection services,
        Action<MassiveClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<MassiveClientOptions>().Configure(configure);
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MassiveClientOptions>, ValidateMassiveClientOptions>());

        IHttpClientBuilder builder = services.AddHttpClient(HttpClientName);

        // ConfigureHttpClient and AddHttpMessageHandler are additive, so a second AddMassive call
        // would attach a second authentication handler — which under the query scheme appends the
        // key to the URL twice. Options delegates stay additive, since layering configuration is
        // what a second call is for.
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(MassiveRegistrationMarker)))
        {
            return builder;
        }

        services.AddSingleton(new MassiveRegistrationMarker());

        builder
            .ConfigureHttpClient(static (provider, http) =>
            {
                MassiveClientOptions options = provider.GetRequiredService<IOptions<MassiveClientOptions>>().Value;

                // Pagination resolves every cursor against this address before sending the caller's
                // key, and throws when it is unset (D14), so the registration must supply it.
                http.BaseAddress = options.BaseAddress;

                // Boundary crossing (produce): the domain Duration converts here and nowhere above,
                // so the BCL temporal type is never named (rule 12).
                http.Timeout = options.Timeout.ToTimeSpan();

                if (!string.IsNullOrWhiteSpace(options.UserAgent))
                {
                    http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
                }
            })
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,

                // Boundary crossing (produce). The client below is resolved once and held for the
                // process lifetime, so the factory never rotates this handler; a pooled connection
                // lifetime is what actually keeps DNS fresh, exactly as core's own transport does.
                PooledConnectionLifetime = Duration.FromMinutes(2).ToTimeSpan(),
            })
            .AddHttpMessageHandler(static provider =>
            {
                MassiveClientOptions options = provider.GetRequiredService<IOptions<MassiveClientOptions>>().Value;

                return new MassiveAuthenticationHandler(options.ApiKey!, options.AuthenticationScheme);
            });

        // Rule 11, against the one thing this package adds that core does not have: a logger.
        // IHttpClientFactory logs headers at Trace, and RedactLoggedHeaders REPLACES the redaction
        // predicate — so a consumer asking to see one of their own headers silently turns the
        // key's value on. PostConfigure runs after every consumer delegate, so widening redaction
        // stays possible while this one header cannot be un-redacted.
        services.PostConfigure<HttpClientFactoryOptions>(HttpClientName, static options =>
        {
            Func<string, bool> configured = options.ShouldRedactHeaderValue;

            options.ShouldRedactHeaderValue = header =>
                string.Equals(header, "Authorization", StringComparison.OrdinalIgnoreCase)
                || configured(header);
        });

        // Singletons rather than the typed-client pattern (D28): AddHttpClient<MassiveRestClient>
        // registers transient, and MassiveRestClient is IDisposable, so the root container would
        // track one undisposed instance per resolution. The client also documents itself as
        // long-lived, and neither wrapper owns what it is handed, so nothing here disposes the
        // factory's HttpClient.
        services.TryAddSingleton(static provider => new MassiveHttpTransport(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName)));

        services.TryAddSingleton(static provider => new MassiveRestClient(
            provider.GetRequiredService<MassiveHttpTransport>()));

        return builder;
    }
}
