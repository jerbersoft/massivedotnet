using System.Net.Http.Headers;

namespace MassiveDotNet.Http;

/// <summary>
/// Attaches Massive platform credentials to every outbound request.
/// </summary>
public sealed class MassiveAuthenticationHandler : DelegatingHandler
{
    private const string ApiKeyQueryParameter = "apiKey";

    private readonly string? _apiKey;
    private readonly Func<string>? _apiKeyProvider;
    private readonly MassiveAuthenticationScheme _scheme;

    /// <summary>Initializes a new instance of the <see cref="MassiveAuthenticationHandler"/> class.</summary>
    /// <param name="apiKey">The API key to present.</param>
    /// <param name="scheme">How the key should be presented.</param>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> is null or whitespace.</exception>
    public MassiveAuthenticationHandler(string apiKey, MassiveAuthenticationScheme scheme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        _apiKey = apiKey;
        _scheme = scheme;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MassiveAuthenticationHandler"/> class that asks
    /// <paramref name="apiKeyProvider"/> for the key on every request (D45).
    /// </summary>
    /// <param name="apiKeyProvider">
    /// Called once per request, before it is sent. A null or whitespace answer fails that request
    /// with an <see cref="InvalidOperationException"/>; an exception it throws propagates unchanged.
    /// </param>
    /// <param name="scheme">How the key should be presented.</param>
    /// <exception cref="ArgumentNullException"><paramref name="apiKeyProvider"/> is <see langword="null"/>.</exception>
    public MassiveAuthenticationHandler(Func<string> apiKeyProvider, MassiveAuthenticationScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(apiKeyProvider);

        _apiKeyProvider = apiKeyProvider;
        _scheme = scheme;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Authenticate(request);
        return base.SendAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    protected override HttpResponseMessage Send(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Authenticate(request);
        return base.Send(request, cancellationToken);
    }

    private static string Provided(Func<string> apiKeyProvider)
    {
        string? apiKey = apiKeyProvider();

        // Rule 11: name the option, never a value. There is no value to name here, and a later edit
        // must not add one.
        return string.IsNullOrWhiteSpace(apiKey)
            ? throw new InvalidOperationException(
                $"{nameof(MassiveClientOptions)}.{nameof(MassiveClientOptions.ApiKeyProvider)} returned no API key.")
            : apiKey;
    }

    private void Authenticate(HttpRequestMessage request)
    {
        string apiKey = _apiKeyProvider is { } apiKeyProvider ? Provided(apiKeyProvider) : _apiKey!;

        if (_scheme == MassiveAuthenticationScheme.BearerToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            return;
        }

        Uri uri = request.RequestUri
            ?? throw new InvalidOperationException("The request URI must be set before authentication is applied.");

        UriBuilder builder = new(uri);
        string escapedKey = Uri.EscapeDataString(apiKey);

        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? $"{ApiKeyQueryParameter}={escapedKey}"
            : $"{builder.Query.TrimStart('?')}&{ApiKeyQueryParameter}={escapedKey}";

        request.RequestUri = builder.Uri;
    }
}
