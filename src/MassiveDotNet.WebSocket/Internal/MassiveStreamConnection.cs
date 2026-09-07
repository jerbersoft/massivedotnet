using System.Buffers;
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
    // Read by Task 11's reconnect backoff, not by the handshake, so IDE0052 sees an unused private
    // field until that lands. Suppressed narrowly rather than exposed through an accessor: this type
    // is partial, so the later half reads _clock directly from its own file, and an internal property
    // minted to satisfy the analyzer would be dead surface the analyzer can never flag again --
    // IDE0052 only sees private members.
#pragma warning disable IDE0052
    private readonly IClock _clock;
#pragma warning restore IDE0052

    private IMassiveWebSocket? _socket;
    private FrameReader? _reader;
    private readonly CancellationTokenSource _shutdown = new();
    private bool _disposed;

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

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = GC.AllocateUninitializedArray<byte>(_options.MaxMessageBytes);

        while (!cancellationToken.IsCancellationRequested)
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
        }

        // The loop only reaches here if cancellation was requested but the receive that would have
        // observed it never came (nothing pending). Throwing here, rather than returning, keeps
        // every exit ReadLoopTask can report consistent: cancellation always ends the task Canceled,
        // never RanToCompletion, so a caller never has to ask which kind of "done" this was.
        cancellationToken.ThrowIfCancellationRequested();
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
    public void AddSink(ITopicSink sink)
    {
        lock (_sinksLock)
        {
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

        // Every registered sink's sequence ends here, and only here -- never on a transient
        // read-loop fault above, which Task 11's reconnect resumes through, so a sequence must
        // survive that. Without this, TopicSink.Complete() existed since Task 10 but nothing ever
        // called it, so a consumer's `await foreach` never ended even after the connection it was
        // reading from had been disposed (F5, review round 1). Complete() on an already-completed
        // channel, and TryWrite racing a Complete() from the loop's own last iteration, are both
        // no-ops/false rather than exceptions, so no lock is needed against Dispatch here.
        foreach (ITopicSink sink in Volatile.Read(ref _sinks).Values)
        {
            sink.Complete();
        }

        if (_socket is not null)
        {
            await _socket.DisposeAsync();
        }

        _shutdown.Dispose();
        _subscribeGate.Dispose();
    }
}
