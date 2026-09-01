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
    /// The per-request timeout. Defaults to 100 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// An optional product token appended to the <c>User-Agent</c> header.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Throws if the options are not in a usable state.
    /// </summary>
    /// <exception cref="InvalidOperationException">The API key is missing, or the base address is not absolute.</exception>
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
    }
}
