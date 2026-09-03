using System.Net;
using System.Text;
using NodaTime;

namespace MassiveDotNet.Rest.Tests;

/// <summary>Captures the outbound request and replays a canned response.</summary>
internal sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public StubHandler(string body) : this(HttpStatusCode.OK, body)
    {
    }

    public Uri? LastRequestUri { get; private set; }

    public string? LastAuthorization { get; private set; }

    public Duration? RetryAfter { get; init; }

    /// <summary>How many requests this handler has seen, so a test can assert none were sent.</summary>
    public int RequestCount { get; private set; }

    /// <summary>The content type of the canned body. The filing file route serves a document, not JSON (D-R4).</summary>
    public string MediaType { get; init; } = "application/json";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        LastRequestUri = request.RequestUri;
        LastAuthorization = request.Headers.Authorization?.ToString();

        HttpResponseMessage response = new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, MediaType),
        };

        if (RetryAfter is { } retryAfter)
        {
            response.Headers.RetryAfter =
                new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter.ToTimeSpan());
        }

        return Task.FromResult(response);
    }
}
