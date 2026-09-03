using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// The filing index, the risk factors, and both taxonomies against the real service: one shape
/// call each, and the pair of taxonomy calls that prove the same wire name really is a string on
/// one route and a number on the other (D-R13).
/// </summary>
public sealed class ReferenceFilingTaxonomiesLiveTests : LiveApiTest
{
    [Fact]
    public async Task TheFilingIndexHonoursATickerAndAFormType()
    {
        MassivePage<FilingIndexEntry> page = await Client.Reference.ListFilingIndexAsync(
            ticker: "AAPL",
            formType: "10-K",
            limit: 2,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (FilingIndexEntry entry in page.Results)
        {
            Assert.Equal("AAPL", entry.Ticker);
            Assert.Equal("10-K", entry.FormType);
            Assert.Equal("0000320193", entry.Cik);
            Assert.NotNull(entry.FilingDate);
            Assert.False(string.IsNullOrEmpty(entry.AccessionNumber));
            Assert.False(string.IsNullOrEmpty(entry.IssuerName));
        }
    }

    [Fact]
    public async Task RiskFactorsCarryTheThreeLevelCategory()
    {
        MassivePage<RiskFactor> page = await Client.Reference.ListRiskFactorsAsync(
            ticker: "AAPL",
            limit: 2,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (RiskFactor factor in page.Results)
        {
            Assert.Equal("AAPL", factor.Ticker);
            Assert.NotNull(factor.FilingDate);
            Assert.False(string.IsNullOrEmpty(factor.PrimaryCategory));
            Assert.False(string.IsNullOrEmpty(factor.SecondaryCategory));
            Assert.False(string.IsNullOrEmpty(factor.TertiaryCategory));
            Assert.False(string.IsNullOrEmpty(factor.SupportingText));
        }
    }

    [Fact]
    public async Task TheDisclosureTaxonomyVersionIsAString()
    {
        MassivePage<DisclosureTaxonomyEntry> page = await Client.Reference.ListDisclosureTaxonomyAsync(
            limit: 5,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (DisclosureTaxonomyEntry entry in page.Results)
        {
            Assert.False(string.IsNullOrEmpty(entry.Taxonomy));
            Assert.False(string.IsNullOrEmpty(entry.PrimaryCategory));
            Assert.False(string.IsNullOrEmpty(entry.Description));
        }
    }

    [Fact]
    public async Task TheRiskFactorTaxonomyVersionIsANumber()
    {
        // The published example spells this version as a string where the schema declares a
        // number; the wire sends the number, which is why the fixture departs from the example
        // (the Task 8 ruling, recorded in D-R12). The gte filter is a double on this route.
        MassivePage<RiskFactorTaxonomyEntry> page = await Client.Reference.ListRiskFactorTaxonomyAsync(
            taxonomy: RangeFilter.Gte(1d),
            limit: 5,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (RiskFactorTaxonomyEntry entry in page.Results)
        {
            Assert.True(entry.Taxonomy >= 1d);
            Assert.False(string.IsNullOrEmpty(entry.PrimaryCategory));
            Assert.False(string.IsNullOrEmpty(entry.Description));
        }
    }
}
