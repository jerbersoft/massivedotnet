using System.Net;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// One call for exchanges (D-G8), which pins a spec/service mismatch rather than the shape it
/// originally set out to check.
/// </summary>
public sealed class StocksExchangesLiveTests : LiveApiTest
{
    [Fact]
    public async Task TheDescribedRouteAnswersNotFound()
    {
        // The OpenAPI description declares GET /stocks/v1/exchanges, so rule 1 keeps it mapped
        // and reachable even though the live service returns a plain-text 404 for it (observed
        // 2026-09-02). The same data is served at /v3/reference/exchanges?asset_class=stocks,
        // which belongs to the Reference group -- a different operation, not a substitute mapping
        // for this one. This test pins the drift so it flips the day the service serves the
        // described route, or the description drops it.
        MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(() =>
            Client.Stocks.ListExchangesAsync(cancellationToken: Ct));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }
}
