using System.Net;
using System.Text;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Serves pre-encoded response bodies, choosing which one from the request's own cursor.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StubHandler"/> and <see cref="PagingStubHandler"/> both build a
/// <c>StringContent</c> per request, which re-encodes the whole body to UTF-8 inside the call.
/// That is invisible in an ordinary assertion and ruinous in an allocation measurement: a 50,000
/// row payload would charge several megabytes of the harness's own work to the SDK. Here the
/// bytes are encoded once, in the constructor, and every response is a view over them.
/// </para>
/// <para>
/// Pages are selected by the <c>cursor</c> the request carries rather than by a counter, because
/// a measurement runs its path several times to warm up: a counter-driven script would serve the
/// second traversal the tail of the first, and the run that was actually measured would walk one
/// page while claiming to walk a hundred.
/// </para>
/// <para>
/// Deliberately minimal in the other direction too: no <c>Content-Type</c> is set, because the
/// transport does not inspect one and every header this handler builds is a byte the measurement
/// would otherwise attribute to the code under test.
/// </para>
/// </remarks>
internal sealed class InlineStubHandler : HttpMessageHandler
{
    private const string CursorParameter = "cursor=";

    private readonly ReadOnlyMemory<byte>[] _pages;

    /// <param name="pages">
    /// Bodies to serve, indexed by the request's <c>cursor</c> value. A request carrying no cursor
    /// gets the first; one naming an index past the end gets the last, so a runaway traversal
    /// fails a count assertion rather than an index check.
    /// </param>
    public InlineStubHandler(params string[] pages) =>
        _pages = [.. pages.Select(page => (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(page))];

    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;

        ReadOnlyMemory<byte> body = _pages[Math.Min(CursorIndex(request.RequestUri), _pages.Length - 1)];

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ReadOnlyMemoryContent(body),
        });
    }

    private static int CursorIndex(Uri? requestUri)
    {
        ReadOnlySpan<char> query = requestUri?.Query ?? default;
        int start = query.IndexOf(CursorParameter, StringComparison.Ordinal);

        if (start < 0)
        {
            return 0;
        }

        ReadOnlySpan<char> value = query[(start + CursorParameter.Length)..];
        int end = value.IndexOf('&');

        return int.Parse(end < 0 ? value : value[..end], provider: null);
    }
}
