using System.Diagnostics.CodeAnalysis;
using MassiveDotNet.WebSocket.Events;
using MassiveDotNet.WebSocket.Internal;
using NodaTime;

namespace MassiveDotNet.WebSocket;

/// <summary>An authenticated stock stream. Each topic is its own sequence.</summary>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "\"Stream\" is this SDK's own domain term for a live market data feed "
        + "(MassiveStreamClient, MassiveStreamOptions), not System.IO.Stream; the name is "
        + "deliberate and consistent, not an accidental collision with the BCL suffix.")]
public sealed class MassiveStockStream : IAsyncDisposable
{
    // H2 (Task 12 pre-flight): throttled to at most one raise per this window, measured on the
    // injected clock rather than the wall clock, so a sustained overflow does not produce a
    // DropObserved (and, through the DI bridge, a log line) per dropped event.
    private static readonly Duration DropThrottleWindow = Duration.FromSeconds(1);

    private readonly MassiveStreamConnection _connection;
    private readonly MassiveStreamOptions _options;
    private readonly IClock _clock;
    private readonly TickerPool _tickers;

    // F4 (Task 12 review round 1): called once, at the end of DisposeAsync, so the client that
    // opened this stream can drop its own reference. Without this, MassiveStreamClient._streams
    // only ever shrinks when the CLIENT itself disposes -- a consumer who opens and closes many
    // streams over the life of a long-running singleton (D28; this type's own remarks say
    // "long-lived and shared") pins a dead MassiveStreamConnection, a TickerPool, and every topic
    // buffer for each one, for the rest of the process.
    private readonly Action? _onDisposed;

    // H3(c) (Task 12 pre-flight): guards the check-create-register sequence in
    // GetOrCreateTradeSink/GetOrCreateQuoteSink below. Two concurrent first-time callers could
    // otherwise both observe the field as null, each mint their own TopicSink, and each call
    // AddSink -- the second call silently overwrites the first in the connection's sink table,
    // which can leave the FIELD (written by whichever caller's assignment happened last) and the
    // connection's actual dispatch table (written by whichever caller's AddSink call happened
    // last) naming two DIFFERENT sinks, since those two "lasts" need not be the same caller. A
    // caller who then reads the field gets a Subscription the connection was never wired to
    // deliver to -- silently orphaned, receiving nothing, ever. Held only across the synchronous
    // mint-and-register, never across the network round trip that follows, and the sink each call
    // returns is the LOCAL value this lock resolved, not a later re-read of the field: re-reading
    // after the round trip is exactly what let two callers observe different "lasts" in the first
    // place (see StockStreamTests.ConcurrentFirstSubscribesToTheSameTopicAllReturnTheSameSubscription
    // and the Task 12 report for how this was watched failing before this fix).
    //
    // F5 (Task 12 review round 1): also guards _disposed, so GetOrCreate*Sink refuses -- naming
    // THIS type, not the internal MassiveStreamConnection a consumer cannot act on -- rather than
    // minting a sink it will discard, or letting a caller reach a connection that has already torn
    // itself down. Set under this same lock in DisposeAsync, the tightened pairing
    // MassiveStreamClient._streamsLock already uses (see its own remarks): a registration that
    // wins the lock first completes normally, and one that loses it either never started or is
    // refused outright -- no interleaving mints a sink nobody will ever complete.
    private readonly object _sinkLock = new();
    private TopicSink<StockTrade>? _trades;
    private TopicSink<StockQuote>? _quotes;
    private bool _disposed;

    private readonly object _dropThrottleLock = new();
    private Instant? _lastDropObservedAt;

    internal MassiveStockStream(
        MassiveStreamConnection connection, MassiveStreamOptions options, IClock clock, Action? onDisposed = null)
    {
        _connection = connection;
        _options = options;
        _clock = clock;
        _tickers = new TickerPool(options.TickerPoolCapacity);
        _onDisposed = onDisposed;
    }

    /// <summary>How many times the underlying connection has been re-established.</summary>
    public int ReconnectCount => _connection.ReconnectCount;

    /// <summary>When the connection was last re-established.</summary>
    public Instant? LastReconnected => _connection.LastReconnected;

    /// <summary>Raised after a reconnect, carrying the running count.</summary>
    /// <remarks>A reconnect means messages were missed; the protocol offers no way to recover them.</remarks>
    public event Action<int>? Reconnected
    {
        add => _connection.Reconnected += value;
        remove => _connection.Reconnected -= value;
    }

