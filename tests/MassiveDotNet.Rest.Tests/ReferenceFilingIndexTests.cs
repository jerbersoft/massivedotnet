using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The filing index: three full filters over strings, a calendar-date range bound from the map
/// (D-R9), and the published sample round-tripping.
/// </summary>
public sealed class ReferenceFilingIndexTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersEveryFilterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceFilingIndex);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListFilingIndexAsync(
                cik: "0000320193",
                ticker: SetFilter.AnyOf("AAPL", "MSFT"),
                formType: RangeFilter.Between("10-K", "10-Q"),
                filingDate: RangeFilter.Gte(new LocalDate(2025, 1, 1)),
                limit: 2,
                sort: "filing_date.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/filings/vX/index"
                + "?cik=0000320193&ticker.any_of=AAPL,MSFT&form_type.gte=10-K&form_type.lte=10-Q&filing_date.gte=2025-01-01&limit=2&sort=filing_date.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceFilingIndex);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<FilingIndexEntry> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListFilingIndexAsync(ticker: "AAPL", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);
        Assert.Equal("1daccfd9794e482e96d104dee6ed432b", page.RequestId);

        FilingIndexEntry entry = page.Results[0];
        Assert.Equal("0000320193-25-000079", entry.AccessionNumber);
        Assert.Equal("0000320193", entry.Cik);
        Assert.Equal("10-K", entry.FormType);
        Assert.Equal(new LocalDate(2025, 10, 31), entry.FilingDate);
        Assert.Equal("Apple Inc.", entry.IssuerName);
        Assert.Equal("AAPL", entry.Ticker);
        Assert.Equal("https://www.sec.gov/Archives/edgar/data/320193/0000320193-25-000079.txt", entry.FilingUrl);

        Assert.Equal("10-Q", page.Results[1].FormType);
    }

    [Fact]
    public async Task EnumerateYieldsTheFirstPageInOrder()
    {
        PagingStubHandler handler = new(Fixtures.ReferenceFilingIndex);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string?> forms = [];

        using (client)
        using (transport)
        {
            await foreach (FilingIndexEntry entry in client.Reference.EnumerateFilingIndexAsync(cancellationToken: Ct))
            {
                forms.Add(entry.FormType);

                if (forms.Count == 2)
                {
                    break;
                }
            }
        }

        Assert.Equal(["10-K", "10-Q"], forms);
        Assert.Single(handler.Requests);
    }
}
