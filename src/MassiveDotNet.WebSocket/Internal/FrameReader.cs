using System.Net.WebSockets;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>Reassembles one logical message from the frames a socket delivers.</summary>
/// <remarks>
/// A WebSocket message arrives in one or more frames, and nothing in the protocol bounds how many.
/// <paramref name="maxMessageBytes"/> is therefore a correctness property rather than a tuning
/// knob: it is what stops a server -- buggy or hostile -- from making the client allocate without
/// limit.
/// </remarks>
internal sealed class FrameReader(IMassiveWebSocket socket, int maxMessageBytes)
{
    /// <summary>Reads one complete message into <paramref name="destination"/>.</summary>
    /// <returns>The message length in bytes.</returns>
    /// <exception cref="MassiveStreamException">
    /// The message exceeds the configured ceiling, or the stream closed while it was being read.
    /// </exception>
    public async ValueTask<int> ReadMessageAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        // The ceiling is enforced against maxMessageBytes itself, not against destination.Length:
        // a caller handing a larger buffer must not silently raise the limit. Clamping the receive
        // window to that same bound -- rather than only checking `written` after the fact -- also
        // stops one oversized frame from overshooting the ceiling in a single call before the loop
        // gets a chance to notice.
        int limit = Math.Min(destination.Length, maxMessageBytes);
        int written = 0;
        ValueWebSocketReceiveResult result;

        do
        {
            if (written == limit)
            {
                // Which bound was hit is the caller's first question, and answering it wrong sends
                // them after the wrong bug: a short destination is this SDK's mistake, an oversized
                // message is the server's. No caller can pass a short one today -- FrameReader is
                // internal and both call sites size at or above the ceiling -- so this arm exists
                // to stay honest if that ever stops being true.
                throw new MassiveStreamException(
                    limit == maxMessageBytes
                        ? $"The stream sent a message larger than the {maxMessageBytes} byte ceiling "
                          + $"({nameof(MassiveStreamOptions.MaxMessageBytes)})."
                        : $"The stream sent a message larger than the {limit} byte destination it "
                          + $"was given, below the {maxMessageBytes} byte ceiling "
                          + $"({nameof(MassiveStreamOptions.MaxMessageBytes)}).");
            }

            result = await socket.ReceiveAsync(destination[written..limit], cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new MassiveStreamException("The stream closed while a message was being read.");
            }

            written += result.Count;
        }
        while (!result.EndOfMessage);

        return written;
    }
}
