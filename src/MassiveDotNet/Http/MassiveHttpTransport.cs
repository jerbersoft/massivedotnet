using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using NodaTime;

namespace MassiveDotNet.Http;

/// <summary>
/// Issues authenticated requests against the Massive platform API and deserializes responses
/// using source-generated metadata.
/// </summary>
/// <remarks>
/// Responses are read with <see cref="HttpCompletionOption.ResponseHeadersRead"/> and
/// deserialized straight off the network stream, so a large payload is never buffered into
/// an intermediate string or byte array.
/// </remarks>
public sealed class MassiveHttpTransport : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    /// <summary>
    /// Creates a transport that owns its own <see cref="HttpClient"/>, configured from
    /// <paramref name="options"/>.
    /// </summary>
    /// <param name="options">The client configuration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="options"/> is incomplete.</exception>
    public MassiveHttpTransport(MassiveClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        MassiveAuthenticationHandler authentication = new(options.ApiKey!, options.AuthenticationScheme)
        {
            InnerHandler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            },
        };

        _httpClient = new HttpClient(authentication, disposeHandler: true)
        {
            BaseAddress = options.BaseAddress,
            // HttpClient is a BCL API, so the domain Duration converts here at the boundary.
            Timeout = options.Timeout.ToTimeSpan(),
        };

        if (!string.IsNullOrWhiteSpace(options.UserAgent))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
        }

        _ownsHttpClient = true;
    }

    /// <summary>
    /// Creates a transport over a caller-supplied <see cref="HttpClient"/>, for use with
    /// <c>IHttpClientFactory</c>. The caller keeps ownership of the client's lifetime, and is
    /// responsible for configuring its base address and authentication.
    /// </summary>
    /// <param name="httpClient">The configured client to send requests on.</param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> is <see langword="null"/>.</exception>
    public MassiveHttpTransport(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
        _ownsHttpClient = false;
    }

    /// <summary>
    /// Issues a GET request and deserializes the response body.
    /// </summary>
    /// <typeparam name="T">The response envelope type.</typeparam>
    /// <param name="requestUri">The request URI, relative to the configured base address.</param>
    /// <param name="typeInfo">Source-generated metadata describing <typeparamref name="T"/>.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The deserialized response, or <see langword="null"/> when the body was empty.</returns>
    /// <exception cref="MassiveRateLimitExceededException">The server responded with HTTP 429.</exception>
    /// <exception cref="MassiveApiException">The server responded with any other error status.</exception>
    public async Task<T?> GetAsync<T>(
        string requestUri,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken).ConfigureAwait(false);
        }

        await using Stream content = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return await JsonSerializer
                .DeserializeAsync(content, typeInfo, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new MassiveApiException(
                response.StatusCode,
                $"The response body from '{requestUri}' could not be deserialized as {typeof(T).Name}.",
                innerException: ex);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static async Task<MassiveApiException> CreateExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        MassiveErrorPayload? payload = null;

        try
        {
            await using Stream content = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            payload = await JsonSerializer
                .DeserializeAsync(content, MassiveCoreJsonContext.Default.MassiveErrorPayload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Error bodies are not schematized; fall back to the status line below.
        }
        catch (HttpRequestException)
        {
            // The body could not be read; fall back to the status line below.
        }

        string message = payload?.BestMessage()
            ?? response.ReasonPhrase
            ?? $"The request failed with status code {(int)response.StatusCode}.";

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            TimeSpan? delta = response.Headers.RetryAfter?.Delta;

            return new MassiveRateLimitExceededException(
                message,
                delta is { } value ? Duration.FromTimeSpan(value) : null,
                payload?.RequestId);
        }

        return new MassiveApiException(response.StatusCode, message, payload?.RequestId);
    }
}
