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
    /// Raised when a reconnect re-sent this stream's subscriptions and the server did not
    /// acknowledge all of them, carrying the pairs that went unacknowledged.
    /// </summary>
    /// <remarks>
    /// Degraded, not terminal, and distinct from <see cref="Faulted"/> for that reason: the socket
    /// is up and every acknowledged topic is still delivering, so no sequence ends. It exists
    /// because the server answers an unrecognised topic with silence rather than an error (D33), so
    /// a replay nobody checks can leave a topic unsubscribed on a connection
    /// <see cref="Reconnected"/> has already reported as healthy -- a stream that looks alive and
    /// delivers nothing, indistinguishable from a quiet market.
    /// <para>
    /// The pairs stay in the subscription registry, so the next reconnect replays them again. A
    /// consumer that wants them back sooner can re-subscribe, which is acknowledgement-counted the
    /// ordinary way and throws if the server ignores it again.
    /// </para>
    /// </remarks>
    public event Action<MassiveStreamSubscriptionException>? SubscriptionsLost
    {
        add => _connection.SubscriptionsLost += value;
        remove => _connection.SubscriptionsLost -= value;
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

        EventRaiser.Raise(DropObserved, topicCode, droppedCount);
    }

    // L1 (Task 14 ruling): an internal escape hatch so a live test can send a topic StockTopic
    // deliberately cannot express (D-W1) -- the enum exists precisely because the server silently
    // ignores a topic code it does not recognise, and the highest-value test in the streaming tier
    // proves that observation still holds. Internal so no consumer can reach it; #21's facades for
    // the other five markets get their own typed topics rather than reusing this.
    /// <summary>Subscribes by the raw wire topic code rather than the <see cref="StockTopic"/> enum.</summary>
    /// <param name="topicCode">The wire code, such as <c>T</c>, or one the enum cannot express.</param>
    /// <param name="tickers">Symbols, or <c>*</c> for every symbol.</param>
    /// <param name="cancellationToken">Cancels the subscribe.</param>
    /// <returns>A task completing once the server has acknowledged every pair.</returns>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    /// <exception cref="MassiveStreamSubscriptionException">
    /// The server acknowledged fewer subscriptions than were requested -- including every pair, when
    /// it does not recognise <paramref name="topicCode"/> at all.
    /// </exception>
    internal Task SubscribeRawAsync(
        string topicCode, IReadOnlyCollection<string> tickers, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _connection.SubscribeAsync(topicCode, tickers, cancellationToken);
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
