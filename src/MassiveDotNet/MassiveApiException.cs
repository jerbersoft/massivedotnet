using System.Net;

namespace MassiveDotNet;

/// <summary>
/// Thrown when the Massive platform API returns an unsuccessful response.
/// </summary>
public class MassiveApiException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="MassiveApiException"/> class.</summary>
    /// <param name="statusCode">The HTTP status code returned by the server.</param>
    /// <param name="message">The error message, preferring the server-supplied text.</param>
    /// <param name="requestId">The server-assigned request identifier, when present.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public MassiveApiException(
        HttpStatusCode statusCode,
        string message,
        string? requestId = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        RequestId = requestId;
    }

    /// <summary>The HTTP status code returned by the server.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// The server-assigned request identifier. Include this when contacting Massive support.
    /// </summary>
    public string? RequestId { get; }
}
