using System.Net.WebSockets;
using MassiveDotNet.WebSocket.Internal;
using NodaTime;
using NodaTime.Testing;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// Issue #66 / D-W22-24. The SDK used to abort every connection: ClientWebSocket.Dispose() does not
/// perform the closing handshake, so a server could not tell a deliberate disconnect from a network
/// failure -- which matters on a plan that allows one connection per cluster, where how fast the
/// server reclaims a departed connection decides whether our own reconnect collides with us.
/// </summary>
public class ClosingHandshakeTests
{
    private const string Connected = """[{"ev":"status","status":"connected","message":"Connected Successfully"}]""";
    private const string AuthSuccess = """[{"ev":"status","status":"auth_success","message":"authenticated"}]""";

    private static MassiveStreamConnection CreateConnection(FakeWebSocket socket)
    {
        MassiveStreamOptions options = new() { ApiKey = "test-key" };

        return new MassiveStreamConnection(options, MassiveMarket.Stocks, () => socket, new FakeClock(Instant.FromUnixTimeSeconds(0)));
    }

    [Fact]
    public async Task DisposingAConnectedStreamSendsANormalClosure()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamConnection connection = CreateConnection(socket);
        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        await connection.DisposeAsync();

        Assert.Equal(1, socket.CloseSentCount);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.SentCloseStatus);

        // Ordering is the requirement, not merely that both happened: a close sent AFTER the socket
        // was torn down reaches a dead socket and tells the server nothing.
        Assert.Equal(1, socket.CloseSentCountAtDispose);
    }

    // Rule 11 by construction rather than by care taken at the site: nothing is rendered into the
    // description, so there is no path by which a key could reach one.
    [Fact]
    public async Task TheCloseCarriesNoDescription()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamConnection connection = CreateConnection(socket);
        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        await connection.DisposeAsync();

        Assert.Null(socket.SentCloseDescription);
    }

    // A stream that never opened has no socket to close. This is the guard on `_socket is not null`,
    // and it must not become a NullReferenceException on the disposal path.
    [Fact]
    public async Task DisposingANeverOpenedStreamSendsNothingAndCompletes()
    {
        FakeWebSocket socket = new();

        MassiveStreamConnection connection = CreateConnection(socket);

        await connection.DisposeAsync();

        Assert.Equal(0, socket.CloseSentCount);
    }

    // The case the production logs actually show: the remote aborted without a close frame, leaving
    // the socket Aborted. Nothing may be pushed onto it, and disposal must still complete.
    [Fact]
    public async Task DisposingAnAbortedStreamSendsNothingAndCompletes()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);
        socket.AbortNext();

        MassiveStreamOptions options = new() { ApiKey = "test-key", Reconnect = null };
        MassiveStreamConnection connection = new(
            options,
            MassiveMarket.Stocks,
            () => socket,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await connection.ReadLoopTask.WaitAsync(TestContext.Current.CancellationToken));

        await connection.DisposeAsync();

        Assert.Equal(0, socket.CloseSentCount);
    }

    // The failure this guards: a close that never completes must not hold disposal open. Without
    // CloseTimeout the await below never returns and the test hangs until the runner's own much
    // longer bound.
    [Fact]
    public async Task ACloseThatNeverCompletesDoesNotHangDisposal()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamConnection connection = CreateConnection(socket);
        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        socket.GateNextClose();

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(Duration.FromSeconds(10).ToTimeSpan());

        await connection.DisposeAsync().AsTask().WaitAsync(cts.Token);

        socket.ReleaseClose();
    }

    // Disposal is a caller saying "I am leaving". A courtesy frame that will not send is not their
    // problem and must never be how their teardown throws.
    [Fact]
    public async Task ACloseThatThrowsDoesNotEscapeDisposal()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamConnection connection = CreateConnection(socket);
        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        socket.ThrowOnClose = true;

        await connection.DisposeAsync();

        // The attempt is recorded before the throw, so this distinguishes "swallowed the failure"
        // from "never tried".
        Assert.Equal(1, socket.CloseSentCount);
    }
}
