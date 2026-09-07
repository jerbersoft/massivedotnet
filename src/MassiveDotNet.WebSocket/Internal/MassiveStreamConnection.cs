using System.Buffers;
using System.Text;
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
    // id, so two overlapping requests could not tell whose acknowledgement arrived.
    private readonly SemaphoreSlim _subscribeGate = new(1, 1);
    private TaskCompletionSource? _pendingAcknowledgements;

    // How many acknowledgements are still owed. Signed rather than a plain non-negative count: the
    // read loop runs continuously, independent of when a subscribe call happens to reach its own
    // send, so a status frame already sitting in the transport (or delivered on another thread
    // before this one finishes its own send) can be processed before SubscribeAsync ever assigns
    // this field. Letting OnStatus decrement unconditionally banks that acknowledgement as negative
    // credit instead of discarding it; SubscribeAsync then *adds* its own count on top rather than
    // overwriting, which redeems whatever was already banked. Reset to 0 whenever a subscribe call
    // ends (success, shortfall, or cancellation) so a later call never inherits stale credit.
    private int _outstanding;

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
            // through this same loop, so each payload is checked for status events before it is
            // ever offered to OnMessage. A frame that carries one is consumed here and stops:
            // OnMessage sees ticks and quotes, never the acknowledgements that produced them.
            if (DispatchStatusEvents(buffer.AsSpan(0, length)))
            {
                continue;
            }

            if (OnMessage is { } handler)
            {
                // Handed the whole message, not a reader the handler could hold across an await:
                // Task 10's ITopicSink.Write(ref Utf8JsonReader) cannot cross one, so the dispatch
                // this loop drives has to be a single synchronous pass over the buffer.
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
    /// <see langword="true"/> if the payload carried at least one status event -- meaning it is a
    /// control frame that must not also be handed to <see cref="OnMessage"/>.
    /// </returns>
    private bool DispatchStatusEvents(ReadOnlySpan<byte> payload)
    {
        // Sized for the acknowledgements a pending subscribe is still waiting on -- correct for
        // every frame this connection sends itself, since SubscribeAsync sets _outstanding before
        // sending. It can still be too small: the read loop runs continuously regardless of send
        // timing, so a frame can be parsed before the subscribe that owns it has published its
        // count (see the field comment on _outstanding). StatusMessage.Parse refuses to truncate
        // rather than drop an event, so that undersized guess throws instead of losing data --
        // caught below and retried against a destination sized from the payload itself, which no
        // JSON array can ever overflow (each object needs at least two bytes, "{}").
        int guess = Math.Max(_outstanding, 1);
        StatusMessage[] statuses;
        int written;

        try
        {
            statuses = new StatusMessage[guess];
            written = StatusMessage.Parse(payload, statuses);
        }
        catch (MassiveStreamException) when (guess < payload.Length)
        {
            statuses = new StatusMessage[payload.Length];
            written = StatusMessage.Parse(payload, statuses);
        }

        for (int i = 0; i < written; i++)
        {
            OnStatus(statuses[i]);
        }

        return written > 0;
    }

    /// <summary>Called for every status event the read loop parses, before anything else sees it.</summary>
    private void OnStatus(in StatusMessage status)
    {
        if (status.Status != StatusMessage.Success)
        {
            return;
        }

        // Decremented unconditionally -- see the field comment on _outstanding for why an event
        // that arrives with no subscribe currently pending must still be counted rather than
        // dropped. _pendingAcknowledgements is only non-null once SubscribeAsync has redeemed
        // whatever was already banked, so completing it here is safe exactly when it is non-null.
        if (Interlocked.Decrement(ref _outstanding) <= 0)
        {
            _pendingAcknowledgements?.TrySetResult();
        }
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

        // One subscribe in flight at a time: acknowledgements carry no correlation id, so two
        // overlapping requests could not tell whose acknowledgement arrived.
        await _subscribeGate.WaitAsync(cancellationToken);

        try
        {
            // Add rather than overwrite: redeems any credit OnStatus already banked in _outstanding
            // for this request (see the field comment) instead of discarding it. In the ordinary
            // case nothing has arrived yet, _outstanding is 0, and this is exactly tickers.Count.
            int remaining = Interlocked.Add(ref _outstanding, tickers.Count);
            _pendingAcknowledgements = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            if (remaining <= 0)
            {
                // Every acknowledgement this request needed was already banked before it got here.
                _pendingAcknowledgements.TrySetResult();
            }

            await SendActionAsync("subscribe", parameters, cancellationToken);

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // Boundary crossing (produce): the domain Duration converts here and nowhere above.
            timeout.CancelAfter(_options.HandshakeTimeout.ToTimeSpan());

            bool everyPairAcknowledged = true;

            try
            {
                await _pendingAcknowledgements.Task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The window closed before every pair was acknowledged.
                everyPairAcknowledged = false;
            }

            // _outstanding holds how many are still missing (0 or negative once satisfied), so this
            // names the true shortfall rather than assuming none arrived when the wait times out.
            int acknowledged = everyPairAcknowledged ? tickers.Count : tickers.Count - Math.Max(_outstanding, 0);

            if (acknowledged < tickers.Count)
            {
                throw new MassiveStreamSubscriptionException(parameters, tickers.Count - acknowledged);
            }

            Registry.Add(topicCode, tickers);
        }
        finally
        {
            // Reset rather than left at whatever it settled on: a call that timed out would
            // otherwise leave a positive remainder that silently discounts the very next
            // subscribe's count, and stray credit from this call has nowhere else to go.
            _outstanding = 0;
            _pendingAcknowledgements = null;
            _subscribeGate.Release();
        }
    }

    /// <summary>
    /// Unsubscribes from every ticker under one topic. Not acknowledgement-counted: an unsubscribe
    /// for a pair the server never had is harmless, where a subscribe that silently did nothing is
    /// data the caller will never see.
    /// </summary>
    /// <param name="topicCode">The wire topic code, such as <c>T</c>.</param>
    /// <param name="tickers">The tickers to unsubscribe from alongside <paramref name="topicCode"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    public async Task UnsubscribeAsync(string topicCode, IReadOnlyCollection<string> tickers, CancellationToken cancellationToken)
    {
        await SendActionAsync(
            "unsubscribe",
            string.Join(',', tickers.Select(ticker => $"{topicCode}.{ticker}")),
            cancellationToken);

        Registry.Remove(topicCode, tickers);
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

        if (_socket is not null)
        {
            await _socket.DisposeAsync();
        }

        _shutdown.Dispose();
        _subscribeGate.Dispose();
    }
}
