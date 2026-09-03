using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The 8-K disclosures and text: an array filter over <c>tickers</c>, a set filter over
/// <c>cik</c>, a date bound from the map as a full filter on one route and a range on the other
/// (D-R9), and the published samples round-tripping.
/// </summary>
public sealed class ReferenceEightKTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task DisclosuresRenderEveryFilterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceEightKDisclosures);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.List8KDisclosuresAsync(
                cik: SetFilter.AnyOf("0000320193", "0000004962"),
                tickers: ArrayFilter.AllOf("AAPL", "AXP"),
                filingDate: RangeFilter.Between(new LocalDate(2025, 1, 1), new LocalDate(2025, 1, 31)),
                tertiaryCategory: "quarterly_results",
                limit: 2,
                sort: "filing_date.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/filings/8-K/vX/disclosures"
                + "?cik.any_of=0000320193,0000004962"
                + "&tickers.all_of=AAPL,AXP"
                + "&filing_date.gte=2025-01-01&filing_date.lte=2025-01-31"
                + "&tertiary_category=quarterly_results"
                + "&limit=2&sort=filing_date.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DisclosuresRenderTheEqualityForms()
    {
        StubHandler handler = new(Fixtures.ReferenceEightKDisclosures);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.List8KDisclosuresAsync(
                cik: "0000320193",
                tickers: ArrayFilter.Contains("AAPL"),
                filingDate: new LocalDate(2025, 1, 14),
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/filings/8-K/vX/disclosures?cik=0000320193&tickers=AAPL&filing_date=2025-01-14",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DisclosuresDeserializeThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceEightKDisclosures);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<EightKDisclosure> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.List8KDisclosuresAsync(tickers: ArrayFilter.Contains("AAPL"), cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);

        EightKDisclosure disclosure = page.Results[0];
        Assert.Equal("0000320193-25-000010", disclosure.AccessionNumber);
        Assert.Equal("0000320193", disclosure.Cik);
        Assert.Equal(new LocalDate(2025, 1, 14), disclosure.FilingDate);
        Assert.Equal("financial_results", disclosure.PrimaryCategory);
        Assert.Equal("earnings_announcement", disclosure.SecondaryCategory);
        Assert.Equal("quarterly_results", disclosure.TertiaryCategory);
        Assert.Equal(["AAPL"], disclosure.Tickers!);
        Assert.StartsWith("On January 14, 2025", disclosure.SupportingText, StringComparison.Ordinal);

        Assert.Equal(["AXP"], page.Results[1].Tickers!);
    }

    [Fact]
    public async Task TextRendersEveryFilterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceEightKText);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.List8KTextAsync(
                cik: "0000004962",
                ticker: RangeFilter.Gte("A"),
                formType: SetFilter.AnyOf("8-K", "10-K"),
                filingDate: RangeFilter.Lte(new LocalDate(2025, 1, 31)),
                limit: 2,
                sort: "filing_date.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/filings/8-K/vX/text"
                + "?cik=0000004962&ticker.gte=A&form_type.any_of=8-K,10-K&filing_date.lte=2025-01-31&limit=2&sort=filing_date.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task TextDeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceEightKText);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<EightKText> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.List8KTextAsync(ticker: "AXP", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);

        EightKText text = page.Results[0];
        Assert.Equal("0000004962-25-000002", text.AccessionNumber);
        Assert.Equal("AXP", text.Ticker);
        Assert.Equal("8-K", text.FormType);
        Assert.Equal(new LocalDate(2025, 1, 15), text.FilingDate);
        Assert.StartsWith("Item 7.01\tRegulation FD Disclosure", text.ItemsText, StringComparison.Ordinal);

        Assert.Equal("AAPL", page.Results[1].Ticker);
    }

    [Fact]
    public async Task EnumerateTextYieldsTheFirstPageInOrder()
    {
        PagingStubHandler handler = new(Fixtures.ReferenceEightKText);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string?> tickers = [];

        using (client)
        using (transport)
        {
            await foreach (EightKText text in client.Reference.Enumerate8KTextAsync(cancellationToken: Ct))
            {
                tickers.Add(text.Ticker);

                if (tickers.Count == 2)
                {
                    break;
                }
            }
        }

        Assert.Equal(["AXP", "AAPL"], tickers);
        Assert.Single(handler.Requests);
    }
}
