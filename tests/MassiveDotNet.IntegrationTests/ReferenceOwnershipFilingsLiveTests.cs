using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// 13-F holdings and forms 3 and 4 against the real service: one shape call each, asserting the
/// filters select and the rows deserialize, including the footnotes both forms share (D-R13).
/// </summary>
public sealed class ReferenceOwnershipFilingsLiveTests : LiveApiTest
{
    // Berkshire Hathaway's CIK. A 13-F filer whose filings are a matter of permanent record.
    private const string BerkshireCik = "0001067983";

    [Fact]
    public async Task ThirteenFHoldingsHonourTheFilerCik()
    {
        MassivePage<ThirteenFHolding> page = await Client.Reference.List13FHoldingsAsync(
            filerCik: BerkshireCik,
            limit: 2,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (ThirteenFHolding holding in page.Results)
        {
            Assert.Equal(BerkshireCik, holding.FilerCik);
            Assert.StartsWith("13F", holding.FormType, StringComparison.Ordinal);
            Assert.NotNull(holding.FilingDate);
            Assert.NotNull(holding.Period);
            Assert.False(string.IsNullOrEmpty(holding.IssuerName));
            Assert.False(string.IsNullOrEmpty(holding.Cusip));
            Assert.True(holding.MarketValue > 0);
        }
    }

    [Fact]
    public async Task Form3FilingsCarryTheOwnerAndTheIssuer()
    {
        MassivePage<Form3Filing> page = await Client.Reference.ListForm3FilingsAsync(
            tickers: ArrayFilter.Contains("AAPL"),
            limit: 1,
            cancellationToken: Ct);

        Form3Filing filing = Assert.Single(page.Results);
        Assert.Contains("AAPL", filing.Tickers ?? []);
        Assert.Equal("Apple Inc.", filing.IssuerName);
        Assert.StartsWith("3", filing.FormType, StringComparison.Ordinal);
        Assert.NotNull(filing.FilingDate);
        Assert.False(string.IsNullOrEmpty(filing.OwnerName));
        Assert.False(string.IsNullOrEmpty(filing.OwnerCik));
        Assert.False(string.IsNullOrEmpty(filing.IssuerCik));
    }

    [Fact]
    public async Task Form4FilingsCarryTheTransactionAndItsFootnotes()
    {
        MassivePage<Form4Filing> page = await Client.Reference.ListForm4FilingsAsync(
            tickers: ArrayFilter.Contains("AAPL"),
            limit: 5,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (Form4Filing filing in page.Results)
        {
            Assert.Contains("AAPL", filing.Tickers ?? []);
            Assert.Equal("Apple Inc.", filing.IssuerName);
            Assert.StartsWith("4", filing.FormType, StringComparison.Ordinal);
            Assert.NotNull(filing.FilingDate);
            Assert.False(string.IsNullOrEmpty(filing.OwnerName));
        }

        // The footnote model is generated from form 3 and reused here, so a live row carrying one
        // is what proves the reuse against the wire rather than against the description (D16).
        // Whether any given page carries one is not ours to choose, so a page without becomes a
        // skip with a reason rather than a failure; the fixture test covers the reuse either way.
        if (page.Results.FirstOrDefault(filing => filing.Footnotes is { Length: > 0 })
            is not { Footnotes: [FilingFootnote footnote, ..] })
        {
            Assert.Skip("No form 4 in this page carried a footnote; the reuse is covered by the fixture test.");
            return;
        }

        Assert.False(string.IsNullOrEmpty(footnote.Id));
        Assert.False(string.IsNullOrEmpty(footnote.Description));
    }

    [Fact]
    public async Task Form4FilingsHonourATransactionCode()
    {
        MassivePage<Form4Filing> page = await Client.Reference.ListForm4FilingsAsync(
            tickers: ArrayFilter.Contains("AAPL"),
            transactionCode: "S",
            limit: 2,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);
        Assert.All(page.Results, filing => Assert.Equal("S", filing.TransactionCode));
    }
}
