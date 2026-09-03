using System.Net;
using System.Text;
using MassiveDotNet.Http;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The transport's body copy (D-R4): the bytes land in the caller's stream unchanged, a failure
/// status raises the same exception <c>GetAsync</c> does, and the arguments are checked the same
/// way. Tested on the transport directly, since its one caller adds no logic of its own.
/// </summary>
public sealed class DownloadTests
{
    private const string Html = "<html><body><p>Item 1A. Risk Factors</p></body></html>";
    private const string FileUri = "/v1/reference/sec/filings/0001683168-26-006873/files/myx_i10k-053126.htm";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MassiveHttpTransport Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = MassiveEndpoints.Production });

    [Fact]
    public async Task CopiesTheBodyToTheDestination()
    {
        StubHandler handler = new(Html) { MediaType = "text/html" };
        using MassiveHttpTransport transport = Create(handler);
        using MemoryStream destination = new();

        await transport.DownloadAsync(FileUri, destination, Ct);

        Assert.Equal(Html, Encoding.UTF8.GetString(destination.ToArray()));
        Assert.Equal("https://api.massive.com" + FileUri, handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task RaisesTheApiExceptionOnAFailureStatus()
    {
        StubHandler handler = new(HttpStatusCode.NotFound, """{ "status": "NOT_FOUND", "error": "File not found.", "request_id": "r" }""");
        using MassiveHttpTransport transport = Create(handler);
        using MemoryStream destination = new();

        MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(() =>
            transport.DownloadAsync(FileUri, destination, Ct));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("File not found.", exception.Message);
        Assert.Equal("r", exception.RequestId);
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task RefusesANullDestination()
    {
        using MassiveHttpTransport transport = Create(new StubHandler(Html));

        await Assert.ThrowsAsync<ArgumentNullException>(() => transport.DownloadAsync(FileUri, null!, Ct));
    }

    [Fact]
    public async Task RefusesABlankRequestUri()
    {
        using MassiveHttpTransport transport = Create(new StubHandler(Html));
        using MemoryStream destination = new();

        await Assert.ThrowsAsync<ArgumentException>(() => transport.DownloadAsync(" ", destination, Ct));
    }

    [Fact]
    public async Task RefusesADisposedTransport()
    {
        MassiveHttpTransport transport = Create(new StubHandler(Html));
        transport.Dispose();
        using MemoryStream destination = new();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.DownloadAsync(FileUri, destination, Ct));
    }
}
