using MassiveDotNet.WebSocket.Internal;
using NodaTime;

namespace MassiveDotNet.WebSocket;

/// <summary>Opens streaming connections to the Massive platform.</summary>
/// <remarks>
/// Long-lived and shared. One instance can open several market streams; each owns its own socket,
/// because the market is a path segment on the feed host rather than a subscription parameter.
/// </remarks>
public sealed class MassiveStreamClient : IAsyncDisposable
{
    private readonly MassiveStreamOptions _options;
    private readonly IClock _clock;

    // H3(a)/(b) (Task 12 pre-flight): guards _streams and _disposed together, the pairing
    // MassiveStreamConnection.AddSink/DisposeAsync already use for exactly the same reason (rule 8
    // registers this client as a DI singleton, D28, and its own doc above says "long-lived and
    // shared" -- more than one caller legitimately holds it and calls in concurrently). List<T>.Add
    // is not thread-safe on its own (H3(a)): concurrent ConnectStocksAsync calls could lose a
    // registration outright, orphaning the connection it just opened -- observed directly at 32
    // threads, 1-2 of 32 sockets left open after DisposeAsync every run (see the Task 12 report).
    // DisposeAsync had no disposal guard at all (H3(b)): a stream opened after disposal was added
    // to a list disposal had already iterated and cleared, so nobody would ever close it -- the
    // third occurrence of this exact bug on this branch (AddSink in Task 10, the registry in
    // Task 11). The lock closes both: a registration that wins it first is in whatever snapshot
    // DisposeAsync takes and gets closed there, and one that loses it either sees _disposed
    // (fast path, before a socket is even opened) or is refused here and closes what it already
    // opened itself -- neither order leaves a connection nobody will ever dispose.
    private readonly object _streamsLock = new();
    private readonly List<IAsyncDisposable> _streams = [];
    private bool _disposed;

    /// <summary>Creates a client.</summary>
    /// <param name="options">The stream configuration. Validated immediately.</param>
    public MassiveStreamClient(MassiveStreamOptions options) : this(options, SystemClock.Instance)
    {
    }

    internal MassiveStreamClient(MassiveStreamOptions options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        _clock = clock;
    }

    /// <summary>Opens an authenticated stock stream.</summary>
    /// <param name="cancellationToken">Cancels the handshake.</param>
    /// <returns>The connected stream.</returns>
    /// <exception cref="MassiveStreamAuthenticationException">
    /// The key was refused, or the plan does not include WebSocket access for this market. The
    /// server's own message is on <see cref="MassiveStreamAuthenticationException.ServerMessage"/>.
    /// </exception>
    public Task<MassiveStockStream> ConnectStocksAsync(CancellationToken cancellationToken = default) =>
        ConnectStocksAsync(() => new ClientWebSocketAdapter(_options), cancellationToken);

    // Takes the factory rather than a socket: a ClientWebSocket cannot be reconnected once aborted,
    // so reconnect calls this back for a fresh one on every attempt. Handing it a single instance
    // would pass every offline test -- the fakes are minted per attempt -- and never reconnect live.
    internal async Task<MassiveStockStream> ConnectStocksAsync(MassiveWebSocketFactory factory, CancellationToken cancellationToken)
    {
        // Fails fast for the ordinary case -- no point opening a socket a disposed client will
        // refuse to track -- but this alone is not sufficient: DisposeAsync could still run between
        // this check and the registration below (a real caller can race a Dispose against a
        // Connect), which the second check under the lock closes.
        ObjectDisposedException.ThrowIf(_disposed, this);

        MassiveStreamConnection connection = new(_options, MassiveMarket.Stocks, factory, _clock);

        await connection.ConnectAsync(cancellationToken);
        connection.StartReading();

        // F4 (Task 12 review round 1): the stream calls Unregister on itself, once, at the end of
        // its own DisposeAsync -- not just when THIS client disposes it -- so a consumer who opens
        // and closes many streams over the life of a long-running singleton (D28) does not pin a
        // dead connection, TickerPool, and every topic buffer for each one, for the rest of the
        // process. `stream` is read inside the lambda only once DisposeAsync eventually invokes
        // it, long after this local has been assigned -- the constructor never calls it itself.
        MassiveStockStream? stream = null;
        stream = new MassiveStockStream(connection, _options, _clock, () => Unregister(stream!));

        lock (_streamsLock)
        {
            if (!_disposed)
            {
                _streams.Add(stream);
                return stream;
            }
        }

        // Lost the race: the client was disposed while this connection was being opened. Nobody
        // else will ever dispose it -- it is not (and will never be) in _streams -- so this closes
        // it itself rather than leaking a live socket and read loop.
        await stream.DisposeAsync();
        throw new ObjectDisposedException(nameof(MassiveStreamClient));
    }

    // F4: Remove on a list that no longer holds this stream -- because DisposeAsync already
    // cleared it, or because this exact stream was never added (the disposed-race path above) --
    // is a harmless no-op, the same "double disposal is clean" property every IAsyncDisposable in
    // this SDK already has.
    private void Unregister(MassiveStockStream stream)
    {
        lock (_streamsLock)
        {
            _streams.Remove(stream);
        }
    }

    /// <summary>How many streams this client currently tracks. Test-only.</summary>
    internal int StreamCount
    {
        get
        {
            lock (_streamsLock)
            {
                return _streams.Count;
            }
        }
    }

    /// <summary>
    /// Performs the handshake for any market and closes immediately, so a test can observe what
    /// the server says about a market this package ships no facade for.
    /// </summary>
    /// <remarks>
    /// Internal because #20 ships only the stocks facade; #21 adds the rest. Entitlement is a
    /// property of the handshake rather than of any facade, so pinning it needs no facade at all.
    /// </remarks>
    internal async Task ConnectRawAsync(MassiveMarket market, CancellationToken cancellationToken)
    {
        // H5 (Task 12 pre-flight): the brief paired an outer `await using ClientWebSocketAdapter`
        // with `connection.DisposeAsync()`, disposing the adapter twice -- ClientWebSocketAdapter
        // wraps ClientWebSocket, whose own Dispose() is idempotent (confirmed by
        // ClientWebSocketAdapterTests.DisposeAsyncIsIdempotent), so the double dispose was harmless
        // but redundant. The factory mints one instance, ConnectAsync assigns it to the
        // connection's own _socket, and the connection's DisposeAsync below is what disposes it --
        // nothing here needs to hold a second reference at all.
        await using MassiveStreamConnection connection = new(
            _options, market, () => new ClientWebSocketAdapter(_options), _clock);

        await connection.ConnectAsync(cancellationToken);
    }

    /// <summary>Closes every stream this client opened.</summary>
    public async ValueTask DisposeAsync()
    {
        IAsyncDisposable[] pending;

        lock (_streamsLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pending = [.. _streams];
            _streams.Clear();
        }

        foreach (IAsyncDisposable stream in pending)
        {
            await stream.DisposeAsync();
        }
    }
}
