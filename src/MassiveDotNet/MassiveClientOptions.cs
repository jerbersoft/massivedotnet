using NodaTime;

namespace MassiveDotNet;

/// <summary>
/// Configuration for a Massive API client.
/// </summary>
public sealed class MassiveClientOptions
{
    private string? _apiKey;

    /// <summary>
    /// The API key used to authenticate requests. Required.
    /// </summary>
    public string? ApiKey
    {
        get => _apiKey;
        set => _apiKey = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// The base address requests are issued against. Defaults to <see cref="MassiveEndpoints.Production"/>.
    /// </summary>
    public Uri BaseAddress { get; set; } = MassiveEndpoints.Production;

    /// <summary>
    /// How the API key is presented to the server. Defaults to
    /// <see cref="MassiveAuthenticationScheme.BearerToken"/>.
    /// </summary>
    public MassiveAuthenticationScheme AuthenticationScheme { get; set; } = MassiveAuthenticationScheme.BearerToken;

    /// <summary>
    /// The per-request timeout. Defaults to 100 seconds, matching <c>HttpClient</c>'s own default.
    /// </summary>
    /// <remarks>
    /// A NodaTime <see cref="Duration"/> rather than a BCL TimeSpan, per constitution rule 12.
    /// It is converted at the <c>HttpClient</c> boundary inside <c>MassiveHttpTransport</c>, and
    /// is ignored when the transport is constructed over a caller-supplied <c>HttpClient</c>,
    /// since that client carries its own timeout.
    /// </remarks>
    public Duration Timeout { get; set; } = Duration.FromSeconds(100);

    /// <summary>
    /// An optional product token appended to the <c>User-Agent</c> header.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Client-side throttling, or <see langword="null"/> to send requests as fast as the caller
    /// issues them. Off by default.
    /// </summary>
    /// <remarks>
    /// Assigning an instance is the whole opt-in:
    /// <c>options.RateLimit = new MassiveRateLimitOptions { PermitsPerWindow = 5 }</c>. The SDK
    /// cannot infer a key's entitlement, so it never throttles unless asked (D30).
    /// </remarks>
    public MassiveRateLimitOptions? RateLimit { get; set; }

    /// <summary>
    /// Bounded retry for HTTP 429 and 5xx, or <see langword="null"/> to surface the first failure.
    /// Off by default.
    /// </summary>
    /// <remarks>
    /// Assigning an instance is the whole opt-in:
    /// <c>options.Retry = new MassiveRetryOptions()</c>. Retry is separate from
    /// <see cref="RateLimit"/> because the two solve different problems — a caller on a paid tier
    /// may want to ride out a transient 502 without pacing their requests at all (D30).
    /// </remarks>
    public MassiveRetryOptions? Retry { get; set; }

    /// <summary>
    /// Throws if the options are not in a usable state.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The API key is missing, the base address is not absolute, or a configured
    /// <see cref="RateLimit"/> or <see cref="Retry"/> holds a value it cannot act on.
    /// </exception>
    public void Validate()
    {
        if (_apiKey is null)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveClientOptions)}.{nameof(ApiKey)} must be set to a non-empty value.");
        }

        if (!BaseAddress.IsAbsoluteUri)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveClientOptions)}.{nameof(BaseAddress)} must be an absolute URI.");
        }

        RateLimit?.Validate();
        Retry?.Validate();
    }
}
