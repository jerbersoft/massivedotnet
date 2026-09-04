using System.Net;
using System.Text;

namespace MassiveDotNet.Benchmarks;

/// <summary>
/// Answers every request inline from a body encoded once, so the harness contributes no
/// per-iteration work of its own to the figure being measured.
/// </summary>
internal sealed class InlineHandler : HttpMessageHandler
{
    private readonly ReadOnlyMemory<byte> _body;

    public InlineHandler(string body) => _body = Encoding.UTF8.GetBytes(body);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ReadOnlyMemoryContent(_body),
        });
}
