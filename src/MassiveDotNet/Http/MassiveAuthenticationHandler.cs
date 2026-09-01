using System.Net.Http.Headers;

namespace MassiveDotNet.Http;

/// <summary>
/// Attaches Massive platform credentials to every outbound request.
/// </summary>
public sealed class MassiveAuthenticationHandler : DelegatingHandler
{
    private const string ApiKeyQueryParameter = "apiKey";

    private readonly string _apiKey;
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

    private void Authenticate(HttpRequestMessage request)
    {
        if (_scheme == MassiveAuthenticationScheme.BearerToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            return;
        }

        Uri uri = request.RequestUri
            ?? throw new InvalidOperationException("The request URI must be set before authentication is applied.");

        UriBuilder builder = new(uri);
        string escapedKey = Uri.EscapeDataString(_apiKey);

        builder.Query = string.IsNullOrEmpty(builder.Query)
            ? $"{ApiKeyQueryParameter}={escapedKey}"
            : $"{builder.Query.TrimStart('?')}&{ApiKeyQueryParameter}={escapedKey}";

        request.RequestUri = builder.Uri;
    }
}
