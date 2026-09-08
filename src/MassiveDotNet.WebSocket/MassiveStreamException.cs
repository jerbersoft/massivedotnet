namespace MassiveDotNet.WebSocket;

/// <summary>The base for every error raised by the streaming client.</summary>
public class MassiveStreamException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    public MassiveStreamException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public MassiveStreamException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
