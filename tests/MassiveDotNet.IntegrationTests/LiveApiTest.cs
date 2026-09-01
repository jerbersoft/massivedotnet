using MassiveDotNet.Rest;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Base class for tests that call the live Massive API.
/// </summary>
/// <remarks>
/// <para>
/// Every derived class carries <c>[Trait("Category", "Integration")]</c>, which is how CI excludes
/// them from its run. CI additionally holds no API key at all, so these cannot silently pass there
/// even if the filter were removed.
/// </para>
/// <para>
/// When no key is present the test skips with an explanatory reason rather than failing. A skip is
/// honest feedback to a developer reading the output; it is not a false green in CI, because CI
/// never executes these at all.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public abstract class LiveApiTest : IDisposable
{
    private MassiveRestClient? _client;
    private bool _disposed;

    /// <summary>A client bound to the live API. Skips the test when no key is configured.</summary>
    protected MassiveRestClient Client
    {
        get
        {
            Assert.SkipUnless(LiveCredentials.IsAvailable, LiveCredentials.MissingKeyReason);
            return _client ??= new MassiveRestClient(LiveCredentials.ApiKey!);
        }
    }

    /// <summary>The ambient cancellation token for the running test.</summary>
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the client.</summary>
    /// <param name="disposing">Whether managed resources should be released.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (disposing)
        {
            _client?.Dispose();
        }
    }
}
