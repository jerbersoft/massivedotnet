using System.Net.WebSockets;
using System.Text;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

public class FakeWebSocketTests
{
    [Fact]
    public async Task ItDeliversAWholeMessageInOneReceive()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText("""[{"ev":"status"}]""");

        byte[] buffer = new byte[128];
        ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer, TestContext.Current.CancellationToken);

        Assert.True(result.EndOfMessage);
        Assert.Equal("""[{"ev":"status"}]""", Encoding.UTF8.GetString(buffer, 0, result.Count));
    }

    // The read loop must never assume a message arrives in one frame; the fake has to be able to
    // prove that, or the reassembly path is untested.
    [Fact]
    public async Task ItCanSplitAMessageAcrossFrames()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueFragmented("""[{"ev":"status"}]""", chunkSize: 5);

        byte[] buffer = new byte[128];
        StringBuilder assembled = new();
        ValueWebSocketReceiveResult result;
        int frames = 0;

        do
        {
            result = await socket.ReceiveAsync(buffer, TestContext.Current.CancellationToken);
            assembled.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            frames++;
        }
        while (!result.EndOfMessage);

        Assert.True(frames > 1);
        Assert.Equal("""[{"ev":"status"}]""", assembled.ToString());
    }

    // Task 7's FrameReader shrinks the destination slice on every call to enforce a message-size
    // ceiling, so a frame that outruns the remaining buffer must be a normal partial read, not a
    // crash. A real ClientWebSocket fills whatever buffer it is given and leaves EndOfMessage
    // false until the frame is exhausted.
    [Fact]
    public async Task ItFillsATooSmallBufferAcrossMultipleReceives()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText("0123456789");

        byte[] buffer = new byte[4];
        StringBuilder assembled = new();

        ValueWebSocketReceiveResult first = await socket.ReceiveAsync(buffer, TestContext.Current.CancellationToken);
        Assert.Equal(4, first.Count);
        Assert.False(first.EndOfMessage);
        assembled.Append(Encoding.UTF8.GetString(buffer, 0, first.Count));

        ValueWebSocketReceiveResult second = await socket.ReceiveAsync(buffer, TestContext.Current.CancellationToken);
        Assert.Equal(4, second.Count);
        Assert.False(second.EndOfMessage);
        assembled.Append(Encoding.UTF8.GetString(buffer, 0, second.Count));

        ValueWebSocketReceiveResult third = await socket.ReceiveAsync(buffer, TestContext.Current.CancellationToken);
        Assert.Equal(2, third.Count);
        Assert.True(third.EndOfMessage);
        assembled.Append(Encoding.UTF8.GetString(buffer, 0, third.Count));

        Assert.Equal("0123456789", assembled.ToString());
    }

    [Fact]
    public async Task ItRecordsWhatWasSent()
    {
        await using FakeWebSocket socket = new();

        await socket.SendAsync(Encoding.UTF8.GetBytes("hello"), TestContext.Current.CancellationToken);

        Assert.Equal(["hello"], socket.Sent);
    }

    // The server closes without a close handshake after auth_failed, so ReceiveAsync throws
    // rather than reporting a Close frame. Reconnect is written against this shape.
    [Fact]
    public async Task ItCanAbortTheWayTheRealServerDoes()
    {
        await using FakeWebSocket socket = new();
        socket.AbortNext();

        await Assert.ThrowsAsync<WebSocketException>(async () =>
            await socket.ReceiveAsync(new byte[128], TestContext.Current.CancellationToken));
    }

    // Distinct from AbortNext: a graceful close is a normal ReceiveAsync result, not a throw.
    // Task 7's FrameReader branches on WebSocketMessageType.Close, so this path needs its own
    // fixture rather than being folded into the abort case.
    [Fact]
    public async Task ItCanEnqueueAnOrdinaryCloseFrame()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueClose(WebSocketCloseStatus.NormalClosure, "bye");

        ValueWebSocketReceiveResult result = await socket.ReceiveAsync(new byte[128], TestContext.Current.CancellationToken);

        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        Assert.True(result.EndOfMessage);
        Assert.Equal(0, result.Count);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.CloseStatus);
        Assert.Equal("bye", socket.CloseStatusDescription);
    }
}
