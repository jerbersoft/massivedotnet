using MassiveDotNet.WebSocket.Internal;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// H5 from the Task 12 pre-flight: the brief's <c>ConnectRawAsync</c> paired an outer
/// <c>await using ClientWebSocketAdapter</c> with <c>connection.DisposeAsync()</c>, which disposes
/// <c>_socket</c> itself -- disposing the adapter twice. This is the evidence for the finding: the
/// underlying <see cref="System.Net.WebSockets.ClientWebSocket.Dispose()"/> is idempotent, so the
/// double dispose was harmless, and <see cref="MassiveStreamClient.ConnectRawAsync"/> was rewritten
/// to drop the redundant outer <c>await using</c> rather than rely on this.
/// </summary>
public class ClientWebSocketAdapterTests
{
    [Fact]
    public async Task DisposeAsyncIsIdempotent()
    {
        ClientWebSocketAdapter adapter = new(new MassiveStreamOptions { ApiKey = "k" });

        await adapter.DisposeAsync();
        await adapter.DisposeAsync();
    }
}
