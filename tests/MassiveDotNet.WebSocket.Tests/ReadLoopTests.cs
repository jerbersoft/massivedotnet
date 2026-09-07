using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using MassiveDotNet.WebSocket.Internal;
using NodaTime;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

public class ReadLoopTests
{
    [Fact]
    public async Task ItReassemblesAMessageSplitAcrossFrames()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueFragmented("""[{"ev":"status","status":"connected","message":"ok"}]""", chunkSize: 7);

        FrameReader reader = new(socket, maxMessageBytes: 4096);
        byte[] destination = new byte[4096];

        int length = await reader.ReadMessageAsync(destination, TestContext.Current.CancellationToken);

        Assert.Equal(
            """[{"ev":"status","status":"connected","message":"ok"}]""",
            Encoding.UTF8.GetString(destination, 0, length));
    }

    // The ceiling is not a tuning knob. Without it a server that never sets EndOfMessage makes the
    // client grow a buffer until it dies, which is the one unbounded path the protocol leaves open.
    [Fact]
    public async Task AMessageBeyondTheCeilingThrowsRatherThanGrowing()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueFragmented(new string('x', 512), chunkSize: 16);

        FrameReader reader = new(socket, maxMessageBytes: 64);

        MassiveStreamException error = await Assert.ThrowsAsync<MassiveStreamException>(async () =>
            await reader.ReadMessageAsync(new byte[64], TestContext.Current.CancellationToken));

        Assert.Contains("64", error.Message, StringComparison.Ordinal);
    }

    // The defect this test guards against: a destination sized larger than maxMessageBytes must
    // still be refused at the ceiling, not at the destination's own length. A guard that only
    // compares written bytes to destination.Length lets any caller with a bigger buffer silently
    // raise the ceiling -- the constructor argument would exist in name only.
    [Fact]
    public async Task ACeilingSmallerThanTheDestinationIsStillEnforced()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueFragmented(new string('x', 512), chunkSize: 16);

        FrameReader reader = new(socket, maxMessageBytes: 64);
        byte[] destination = new byte[4096];

        MassiveStreamException error = await Assert.ThrowsAsync<MassiveStreamException>(async () =>
            await reader.ReadMessageAsync(destination, TestContext.Current.CancellationToken));

        Assert.Contains("64", error.Message, StringComparison.Ordinal);
    }

    // Two different faults reached one message: a destination shorter than the ceiling is this SDK
    // handing itself too small a buffer, where an overrun is the server sending too much. Reporting
    // the ceiling for both sends a caller after the server for a bug on our side. Unreachable today
    // -- FrameReader is internal and both call sites size at or above the ceiling -- so this pins the
    // arm rather than a path anyone can hit.
    [Fact]
    public async Task AShortDestinationIsReportedAsTheBufferNotTheCeiling()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueFragmented(new string('x', 512), chunkSize: 16);

        FrameReader reader = new(socket, maxMessageBytes: 4096);
        byte[] destination = new byte[64];

        MassiveStreamException error = await Assert.ThrowsAsync<MassiveStreamException>(async () =>
            await reader.ReadMessageAsync(destination, TestContext.Current.CancellationToken));

        Assert.Contains("64 byte destination", error.Message, StringComparison.Ordinal);
        Assert.Contains("below the 4096 byte ceiling", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLoopDeliversEveryMessageInOrder()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText("""[{"ev":"status","status":"connected","message":"ok"}]""");
        socket.EnqueueText("""[{"ev":"status","status":"auth_success","message":"authenticated"}]""");

        List<string> seen = [];
        FrameReader reader = new(socket, maxMessageBytes: 4096);
        byte[] destination = new byte[4096];

        for (int i = 0; i < 2; i++)
        {
            int length = await reader.ReadMessageAsync(destination, TestContext.Current.CancellationToken);
            seen.Add(Encoding.UTF8.GetString(destination, 0, length));
        }

        Assert.Equal(2, seen.Count);
        Assert.Contains("connected", seen[0], StringComparison.Ordinal);
        Assert.Contains("auth_success", seen[1], StringComparison.Ordinal);
    }

    // EnqueueClose produces a genuine Close frame -- distinct from AbortNext's throw -- so this is
    // the first test able to exercise FrameReader's Close branch at all.
    [Fact]
    public async Task ACleanCloseFrameThrowsRatherThanReturningAPartialMessage()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueClose(WebSocketCloseStatus.NormalClosure, "bye");

        FrameReader reader = new(socket, maxMessageBytes: 4096);

        await Assert.ThrowsAsync<MassiveStreamException>(async () =>
            await reader.ReadMessageAsync(new byte[4096], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StartReadingDispatchesEachMessageToOnMessageInOrder()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText("""[{"ev":"status","status":"connected","message":"ok"}]""");
        socket.EnqueueText(AuthSuccess);
        socket.EnqueueText("""[{"ev":"T","sym":"AAPL"}]""");
        socket.EnqueueText("""[{"ev":"Q","sym":"MSFT"}]""");

        await using MassiveStreamConnection connection = CreateConnection(socket);
        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        List<string> received = [];
        TaskCompletionSource secondMessage = new(TaskCreationOptions.RunContinuationsAsynchronously);

        connection.OnMessage = message =>
        {
            received.Add(Encoding.UTF8.GetString(message.Span));

            if (received.Count == 2)
            {
                secondMessage.TrySetResult();
            }

            return ValueTask.CompletedTask;
        };

        connection.StartReading();
        await secondMessage.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, received.Count);
        Assert.Contains("AAPL", received[0], StringComparison.Ordinal);
        Assert.Contains("MSFT", received[1], StringComparison.Ordinal);
    }

    // The allocation this loop is not allowed to repeat: ReadLoopAsync allocates its buffer once,
    // before the while loop starts, and hands OnMessage a slice of that same array every time. If
    // a future change moved the allocation inside the loop, this would be the test to catch it --
    // every dispatched message would come back backed by a different array.
    [Fact]
    public async Task TheLoopHandsOnMessageTheSameBackingArrayEveryTime()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText("""[{"ev":"status","status":"connected","message":"ok"}]""");
        socket.EnqueueText(AuthSuccess);
        socket.EnqueueText("""[{"ev":"T","sym":"AAPL"}]""");
        socket.EnqueueText("""[{"ev":"Q","sym":"MSFT"}]""");

        await using MassiveStreamConnection connection = CreateConnection(socket);
        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        List<byte[]> backingArrays = [];
        TaskCompletionSource secondMessage = new(TaskCreationOptions.RunContinuationsAsynchronously);

        connection.OnMessage = message =>
        {
            // MemoryMarshal.TryGetArray recovers the underlying array without copying, so this
            // inspects identity rather than content -- exactly what "allocated once" claims.
            Assert.True(MemoryMarshal.TryGetArray(message, out ArraySegment<byte> segment));
            backingArrays.Add(segment.Array!);

            if (backingArrays.Count == 2)
            {
                secondMessage.TrySetResult();
            }

            return ValueTask.CompletedTask;
        };

        connection.StartReading();
        await secondMessage.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Same(backingArrays[0], backingArrays[1]);
    }

    // FrameReader does not distinguish a close between messages from one mid-message -- both are
    // "the stream closed while a message was being read" from its point of view (Step 3). So a
    // clean close ends the loop the same way an abrupt drop does: the task faults rather than
    // completing, and a caller learns which happened from the exception type, not the task status.
    [Fact]
    public async Task ACleanCloseFaultsTheLoopTaskWithAStreamException()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText("""[{"ev":"status","status":"connected","message":"ok"}]""");
        socket.EnqueueText(AuthSuccess);
        socket.EnqueueClose(WebSocketCloseStatus.NormalClosure, "bye");

        await using MassiveStreamConnection connection = CreateConnection(socket);
        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        connection.StartReading();

        await Assert.ThrowsAsync<MassiveStreamException>(
            async () => await connection.ReadLoopTask.WaitAsync(TestContext.Current.CancellationToken));
        Assert.True(connection.ReadLoopTask.IsFaulted);
    }

    // An abrupt drop is a fault: the loop's task must fault rather than vanish, so a caller awaiting
    // ReadLoopTask observes the failure instead of a loop that silently stopped delivering messages.
    [Fact]
    public async Task AnAbruptDropFaultsTheLoopTask()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText("""[{"ev":"status","status":"connected","message":"ok"}]""");
        socket.EnqueueText(AuthSuccess);
        socket.AbortNext();

        await using MassiveStreamConnection connection = CreateConnection(socket);
        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        connection.StartReading();

        await Assert.ThrowsAsync<WebSocketException>(
            async () => await connection.ReadLoopTask.WaitAsync(TestContext.Current.CancellationToken));
        Assert.True(connection.ReadLoopTask.IsFaulted);
    }

    // Cancellation is a caller-requested exit, not a fault: the loop's task must observe it as a
    // cancellation rather than as an unhandled exception.
    [Fact]
    public async Task CancellationEndsTheLoopWithoutFaulting()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText("""[{"ev":"status","status":"connected","message":"ok"}]""");
        socket.EnqueueText(AuthSuccess);

        await using MassiveStreamConnection connection = CreateConnection(socket);
        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        connection.StartReading();
        await connection.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await connection.ReadLoopTask.WaitAsync(TestContext.Current.CancellationToken));
        Assert.True(connection.ReadLoopTask.IsCanceled);
    }

    private const string AuthSuccess = """[{"ev":"status","status":"auth_success","message":"authenticated"}]""";

    private static MassiveStreamConnection CreateConnection(FakeWebSocket socket)
    {
        MassiveStreamOptions options = new() { ApiKey = "test-key" };

        return new MassiveStreamConnection(options, MassiveMarket.Stocks, () => socket, SystemClock.Instance);
    }
}
