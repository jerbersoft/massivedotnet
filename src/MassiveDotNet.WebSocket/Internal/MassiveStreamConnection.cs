using System.Buffers;
using System.Net.WebSockets;
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

    // Task 7's frame reassembly replaces this with the bounded implementation and its own tests;
    // the handshake only ever needs one message read at a time, so this loops ReceiveAsync until
    // the server marks the message complete.
    private async Task<int> ReadMessageAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int offset = 0;

        while (true)
        {
            ValueWebSocketReceiveResult result = await _socket!.ReceiveAsync(buffer[offset..], cancellationToken);
            offset += result.Count;

            if (result.EndOfMessage)
            {
                return offset;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket is not null)
        {
            await _socket.DisposeAsync();
        }
    }
}
