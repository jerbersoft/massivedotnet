using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>One topic's bounded buffer and the converter that fills it.</summary>
internal sealed class TopicSink<T> : ITopicSink
{
    // JsonSerializerOptions.Default is annotated RequiresUnreferencedCode/RequiresDynamicCode --
    // reading it walks a reflection-based resolution path, which fails the AOT smoke publish (rule
    // 3). An empty, unconfigured instance carries neither annotation, and is enough here: no
    // streaming converter's Read ever consults its options parameter, so no caller-supplied
    // configuration could reach them regardless of what this holds.
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
            itemDropped: _ =>
            {
                subscription!.RecordDrop();

                // A second, independent notification alongside RecordDrop above, not instead of
                // it: RecordDrop is what makes Subscription.DroppedCount exact, and this is a
                // separate seam an owner (MassiveStockStream, Task 12) can subscribe to without
                // polling that count. Left wired exactly where RecordDrop already runs so a drop
                // can never update one and not the other.
                ItemDropped?.Invoke();
            });

        subscription = new MassiveTopicSubscription<T>(_channel.Reader);
        Subscription = subscription;
    }

    public string TopicCode { get; }

    /// <summary>The caller-facing sequence.</summary>
    public MassiveTopicSubscription<T> Subscription { get; }

    /// <summary>Raised every time a write drops the oldest buffered event.</summary>
    public event Action? ItemDropped;

    /// <summary>Raised when the converter refused an event and it was dropped.</summary>
    internal event Action<JsonException>? EventMalformed;

    public void Write(ref Utf8JsonReader reader)
    {
        T value;

        try
        {
            // The null-forgiving operator matches JsonConverter{T}.Read's own signature, T? Read(...):
            // annotated that way because T is unconstrained and a converter for a reference-typed model
            // COULD return null, but no streaming converter this SDK hands to a TopicSink ever does --
            // each one either returns a genuine value or throws.
            value = _converter.Read(ref reader, typeof(T), EmptyOptions)!;
        }
        catch (JsonException malformed)
        {
            // D-W19, issue #65. The reader handed in here is Dispatch's own `replay` -- a struct
            // COPY of the frame reader, and the real one is already parked on this object's end
            // token -- so whatever position the converter left THIS reader in cannot affect the
            // caller, and the dispatch loop takes the next event with no resync. That property is
            // the whole reason a per-event drop is safe, and it is why the catch is here rather
            // than one level out: widening it to cover ReadEventCode would swallow a failure that
            // strands the REAL reader mid-object, with no topic to attribute the loss to. This
            // drops a bad FIELD. A bad FRAME stays terminal.
            Subscription.RecordMalformed();

            // Two notifications from one place, exactly as the drop callback above does it:
            // RecordMalformed is what makes Subscription.MalformedCount exact, and this is the
            // separate seam MassiveStockStream subscribes to. The exception travels with it
            // because it is now the ONLY route the refused value has out of the SDK -- this used
            // to be terminal, so the message reached a consumer through Faulted.
            EventMalformed?.Invoke(malformed);

            return;
        }

        // TryWrite, never WriteAsync. Every topic shares one read loop: a writer that waits stalls
        // the socket, closes the receive window, and gets the connection dropped for being a slow
        // consumer -- taking down the topics that were keeping up (D-W4). With DropOldest this
        // always succeeds, and the eviction is counted rather than hidden.
        _channel.Writer.TryWrite(value);
    }

    public void Complete() => _channel.Writer.TryComplete();
}
