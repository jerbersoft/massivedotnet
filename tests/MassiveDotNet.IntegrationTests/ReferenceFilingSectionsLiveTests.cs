using System.Net;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// The 10-K sections and the two 8-K routes against the real service: one shape call each on the
/// served revisions, and the pin that the declared <c>vX_0</c> sections revision answers 404
/// (D21, D-R13).
/// </summary>
public sealed class ReferenceFilingSectionsLiveTests : LiveApiTest
{
    [Fact]
    public async Task TenKSectionsReturnTheRequestedSection()
    {
        MassivePage<TenKSection> page = await Client.Reference.List10KSectionsAsync(
            ticker: "AAPL",
            section: "risk_factors",
            limit: 1,
            cancellationToken: Ct);

        TenKSection section = Assert.Single(page.Results);
        Assert.Equal("AAPL", section.Ticker);
        Assert.Equal("risk_factors", section.Section);
        Assert.NotNull(section.PeriodEnd);
        Assert.NotNull(section.FilingDate);
        Assert.False(string.IsNullOrEmpty(section.Text));
        Assert.False(string.IsNullOrEmpty(section.FilingUrl));
    }

    [Fact]
    public async Task TenKSectionsHonourASetOfSections()
    {
        // The section filter is the only SetFilter on this route, and the any_of form is what a
        // fixture cannot prove renders acceptably to the server.
        MassivePage<TenKSection> page = await Client.Reference.List10KSectionsAsync(
            ticker: "AAPL",
            section: SetFilter.AnyOf("business", "risk_factors"),
            limit: 2,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);
        Assert.All(page.Results, section => Assert.True(section.Section is "business" or "risk_factors"));
    }

    [Fact]
    public async Task TheVx0SectionsRevisionAnswersNotFound()
    {
        // The description declares the vX_0 revision beside the vX one, so rule 1 keeps it mapped
        // and D21 keeps it that way until the description drops it: the service answered a
        // plain-text 404 on 2026-09-03. This pins the drift so it flips the day the route is
        // served, at which point D26 says the plain name moves here.
        MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(() =>
            Client.Reference.List10KSectionsVx0Async(ticker: "AAPL", limit: 1, cancellationToken: Ct));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task EightKDisclosuresHonourAnArrayFilterOnTickers()
    {
        // tickers is an array field, so the plain form asks for rows containing the value. The
        // array filter's wire form is the thing a fixture cannot check against the server.
        MassivePage<EightKDisclosure> page = await Client.Reference.List8KDisclosuresAsync(
            tickers: ArrayFilter.Contains("AAPL"),
            limit: 2,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (EightKDisclosure disclosure in page.Results)
        {
            Assert.Contains("AAPL", disclosure.Tickers ?? []);
            Assert.NotNull(disclosure.FilingDate);
            Assert.False(string.IsNullOrEmpty(disclosure.PrimaryCategory));
            Assert.False(string.IsNullOrEmpty(disclosure.TertiaryCategory));
        }
    }

    [Fact]
    public async Task EightKTextReturnsTheItemText()
    {
        MassivePage<EightKText> page = await Client.Reference.List8KTextAsync(
            ticker: "AAPL",
            filingDate: RangeFilter.Gte(new LocalDate(2025, 1, 1)),
            limit: 1,
            cancellationToken: Ct);

        EightKText text = Assert.Single(page.Results);
        Assert.Equal("AAPL", text.Ticker);
        Assert.StartsWith("8-K", text.FormType, StringComparison.Ordinal);
        Assert.NotNull(text.FilingDate);
        Assert.True(text.FilingDate >= new LocalDate(2025, 1, 1));
        Assert.False(string.IsNullOrEmpty(text.ItemsText));
    }
}
