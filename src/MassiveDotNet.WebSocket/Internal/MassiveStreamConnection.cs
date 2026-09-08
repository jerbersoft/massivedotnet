using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MassiveDotNet.Serialization;
using NodaTime;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>Owns one socket, its handshake, its read loop, and its reconnect state.</summary>
internal sealed partial class MassiveStreamConnection : IAsyncDisposable
{
    private readonly MassiveStreamOptions _options;
    private readonly MassiveWebSocketFactory _factory;
    private readonly IClock _clock;

    private IMassiveWebSocket? _socket;
    private FrameReader? _reader;
    private readonly CancellationTokenSource _shutdown = new();
    private bool _disposed;

    // Reset to zero after every message this connection successfully reads -- NOT after a
    // reconnect by itself (Task 11 review round 1, finding 5: a prior comment here claimed the
    // latter too, but the code never did it). Resetting on connect+auth alone would let a server
    // that accepts, authenticates, and immediately drops again be hammered at InitialBackoff
    // forever; resetting only once a message is actually processed is the honest signal that the
    // connection is working, not merely open. BackoffFor uses this to size the next attempt's delay.
    private int _reconnectAttempt;

    // One subscribe (or unsubscribe) in flight at a time: acknowledgements carry no correlation
    // id, so two overlapping requests could not tell whose acknowledgement arrived. Both
    // SubscribeAsync and UnsubscribeAsync take this, even though only the former waits on it.
    private readonly SemaphoreSlim _subscribeGate = new(1, 1);

    // Guards _pendingAcks and _pendingAcknowledgements together. OnStatus reads both to decide
    // whether a match should signal completion, and SubscribeAsync writes both to publish a new
    // request; a window where one is updated and not the other is a lost wakeup (a prior version
    // of this file had exactly that gap between an Interlocked.Add and the next statement -- fixed
    // here by making "publish" and "match" each a single critical section under one plain lock,
    // not a pair of individually-atomic fields that must additionally stay in step with each
    // other). Contention is trivial -- a handful of control frames -- so a lock is the right
    // instrument, not further Interlocked bookkeeping.
    private readonly object _ackLock = new();

    // The exact acknowledgement message text still owed for the in-flight subscribe, keyed by that
    // text and valued by the ticker it names -- e.g. "subscribed to: T.AAPL" -> "AAPL". Matching by
    // content rather than counting is load-bearing: the server acknowledges an unsubscribe with the
    // same "success" status a subscribe uses, just a different verb in the message ("unsubscribed
    // to: T.AAPL", confirmed on the wire 2026-09-07), so a bare count cannot tell the two apart --
    // and a prior version of this file that counted regardless of content let an unsubscribe's own
    // acknowledgement silently satisfy whatever subscribe happened to ask next. Because the key is
    // the full message text, only a message this connection is actually waiting on can ever match;
    // an unsubscribe's acknowledgement, a stale one from an already-finished request, or a bare
    // "connected"/"auth_success" never collide with it and are simply ignored.
    private Dictionary<string, string>? _pendingAcks;
    private TaskCompletionSource? _pendingAcknowledgements;

    public MassiveStreamConnection(
        MassiveStreamOptions options,
        MassiveMarket market,
        MassiveWebSocketFactory factory,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(clock);
        options.Validate();

        _options = options;
        _factory = factory;
        _clock = clock;
        Endpoint = new Uri(options.Feed, market.ToPathSegment());
    }

    /// <summary>The URI this connection opens: the feed host with the market as its path.</summary>
    public Uri Endpoint { get; }

    /// <summary>Receives the payload of every message the socket delivers, in order.</summary>
    /// <remarks>
    /// Attached by the façade. It must never block: every topic shares this one loop, so a handler
    /// that waits stalls the socket, closes the receive window, and gets the connection dropped for
    /// being a slow consumer -- taking down the topics that were keeping up (D-W4).
    /// </remarks>
    public Func<ReadOnlyMemory<byte>, ValueTask>? OnMessage { get; set; }

    /// <summary>The loop's task, so shutdown -- and Task 11's reconnect -- can observe how it ended.</summary>
    public Task ReadLoopTask { get; private set; } = Task.CompletedTask;

    /// <summary>Every live subscription, for replay after a reconnect.</summary>
    public SubscriptionRegistry Registry { get; } = new();

    /// <summary>Opens the socket and authenticates, returning only once the server accepts.</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Boundary crossing (produce): the domain Duration converts here and nowhere above.
        timeout.CancelAfter(_options.HandshakeTimeout.ToTimeSpan());

        _socket = _factory();
        await _socket.ConnectAsync(Endpoint, timeout.Token);

