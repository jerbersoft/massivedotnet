using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Conditions: a <see cref="MarketType"/> on <c>asset_class</c> (D-R6), and one model,
/// <see cref="UpdateRule"/>, declared at <c>consolidated</c> and verified by the generator at
/// <c>market_center</c> (decision D16).
/// </summary>
public sealed class ReferenceConditionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersEveryParameterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceConditions);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListConditionsAsync(
                assetClass: MarketType.Stocks,
                dataType: "trade",
                id: 2,
                sip: "CTA",
                order: SortOrder.Ascending,
                limit: 10,
                sort: "id",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v3/reference/conditions?asset_class=stocks&data_type=trade&id=2&sip=CTA&order=asc&limit=10&sort=id",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSampleThroughEveryNestedModel()
    {
        StubHandler handler = new(Fixtures.ReferenceConditions);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Condition> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListConditionsAsync(cancellationToken: Ct);
        }

        Condition condition = Assert.Single(page.Results);
        Assert.Equal(2, condition.Id);
        Assert.Equal("Average Price Trade", condition.Name);
        Assert.Equal("condition", condition.Type);
        Assert.Equal("stocks", condition.AssetClass);
        Assert.Equal(["trade"], condition.DataTypes);
        Assert.Null(condition.Abbreviation);
        Assert.Null(condition.Exchange);
        Assert.Null(condition.IsLegacy);

        Assert.Equal("B", condition.SipMapping.Cta);
        Assert.Equal("W", condition.SipMapping.Utp);
        Assert.Null(condition.SipMapping.Opra);

        Assert.NotNull(condition.UpdateRules);
        Assert.False(condition.UpdateRules.Consolidated.UpdatesHighLow);
        Assert.False(condition.UpdateRules.Consolidated.UpdatesOpenClose);
        Assert.True(condition.UpdateRules.Consolidated.UpdatesVolume);
        Assert.True(condition.UpdateRules.MarketCenter.UpdatesVolume);

        Assert.False(page.HasMore);
        Assert.Equal("31d59dda-80e5-4721-8496-d0d32a654afe", page.RequestId);
    }

    [Fact]
    public async Task EnumerateWalksTheSinglePage()
    {
        StubHandler handler = new(Fixtures.ReferenceConditions);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<int> ids = [];

        using (client)
        using (transport)
        {
            await foreach (Condition condition in client.Reference.EnumerateConditionsAsync(assetClass: MarketType.Stocks, cancellationToken: Ct))
            {
                ids.Add(condition.Id);
            }
        }

        Assert.Equal(2, Assert.Single(ids));
    }
}
