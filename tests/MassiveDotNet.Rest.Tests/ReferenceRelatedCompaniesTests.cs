using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>Related companies: an unpaginated array under <c>results</c> of one-property rows.</summary>
public sealed class ReferenceRelatedCompaniesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task BuildsTheDocumentedRequestPath()
    {
        StubHandler handler = new(Fixtures.ReferenceRelatedCompanies);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListRelatedCompaniesAsync("AAPL", Ct);
        }

        Assert.Equal("https://api.massive.com/v1/related-companies/AAPL", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceRelatedCompanies);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        RelatedCompany[] companies;

        using (client)
        using (transport)
        {
            companies = await client.Reference.ListRelatedCompaniesAsync("AAPL", Ct);
        }

        Assert.Equal(10, companies.Length);
        Assert.Equal("MSFT", companies[0].Ticker);
        Assert.Equal("PYPL", companies[^1].Ticker);
    }
}
