using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The ownership filings: 13-F holdings and forms 3 and 4. Set filters over the CIKs, an array
/// filter over tickers, calendar-date ranges bound from the map (D-R9), and the published samples
/// round-tripping, the two forms through one <see cref="FilingFootnote"/> model (D16).
/// </summary>
public sealed class ReferenceOwnershipFilingsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task ThirteenFRendersEveryFilterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceThirteenFHoldings);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.List13FHoldingsAsync(
                filerCik: SetFilter.AnyOf("0001067983", "0001166559"),
                filingDate: RangeFilter.Between(new LocalDate(2024, 7, 1), new LocalDate(2024, 12, 31)),
                limit: 2,
                sort: "filing_date.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/filings/vX/13-F"
                + "?filer_cik.any_of=0001067983,0001166559&filing_date.gte=2024-07-01&filing_date.lte=2024-12-31&limit=2&sort=filing_date.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task ThirteenFDeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceThirteenFHoldings);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<ThirteenFHolding> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.List13FHoldingsAsync(filerCik: "0001067983", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);

        ThirteenFHolding holding = page.Results[0];
        Assert.Equal("0000950123-24-011775", holding.AccessionNumber);
        Assert.Equal("0001067983", holding.FilerCik);
        Assert.Equal("AMAZON COM INC", holding.IssuerName);
        Assert.Equal("023135106", holding.Cusip);
        Assert.Equal("13F-HR", holding.FormType);
        Assert.Equal(new LocalDate(2024, 11, 14), holding.FilingDate);
        Assert.Equal(new LocalDate(2024, 9, 30), holding.Period);
        Assert.Equal(1439212920L, holding.MarketValue);
        Assert.Equal(7724000L, holding.SharesOrPrincipalAmount);
        Assert.Equal("SH", holding.SharesOrPrincipalType);
        Assert.Equal(7724000L, holding.VotingAuthoritySole);
        Assert.Equal(0L, holding.VotingAuthorityShared);
        Assert.Equal(["Buffett Warren E"], holding.OtherManagers!);
        Assert.Null(holding.PutCall);

        Assert.Equal("AMERICAN EXPRESS CO", page.Results[1].IssuerName);
    }

    [Fact]
    public async Task Form3RendersEveryFilterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceForm3Filings);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListForm3FilingsAsync(
                issuerCik: "0001903508",
                ownerCik: SetFilter.AnyOf("0002125791", "0000000001"),
                tickers: ArrayFilter.AnyOf("PPHC", "AAPL"),
                formType: "3",
                filingDate: RangeFilter.Gte(new LocalDate(2026, 3, 1)),
                limit: 1,
                sort: "filing_date.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/filings/vX/form-3"
                + "?issuer_cik=0001903508&owner_cik.any_of=0002125791,0000000001&tickers.any_of=PPHC,AAPL&form_type=3&filing_date.gte=2026-03-01&limit=1&sort=filing_date.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task Form3DeserializesThePublishedSampleThroughTheFootnotes()
    {
        StubHandler handler = new(Fixtures.ReferenceForm3Filings);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Form3Filing> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListForm3FilingsAsync(tickers: ArrayFilter.Contains("PPHC"), cancellationToken: Ct);
        }

        Form3Filing filing = Assert.Single(page.Results);
        Assert.True(page.HasMore);
        Assert.Equal("0001628280-26-022046", filing.AccessionNumber);
        Assert.Equal("3", filing.FormType);
        Assert.Equal(new LocalDate(2026, 3, 30), filing.FilingDate);
        Assert.Equal(new LocalDate(2026, 3, 20), filing.PeriodOfReport);
        Assert.Equal("Public Policy Holding Company, Inc.", filing.IssuerName);
        Assert.Equal("Mazzanti Matthew Ross", filing.OwnerName);
        Assert.Equal("Chief Administrative Officer", filing.OfficerTitle);
        Assert.True(filing.IsOfficer);
        Assert.False(filing.IsDirector);
        Assert.Null(filing.IsRule10b51Plan);
        Assert.Null(filing.IsNotSubjectToSection16);
        Assert.Equal("Options", filing.SecurityTitle);
        Assert.Equal("D", filing.DirectOrIndirect);
        Assert.Null(filing.SharesOwned);
        Assert.Null(filing.ExercisePrice);
        Assert.Equal(9000d, filing.UnderlyingSecurityShares);
        Assert.Equal(["PPHC"], filing.Tickers!);

        Assert.NotNull(filing.Footnotes);
        Assert.Equal(2, filing.Footnotes.Length);
        Assert.Equal("F1", filing.Footnotes[0].Id);
        Assert.StartsWith("The options granted", filing.Footnotes[0].Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Form4RendersTheEqualityForms()
    {
        StubHandler handler = new(Fixtures.ReferenceForm4Filings);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListForm4FilingsAsync(
                tickers: ArrayFilter.Contains("PAM"),
                filingDate: new LocalDate(2026, 3, 30),
                transactionCode: "A",
                limit: 1,
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/filings/vX/form-4?tickers=PAM&filing_date=2026-03-30&transaction_code=A&limit=1",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task Form4DeserializesThePublishedSampleThroughTheFootnotes()
    {
        StubHandler handler = new(Fixtures.ReferenceForm4Filings);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Form4Filing> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListForm4FilingsAsync(tickers: ArrayFilter.Contains("PAM"), cancellationToken: Ct);
        }

        Form4Filing filing = Assert.Single(page.Results);
        Assert.True(page.HasMore);
        Assert.Equal("0002123147-26-000002", filing.AccessionNumber);
        Assert.Equal("4", filing.FormType);
        Assert.Equal("Pampa Energy Inc.", filing.IssuerName);
        Assert.Equal("Zuberbuhler Adolfo Fernando", filing.OwnerName);
        Assert.Equal("transaction", filing.RecordType);
        Assert.Equal("A", filing.TransactionCode);
        Assert.Equal("A", filing.TransactionAcquiredDisposed);
        Assert.Equal("O", filing.TransactionTimeliness);
        Assert.Equal(new LocalDate(2026, 3, 27), filing.TransactionDate);
        Assert.Equal(new LocalDate(2026, 3, 27), filing.DeemedExecutionDate);
        Assert.Equal(new LocalDate(2026, 3, 27), filing.ExpirationDate);
        Assert.Equal(12923d, filing.TransactionShares);
        Assert.Equal(88.167, filing.TransactionPricePerShare);
        Assert.Equal(1139382.141, filing.TransactionValue);
        Assert.Equal(2759d, filing.SharesOwnedFollowingTransaction);
        Assert.False(filing.IsRule10b51Plan);
        Assert.False(filing.IsEquitySwapInvolved);
        Assert.False(filing.IsNotSubjectToSection16);
        Assert.Null(filing.DateOfOriginalSubmission);
        Assert.Equal(["PAM"], filing.Tickers!);

        FilingFootnote footnote = Assert.Single(filing.Footnotes ?? []);
        Assert.Equal("F1", footnote.Id);
    }

    [Fact]
    public async Task EnumerateThirteenFYieldsTheFirstPageInOrder()
    {
        PagingStubHandler handler = new(Fixtures.ReferenceThirteenFHoldings);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string?> issuers = [];

        using (client)
        using (transport)
        {
            await foreach (ThirteenFHolding holding in client.Reference.Enumerate13FHoldingsAsync(filerCik: "0001067983", cancellationToken: Ct))
            {
                issuers.Add(holding.IssuerName);

                if (issuers.Count == 2)
                {
                    break;
                }
            }
        }

        Assert.Equal(["AMAZON COM INC", "AMERICAN EXPRESS CO"], issuers);
        Assert.Single(handler.Requests);
    }
}
