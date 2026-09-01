using System.Net;

namespace MassiveDotNet;

/// <summary>
/// Thrown when the Massive platform API rejects a request with HTTP 429.
/// </summary>
public sealed class MassiveRateLimitExceededException : MassiveApiException
{
    /// <summary>Initializes a new instance of the <see cref="MassiveRateLimitExceededException"/> class.</summary>
    /// <param name="message">The error message, preferring the server-supplied text.</param>
    /// <param name="retryAfter">How long to wait before retrying, from the <c>Retry-After</c> header.</param>
    /// <param name="requestId">The server-assigned request identifier, when present.</param>
    public MassiveRateLimitExceededException(
        string message,
        TimeSpan? retryAfter = null,
        string? requestId = null)
        : base(HttpStatusCode.TooManyRequests, message, requestId)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// How long the server asked the caller to wait before retrying, when it supplied a
    /// <c>Retry-After</c> header.
    /// </summary>
    public TimeSpan? RetryAfter { get; }
}
