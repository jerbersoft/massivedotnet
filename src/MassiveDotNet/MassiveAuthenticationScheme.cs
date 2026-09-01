namespace MassiveDotNet;

/// <summary>
/// Determines how the API key is presented to the Massive platform API.
/// </summary>
public enum MassiveAuthenticationScheme
{
    /// <summary>
    /// Send the key as an <c>Authorization: Bearer &lt;key&gt;</c> header. This is the default
    /// because, unlike a query string, the header is not captured by access logs, proxies,
    /// or browser history.
    /// </summary>
    BearerToken = 0,

    /// <summary>
    /// Send the key as an <c>apiKey</c> query string parameter. Supported for compatibility with
    /// tooling that cannot set request headers; prefer <see cref="BearerToken"/> otherwise.
    /// </summary>
    QueryString = 1,
}