    // H1 (ruling override -- "the most important item in the task"): the brief exposed
    // Reconnected/ReconnectCount/LastReconnected but not this, even though Task 11 built an entire
    // single-terminal-stop path (MassiveStreamConnection.StopPermanently) whose whole purpose is
    // that a consumer learns WHY a stream stopped for good. Without forwarding it, a consumer sees
    // `await foreach` simply end and cannot tell an authentication failure from their own
    // disposal -- the design that counts drops has no business hiding gaps, arriving at the exact
    // layer facing the caller. Forwarded exactly as Reconnected already is, add/remove straight
    // through to the connection.
    /// <summary>Raised once the stream has stopped for good and will not reconnect.</summary>
    /// <remarks>
    /// Three terminal stops reach this: reconnect disabled, an authentication failure surfacing
    /// during a reconnect attempt, and a parse failure that is never reconnectable. Each raises
    /// this, with the exception that ended the stream, before completing every subscription's
    /// sequence -- so a consumer whose <c>await foreach</c> ends can learn why, rather than merely
    /// observing that it did.
    /// </remarks>
    public event Action<Exception>? Faulted
    {
        add => _connection.Faulted += value;
        remove => _connection.Faulted -= value;
    }

    /// <summary>
    /// Raised when a topic buffer overflowed and dropped an event, naming the topic's wire code
    /// and that topic's own running drop count, throttled to at most once a second so a sustained
    /// overflow does not produce an unbounded stream of notifications.
    /// </summary>
    /// <remarks>
    /// F7 (Task 12 review round 1): the count travels IN the event rather than requiring a caller
    /// to separately hold every subscription and read its own
    /// <see cref="MassiveTopicSubscription{T}.DroppedCount"/> -- the original signature took no
    /// topic at all, which left no correct way for the DI package's <c>LogStreamHealth</c> to
    /// report on more than one topic (see that method's own remarks). Every handler is invoked
    /// with its own try/catch (F1): a notification that something was dropped must never itself
    /// take down the whole live feed -- exactly the defect Task 11 fixed for
    /// <c>MassiveStreamConnection.Faulted</c>, twenty lines from this one.
    /// </remarks>
    public event Action<string, long>? DropObserved;

    /// <summary>Subscribes to tick-level trades.</summary>
    /// <param name="tickers">Symbols, or <c>*</c> for every symbol.</param>
    /// <param name="cancellationToken">Cancels the subscribe.</param>
    /// <returns>
    /// This stream's trade sequence. Calling again widens the ticker set and returns the same
    /// sequence, so a topic has one buffer and one consumer however many times it is called.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    /// <exception cref="MassiveStreamSubscriptionException">
    /// The server acknowledged fewer subscriptions than were requested.
    /// </exception>
    public async Task<MassiveTopicSubscription<StockTrade>> SubscribeTradesAsync(
        IReadOnlyCollection<string> tickers,
        CancellationToken cancellationToken = default)
    {
        TopicSink<StockTrade> sink = GetOrCreateTradeSink();

        await _connection.SubscribeAsync(StockTopic.Trades.ToCode(), tickers, cancellationToken);

        return sink.Subscription;
    }

