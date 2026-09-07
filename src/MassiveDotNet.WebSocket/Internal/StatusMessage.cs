using System.Text.Json;
using MassiveDotNet.Serialization;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>One <c>ev: status</c> control message.</summary>
/// <remarks>
/// Everything inbound is a JSON array, control messages included, and one frame can carry several
/// of them — a subscribe to two topics is acknowledged in a single frame.
/// </remarks>
internal readonly record struct StatusMessage(string Status, string Message)
{
    public const string Connected = "connected";
    public const string AuthSuccess = "auth_success";
    public const string AuthFailed = "auth_failed";
    public const string Success = "success";

    private const string ModelName = nameof(StatusMessage);

    /// <summary>
    /// Reads every status message in <paramref name="payload"/> into <paramref name="destination"/>.
    /// </summary>
    /// <returns>How many were written. Non-status events are skipped.</returns>
    /// <exception cref="MassiveStreamException">
    /// The payload is not a JSON array, or carries more status events than
    /// <paramref name="destination"/> can hold. The latter is refused rather than truncated: a
    /// silently dropped event is exactly the data loss this SDK refuses elsewhere.
    /// </exception>
    public static int Parse(ReadOnlySpan<byte> payload, Span<StatusMessage> destination)
    {
        Utf8JsonReader reader = new(payload);
        int written = 0;

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            throw new MassiveStreamException("The stream sent a message that is not a JSON array.");
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            string? kind = null;
            string? status = null;
            string? message = null;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("ev"u8))
                {
                    reader.Read();
                    kind = JsonValueReader.ReadString(ref reader, ModelName, "ev");
                }
                else if (reader.ValueTextEquals("status"u8))
                {
                    reader.Read();
                    status = JsonValueReader.ReadString(ref reader, ModelName, "status");
                }
                else if (reader.ValueTextEquals("message"u8))
                {
                    reader.Read();
                    message = JsonValueReader.ReadString(ref reader, ModelName, "message");
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            if (kind == "status" && status is not null)
            {
                if (written == destination.Length)
                {
                    // A caller sizes the destination for what it expects; a frame carrying more
                    // status events than that is data the caller asked for and would not get back,
                    // the same silent loss D29 refuses for a runaway cursor. Refusing here is
                    // cheap: the handshake destination is always sized for one, and Task 7's read
                    // loop sizes its own destination for the widest frame it accepts.
                    throw new MassiveStreamException(
                        "The stream sent more status events in one frame than the destination could hold.");
                }

                destination[written++] = new StatusMessage(status, message ?? string.Empty);
            }
        }

        return written;
    }
}
