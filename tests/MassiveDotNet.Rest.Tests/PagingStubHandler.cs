using System.Net;
using System.Text;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Serves a scripted sequence of response bodies, recording every request. Unlike
/// <see cref="StubHandler"/> this keeps the full request history, which is what pagination
/// assertions are made against.
/// </summary>
internal sealed class PagingStubHandler : HttpMessageHandler
{
    private readonly string[] _pages;

    public PagingStubHandler(params string[] pages) => _pages = pages;

    public List<Uri> Requests { get; } = [];

    public List<string?> Authorizations { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        Authorizations.Add(request.Headers.Authorization?.ToString());

        // Past the end of the script, keep serving the last page so a runaway
        // traversal shows up as a failed count assertion rather than an IndexOutOfRange.
        string body = _pages[Math.Min(Requests.Count - 1, _pages.Length - 1)];

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}
