using System.Net;
using NodaTime;

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
        Duration? retryAfter = null,
        string? requestId = null)
        : base(HttpStatusCode.TooManyRequests, message, requestId)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// How long the server asked the caller to wait before retrying, when it supplied a
    /// <c>Retry-After</c> header carrying a delta-seconds value.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> when the header was absent, or when it carried an HTTP-date rather
    /// than a delta. The date form is not converted, because doing so would mean trusting the
    /// local clock against the server's; treat <see langword="null"/> as "no hint given" and fall
    /// back to your own backoff policy.
    /// </remarks>
    public Duration? RetryAfter { get; }
}
