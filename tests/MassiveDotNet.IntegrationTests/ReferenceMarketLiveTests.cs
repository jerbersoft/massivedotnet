using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Market status, conditions, and exchanges against the real service: the body-object payload
/// deserializes with every nested object the live wire sends (D-R13), and one shape call each
/// for the other two.
/// </summary>
public sealed class ReferenceMarketLiveTests : LiveApiTest
{
    [Fact]
    public async Task MarketStatusDeserializesItsBody()
    {
        MarketStatus status = await Client.Reference.GetMarketStatusAsync(Ct);

        Assert.False(string.IsNullOrEmpty(status.Market));
        Assert.NotNull(status.ServerTime);
        Assert.NotNull(status.Exchanges);
        Assert.False(string.IsNullOrEmpty(status.Exchanges.Nyse));
        Assert.NotNull(status.Currencies);

        // The published example omits indicesGroups; the live wire sent it on 2026-09-03.
        Assert.NotNull(status.IndexGroups);
    }

    [Fact]
    public async Task ConditionsHonourTheAssetClassAndDataType()
    {
        MassivePage<Condition> page = await Client.Reference.ListConditionsAsync(
            assetClass: MarketType.Stocks,
            dataType: "trade",
            limit: 5,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (Condition condition in page.Results)
        {
            Assert.Equal("stocks", condition.AssetClass);
            Assert.Contains("trade", condition.DataTypes);
            Assert.NotNull(condition.SipMapping);
        }
    }

    [Fact]
    public async Task ExchangesIncludeTheNewYorkStockExchange()
    {
        Exchange[] exchanges = await Client.Reference.ListExchangesAsync(assetClass: MarketType.Stocks, cancellationToken: Ct);

        Assert.Contains(exchanges, exchange => exchange.Mic == "XNYS");
        Assert.All(exchanges, exchange => Assert.Equal("stocks", exchange.AssetClass));
    }
}
