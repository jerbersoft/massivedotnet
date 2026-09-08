namespace MassiveDotNet.WebSocket;

/// <summary>The server refused the stream's authentication message.</summary>
/// <remarks>
/// The server answers <c>auth_failed</c> for two unrelated reasons — a key it does not accept, and
/// a plan that does not include WebSocket access for the market — distinguished only by the prose
/// it sends. The SDK cannot tell them apart without reading that prose, so it reports the server's
/// own words in <see cref="ServerMessage"/> rather than inventing a category (D-W6).
/// A failure of this kind is terminal: the stream does not reconnect after it.
/// </remarks>
public sealed class MassiveStreamAuthenticationException : MassiveStreamException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="serverMessage">The message the server sent, verbatim.</param>
    public MassiveStreamAuthenticationException(string serverMessage)
        : base($"The Massive stream refused authentication: {serverMessage}") =>
        ServerMessage = serverMessage;

    /// <summary>The server's own message, unmodified.</summary>
    public string ServerMessage { get; }
}
