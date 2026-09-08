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

    /// <summary>
    /// When set, every "subscribe" action sent through this socket is acknowledged immediately
    /// and synchronously, as part of the send itself -- "success", naming each pair exactly as
    /// the real server does ("subscribed to: T.AAPL") -- rather than depending on a test to
    /// separately notice the send and enqueue a response later.
    /// </summary>
    /// <remarks>
    /// Off by default: every existing test drives acknowledgement timing itself, and turning this
    /// on for everyone would silently change what those tests exercise. Added for tests that need
    /// many concurrent subscribes to complete reliably without depending on a separate thread or
    /// task noticing each send in time (Task 12 review round 1, F2): a prior design used a
    /// dedicated responder thread racing 64 blocked participants for the same acknowledgement, and
    /// a full-solution run (22 attempts) found it still flaked about 9% of the time -- a false
    /// *failure* on correct code, which is worse than a false negative because it breaks a green
    /// build. With this on, nothing needs to "notice" a send at all, so there is no scheduling
    /// window left to lose.
    /// </remarks>
    public bool AutoAcknowledgeSubscribes { get; set; }

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

    private TaskCompletionSource? _connectGate;

    /// <summary>
    /// Makes the NEXT <see cref="ConnectAsync"/> call wait until <see cref="ReleaseConnect"/> is
    /// called. Real thread scheduling cannot be trusted to land a test's own assertions inside a
    /// handshake that otherwise completes synchronously (nothing on this fake actually awaits
    /// anything), so this gives a test a deterministic window to observe what a caller does while a
    /// reconnect attempt is still in flight.
    /// </summary>
    public void GateNextConnect() => _connectGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Releases a connect gated by <see cref="GateNextConnect"/>. A no-op if none is gated.</summary>
    public void ReleaseConnect() => _connectGate?.TrySetResult();

    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        // The field is deliberately NOT cleared here before awaiting: ReleaseConnect reads the
        // same field, so clearing it first would race a test calling ReleaseConnect after this
        // await has already started but before it reassigns anything -- ReleaseConnect would then
        // find null and silently no-op forever, hanging until cancellationToken's own much longer
        // bound. Leaving the field set means ReleaseConnect can always find the gate it needs to
        // complete; awaiting an already-completed Task on a later call is harmless.
        if (_connectGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        ConnectCount++;
        State = WebSocketState.Open;
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        string text = Encoding.UTF8.GetString(buffer.Span);
        Sent.Add(text);
        SentSignal.TrySetResult();
        SentSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (AutoAcknowledgeSubscribes && TryBuildSubscribeAcknowledgement(text, out string acknowledgement))
        {
            EnqueueText(acknowledgement);
        }

        return ValueTask.CompletedTask;
    }

    // Parses exactly the shape MassiveStreamConnection.SendActionAsync produces --
    // {"action":"subscribe","params":"T.AAPL,T.MSFT"} -- and builds the "success" acknowledgement
    // the real server sends for it, one status event per pair. Plain string slicing rather than a
    // JSON parser: this fake only ever needs to understand text it produced itself, through that
    // one call site, so there is nothing here that needs to tolerate an arbitrary shape.
    private static bool TryBuildSubscribeAcknowledgement(string sentText, out string acknowledgement)
    {
        acknowledgement = string.Empty;

        if (!sentText.Contains("\"action\":\"subscribe\"", StringComparison.Ordinal))
        {
            return false;
        }

        const string ParamsMarker = "\"params\":\"";
        int start = sentText.IndexOf(ParamsMarker, StringComparison.Ordinal);

        if (start < 0)
        {
            return false;
        }

        start += ParamsMarker.Length;
        int end = sentText.IndexOf('"', start);

        if (end < 0)
        {
            return false;
        }

        string[] pairs = sentText[start..end].Split(',', StringSplitOptions.RemoveEmptyEntries);

        string events = string.Join(
            ',',
            pairs.Select(pair => $$"""{"ev":"status","status":"success","message":"subscribed to: {{pair}}"}"""));

        acknowledgement = $"[{events}]";
        return true;
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
