using NodaTime;

namespace MassiveDotNet.WebSocket;

/// <summary>Configuration for a Massive streaming client.</summary>
public sealed class MassiveStreamOptions
{
    private string? _apiKey;

    /// <summary>The API key used to authenticate the stream. Required.</summary>
    /// <remarks>
    /// Unlike REST, this key travels in a message rather than a header (D-W9). It is never
    /// rendered into an exception message, traced, or written to disk (rule 11).
    /// </remarks>
    public string? ApiKey
    {
        get => _apiKey;
        set => _apiKey = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>The feed host to connect to. Defaults to <see cref="MassiveFeeds.RealTime"/>.</summary>
    public Uri Feed { get; set; } = MassiveFeeds.RealTime;

    /// <summary>
    /// How many events each subscribed topic buffers before the oldest is dropped. Defaults to 1024.
    /// </summary>
    /// <remarks>
    /// This is the SDK's whole memory budget for a stream, and it is arithmetic a caller can do in
    /// advance: capacity multiplied by the event size, multiplied by the number of topics
    /// subscribed. Nothing here grows with time or with messages received.
    /// </remarks>
    public int TopicBufferCapacity { get; set; } = 1024;

    /// <summary>How many distinct ticker symbols are interned. Defaults to 16384.</summary>
    public int TickerPoolCapacity { get; set; } = 16384;

    /// <summary>The largest message accepted, in bytes. Defaults to 4 MiB.</summary>
    /// <remarks>
    /// A ceiling on frame reassembly. Without it a server could make the client allocate without
    /// limit, which is the one unbounded growth path the protocol leaves open.
    /// </remarks>
    public int MaxMessageBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>How long connect, authentication, and subscription acknowledgement may take. Defaults to 10 seconds.</summary>
    public Duration HandshakeTimeout { get; set; } = Duration.FromSeconds(10);

    /// <summary>The WebSocket keep-alive interval. Defaults to 20 seconds.</summary>
    public Duration KeepAliveInterval { get; set; } = Duration.FromSeconds(20);

    /// <summary>An optional product token appended to the <c>User-Agent</c> header.</summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Reconnect behaviour, or <see langword="null"/> to surface a dropped connection instead of
    /// re-establishing it. On by default.
    /// </summary>
    /// <remarks>
    /// This inverts D30's posture, where rate limiting and retry are off until a caller opts in.
    /// The reason they differ: a caller's request rate is theirs to choose and the SDK cannot infer
    /// a tier, whereas a stream that ends on the first dropped connection is not a streaming client
    /// at all. Authentication failure is never retried regardless of this setting (D-W6).
    /// </remarks>
    public MassiveStreamReconnectOptions? Reconnect { get; set; } = new();

    /// <summary>Throws if the options are not in a usable state.</summary>
    /// <exception cref="InvalidOperationException">
    /// The API key is missing, the feed is not an absolute URI, or a capacity or duration cannot be
    /// acted on.
    /// </exception>
    public void Validate()
    {
        // No message below names the key's value. Rule 11 is easy to honour in a getter and easy
        // to lose in an error path, which is the path that gets logged.
        if (_apiKey is null)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamOptions)}.{nameof(ApiKey)} must be set to a non-empty value.");
        }

        if (!Feed.IsAbsoluteUri)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamOptions)}.{nameof(Feed)} must be an absolute URI.");
        }

        if (TopicBufferCapacity <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamOptions)}.{nameof(TopicBufferCapacity)} must be positive.");
        }

        if (TickerPoolCapacity <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamOptions)}.{nameof(TickerPoolCapacity)} must be positive.");
        }

        if (MaxMessageBytes <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamOptions)}.{nameof(MaxMessageBytes)} must be positive.");
        }

        if (HandshakeTimeout <= Duration.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamOptions)}.{nameof(HandshakeTimeout)} must be positive.");
        }

        Reconnect?.Validate();
    }
}
