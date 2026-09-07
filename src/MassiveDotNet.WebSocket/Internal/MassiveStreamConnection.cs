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
    }
}
