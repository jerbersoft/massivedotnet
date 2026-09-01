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

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        LastAuthorization = request.Headers.Authorization?.ToString();

        HttpResponseMessage response = new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        if (RetryAfter is { } retryAfter)
        {
            response.Headers.RetryAfter =
                new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter.ToTimeSpan());
        }

        return Task.FromResult(response);
    }
}
