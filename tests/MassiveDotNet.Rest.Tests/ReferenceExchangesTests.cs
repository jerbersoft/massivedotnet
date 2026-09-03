using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The cross-asset exchanges list: an unpaginated array whose fixture is a reviewed live
/// capture (D-R12), distinct from the stocks-only <c>StockExchange</c> (D-R7).
/// </summary>
public sealed class ReferenceExchangesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersTheAssetClassAndLocale()
    {
        StubHandler handler = new(Fixtures.ReferenceExchanges);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListExchangesAsync(assetClass: MarketType.Stocks, locale: "us", cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v3/reference/exchanges?asset_class=stocks&locale=us", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesTheCapturedResponse()
    {
        StubHandler handler = new(Fixtures.ReferenceExchanges);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        Exchange[] exchanges;

        using (client)
        using (transport)
        {
            exchanges = await client.Reference.ListExchangesAsync(cancellationToken: Ct);
        }

        Assert.Equal(27, exchanges.Length);

        Exchange amex = exchanges[0];
        Assert.Equal(1, amex.Id);
        Assert.Equal("exchange", amex.Type);
        Assert.Equal("stocks", amex.AssetClass);
        Assert.Equal("us", amex.Locale);
        Assert.Equal("NYSE American, LLC", amex.Name);
        Assert.Equal("AMEX", amex.Acronym);
        Assert.Equal("XASE", amex.Mic);
        Assert.Equal("XNYS", amex.OperatingMic);
        Assert.Equal("A", amex.ParticipantId);
        Assert.Equal("https://www.nyse.com/markets/nyse-american", amex.Url);

        Exchange utp = exchanges[4];
        Assert.Equal("SIP", utp.Type);
        Assert.Null(utp.Mic);
        Assert.Null(utp.Acronym);

        Assert.Contains(exchanges, exchange => exchange.Mic == "XNYS" && exchange.Name == "New York Stock Exchange");
    }
}
