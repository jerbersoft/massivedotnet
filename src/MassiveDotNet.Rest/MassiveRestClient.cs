using MassiveDotNet.Http;

namespace MassiveDotNet.Rest;

/// <summary>
/// The entry point to the Massive REST API. Endpoints are reached through groups that mirror
/// the platform's own taxonomy, for example <see cref="Stocks"/> and <see cref="Reference"/>.
/// </summary>
/// <remarks>
/// This type is thread-safe and intended to be long-lived. Create one per application and
/// share it, rather than creating one per request.
/// </remarks>
public sealed class MassiveRestClient : IDisposable
{
    private readonly MassiveHttpTransport _transport;
    private readonly bool _ownsTransport;
    private bool _disposed;

    /// <summary>Creates a client authenticated with an API key.</summary>
    /// <param name="apiKey">The Massive API key.</param>
    public MassiveRestClient(string apiKey)
        : this(new MassiveClientOptions { ApiKey = apiKey })
    {
    }

    /// <summary>Creates a client from a full set of options.</summary>
    /// <param name="options">The client configuration.</param>
    public MassiveRestClient(MassiveClientOptions options)
    {
        _transport = new MassiveHttpTransport(options);
        _ownsTransport = true;
    }

    /// <summary>
    /// Creates a client over a caller-supplied transport, for use with dependency injection.
    /// The caller retains ownership of the transport's lifetime.
    /// </summary>
    /// <param name="transport">The transport to issue requests on.</param>
    /// <exception cref="ArgumentNullException"><paramref name="transport"/> is <see langword="null"/>.</exception>
    public MassiveRestClient(MassiveHttpTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);

        _transport = transport;
        _ownsTransport = false;
    }

    /// <summary>US equities: aggregates, trades, quotes, snapshots, and technical indicators.</summary>
    public StocksGroup Stocks => new(_transport);

    /// <summary>Reference data: tickers, news, corporate actions, exchanges, and conditions.</summary>
    public ReferenceGroup Reference => new(_transport);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_ownsTransport)
        {
            _transport.Dispose();
        }
    }
}
