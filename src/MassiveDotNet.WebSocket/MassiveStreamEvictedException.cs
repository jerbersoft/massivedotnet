namespace MassiveDotNet.WebSocket;

/// <summary>The server closed this connection to make room for another one on the same key.</summary>
/// <remarks>
/// Massive answers a connection it is about to evict with a <c>max_connections</c> status and then
/// closes it, so eviction is the one documented disconnect cause that announces itself. The SDK
/// keeps that announcement and reports it here instead of the bare drop, which carries no close
/// code and reads exactly like a slow consumer or a network failure (D42).
/// <para>
/// The drop itself travels as <see cref="Exception.InnerException"/>: it is still what physically
/// ended the read, and substituting the cause is not a reason to discard the evidence.
/// </para>
/// <para>
/// Unlike <see cref="MassiveStreamAuthenticationException"/> this is <b>not</b> terminal by itself.
/// The reconnect policy is unchanged, so a stream configured to reconnect will reconnect -- and
/// two clients sharing a one-connection plan will keep displacing each other. The SDK reports;
/// what to do about it is the consumer's decision, and it needs this to make it. A consumer whose
/// stream reconnects never sees this exception at all, because the connection does not end: there,
/// the eviction is readable as <see cref="MassiveStockStream.EvictionCount"/> and
/// <see cref="MassiveStockStream.LastEvictionMessage"/>.
/// </para>
/// </remarks>
public sealed class MassiveStreamEvictedException : MassiveStreamException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="serverMessage">The message the server sent, verbatim.</param>
    /// <param name="innerException">The drop that ended the connection.</param>
    public MassiveStreamEvictedException(string serverMessage, Exception innerException)
        : base($"The Massive stream evicted this connection: {serverMessage}", innerException) =>
        ServerMessage = serverMessage;

    /// <summary>The server's own message, unmodified.</summary>
    /// <remarks>
    /// Reported rather than categorised, for the reason D35 gives for <c>auth_failed</c> and D37
    /// gives for a refused subscribe: the SDK cannot improve on the server's own words without
    /// inventing a category that drifts the moment the service rewords one. A handler that wants
    /// to act on the eviction acts on the exception's type, which is what makes this status
    /// different from <c>error</c> -- the status value itself names the cause.
    /// </remarks>
    public string ServerMessage { get; }
}
