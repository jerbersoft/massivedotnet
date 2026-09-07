using System.Net.WebSockets;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>
/// The subset of <see cref="ClientWebSocket"/> this SDK uses, behind an interface so the transport
/// can be driven without a socket.
/// </summary>
/// <remarks>
/// <see cref="ClientWebSocket"/> is sealed, so a test double is impossible without this seam. It is
/// the same shape the REST tests get from stubbing <c>HttpMessageHandler</c>, and it is what keeps
/// reconnect, backpressure, and frame reassembly testable in the offline tier (rule 13).
/// </remarks>
internal interface IMassiveWebSocket : IAsyncDisposable
{
    /// <summary>The socket's current state.</summary>
    WebSocketState State { get; }

    /// <summary>Opens the connection.</summary>
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    /// <summary>Sends one complete text message.</summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>Receives the next frame, which may be part of a larger message.</summary>
    ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>Closes the connection politely, if it is still open.</summary>
    Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken);
}

/// <summary>
/// Creates a socket. Reconnect needs a fresh one per attempt, because a <see cref="ClientWebSocket"/>
/// cannot be reopened once it has been aborted.
/// </summary>
internal delegate IMassiveWebSocket MassiveWebSocketFactory();
