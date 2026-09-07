using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>One topic's bounded buffer and the converter that fills it.</summary>
internal sealed class TopicSink<T> : ITopicSink
{
    // JsonSerializerOptions.Default is annotated RequiresUnreferencedCode/RequiresDynamicCode --
    // reading it walks a reflection-based resolution path, which fails the AOT smoke publish (rule
    // 3). An empty, unconfigured instance carries neither annotation, and is enough here: neither
    // StockTradeConverter nor StockQuoteConverter's Read ever consults its options parameter, so no
    // caller-supplied configuration could reach them regardless of what this holds.
    private static readonly JsonSerializerOptions EmptyOptions = new();

    private readonly Channel<T> _channel;
    private readonly JsonConverter<T> _converter;

    public TopicSink(string topicCode, int capacity, JsonConverter<T> converter)
    {
        TopicCode = topicCode;
        _converter = converter;

        // The callback below closes over this local, not the Subscription property: at the point
        // the channel is constructed, Subscription has not been assigned yet (it needs the
        // channel's Reader, so it cannot come first), and the property's non-nullable return type
        // makes referencing it directly here a possibly-null dereference by the time the nullable
        // analyzer is done. The callback itself never runs until a write drops something, which
        // cannot happen before the constructor -- and therefore this assignment -- has completed.
        MassiveTopicSubscription<T>? subscription = null;

        // DropOldest with an itemDropped callback: the runtime reports the eviction, so the count
        // is exact. Inferring it from Reader.Count before a write would race with the consumer and
        // be wrong in both directions.
        _channel = Channel.CreateBounded<T>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            },
            itemDropped: _ => subscription!.RecordDrop());

        subscription = new MassiveTopicSubscription<T>(_channel.Reader);
        Subscription = subscription;
    }

    public string TopicCode { get; }

    /// <summary>The caller-facing sequence.</summary>
    public MassiveTopicSubscription<T> Subscription { get; }

    public void Write(ref Utf8JsonReader reader)
    {
        // The null-forgiving operator matches JsonConverter{T}.Read's own signature, T? Read(...):
        // annotated that way because T is unconstrained and a converter for a reference-typed model
        // COULD return null, but neither converter this SDK hands to a TopicSink ever does --
        // StockTradeConverter and StockQuoteConverter either return a genuine value or throw.
        T value = _converter.Read(ref reader, typeof(T), EmptyOptions)!;

        // TryWrite, never WriteAsync. Every topic shares one read loop: a writer that waits stalls
        // the socket, closes the receive window, and gets the connection dropped for being a slow
        // consumer -- taking down the topics that were keeping up (D-W4). With DropOldest this
        // always succeeds, and the eviction is counted rather than hidden.
        _channel.Writer.TryWrite(value);
    }

    public void Complete() => _channel.Writer.TryComplete();
}
