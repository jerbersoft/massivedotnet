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

        // Proves a close was actually sent before trusting the description on it -- otherwise this
        // assertion passes vacuously against a connection that sent nothing at all, which is not a
        // hypothetical: it is exactly the state this file's own RED run was in before Task 2 wired
        // up CloseQuietlyAsync.
        Assert.Equal(1, socket.CloseSentCount);
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

    // Pins that disposal completes cleanly across a full connect-fault-dispose lifecycle on a
    // socket the remote aborted -- the case production logs actually show, and a path exercised
    // nowhere else in this file. The zero below is the FAKE's own State guard holding (Aborted is
    // neither Open nor CloseReceived), not evidence that production code skipped the close attempt
    // -- that distinction, and the throw-swallowing it implies, is what
    // ACloseThatThrowsDoesNotEscapeDisposal covers instead.
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

    private static MassiveStreamOptions FastReconnect() => new()
    {
        ApiKey = "k",
        Reconnect = new MassiveStreamReconnectOptions
        {
            InitialBackoff = Duration.FromMilliseconds(1),
            MaxBackoff = Duration.FromMilliseconds(5),
            Jitter = 0,
        },
    };

    // Case 1 from D-W23: a message past MaxMessageBytes faults the read loop while the socket is
    // still OPEN, so the handover has a live connection to close politely. This is the case that
    // makes the reconnect call site load-bearing rather than decorative.
    [Fact]
    public async Task TheReconnectHandoverClosesAPreviousSocketThatIsStillOpen()
    {
        FakeWebSocket first = new();
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);
        first.EnqueueFragmented(new string('x', 4096), chunkSize: 64);

        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        MassiveStreamOptions options = FastReconnect();
        options.MaxMessageBytes = 256;

        await using MassiveStreamConnection connection = new(
            options,
            MassiveMarket.Stocks,
            () => created++ == 0 ? first : second,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        await WaitUntilAsync(() => second.ConnectCount > 0);

        Assert.Equal(1, first.CloseSentCount);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, first.SentCloseStatus);
    }

    // Case 2 from D-W23: the server sent a close frame first, which FrameReader reports as a
    // MassiveStreamException while leaving the socket CloseReceived, not Aborted. Before this task
    // the SDK received that close and never answered it; this is the first code path that does.
    // Unlike the Aborted test below, the fake's State guard PERMITS a send here, so this is the one
    // that actually exercises -- and would fail without -- the CloseQuietlyAsync call this task
    // added to the handover.
    [Fact]
    public async Task TheReconnectHandoverAnswersACloseTheServerSent()
    {
        FakeWebSocket first = new();
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);
        first.EnqueueClose(WebSocketCloseStatus.NormalClosure, "bye");

        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        await using MassiveStreamConnection connection = new(
            FastReconnect(),
            MassiveMarket.Stocks,
            () => created++ == 0 ? first : second,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        await WaitUntilAsync(() => second.ConnectCount > 0);

        Assert.Equal(1, first.CloseSentCount);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, first.SentCloseStatus);
    }

    // Pins that a reconnect handover completes cleanly across a full connect-fault-reconnect
    // lifecycle on a socket the remote aborted -- the case production logs actually show, and a
    // lifecycle exercised nowhere else in this file. The zero below is the FAKE's own State guard
    // holding (Aborted is neither Open nor CloseReceived), not evidence that this call site
    // attempted and skipped the close -- that discriminative guard is
    // TheReconnectHandoverAnswersACloseTheServerSent, above.
    [Fact]
    public async Task TheReconnectHandoverSendsNothingOnAnAbortedSocket()
    {
        FakeWebSocket first = new();
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);
        first.AbortNext();

        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        await using MassiveStreamConnection connection = new(
            FastReconnect(),
            MassiveMarket.Stocks,
            () => created++ == 0 ? first : second,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        await WaitUntilAsync(() => second.ConnectCount > 0);

        Assert.Equal(0, first.CloseSentCount);
    }

    // Polling rather than a signal: the handover happens inside the read loop, which exposes no
    // hook a test can await. Bounded so a regression fails readably instead of hanging.
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource cts = new();
        cts.CancelAfter(Duration.FromSeconds(5).ToTimeSpan());

        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(Duration.FromMilliseconds(20).ToTimeSpan(), cts.Token);
        }
    }
}
