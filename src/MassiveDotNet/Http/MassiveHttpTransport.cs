using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using NodaTime;

namespace MassiveDotNet.Http;

/// <summary>
/// Issues authenticated requests against the Massive platform API and deserializes responses
/// using source-generated metadata, or copies a document body to a caller's stream.
/// </summary>
/// <remarks>
/// <para>
/// Responses are read with <see cref="HttpCompletionOption.ResponseHeadersRead"/> and
/// deserialized straight off the network stream, so a large payload is never buffered into
/// an intermediate string or byte array.
/// </para>
/// <para>
/// This type sits on the SDK's only BCL temporal boundary. <c>HttpClient</c>,
/// <c>SocketsHttpHandler</c>, and the <c>Retry-After</c> header all traffic in TimeSpan, which
/// no SDK can change. Constitution rule 12 therefore forbids <em>naming</em> the BCL type rather
/// than pretending it does not exist: every crossing below is an inline conversion through
/// NodaTime, so no BCL temporal type is ever declared. See the "Temporal types" section of
/// CLAUDE.md for the full policy and the table of known boundary points.
/// </para>
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
                // Boundary crossing (produce): converted inline from a Duration, so the
                // BCL type is never named. Two minutes keeps connections fresh enough to
                // follow DNS changes without re-establishing TLS on every request.
                PooledConnectionLifetime = Duration.FromMinutes(2).ToTimeSpan(),
            },
        };

        _httpClient = new HttpClient(authentication, disposeHandler: true)
        {
            BaseAddress = options.BaseAddress,
            // Boundary crossing (produce): the domain Duration converts here and nowhere above.
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

    /// <summary>
    /// Issues a GET request and copies the response body to <paramref name="destination"/>
    /// unchanged, for a route that serves a document rather than JSON.
    /// </summary>
    /// <param name="requestUri">The request URI, relative to the configured base address.</param>
    /// <param name="destination">The stream the body is written to. The caller keeps ownership of it.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>A task that completes once the whole body has been written.</returns>
    /// <remarks>
    /// The content type is not inspected: the caller asked for the bytes, and the one route that
    /// needs this, the SEC filing file, names each file's type and size in its listing (decision
    /// D25). The body streams from the network into <paramref name="destination"/> with no
    /// intermediate buffer, as every other response does.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="requestUri"/> or <paramref name="destination"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="requestUri"/> is empty or whitespace.</exception>
    /// <exception cref="ObjectDisposedException">This transport has been disposed.</exception>
    /// <exception cref="MassiveRateLimitExceededException">The server responded with HTTP 429.</exception>
    /// <exception cref="MassiveApiException">The server responded with any other error status.</exception>
    public async Task DownloadAsync(string requestUri, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken).ConfigureAwait(false);
        }

        await response.Content.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Issues a GET request and then follows the response's <c>next_url</c> cursor, yielding every
    /// item from every page.
    /// </summary>
    /// <typeparam name="TEnvelope">The paged response envelope type.</typeparam>
    /// <typeparam name="TItem">The result item type.</typeparam>
    /// <param name="requestUri">The first page's URI, relative to the configured base address.</param>
    /// <param name="typeInfo">Source-generated metadata describing <typeparamref name="TEnvelope"/>.</param>
    /// <param name="cancellationToken">A token to cancel the traversal.</param>
    /// <returns>Every item across every page, in the order the server returned them.</returns>
    /// <remarks>
    /// <para>
    /// Exactly one page is in flight at a time: the next request is issued only once the previous
    /// page has been fully consumed, so a caller who stops early stops the traffic too.
    /// </para>
    /// <para>
    /// Cancellation is observed at page boundaries. The token is checked before each request, so a
    /// cancelled traversal issues no further request, but the items already deserialized from the
    /// page in hand are still yielded before the cancellation surfaces. Stopping mid-page is what
    /// <c>break</c> is for, and costs nothing on the traversals that never cancel.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="requestUri"/> or <paramref name="typeInfo"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="requestUri"/> is empty or whitespace.</exception>
    /// <exception cref="ObjectDisposedException">This transport has been disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// A cursor was returned but the underlying client has no base address, so the cursor's origin
    /// cannot be checked. Raised while enumerating rather than from this call, since it depends on
    /// what the server sends back.
    /// </exception>
    /// <exception cref="MassiveApiException">
    /// The server responded with an error status, or returned a cursor that is not a usable URI or
    /// points outside the configured base address.
    /// </exception>
    public IAsyncEnumerable<TItem> EnumerateAsync<TEnvelope, TItem>(
        string requestUri,
        JsonTypeInfo<TEnvelope> typeInfo,
        CancellationToken cancellationToken = default)
        where TEnvelope : class, IPagedEnvelope<TItem>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Validation happens here rather than in the iterator below, so a bad argument throws at
        // the call site instead of being deferred until someone starts enumerating.
        return EnumerateCoreAsync<TEnvelope, TItem>(requestUri, typeInfo, cancellationToken);
    }

    /// <summary>
    /// Throws when a response offered a pagination cursor that this SDK cannot follow, because the
    /// operation's result is a single object rather than a page of items.
    /// </summary>
    /// <param name="nextUrl">The response's <c>next_url</c>, or <see langword="null"/> when it sent none.</param>
    /// <param name="requestUri">The request that produced the response, named in the exception.</param>
    /// <param name="requestId">The response's request identifier, carried by the exception when present.</param>
    /// <remarks>
    /// Called by generated code for the operations whose OpenAPI success schema declares
    /// <c>next_url</c> on a result that is one object (decision D17). A blank cursor is the absence
    /// it means, as it is everywhere else in this transport. A real one is a page the caller will
    /// never receive, and missing data is reported loudly in this SDK rather than dropped.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="requestUri"/> is empty or whitespace.</exception>
    /// <exception cref="MassiveApiException"><paramref name="nextUrl"/> is a cursor.</exception>
    public static void ThrowIfUnfollowableCursor(string? nextUrl, string requestUri, string? requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);

        if (string.IsNullOrWhiteSpace(nextUrl))
        {
            return;
        }

        throw new MassiveApiException(
            HttpStatusCode.OK,
            $"The response from '{requestUri}' offered a 'next_url' cursor, but this operation returns a "
            + "single object that cannot be paged, so the further page was not retrieved.",
            requestId);
    }

    private async IAsyncEnumerable<TItem> EnumerateCoreAsync<TEnvelope, TItem>(
        string requestUri,
        JsonTypeInfo<TEnvelope> typeInfo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TEnvelope : class, IPagedEnvelope<TItem>
    {
        string? next = requestUri;

        while (next is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TEnvelope? envelope = await GetAsync(next, typeInfo, cancellationToken).ConfigureAwait(false);

            if (envelope is null)
            {
                yield break;
            }

            foreach (TItem item in envelope.Results ?? [])
            {
                yield return item;
            }

            // The previous page becomes garbage here: nothing accumulates across the traversal.
            //
            // A blank cursor ends the traversal, deliberately and not as a side effect of
            // IsNullOrWhiteSpace being convenient. An empty or whitespace next_url is not a URL,
            // yet it parses as a *relative* one, and resolving it against the base address yields
            // the base address itself -- which passes the origin check and re-requests the first
            // page, forever, yielding duplicates and burning quota. `"next_url": ""` is also a
            // common way for a service to say "no next page", so the empty string is honoured as
            // the absence it means rather than followed as the URL it is not.
            next = string.IsNullOrWhiteSpace(envelope.NextUrl)
                ? null
                : ResolveCursor(envelope.NextUrl).AbsoluteUri;
        }
    }

    /// <summary>
    /// Validates a server-supplied cursor and resolves it to an absolute URI.
    /// </summary>
    /// <remarks>
    /// The cursor is compared, never rebuilt: some endpoints move state into the path rather than
    /// the query string, so reconstructing it from the original arguments silently restarts the
    /// traversal. The origin check exists because the SDK re-attaches the API key to every page,
    /// and <c>next_url</c> is a URL chosen by the response body.
    /// </remarks>
    private Uri ResolveCursor(string nextUrl)
    {
        if (!Uri.TryCreate(nextUrl, UriKind.RelativeOrAbsolute, out Uri? cursor))
        {
            throw new MassiveApiException(
                HttpStatusCode.OK,
                "The server returned a 'next_url' value that is not a valid URI.");
        }

        if (_httpClient.BaseAddress is not { } baseAddress)
        {
            throw new InvalidOperationException(
                "Following a pagination cursor requires HttpClient.BaseAddress to be set, because "
                + "the cursor's origin is checked against it before the API key is sent.");
        }

        // Resolve to an absolute URI before checking anything about its origin. A network-path
        // reference such as "//evil.example/x" fails Uri.IsAbsoluteUri -- RFC 3986 treats it as
        // relative -- yet combining it with a base still lets it supply its own authority, so a
        // check gated on "is this cursor absolute" never runs for exactly the shape it most needs
        // to catch. Comparing origins only after resolution closes that gap: there is one
        // comparison, and it always sees the authority the request will actually be sent to.
        // The TryCreate(base, cursor, out) overload also reports a malformed combination (for
        // example "///evil.example/x") by returning false rather than throwing, so a cursor that
        // is syntactically relative but cannot be combined with the base still surfaces as the
        // documented MassiveApiException instead of an unhandled UriFormatException.
        Uri resolved;
        if (cursor.IsAbsoluteUri)
        {
            resolved = cursor;
        }
        else if (!Uri.TryCreate(baseAddress, cursor, out resolved!))
        {
            throw new MassiveApiException(
                HttpStatusCode.OK,
                "The server returned a 'next_url' value that is not a valid URI.");
        }

        bool sameOrigin = Uri.Compare(
            baseAddress,
            resolved,
            UriComponents.SchemeAndServer,
            UriFormat.UriEscaped,
            StringComparison.OrdinalIgnoreCase) == 0;

        if (!sameOrigin)
        {
            throw new MassiveApiException(
                HttpStatusCode.OK,
                $"The server returned a 'next_url' pointing at "
                + $"'{resolved.GetLeftPart(UriPartial.Authority)}', which is not the configured base "
                + $"address '{baseAddress.GetLeftPart(UriPartial.Authority)}'. The cursor was not "
                + "followed, so the API key was not sent to that host.");
        }

        return resolved;
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
            // Boundary crossing (consume): the pattern match leaves `delta` implicitly typed,
            // so the BCL type is never written down. A typed local here would compile and pass
            // reflection-based checks, and is caught only by the source scan in TemporalTypeTests.
            //
            // Delta is null when the server sent an HTTP-date rather than a delta-seconds value;
            // that is surfaced as no retry hint rather than a computed one, since converting it
            // would require trusting the client clock against the server's.
            Duration? retryAfter = response.Headers.RetryAfter?.Delta is { } delta
                ? Duration.FromTimeSpan(delta)
                : null;

            return new MassiveRateLimitExceededException(message, retryAfter, payload?.RequestId);
        }

        return new MassiveApiException(response.StatusCode, message, payload?.RequestId);
    }
}
