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

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("""{"status":"OK","results":[]}"""),
            RequestMessage = request,
        });
    }
}
