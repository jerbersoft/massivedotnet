namespace MassiveDotNet.Extensions.DependencyInjection.Tests;

/// <summary>
/// A primary handler that records what the pipeline produced and answers with an empty envelope.
/// Installed through the <c>IHttpClientBuilder</c> that <c>AddMassive</c> returns, which is the
/// point of returning it: the registration's wire behaviour is observable without a live server.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public HttpRequestMessage Last => Requests[^1];

    /// <summary>
    /// Each request's URI as it was at the moment it was sent. Retry re-sends the same
    /// <see cref="HttpRequestMessage"/> instance, so reading <c>Requests[i].RequestUri</c>
    /// afterwards would report every attempt's URI as the last one's.
    /// </summary>
    public List<string?> SentUris { get; } = [];

    /// <summary>
    /// Statuses to answer with, one per request; the last repeats once exhausted. Defaults to
    /// answering every request with 200.
    /// </summary>
    public IReadOnlyList<System.Net.HttpStatusCode> Statuses { get; init; } =
        [System.Net.HttpStatusCode.OK];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        System.Net.HttpStatusCode status = Statuses[Math.Min(Requests.Count, Statuses.Count - 1)];

        Requests.Add(request);
        SentUris.Add(request.RequestUri?.ToString());

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent("""{"status":"OK","results":[]}"""),
            RequestMessage = request,
        });
    }
}