    private TopicSink<StockTrade> GetOrCreateTradeSink()
    {
        lock (_sinkLock)
        {
            // F5 (Task 12 review round 1): checked before minting anything, so a disposed stream
            // never does work it is about to throw away, and the exception names THIS type rather
            // than the internal connection a consumer cannot name or act on.
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_trades is null)
            {
                string topicCode = StockTopic.Trades.ToCode();
                TopicSink<StockTrade> sink = new(topicCode, _options.TopicBufferCapacity, new StockTradeConverter(_tickers));

                sink.ItemDropped += () => OnItemDropped(topicCode, sink.Subscription.DroppedCount);
                _connection.AddSink(sink);
                _trades = sink;
            }

            return _trades;
        }
    }

    /// <summary>Subscribes to NBBO quotes.</summary>
    /// <param name="tickers">Symbols, or <c>*</c> for every symbol.</param>
    /// <param name="cancellationToken">Cancels the subscribe.</param>
    /// <returns>This stream's quote sequence, on the same terms as trades.</returns>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    /// <exception cref="MassiveStreamSubscriptionException">
    /// The server acknowledged fewer subscriptions than were requested.
    /// </exception>
    public async Task<MassiveTopicSubscription<StockQuote>> SubscribeQuotesAsync(
        IReadOnlyCollection<string> tickers,
        CancellationToken cancellationToken = default)
    {
        TopicSink<StockQuote> sink = GetOrCreateQuoteSink();

        await _connection.SubscribeAsync(StockTopic.Quotes.ToCode(), tickers, cancellationToken);

        return sink.Subscription;
    }

    private TopicSink<StockQuote> GetOrCreateQuoteSink()
    {
        lock (_sinkLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_quotes is null)
            {
                string topicCode = StockTopic.Quotes.ToCode();
                TopicSink<StockQuote> sink = new(topicCode, _options.TopicBufferCapacity, new StockQuoteConverter(_tickers));

                sink.ItemDropped += () => OnItemDropped(topicCode, sink.Subscription.DroppedCount);
                _connection.AddSink(sink);
                _quotes = sink;
            }

            return _quotes;
        }
    }

    // The count carried is the subscription's own running total AT THE MOMENT of the drop that
    // triggered this call -- not necessarily the total once a whole burst has finished. The
    // throttle below is edge-triggered (raises on the first qualifying drop in a fresh window, then
    // silently discards every further call until the window elapses), and single-threaded (the
    // read loop is this stream's only writer), so there is no later point at which a throttled-out
    // call's own, more current count could still be reported: DropObservedIsThrottledToAtMostOncePerSecond
    // pins this by asserting the exact number, not just that a raise happened. This is unchanged
    // throttle behaviour, not new for F7 -- only the count now travels with the raise at all.
    private void OnItemDropped(string topicCode, long droppedCount)
    {
        Instant now = _clock.GetCurrentInstant();

        lock (_dropThrottleLock)
        {
            if (_lastDropObservedAt is { } last && now - last < DropThrottleWindow)
            {
                return;
            }

            _lastDropObservedAt = now;
        }

        RaiseSafely(DropObserved, topicCode, droppedCount);
    }

    // F1 (Task 12 review round 1, CRITICAL): a throwing DropObserved handler used to propagate
    // straight into the read loop -- OnItemDropped runs on that thread, called from
    // TopicSink.Write -> TryWrite -> the channel's itemDropped callback -> ItemDropped?.Invoke().
    // The exception landed in ReadLoopAsync's own outer catch, which treats it as a terminal fault:
    // StopPermanently ran, Faulted fired with the HANDLER's exception, and every sink completed --
    // so a notification that some events were merely dropped took down the entire live feed,
    // strictly worse than the drop it was reporting. This is exactly the defect Task 11 fixed for
    // MassiveStreamConnection.Faulted (see StopPermanently's own remarks, twenty lines from that
    // one): a multicast delegate stops calling subscribers the instant one throws, so each
    // subscriber needs its own try/catch, not one wrapped around the whole invocation. A single
    // small helper rather than inlining this at the raise site, so a third event added to this
    // class later reuses it instead of repeating the dance a third time. Swallowed rather than
    // logged -- core has no logger to hand it to; the ILogger bridge is the DI package's job.
    private static void RaiseSafely<T1, T2>(Action<T1, T2>? handlers, T1 argument1, T2 argument2)
    {
        foreach (Delegate handler in handlers?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action<T1, T2>)handler)(argument1, argument2);
            }
            catch
            {
                // See the remarks above: a consumer's handler throwing is not this stream's
                // problem, and must never end the stream for every OTHER consumer too.
            }
        }
    }

    /// <summary>Stops receiving a topic for the given symbols.</summary>
    /// <param name="topic">The topic.</param>
    /// <param name="tickers">The symbols to drop.</param>
    /// <param name="cancellationToken">Cancels the unsubscribe.</param>
    /// <returns>A task completing once the message has been sent.</returns>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    public Task UnsubscribeAsync(
        StockTopic topic,
        IReadOnlyCollection<string> tickers,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _connection.UnsubscribeAsync(topic.ToCode(), tickers, cancellationToken);
    }

    /// <summary>Closes the stream and ends every topic sequence.</summary>
    public async ValueTask DisposeAsync()
    {
        lock (_sinkLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _trades?.Complete();
        _quotes?.Complete();

        await _connection.DisposeAsync();

        // F4: last, so the client only ever unregisters a stream that has genuinely finished
        // tearing itself down.
        _onDisposed?.Invoke();
    }
}
