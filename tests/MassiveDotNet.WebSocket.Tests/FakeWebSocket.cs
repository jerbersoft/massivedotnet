using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using MassiveDotNet.WebSocket.Internal;

namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// A scripted <see cref="IMassiveWebSocket"/>. Inbound frames are queued by the test; outbound
/// messages are recorded verbatim so a test can assert on the exact wire text.
/// </summary>
internal sealed class FakeWebSocket : IMassiveWebSocket
{
    private readonly record struct Frame(byte[] Payload, bool EndOfMessage, bool Abort);

    private readonly Channel<Frame> _inbound = Channel.CreateUnbounded<Frame>();

    public WebSocketState State { get; private set; } = WebSocketState.None;

    /// <summary>Every message sent, decoded as UTF-8, in order.</summary>
    public List<string> Sent { get; } = [];

    /// <summary>Completes once a message has been sent, so a test need not poll.</summary>
    public TaskCompletionSource SentSignal { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int ConnectCount { get; private set; }

    /// <summary>Queues one complete message.</summary>
    public void EnqueueText(string message) =>
        _inbound.Writer.TryWrite(new Frame(Encoding.UTF8.GetBytes(message), EndOfMessage: true, Abort: false));

    /// <summary>Queues one message split across frames, to exercise reassembly.</summary>
    public void EnqueueFragmented(string message, int chunkSize)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message);

        for (int offset = 0; offset < payload.Length; offset += chunkSize)
        {
            int length = Math.Min(chunkSize, payload.Length - offset);
            bool last = offset + length >= payload.Length;

            _inbound.Writer.TryWrite(new Frame(payload[offset..(offset + length)], last, Abort: false));
        }
    }

    /// <summary>Makes the next receive throw, the way the server behaves after auth_failed.</summary>
    public void AbortNext() =>
        _inbound.Writer.TryWrite(new Frame([], EndOfMessage: true, Abort: true));

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        ConnectCount++;
        State = WebSocketState.Open;
        return Task.CompletedTask;
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        Sent.Add(Encoding.UTF8.GetString(buffer.Span));
        SentSignal.TrySetResult();
        SentSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        Frame frame = await _inbound.Reader.ReadAsync(cancellationToken);

        if (frame.Abort)
        {
            State = WebSocketState.Aborted;
            throw new WebSocketException(
                WebSocketError.ConnectionClosedPrematurely,
                "The remote party closed the WebSocket connection without completing the close handshake.");
        }

        frame.Payload.CopyTo(buffer.Span);

        return new ValueWebSocketReceiveResult(frame.Payload.Length, WebSocketMessageType.Text, frame.EndOfMessage);
    }

    public Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        State = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        State = WebSocketState.Closed;
        _inbound.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
