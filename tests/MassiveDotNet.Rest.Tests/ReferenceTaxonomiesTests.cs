using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The two taxonomies: one <c>taxonomy</c> field typed string with the full comparator set, the
/// other typed number with the four bounds, so the same wire name renders a string on one route
/// and a double on the other; and the published samples round-tripping, the risk-factor one
/// corrected to the number its schema and the wire carry (D-R12).
/// </summary>
public sealed class ReferenceTaxonomiesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task DisclosuresRenderAStringTaxonomyAndTheCategoryFilters()
    {
        StubHandler handler = new(Fixtures.ReferenceDisclosureTaxonomy);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListDisclosureTaxonomyAsync(
                taxonomy: RangeFilter.Gte("1.0"),
                primaryCategory: SetFilter.AnyOf("financial_results", "leadership_and_governance"),
                secondaryCategory: RangeFilter.Gte("a"),
                tertiaryCategory: "ceo_appointment",
                limit: 2,
                cancellationToken: Ct);
        }

        // taxonomy.gte carries a string here and a double on the risk-factor route below; the two
        // renderings side by side are what prove the same wire name is two element types.
        Assert.Equal(
            "https://api.massive.com/stocks/taxonomies/vX/disclosures"
                + "?taxonomy.gte=1.0&primary_category.any_of=financial_results,leadership_and_governance&secondary_category.gte=a&tertiary_category=ceo_appointment&limit=2",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DisclosuresDeserializeThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceDisclosureTaxonomy);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<DisclosureTaxonomyEntry> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListDisclosureTaxonomyAsync(cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.False(page.HasMore);

        DisclosureTaxonomyEntry entry = page.Results[0];
        Assert.Equal("1.0", entry.Taxonomy);
        Assert.Equal("leadership_and_governance", entry.PrimaryCategory);
        Assert.Equal("executive_leadership", entry.SecondaryCategory);
        Assert.Equal("ceo_appointment", entry.TertiaryCategory);
        Assert.StartsWith("New CEO appointment", entry.Description, StringComparison.Ordinal);

        Assert.Equal("quarterly_earnings", page.Results[1].TertiaryCategory);
    }

    [Fact]
    public async Task RiskFactorsRenderANumericTaxonomyRange()
    {
        StubHandler handler = new(Fixtures.ReferenceRiskFactorTaxonomy);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListRiskFactorTaxonomyAsync(
                taxonomy: RangeFilter.Between(1.0, 1.5),
                tertiaryCategory: RangeFilter.Lt("D"),
                limit: 2,
                cancellationToken: Ct);
        }

        // A double renders its shortest round-trip form, so 1.0 is "1" on the wire.
        Assert.Equal(
            "https://api.massive.com/stocks/taxonomies/vX/risk-factors?taxonomy.gte=1&taxonomy.lte=1.5&tertiary_category.lt=D&limit=2",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task RiskFactorsDeserializeTheCorrectedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceRiskFactorTaxonomy);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<RiskFactorTaxonomyEntry> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListRiskFactorTaxonomyAsync(cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.False(page.HasMore);

        RiskFactorTaxonomyEntry entry = page.Results[0];
        Assert.Equal(1.0, entry.Taxonomy);
        Assert.Equal("Governance & Stakeholder", entry.PrimaryCategory);
        Assert.Equal("Organizational & Management", entry.SecondaryCategory);
        Assert.Equal("Performance management and accountability", entry.TertiaryCategory);
        Assert.StartsWith("Risk from inadequate performance", entry.Description, StringComparison.Ordinal);

        Assert.Equal("Data & Privacy", page.Results[1].SecondaryCategory);
    }
}
