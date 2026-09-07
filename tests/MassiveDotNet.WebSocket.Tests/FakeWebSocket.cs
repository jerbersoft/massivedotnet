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
    private readonly record struct Frame(
        byte[] Payload,
        bool EndOfMessage,
        bool Abort,
        WebSocketMessageType MessageType,
        WebSocketCloseStatus? CloseStatus,
        string? CloseStatusDescription);

    private readonly Channel<Frame> _inbound = Channel.CreateUnbounded<Frame>();

    // The frame currently being delivered and how far into its payload the last receive got to. A
    // real socket fills whatever buffer it is handed rather than requiring one big enough for a
    // whole frame, so one enqueued frame can span several ReceiveAsync calls (rule: this fake must
    // behave like the socket it stands in for, not like a convenient shortcut).
    private Frame? _current;
    private int _offset;

    public WebSocketState State { get; private set; } = WebSocketState.None;

    /// <summary>Every message sent, decoded as UTF-8, in order.</summary>
    public List<string> Sent { get; } = [];

    /// <summary>
    /// The status from the most recently delivered close frame, mirroring
    /// <see cref="ClientWebSocket.CloseStatus"/>, which only becomes meaningful after a close is
    /// actually received rather than merely queued.
    /// </summary>
    public WebSocketCloseStatus? CloseStatus { get; private set; }

    /// <summary>The description from the most recently delivered close frame.</summary>
    public string? CloseStatusDescription { get; private set; }

    /// <summary>Completes once a message has been sent, so a test need not poll.</summary>
    public TaskCompletionSource SentSignal { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int ConnectCount { get; private set; }

    /// <summary>Queues one complete message.</summary>
    public void EnqueueText(string message) =>
        _inbound.Writer.TryWrite(new Frame(
            Encoding.UTF8.GetBytes(message),
            EndOfMessage: true,
            Abort: false,
            WebSocketMessageType.Text,
            CloseStatus: null,
            CloseStatusDescription: null));

    /// <summary>Queues one message split across frames, to exercise reassembly.</summary>
    public void EnqueueFragmented(string message, int chunkSize)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message);

        for (int offset = 0; offset < payload.Length; offset += chunkSize)
        {
            int length = Math.Min(chunkSize, payload.Length - offset);
            bool last = offset + length >= payload.Length;

            _inbound.Writer.TryWrite(new Frame(
                payload[offset..(offset + length)],
                last,
                Abort: false,
                WebSocketMessageType.Text,
                CloseStatus: null,
                CloseStatusDescription: null));
        }
    }

    /// <summary>
    /// Queues an ordinary close frame, the way a graceful server-initiated close behaves. Distinct
    /// from <see cref="AbortNext"/>: a real <see cref="ClientWebSocket"/> reports a clean close as
    /// a normal <see cref="ReceiveAsync"/> result carrying <see cref="WebSocketMessageType.Close"/>
    /// with zero bytes -- the status and description surface separately, never through the buffer
    /// -- while an abrupt drop throws instead of returning at all.
    /// </summary>
    public void EnqueueClose(WebSocketCloseStatus status, string? description) =>
        _inbound.Writer.TryWrite(new Frame(
            [],
            EndOfMessage: true,
            Abort: false,
            WebSocketMessageType.Close,
            status,
            description));

    /// <summary>Makes the next receive throw, the way the server behaves after auth_failed.</summary>
    public void AbortNext() =>
        _inbound.Writer.TryWrite(new Frame(
            [],
            EndOfMessage: true,
            Abort: true,
            WebSocketMessageType.Close,
            CloseStatus: null,
            CloseStatusDescription: null));

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
        // A frame left partly delivered by a previous, too-small buffer carries over; only pull a
        // fresh one off the queue once the current one is exhausted.
        Frame frame;

        if (_current is { } inProgress)
        {
            frame = inProgress;
        }
        else
        {
            frame = await _inbound.Reader.ReadAsync(cancellationToken);
            _offset = 0;

            if (frame.Abort)
            {
                State = WebSocketState.Aborted;
                throw new WebSocketException(
                    WebSocketError.ConnectionClosedPrematurely,
                    "The remote party closed the WebSocket connection without completing the close handshake.");
            }
        }

        int remaining = frame.Payload.Length - _offset;
        int count = Math.Min(remaining, buffer.Length);

        frame.Payload.AsSpan(_offset, count).CopyTo(buffer.Span);
        _offset += count;

        bool frameExhausted = _offset >= frame.Payload.Length;
        bool endOfMessage = frameExhausted && frame.EndOfMessage;

        // Carry the frame to the next call if the buffer could not hold all of it; otherwise the
        // next receive starts a fresh frame.
        _current = frameExhausted ? null : frame;

        if (frameExhausted && frame.MessageType == WebSocketMessageType.Close)
        {
            State = WebSocketState.CloseReceived;
            CloseStatus = frame.CloseStatus;
            CloseStatusDescription = frame.CloseStatusDescription;
        }

        return new ValueWebSocketReceiveResult(count, frame.MessageType, endOfMessage);
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
