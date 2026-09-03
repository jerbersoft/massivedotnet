using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The 10-K sections: three filter shapes derived from the spec's suffix sets (D15), two
/// calendar-date ranges bound from the map (D-R9), and two revisions of one route sharing a
/// model, of which the served one keeps the plain name (D26).
/// </summary>
public sealed class ReferenceTenKSectionsTests
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
        StubHandler handler = new(Fixtures.ReferenceTenKSections);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.List10KSectionsAsync(
                cik: SetFilter.AnyOf("0000320193", "0000789019"),
                ticker: RangeFilter.Between("A", "N"),
                section: SetFilter.AnyOf("business", "risk_factors"),
                filingDate: RangeFilter.Gte(new LocalDate(2023, 1, 1)),
                periodEnd: RangeFilter.Lt(new LocalDate(2024, 1, 1)),
                limit: 2,
                sort: "period_end.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/filings/10-K/vX/sections"
                + "?cik.any_of=0000320193,0000789019"
                + "&ticker.gte=A&ticker.lte=N"
                + "&section.any_of=business,risk_factors"
                + "&filing_date.gte=2023-01-01"
                + "&period_end.lt=2024-01-01"
                + "&limit=2&sort=period_end.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceTenKSections);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<TenKSection> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.List10KSectionsAsync(ticker: "AAPL", section: "risk_factors", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);
        Assert.Equal("a3f8b2c1d4e5f6g7", page.RequestId);

        TenKSection section = page.Results[0];
        Assert.Equal("0000320193", section.Cik);
        Assert.Equal("AAPL", section.Ticker);
        Assert.Equal("risk_factors", section.Section);
        Assert.Equal(new LocalDate(2023, 11, 3), section.FilingDate);
        Assert.Equal(new LocalDate(2023, 9, 30), section.PeriodEnd);
        Assert.Equal("https://www.sec.gov/Archives/edgar/data/320193/0000320193-23-000106.txt", section.FilingUrl);
        Assert.StartsWith("Item 1A. Risk Factors\n\n", section.Text, StringComparison.Ordinal);

        Assert.Equal("MSFT", page.Results[1].Ticker);
    }

    [Fact]
    public async Task TheVx0RevisionBuildsItsOwnPathOverTheSameModel()
    {
        // Both revisions are declared, so both ship (rule 2); the vX_0 one carries its segment in
        // its name because the vX one is served (D26), and its path is its own.
        StubHandler handler = new(Fixtures.ReferenceTenKSections);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<TenKSection> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.List10KSectionsVx0Async(ticker: "AAPL", limit: 1, cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/stocks/filings/10-K/vX_0/sections?ticker=AAPL&limit=1", handler.LastRequestUri?.ToString());
        Assert.Equal("AAPL", page.Results[0].Ticker);
    }

    [Fact]
    public async Task EnumerateFollowsTheSampleCursorThenStops()
    {
        // The sample's cursor points at the same origin, so the traversal follows it verbatim
        // (D14); the stub serves the same page again, which has a cursor too, so the test stops
        // the traversal itself after the seam it set out to cross.
        PagingStubHandler handler = new(Fixtures.ReferenceTenKSections);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string?> tickers = [];

        using (client)
        using (transport)
        {
            await foreach (TenKSection section in client.Reference.Enumerate10KSectionsAsync(section: "risk_factors", cancellationToken: Ct))
            {
                tickers.Add(section.Ticker);

                if (tickers.Count == 3)
                {
                    break;
                }
            }
        }

        Assert.Equal(["AAPL", "MSFT", "AAPL"], tickers);
        Assert.Equal(2, handler.Requests.Count);
        Assert.StartsWith("https://api.massive.com/stocks/filings/10-K/vX/sections?cursor=", handler.Requests[1].ToString(), StringComparison.Ordinal);
    }
}
