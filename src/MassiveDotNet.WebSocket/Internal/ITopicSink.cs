using System.Text.Json;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>Where the dispatcher hands an event whose <c>ev</c> matches this topic.</summary>
internal interface ITopicSink
{
    /// <summary>The wire code this sink claims, such as <c>T</c>.</summary>
    string TopicCode { get; }

    /// <summary>Parses one event object and buffers it. Must never block.</summary>
    void Write(ref Utf8JsonReader reader);

    /// <summary>Ends the sequence.</summary>
    void Complete();
}
