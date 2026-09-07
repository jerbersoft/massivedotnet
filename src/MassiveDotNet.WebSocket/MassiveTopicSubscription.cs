using System.Threading.Channels;

namespace MassiveDotNet.WebSocket;

/// <summary>A live subscription to one topic: the events, and how many were dropped.</summary>
/// <typeparam name="T">The event type.</typeparam>
/// <remarks>
/// One sequence per topic, and one consumer per sequence. Enumerating twice throws rather than
/// letting two loops silently steal events from each other (D-W3).
/// </remarks>
public sealed class MassiveTopicSubscription<T> : IAsyncEnumerable<T>
{
    private readonly ChannelReader<T> _reader;
    private long _dropped;
    private int _enumerated;

    internal MassiveTopicSubscription(ChannelReader<T> reader) => _reader = reader;

    /// <summary>
    /// How many events were discarded because this topic's buffer was full when they arrived.
    /// </summary>
    /// <remarks>
    /// Monotonic, and exact rather than estimated. A non-zero value means the consumer is slower
    /// than the feed: raise <see cref="MassiveStreamOptions.TopicBufferCapacity"/>, or do less work
    /// in the loop. A consumer wiring the SDK through <c>AddMassiveStream</c> is warned on their
    /// logger automatically.
    /// </remarks>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    internal void RecordDrop() => Interlocked.Increment(ref _dropped);

    /// <summary>Enumerates this topic's events. May be called only once.</summary>
    /// <param name="cancellationToken">Ends the enumeration.</param>
    /// <returns>The enumerator.</returns>
    /// <exception cref="InvalidOperationException">The sequence is already being enumerated.</exception>
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _enumerated, 1) == 1)
        {
            throw new InvalidOperationException(
                $"This {nameof(MassiveTopicSubscription<T>)} is already being enumerated. A topic has "
                + "one sequence and one consumer; fan out in your own code if several need the events.");
        }

        return _reader.ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
    }
}
