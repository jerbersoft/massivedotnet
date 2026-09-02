using MassiveDotNet.Http;

namespace MassiveDotNet.Rest;

/// <summary>
/// Reference data across asset classes: tickers, news, corporate actions, exchanges, and
/// conditions. Reached through <see cref="MassiveRestClient.Reference"/>.
/// </summary>
/// <remarks>
/// This is a <see langword="struct"/> wrapping the shared transport, so navigating to a group
/// costs no allocation. Endpoint methods live in the generated half of this partial type.
/// </remarks>
public readonly partial struct ReferenceGroup
{
    private readonly MassiveHttpTransport _transport;

    internal ReferenceGroup(MassiveHttpTransport transport) => _transport = transport;
}
