using MassiveDotNet.Http;

namespace MassiveDotNet.Rest;

/// <summary>
/// Endpoints covering US equities. Reached through <see cref="MassiveRestClient.Stocks"/>.
/// </summary>
/// <remarks>
/// This is a <see langword="struct"/> wrapping the shared transport, so navigating to a group
/// costs no allocation. Endpoint methods live in the generated half of this partial type.
/// </remarks>
public readonly partial struct StocksGroup
{
    private readonly MassiveHttpTransport _transport;

    internal StocksGroup(MassiveHttpTransport transport) => _transport = transport;
}
