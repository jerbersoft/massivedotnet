using System.Net.WebSockets;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>The real <see cref="IMassiveWebSocket"/>, over <see cref="ClientWebSocket"/>.</summary>
internal sealed class ClientWebSocketAdapter : IMassiveWebSocket
{
    private readonly ClientWebSocket _socket = new();

    public ClientWebSocketAdapter(MassiveStreamOptions options)
    {
        // Boundary crossing (produce): converted inline from a Duration, so the BCL type is never
        // named. See CLAUDE.md, "Temporal types" -- this is the row anticipated for #20.
        _socket.Options.KeepAliveInterval = options.KeepAliveInterval.ToTimeSpan();

        if (!string.IsNullOrWhiteSpace(options.UserAgent))
        {
            _socket.Options.SetRequestHeader("User-Agent", options.UserAgent);
        }
    }

    public WebSocketState State => _socket.State;

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
        _socket.ConnectAsync(uri, cancellationToken);

    public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) =>
        _socket.SendAsync(buffer, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    public ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        _socket.ReceiveAsync(buffer, cancellationToken);

    public Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
        _socket.State is WebSocketState.Open or WebSocketState.CloseReceived
            ? _socket.CloseAsync(closeStatus, statusDescription, cancellationToken)
            : Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
