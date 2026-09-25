using System.Net;
using System.Text;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Fails the first <c>failures</c> requests the way <c>SocketsHttpHandler</c> reports a connection
/// that broke before any answer arrived, then answers <c>200</c>. <see cref="ScriptedHandler"/>
/// scripts responses, and a transport failure is not one: it is an exception thrown in place of
/// a response.
/// </summary>
/// <remarks>
/// The exception mirrors the runtime's own shape for a reset mid-send: an
/// <see cref="HttpRequestException"/> carrying the given <see cref="HttpRequestError"/> around an
/// <see cref="IOException"/> (dotnet/runtime v10.0.0, <c>HttpConnection.MapSendException</c>).
/// </remarks>
internal sealed class TransportFailureHandler(HttpRequestError error, int failures) : HttpMessageHandler
{
    private int _requestCount;

    public int RequestCount => Volatile.Read(ref _requestCount);

    /// <summary>
    /// Runs just before a failure is thrown. The cancellation test uses it to cancel the caller's
    /// token mid-send, from a handler that, unlike the runtime's, does not observe it afterwards.
    /// </summary>
    public Action? BeforeFailing { get; init; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        int attempt = Interlocked.Increment(ref _requestCount);

        if (attempt <= failures)
        {
            BeforeFailing?.Invoke();

            throw new HttpRequestException(
                error,
                "An error occurred while sending the request.",
                new IOException("Unable to read data from the transport connection: Connection reset by peer."));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"status":"OK","results":[]}""", Encoding.UTF8, "application/json"),
        });
    }
}
