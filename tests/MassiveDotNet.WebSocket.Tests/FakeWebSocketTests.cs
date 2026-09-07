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
}
