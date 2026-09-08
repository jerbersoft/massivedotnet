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
    private readonly object _sinkLock = new();
    private TopicSink<StockTrade>? _trades;
    private TopicSink<StockQuote>? _quotes;

    private readonly object _dropThrottleLock = new();
    private Instant? _lastDropObservedAt;

    internal MassiveStockStream(MassiveStreamConnection connection, MassiveStreamOptions options, IClock clock)
    {
        _connection = connection;
        _options = options;
        _clock = clock;
        _tickers = new TickerPool(options.TickerPoolCapacity);
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
    /// Raised when a topic buffer overflowed and dropped an event, throttled to at most once a
    /// second so a sustained overflow does not produce an unbounded stream of notifications.
    /// </summary>
    /// <remarks>
    /// The exact count is always available on the affected subscription's
    /// <see cref="MassiveTopicSubscription{T}.DroppedCount"/>; this event is a cue to go read it,
    /// not a count of its own. It is what the DI package's logging bridge watches.
    /// </remarks>
    public event Action? DropObserved;

    /// <summary>Subscribes to tick-level trades.</summary>
    /// <param name="tickers">Symbols, or <c>*</c> for every symbol.</param>
    /// <param name="cancellationToken">Cancels the subscribe.</param>
    /// <returns>
    /// This stream's trade sequence. Calling again widens the ticker set and returns the same
    /// sequence, so a topic has one buffer and one consumer however many times it is called.
    /// </returns>
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
            if (_trades is null)
            {
                TopicSink<StockTrade> sink = new(
                    StockTopic.Trades.ToCode(),
                    _options.TopicBufferCapacity,
                    new StockTradeConverter(_tickers));

                sink.ItemDropped += OnItemDropped;
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
            if (_quotes is null)
            {
                TopicSink<StockQuote> sink = new(
                    StockTopic.Quotes.ToCode(),
                    _options.TopicBufferCapacity,
                    new StockQuoteConverter(_tickers));

                sink.ItemDropped += OnItemDropped;
                _connection.AddSink(sink);
                _quotes = sink;
            }

            return _quotes;
        }
    }

    private void OnItemDropped()
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

        DropObserved?.Invoke();
    }

    /// <summary>Stops receiving a topic for the given symbols.</summary>
    /// <param name="topic">The topic.</param>
    /// <param name="tickers">The symbols to drop.</param>
    /// <param name="cancellationToken">Cancels the unsubscribe.</param>
    /// <returns>A task completing once the message has been sent.</returns>
    public Task UnsubscribeAsync(
        StockTopic topic,
        IReadOnlyCollection<string> tickers,
        CancellationToken cancellationToken = default) =>
        _connection.UnsubscribeAsync(topic.ToCode(), tickers, cancellationToken);

    /// <summary>Closes the stream and ends every topic sequence.</summary>
    public async ValueTask DisposeAsync()
    {
        _trades?.Complete();
        _quotes?.Complete();

        await _connection.DisposeAsync();
    }
}