        await ExpectStatusAsync(StatusMessage.Connected, timeout.Token);
        await SendAuthenticationAsync(timeout.Token);
        await ExpectAuthenticationAsync(timeout.Token);
    }

    /// <summary>Begins the background read loop. Called once authentication has succeeded.</summary>
    /// <remarks>
    /// The loop owns one buffer for the connection's whole life -- allocated here, not inside the
    /// loop, so a long-lived stream costs one allocation total rather than one per message.
    /// </remarks>
    public void StartReading()
    {
        _reader = new FrameReader(_socket!, _options.MaxMessageBytes);
        ReadLoopTask = Task.Run(() => ReadLoopAsync(_shutdown.Token), _shutdown.Token);
    }

    /// <summary>How many times this connection has been re-established.</summary>
    /// <remarks>
    /// A reconnect means messages were missed while the socket was down. The protocol offers no
    /// cursor, so the gap cannot be repaired -- only reported, which is why this counter exists
    /// beside <see cref="MassiveTopicSubscription{T}.DroppedCount"/> (D-W7).
    /// </remarks>
    public int ReconnectCount => Volatile.Read(ref _reconnectCount);

    // Written by the read-loop thread, polled by a caller on any thread. An int cannot tear, so
    // this is about VISIBILITY, not atomicity -- but that is the same reason _lastReconnectedTicks
    // beside it is volatile, and a plain auto-property here let a caller read a stale count
    // indefinitely. Final review, F3: the sibling got the treatment and this did not, twenty lines
    // apart, which is this branch's own "the fix landed on a member, not the shape" pattern.
    private int _reconnectCount;

    // D5's pattern: the raw epoch value is what the read-loop thread writes and a caller polls
    // from any thread, so it is a single atomic long (Volatile.Read/Write) rather than the
    // Instant? itself. Instant wraps a Duration (an int days plus a long nanoOfDay), well over a
    // native word, so writing it directly could let a cross-thread reader observe a HasValue of
    // true over a half-written value -- a torn, nonsensical timestamp (Task 11 review round 1,
    // finding 6). NoReconnectYet sits outside any real Unix-tick range a live clock produces.
    private const long NoReconnectYet = long.MinValue;
    private long _lastReconnectedTicks = NoReconnectYet;

    /// <summary>When the connection was last re-established.</summary>
    public Instant? LastReconnected
    {
        get
        {
            long ticks = Volatile.Read(ref _lastReconnectedTicks);
            return ticks == NoReconnectYet ? null : Instant.FromUnixTimeTicks(ticks);
        }
    }

    /// <summary>Raised after a successful reconnect, carrying the running count.</summary>
    public event Action<int>? Reconnected;

    /// <summary>Raised when the stream has stopped for good.</summary>
    public event Action<Exception>? Faulted;

    /// <summary>The delay before a given attempt, exposed for testing.</summary>
    /// <param name="options">The backoff configuration.</param>
    /// <param name="attempt">The zero-based attempt number.</param>
    /// <param name="jitterFactor">Between -1 and 1; the tests pass the extremes, the loop randomizes.</param>
    internal static Duration BackoffFor(MassiveStreamReconnectOptions options, int attempt, double jitterFactor)
    {
        double growth = Math.Pow(options.BackoffMultiplier, attempt);
        Duration raw = options.InitialBackoff * growth;
        Duration capped = raw > options.MaxBackoff ? options.MaxBackoff : raw;

        return capped * (1.0 + (options.Jitter * jitterFactor));
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = GC.AllocateUninitializedArray<byte>(_options.MaxMessageBytes);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    int length = await _reader!.ReadMessageAsync(buffer, cancellationToken);

                    // Every control message -- a subscribe or unsubscribe acknowledgement -- arrives
                    // through this same loop, so each payload is checked for status events first. Dispatch
                    // to the registered sinks then runs unconditionally: a status object routes to no sink
                    // (nothing registers one under "status"), so nothing is handled twice, and a frame
                    // carrying BOTH a status event and tick data must still reach whichever sink the tick
                    // belongs to -- ruling T1 on Task 10, and exactly the silent data loss this SDK
                    // refuses everywhere else.
                    DispatchStatusEvents(buffer.AsSpan(0, length));
                    Dispatch(buffer.AsSpan(0, length));

                    // OnMessage is a separate observer seam, independent of the dispatch above -- it is
                    // not how an event reaches its sink, so it is never what gates or substitutes for that
                    // (F2, Task 10 review round 1: an earlier version only defaulted OnMessage to drive
                    // dispatch, which meant a caller who set OnMessage for their own purposes silently
                    // suppressed every sink). Handed the whole message, not a reader the handler could
                    // hold across an await.
                    if (OnMessage is { } handler)
                    {
                        await handler(buffer.AsMemory(0, length));
                    }

                    _reconnectAttempt = 0;
                }
                // Only WebSocketException and MassiveStreamException are read-loop faults treated as
                // reconnectable -- an unexpected close (D-W7). A JsonException out of Dispatch (a
                // malformed event, see ReadEventCode) is deliberately NOT one of them: the socket is
                // healthy, the server will resend the same shape on the next message, and
                // reconnecting to "fix" a parse failure would tear down every other topic's live data
                // and hammer the service in a backoff loop over nothing the drop caused -- the same
                // posture D-W6 refuses for a rejected key. Do not widen this filter to catch it (G3,
                // Task 11 review).
                catch (Exception error) when (error is WebSocketException or MassiveStreamException)
                {
                    if (await TryReconnectAsync(cancellationToken))
                    {
                        continue;
                    }

                    throw;
                }
            }

            // The loop only reaches here if cancellation was requested but the receive that would have
            // observed it never came (nothing pending). Throwing here, rather than returning, keeps
            // every exit ReadLoopTask can report consistent: cancellation always ends the task Canceled,
            // never RanToCompletion, so a caller never has to ask which kind of "done" this was.
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation is a caller-requested exit, not a fault: DisposeAsync completes the sinks.
            throw;
        }
        catch (Exception error)
        {
            // Every path that reaches here is terminal -- nothing further will ever arrive on this
            // connection: reconnect was declined (disabled, or exhausted its own retry loop), an
            // authentication failure surfaced during a reconnect attempt (rethrown from
            // TryReconnectAsync rather than the drop that triggered the attempt, so the caller learns
            // WHY the stream stopped, D-W6/G5), or the fault never entered the reconnect filter above
            // at all (a JsonException, G3). Exactly one of those three stops reaches here, and this is
            // the only place that raises Faulted, so a consumer never sees it twice (G4).
            StopPermanently(error);
            throw;
        }
    }

    private async Task<bool> TryReconnectAsync(CancellationToken cancellationToken)
    {
        if (_options.Reconnect is not { } reconnect)
        {
            // Faulted is raised by ReadLoopAsync's own outer catch, once, after this returns false
            // and the caller's `throw;` rethrows the original drop unchanged -- never here, and
            // never with a substitute exception (G4/G5, Task 11 review).
            return false;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            Duration delay = BackoffFor(reconnect, _reconnectAttempt++, Random.Shared.NextDouble() * 2.0 - 1.0);
            // Boundary crossing (produce): the Duration converts here, naming no BCL type.
            await Task.Delay(delay.ToTimeSpan(), cancellationToken);

            // Held only across THIS attempt's connect-and-replay, never around the whole retry
            // loop's backoff sleeps -- taking it earlier would block a caller's SubscribeAsync/
            // UnsubscribeAsync across arbitrarily many delays instead of one handshake. Without
            // this gate, SendActionAsync (the replay below, and every caller send) reached for
            // _socket with no coordination while this method swapped it underneath it: a caller's
            // frame could land on a live-but-unauthenticated socket mid-handshake, or on one
            // ConnectAsync had already disposed, and UnsubscribeAsync -- which waits for no
            // acknowledgement -- would then remove a pair from the registry the server never
            // actually dropped (Task 11 review round 1, finding 4). Cancellation-aware so a
            // shutdown requested while parked here still exits cleanly instead of waiting out the
            // gate.
            //
            // Not a deadlock, and the direction that matters is not the obvious one: the handshake
            // below reads the socket inline through ConnectAsync/ReadStatusAsync and does not
            // depend on the read loop pumping, so it needs nothing from the very loop holding this
            // gate. What DOES matter is a caller who already holds this gate, awaiting an
            // acknowledgement only the read loop can deliver, while this loop blocks acquiring the
            // same gate for the replay. Still not a deadlock -- the caller's ack wait is bounded by
            // HandshakeTimeout, after which it throws MassiveStreamSubscriptionException and
            // releases the gate in its own finally -- but it does mean a reconnect attempt can be
            // delayed by up to HandshakeTimeout (10s default) behind an in-flight subscribe.
            // Accepted: a bounded delay on an already-degraded connection is the better trade
            // against the state divergence this gate exists to prevent (F-11.3, Task 11 review
            // round 1).
            await _subscribeGate.WaitAsync(cancellationToken);

            try
            {
                if (_socket is { } previous)
                {
                    await previous.DisposeAsync();
                }

                await ConnectAsync(cancellationToken);
                _reader = new FrameReader(_socket!, _options.MaxMessageBytes);

                // Replay before reporting success: a caller told the stream is back has every
                // right to assume their subscriptions came back with it.
                foreach (string parameters in Registry.Parameters)
                {
                    await SendActionAsync("subscribe", parameters, cancellationToken);
                }

                Volatile.Write(ref _reconnectCount, _reconnectCount + 1);
                // D5's pattern: see the _lastReconnectedTicks field comment for why this is a raw
                // atomic write rather than assigning LastReconnected directly.
                Volatile.Write(ref _lastReconnectedTicks, _clock.GetCurrentInstant().ToUnixTimeTicks());
                // Guarded like Faulted below: a throwing handler here escaped every filter
                // in this try and killed the stream through StopPermanently -- right after a
                // reconnect that had just succeeded (see EventRaiser).
                EventRaiser.Raise(Reconnected, ReconnectCount);

                return true;
            }
            catch (MassiveStreamAuthenticationException)
            {
                // Terminal, and rethrown rather than swallowed here: retrying a refused key hammers
                // the service until the account is limited, and the server closes abruptly after
                // auth_failed, so every further attempt would reconnect straight into the same
                // refusal (D-W6). Propagating THIS exception -- not the drop that triggered the
                // attempt -- is what lets ReadLoopAsync's outer catch report the auth failure, not
                // the transient close, as why the stream stopped (G5).
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Transient: ConnectAsync's own linked CTS timed the handshake out at
                // HandshakeTimeout. This is not a caller-requested shutdown -- the filter above
                // already routes that case elsewhere -- it is the ordinary shape of a partial
                // outage (a load balancer that accepts TCP while the backend is down answers
                // exactly this way), and it is more likely than the clean WebSocketException the
                // rest of this loop is written for. Falling through and backing off again is what
                // makes this a reconnect rather than a stream that gives up on the first slow
                // handshake (Task 11 review round 1, finding 3).
            }
            catch (Exception error) when (error is WebSocketException or MassiveStreamException)
            {
                // Transient: fall through and back off again.
            }
            finally
            {
                _subscribeGate.Release();
            }
        }

        // Reaching here means the while condition above was false -- cancellation WAS requested;
        // that is the only way out of the loop besides the `return true` inside it. Throwing here
        // rather than returning false keeps this a caller-requested exit rather than a spurious
        // terminal stop: without it, a transient catch above landing in the narrow window right
        // before this re-check would let ReadLoopAsync's `throw;` rethrow the original drop, which
        // its outer OCE filter does not match (it is not itself an OperationCanceledException), so
        // Faulted would fire on what was really just a shutdown (Task 11 review round 1, finding 8).
        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    // The single place that ends a connection for good, called from ReadLoopAsync's outer catch.
    // Three terminal stops reach it: reconnect declined (disabled, or a caller-requested shutdown
    // during a retry attempt), an authentication failure surfacing during a reconnect attempt, and
    // a fault the reconnect filter never treats as transient at all (a JsonException out of
    // Dispatch, G3). Faulted is notified first, then every sink is completed -- a consumer learns
    // WHY the stream stopped before its sequences end, which is the more useful order -- but the
    // ordering is not what guarantees CompleteAllSinks() below always runs; the per-handler
    // try/catch is (Task 11 review round 1, finding 2, F-11.4). Completing sinks here matters as
    // much as raising Faulted does: without it, every one of the three stops leaves a consumer's
    // `await foreach` parked on a sequence nothing is left alive to end, which is exactly the hang
    // Task 10's F5 closed, arriving through a third door (G4, Task 11 review). This is deliberately
    // NOT called for a transient fault that reconnects successfully -- F5's rule ("never complete on
    // a transient read-loop fault") is unchanged by this: the distinction that matters is terminal
    // vs transient, not fault vs dispose, and a transient drop's sequence must survive the reconnect
    // that resumes it.
    private void StopPermanently(Exception cause)
    {
        // Through EventRaiser, so one throwing subscriber loses only its own notification -- never
        // its neighbours', and never CompleteAllSinks() below, which is what a bare Invoke here
        // skipped when a handler threw (finding 2's original repro).
        EventRaiser.Raise(Faulted, cause);

        CompleteAllSinks();
    }

    // Shared by StopPermanently and DisposeAsync: both are the same "nothing further will ever
    // arrive" moment from a sink's point of view, just reached by different callers -- one when the
    // read loop stops for good, the other when the caller asks to stop. Complete() on an
    // already-completed channel, and TryWrite racing a Complete() from the loop's own last
    // iteration, are both no-ops/false rather than exceptions, so no lock is needed against
    // Dispatch here, and TopicSink.Complete() is TryComplete() underneath, so calling this a second
    // time (a terminal stop followed by disposal, or the reverse) is a harmless no-op too, not a
    // double-completion error.
    //
    // The SNAPSHOT is taken under _sinksLock even though the completing is not: an unlocked read
    // could miss a registration landing concurrently, and that sink would then be completed by
    // nobody -- this method has already passed this point -- hanging its consumer for good. Taking
    // the lock pairs with AddSink's _disposed check so every sink AddSink ever accepts is either in
    // this array or refused outright. Complete() is called outside the lock because it can run
    // arbitrary continuations on the consumer's side, which is not work to do while holding a lock
    // the read loop's dispatch may want.
    private void CompleteAllSinks()
    {
        ITopicSink[] pending;

        lock (_sinksLock)
        {
            pending = [.. Volatile.Read(ref _sinks).Values];
        }

        foreach (ITopicSink sink in pending)
        {
            sink.Complete();
        }
    }

    /// <summary>
    /// Parses <paramref name="payload"/> for status events and routes each to <see cref="OnStatus"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the payload carried at least one status event. No longer gates
    /// whether <see cref="OnMessage"/> also sees the payload (ruling T1 on Task 10): a frame can
    /// carry a status event and tick data together, and the read loop now offers every frame to
    /// <see cref="OnMessage"/> regardless of what this returns.
    /// </returns>
    private bool DispatchStatusEvents(ReadOnlySpan<byte> payload)
    {
        int count = CountStatusEvents(payload, out bool isArray);

        if (isArray && count == 0)
        {
            // A genuine data frame (a trade or quote tick): no "ev": "status" object anywhere in
            // it, so StatusMessage.Parse would find nothing here either. Skipped rather than run a
            // second time -- the count above already proves it, and this is the hot path, running
            // on every message the socket ever delivers.
            return false;
        }

        // Sized to the actual number of status events the payload carries -- never to the
        // payload's byte length. A byte-length-sized destination was the previous design here and
        // was measured: 7.3 MB parsing a 178 KB frame, 64 MiB on the Large Object Heap at the
        // 4 MiB default MaxMessageBytes -- reachable from an ordinary batched multi-ticker
        // unsubscribe acknowledgement, and squarely against this SDK's minimal-allocation goal
        // (D31). Rented, not allocated with `new`, because this runs on every control frame this
        // connection ever receives, not just the rare oversized one. `Math.Max(count, 1)` keeps a
        // malformed non-array payload routed through StatusMessage.Parse's own "not a JSON array"
        // check rather than skipped, matching this method's behaviour before this fix.
        int capacity = Math.Max(count, 1);
        StatusMessage[] rented = ArrayPool<StatusMessage>.Shared.Rent(capacity);

        try
        {
            int written = StatusMessage.Parse(payload, rented.AsSpan(0, capacity));

            for (int i = 0; i < written; i++)
            {
                OnStatus(rented[i]);
            }

            return written > 0;
        }
        finally
        {
            // clearArray: true -- StatusMessage holds strings, and a pooled array that kept them
            // would extend their lifetime for whichever caller rents this slot next.
            ArrayPool<StatusMessage>.Shared.Return(rented, clearArray: true);
        }
    }

    /// <summary>
    /// Counts the <c>ev: status</c> events in <paramref name="payload"/> without allocating one, so
    /// <see cref="DispatchStatusEvents"/> can size its real parse to the event count rather than
    /// the payload's byte length (see the sizing comment there for why that distinction matters).
    /// </summary>
    /// <param name="payload">The raw message bytes, exactly as the socket delivered them.</param>
    /// <param name="isArray">
    /// Whether the payload's top-level token is a JSON array -- the shape every inbound message
    /// takes. <see langword="false"/> means the real parse should still run so its own
    /// "not a JSON array" error reaches the caller unchanged, rather than this method silently
    /// treating a malformed payload as an ordinary data frame.
    /// </param>
    internal static int CountStatusEvents(ReadOnlySpan<byte> payload, out bool isArray)
    {
        Utf8JsonReader reader = new(payload);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            isArray = false;
            return 0;
        }

        isArray = true;
        int count = 0;

        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            bool isStatus = false;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("ev"u8))
                {
                    reader.Read();
                    isStatus = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("status"u8);

                    // Skip even though `ev` is a string on every real frame: if a malformed one ever
                    // makes it an object or array, reading without skipping walks INTO it and every
                    // subsequent token is misread as a property of the outer event. Skip is a no-op
                    // on the scalar this always is, so the honest case pays nothing.
                    reader.Skip();
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            if (isStatus)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Called for every status event the read loop parses, before anything else sees it.</summary>
    private void OnStatus(in StatusMessage status)
    {
        if (status.Status != StatusMessage.Success)
        {
            return;
        }

        string message = status.Message;

        lock (_ackLock)
        {
            // Matched by the exact message text, not counted -- see the field comment on
            // _pendingAcks for why a bare count cannot tell a subscribe's acknowledgement from an
            // unsubscribe's. Anything that does not match a currently-tracked message is ignored
            // here rather than credited toward whatever request happens to be waiting next.
            if (_pendingAcks is { } pending && pending.Remove(message) && pending.Count == 0)
            {
                _pendingAcknowledgements?.TrySetResult();
            }
        }
    }

    // Guards the read-copy-publish in AddSink. A first version of AddSink read _sinks, copied it,
    // and published the copy with no lock at all (ruling T3 on Task 10); that stopped Dispatch's
    // read from ever seeing a torn Dictionary, but left the WRITE side unguarded, and two
    // concurrent AddSink calls can both read the same snapshot, each build a copy holding only its
    // own addition, and the second Volatile.Write silently discard the first sink -- measured by
    // review round 1 at 8 threads x 200 topics: 1,600 expected, 320 survived, 1,280 lost with no
    // exception anywhere (F1). A consumer that subscribes, gets acknowledged, and then never
    // receives anything is a worse failure than T3's corruption, which at least throws. This lock
    // serializes AddSink only -- rare, a subscribe call -- and buys nothing on the read side, which
    // stays exactly as lock-free as before: Dispatch still takes one Volatile.Read snapshot per
    // frame and never touches this lock. A ConcurrentDictionary would also fix the write race, but
    // its per-bucket locking is a cost paid on every read too, for safety a read-only consumer
    // never needed once the write side is correctly serialized on its own.
    private readonly object _sinksLock = new();
    private Dictionary<string, ITopicSink> _sinks = new(StringComparer.Ordinal);

    /// <summary>Registers where <see cref="Dispatch"/> routes an event carrying this sink's topic code.</summary>
    /// <param name="sink">The sink. Replaces any sink already registered under the same topic code.</param>
    /// <exception cref="ObjectDisposedException">The connection is disposed.</exception>
    public void AddSink(ITopicSink sink)
    {
        lock (_sinksLock)
        {
            // Disposal completes every sink it can see and then never runs again, so a sink accepted
            // after that point would keep a consumer parked on a sequence nothing is left to end.
            // Checked under the same lock disposal snapshots beneath, which is what makes the pair
            // exhaustive: a registration that wins the lock first is in disposal's snapshot and gets
            // completed, and one that loses it sees _disposed and is refused here. Neither order
            // leaves an orphan.
            ObjectDisposedException.ThrowIf(_disposed, this);

            Dictionary<string, ITopicSink> current = Volatile.Read(ref _sinks);
            Dictionary<string, ITopicSink> updated = new(current, StringComparer.Ordinal) { [sink.TopicCode] = sink };
            Volatile.Write(ref _sinks, updated);
        }
    }

    /// <summary>Routes every event in <paramref name="payload"/> to the sink its <c>ev</c> code names.</summary>
    /// <remarks>
    /// Synchronous, not <see langword="async"/>: <see cref="Utf8JsonReader"/> is a
    /// <see langword="ref struct"/> and this method holds one across the whole pass, so every sink
    /// is driven in one walk with no opportunity to await mid-frame (constraint noted in the
    /// Task 10 brief for <see cref="ITopicSink.Write"/>). Runs unconditionally from the read loop,
    /// independently of <see cref="OnMessage"/>: dispatch is not something a caller who sets
    /// OnMessage for their own purposes can suppress or substitute for (F2, Task 10 review round 1).
    /// </remarks>
    private void Dispatch(ReadOnlySpan<byte> payload)
    {
        Utf8JsonReader reader = new(payload);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            throw new MassiveStreamException("The stream sent a message that is not a JSON array.");
        }

        // One snapshot for the whole frame, not one per event: several events sharing a frame then
        // share one table even if AddSink races this read (see the _sinks field comment). A sink
        // that another event's Write call registers mid-frame is therefore not retroactively fed
        // the rest of THIS frame -- it starts receiving on the next one (F4, Task 10 review round 1).
        Dictionary<string, ITopicSink> sinks = Volatile.Read(ref _sinks);

        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            // A struct copy is a free bookmark. "ev" was first in every message observed, but
            // nothing in the protocol promises that, so the position is saved and the object is
            // re-read from the start once the code is known.
            Utf8JsonReader start = reader;
            string? code = ReadEventCode(ref reader);

            if (code is not null && sinks.TryGetValue(code, out ITopicSink? sink))
            {
                // Every data frame is walked twice: DispatchStatusEvents/CountStatusEvents already
                // walked it once to check for status events, and this is the second walk, over the
                // same bytes, to find "ev" again. Both passes are allocation-free, so the cost is
                // CPU, not memory. Left as-is on purpose (ruling T2 on Task 10): the coherent fix
                // would delete machinery Tasks 8 and 9 spent getting right, on no more than a hunch
                // that the double walk costs enough to matter -- not a measurement.
                Utf8JsonReader replay = start;
                sink.Write(ref replay);
            }
        }
    }

    /// <summary>Reads an object's <c>ev</c>, leaving the reader on that object's end token.</summary>
    /// <param name="reader">A reader positioned on the object's <c>StartObject</c> token.</param>
    /// <returns>The event code, or <see langword="null"/> if the object carries no <c>ev</c>.</returns>
    /// <exception cref="JsonException"><c>ev</c> is present but is neither a string nor <see langword="null"/>.</exception>
    internal static string? ReadEventCode(ref Utf8JsonReader reader)
    {
        const string Model = "Event";
        string? code = null;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool isEventCode = reader.ValueTextEquals("ev"u8);
            reader.Read();

            if (isEventCode)
            {
                // Through JsonValueReader, the way every other field in this codebase is read, so a
                // malformed "ev" surfaces as a JsonException naming the model and property rather
                // than Utf8JsonReader's own InvalidOperationException -- the same class of finding
                // as Task 9's "sym" (ruling T4 on Task 10).
                code = JsonValueReader.ReadString(ref reader, Model, "ev");
            }

            // Skip either way. It is a no-op on "ev" itself -- JsonValueReader.ReadString above
            // already leaves the reader on the scalar it read, or has thrown before this line runs
            // -- but every OTHER property needs it to step over a container value rather than walk
            // into it: the exact shape of the bug Task 8 found in this method's sibling,
            // CountStatusEvents, where a read without a skip let the reader misread everything after
            // a malformed value as the outer object's own properties.
            reader.Skip();
        }

        return code;
    }

    /// <summary>Subscribes to every ticker under one topic, throwing unless every pair is acknowledged.</summary>
    /// <param name="topicCode">The wire topic code, such as <c>T</c>.</param>
    /// <param name="tickers">The tickers to subscribe to alongside <paramref name="topicCode"/>.</param>
    /// <param name="cancellationToken">Cancels the request. Does not cancel the acknowledgement wait's own bound (see <see cref="MassiveStreamOptions.HandshakeTimeout"/>).</param>
    /// <exception cref="MassiveStreamSubscriptionException">
    /// Fewer acknowledgements arrived than pairs were requested, within the acknowledgement window
    /// (see decision D-W2). The unacknowledged pairs would never have delivered data.
    /// </exception>
    public async Task SubscribeAsync(string topicCode, IReadOnlyCollection<string> tickers, CancellationToken cancellationToken)
    {
        string parameters = string.Join(',', tickers.Select(ticker => $"{topicCode}.{ticker}"));

        // One subscribe (or unsubscribe) in flight at a time: acknowledgements carry no
        // correlation id, so two overlapping requests could not tell whose acknowledgement arrived.
        await _subscribeGate.WaitAsync(cancellationToken);

        try
        {
            Dictionary<string, string> pending = new(StringComparer.Ordinal);

            foreach (string ticker in tickers)
            {
                pending[$"subscribed to: {topicCode}.{ticker}"] = ticker;
            }

            TaskCompletionSource acknowledgements = new(TaskCreationOptions.RunContinuationsAsynchronously);

            // Published as one unit: OnStatus reads _pendingAcks and _pendingAcknowledgements
            // together under the same lock to decide whether a match completes the wait, so both
            // must become visible together, not one after the other.
            lock (_ackLock)
            {
                _pendingAcks = pending;
                _pendingAcknowledgements = acknowledgements;

                if (pending.Count == 0)
                {
                    acknowledgements.TrySetResult();
                }
            }

            await SendActionAsync("subscribe", parameters, cancellationToken);

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // Boundary crossing (produce): the domain Duration converts here and nowhere above.
            timeout.CancelAfter(_options.HandshakeTimeout.ToTimeSpan());

            try
            {
                await acknowledgements.Task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The window closed before every pair was acknowledged; `pending` below still
                // holds exactly which ones, read under the same lock OnStatus removes them under.
            }

            string[] unacknowledgedTickers;

            lock (_ackLock)
            {
                unacknowledgedTickers = [.. pending.Values];
            }

            if (unacknowledgedTickers.Length > 0)
            {
                // Whatever *was* acknowledged is still real and still live -- a partial shortfall
                // must not leave the registry believing none of it landed, since Task 11's
                // reconnect replays exactly what Registry holds.
                string[] acknowledgedTickers =
                    [.. tickers.Except(unacknowledgedTickers, StringComparer.Ordinal)];

                if (acknowledgedTickers.Length > 0)
                {
                    Registry.Add(topicCode, acknowledgedTickers);
                }

                throw new MassiveStreamSubscriptionException(parameters, unacknowledgedTickers.Length);
            }

            Registry.Add(topicCode, tickers);
        }
        finally
        {
            lock (_ackLock)
            {
                _pendingAcks = null;
                _pendingAcknowledgements = null;
            }

            _subscribeGate.Release();
        }
    }

    /// <summary>
    /// Unsubscribes from every ticker under one topic. Not acknowledgement-counted: an unsubscribe
    /// for a pair the server never had is harmless, where a subscribe that silently did nothing is
    /// data the caller will never see. The server does still acknowledge it (with the same
    /// "success" status a subscribe gets, just a different verb in the message, confirmed on the
    /// wire 2026-09-07) -- OnStatus sees that acknowledgement through the read loop like any other
    /// and ignores it, since nothing this method sends is ever tracked in _pendingAcks.
    /// </summary>
    /// <param name="topicCode">The wire topic code, such as <c>T</c>.</param>
    /// <param name="tickers">The tickers to unsubscribe from alongside <paramref name="topicCode"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task UnsubscribeAsync(string topicCode, IReadOnlyCollection<string> tickers, CancellationToken cancellationToken)
    {
        // Same gate as SubscribeAsync: the field's own comment says one subscribe *or unsubscribe*
        // in flight at a time, and honouring that here (even though this method waits on nothing
        // of its own) is what keeps a concurrent SubscribeAsync's _pendingAcks publish from ever
        // racing this method's send.
        await _subscribeGate.WaitAsync(cancellationToken);

        try
        {
            await SendActionAsync(
                "unsubscribe",
                string.Join(',', tickers.Select(ticker => $"{topicCode}.{ticker}")),
                cancellationToken);

            Registry.Remove(topicCode, tickers);
        }
        finally
        {
            _subscribeGate.Release();
        }
    }

    private async Task SendActionAsync(string action, string parameters, CancellationToken cancellationToken)
    {
        byte[] frame = Encoding.UTF8.GetBytes($$"""{"action":"{{action}}","params":"{{parameters}}"}""");
        await _socket!.SendAsync(frame, cancellationToken);
    }

    private async Task SendAuthenticationAsync(CancellationToken cancellationToken)
    {
        // Rule 11 (D-W9). The key is a frame body here, not a header, so it is built into a rented
        // buffer, sent, and wiped -- never interpolated into anything that could be logged, and
        // never held in a field beyond this call.
        string frame = $$"""{"action":"auth","params":"{{_options.ApiKey}}"}""";
        int byteCount = Encoding.UTF8.GetByteCount(frame);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            int written = Encoding.UTF8.GetBytes(frame, buffer);
            await _socket!.SendAsync(buffer.AsMemory(0, written), cancellationToken);
        }
        finally
        {
            Array.Clear(buffer, 0, byteCount);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task ExpectAuthenticationAsync(CancellationToken cancellationToken)
    {
        StatusMessage status = await ReadStatusAsync(cancellationToken);

        if (status.Status == StatusMessage.AuthSuccess)
        {
            return;
        }

        if (status.Status == StatusMessage.AuthFailed)
        {
            throw new MassiveStreamAuthenticationException(status.Message);
        }

        throw new MassiveStreamException(
            $"The stream answered authentication with an unexpected status '{status.Status}'.");
    }

    private async Task ExpectStatusAsync(string expected, CancellationToken cancellationToken)
    {
        StatusMessage status = await ReadStatusAsync(cancellationToken);

        if (status.Status != expected)
        {
            throw new MassiveStreamException(
                $"The stream sent status '{status.Status}' where '{expected}' was expected: {status.Message}");
        }
    }

    private async Task<StatusMessage> ReadStatusAsync(CancellationToken cancellationToken)
    {
        // The handshake is strictly sequential, so this reads directly; the continuous loop that
        // Task 7 starts takes over only once authentication has succeeded.
        using IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(_options.MaxMessageBytes);
        int length = await ReadMessageAsync(owner.Memory, cancellationToken);

        StatusMessage[] statuses = new StatusMessage[1];

        return StatusMessage.Parse(owner.Memory.Span[..length], statuses) == 1
            ? statuses[0]
            : throw new MassiveStreamException("The stream sent a message carrying no status event.");
    }

    // The handshake is strictly sequential -- one message at a time -- so a fresh FrameReader here
    // costs nothing next to the loop's own buffer, which StartReading allocates once and reuses for
    // the connection's whole life (see ReadLoopAsync).
    private ValueTask<int> ReadMessageAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        new FrameReader(_socket!, _options.MaxMessageBytes).ReadMessageAsync(buffer, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Cancel before the socket underneath the loop is torn down, so a pending receive fails on
        // cancellation rather than on a socket that vanished out from under it.
        await _shutdown.CancelAsync();

        try
        {
            await ReadLoopTask;
        }
        catch (Exception)
        {
            // The loop's own outcome -- cancelled, faulted, or never started -- stays observable on
            // ReadLoopTask itself. Disposing must not throw a second time merely because the caller
            // chose to stop the stream; Faulted (Task 11) is where a consumer learns why it stopped.
        }

        // Every registered sink's sequence ends on disposal -- and, since Task 11, on a TERMINAL
        // read-loop stop too (StopPermanently), never on a TRANSIENT one, which reconnect resumes
        // through, so a sequence must survive that (F5's rule, unchanged: see StopPermanently and
        // CompleteAllSinks for the terminal-vs-transient distinction and why calling this twice is
        // safe). Without completing sinks somewhere, TopicSink.Complete() existed since Task 10 but
        // nothing ever called it, so a consumer's `await foreach` never ended even after the
        // connection it was reading from had been disposed (F5, review round 1).
        CompleteAllSinks();

        if (_socket is not null)
        {
            await _socket.DisposeAsync();
        }

        _shutdown.Dispose();
        _subscribeGate.Dispose();
    }
}
