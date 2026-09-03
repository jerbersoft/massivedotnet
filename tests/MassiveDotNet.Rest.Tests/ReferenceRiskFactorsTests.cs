using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Risk factors: a date field carrying the full comparator set bound from the map (D-R9), so a
/// set of calendar dates renders under <c>any_of</c>, and the published sample round-tripping
/// with no cursor.
/// </summary>
public sealed class ReferenceRiskFactorsTests
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
        StubHandler handler = new(Fixtures.ReferenceRiskFactors);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListRiskFactorsAsync(
                filingDate: SetFilter.AnyOf(new LocalDate(2025, 9, 19), new LocalDate(2025, 9, 20)),
                ticker: "MGLD",
                cik: RangeFilter.Gte("0001000000"),
                limit: 1,
                sort: "filing_date.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/filings/vX/risk-factors"
                + "?filing_date.any_of=2025-09-19,2025-09-20&ticker=MGLD&cik.gte=0001000000&limit=1&sort=filing_date.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceRiskFactors);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<RiskFactor> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListRiskFactorsAsync(ticker: "MGLD", cancellationToken: Ct);
        }

        RiskFactor factor = Assert.Single(page.Results);
        Assert.False(page.HasMore);
        Assert.Equal("c7856101f86c20d855b0ea1c5a6d6efa", page.RequestId);
        Assert.Equal("0001005101", factor.Cik);
        Assert.Equal("MGLD", factor.Ticker);
        Assert.Equal(new LocalDate(2025, 9, 19), factor.FilingDate);
        Assert.Equal("financial_and_market", factor.PrimaryCategory);
        Assert.Equal("credit_and_liquidity", factor.SecondaryCategory);
        Assert.Equal("access_to_capital_and_financing", factor.TertiaryCategory);
        Assert.StartsWith("In addition to the net proceeds", factor.SupportingText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnumerateStopsWhenTheSampleOffersNoCursor()
    {
        PagingStubHandler handler = new(Fixtures.ReferenceRiskFactors);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        int count = 0;

        using (client)
        using (transport)
        {
            await foreach (RiskFactor factor in client.Reference.EnumerateRiskFactorsAsync(ticker: "MGLD", cancellationToken: Ct))
            {
                count++;
            }
        }

        Assert.Equal(1, count);
        Assert.Single(handler.Requests);
    }
}
