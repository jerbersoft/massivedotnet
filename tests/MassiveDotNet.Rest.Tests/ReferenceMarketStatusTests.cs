using System.Net;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Market status: a body-object payload with no envelope (decision D17), three nested models,
/// and a server time carrying a UTC offset that the <see cref="Instant"/> converter reads.
/// </summary>
public sealed class ReferenceMarketStatusTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task BuildsTheDocumentedRequestPath()
    {
        StubHandler handler = new(Fixtures.ReferenceMarketStatus);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.GetMarketStatusAsync(Ct);
        }

        Assert.Equal("https://api.massive.com/v1/marketstatus/now", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceMarketStatus);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MarketStatus status;

        using (client)
        using (transport)
        {
            status = await client.Reference.GetMarketStatusAsync(Ct);
        }

        Assert.Equal("extended-hours", status.Market);
        Assert.True(status.IsAfterHours);
        Assert.False(status.IsEarlyHours);

        // 17:37:37 at -05:00 is 22:37:37Z; the offset must be applied, not dropped.
        Assert.Equal(Instant.FromUtc(2020, 11, 10, 22, 37, 37), status.ServerTime);

        Assert.NotNull(status.Exchanges);
        Assert.Equal("extended-hours", status.Exchanges.Nyse);
        Assert.Equal("extended-hours", status.Exchanges.Nasdaq);
        Assert.Equal("closed", status.Exchanges.Otc);

        Assert.NotNull(status.Currencies);
        Assert.Equal("open", status.Currencies.Fx);
        Assert.Equal("open", status.Currencies.Crypto);

        Assert.Null(status.IndexGroups);
    }

    [Fact]
    public async Task ANullBodyIsReported()
    {
        StubHandler handler = new("null");
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Reference.GetMarketStatusAsync(Ct));

            Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
            Assert.Null(exception.RequestId);
            Assert.Contains("carried no payload", exception.Message, StringComparison.Ordinal);
        }
    }
}
