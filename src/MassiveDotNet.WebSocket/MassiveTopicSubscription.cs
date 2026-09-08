using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace MassiveDotNet.WebSocket;

/// <summary>A live subscription to one topic: the events, and how many were dropped.</summary>
/// <typeparam name="T">The event type.</typeparam>
/// <remarks>
/// One sequence per topic, and one consumer AT A TIME. Enumerating while another loop is already
/// running throws rather than letting two loops silently steal events from each other (D-W3) --
/// but the claim is about concurrency, not about the sequence's lifetime, so an enumeration that
/// ENDS releases the sequence for the next one.
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
    /// in the loop.
    /// </remarks>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    internal void RecordDrop() => Interlocked.Increment(ref _dropped);

    /// <summary>Enumerates this topic's events. One consumer at a time.</summary>
    /// <param name="cancellationToken">Ends the enumeration.</param>
    /// <returns>The enumerator.</returns>
    /// <exception cref="InvalidOperationException">
    /// Another loop is enumerating this sequence right now.
    /// </exception>
    /// <remarks>
    /// The guard is released when an enumeration ends -- by cancellation, by <c>break</c>, by an
    /// exception, or by the sequence completing -- so the topic can be consumed again afterwards.
    /// It used to be a one-way latch, which made a cancelled <c>await foreach</c> permanently burn
    /// the topic's only sequence: re-subscribing hands back this same object (D-W3 gives a topic
    /// one buffer however many times it is subscribed), so the next consumer got an
    /// <see cref="InvalidOperationException"/> saying the sequence was "already being enumerated"
    /// when nobody was, with no recovery short of tearing down the whole stream. Cancelling a
    /// consumer loop is ordinary -- a timeout, a shutdown, a caller taking a break -- and D-W3's
    /// argument is about two loops stealing from each other concurrently, which this still refuses.
    /// </remarks>
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _enumerated, 1) == 1)
        {
            throw new InvalidOperationException(
                $"This {nameof(MassiveTopicSubscription<T>)} is already being enumerated. A topic has "
                + "one sequence and one consumer at a time; fan out in your own code if several need "
                + "the events.");
        }

        return EnumerateAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
    }

    // The release lives in a finally rather than beside the reader's own completion, because every
    // way an enumeration can end has to release it: `await foreach` disposes the enumerator on
    // break, on an exception, and on cancellation just as it does on a clean finish.
    private async IAsyncEnumerable<T> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (T item in _reader.ReadAllAsync(cancellationToken))
            {
                yield return item;
            }
        }
        finally
        {
            Volatile.Write(ref _enumerated, 0);
        }
    }
}
