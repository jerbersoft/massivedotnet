using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Exchanges: the one paginated operation in this batch with a single optional parameter, and
/// the one whose traversal is proven against two stub pages.
/// </summary>
public sealed class StocksExchangesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Cursor = "https://api.massive.com/stocks/v1/exchanges?cursor=YWZ0ZXI9MTI";

    /// <summary>The published sample with a cursor added, since the sample itself has none.</summary>
    private static readonly string FirstPage = Fixtures.StocksExchanges.Replace(
        "\"count\": 2,",
        $"\"count\": 2,\n  \"next_url\": \"{Cursor}\",",
        StringComparison.Ordinal);

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task BuildsTheDocumentedRequestPath()
    {
        StubHandler handler = new(Fixtures.StocksExchanges);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListExchangesAsync(limit: 2, cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/stocks/v1/exchanges?limit=2", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesTheCorrectedSample()
    {
        StubHandler handler = new(Fixtures.StocksExchanges);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<StockExchange> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListExchangesAsync(cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);

        StockExchange nyse = page.Results[0];
        Assert.Equal("10", nyse.Id);
        Assert.Equal("New York Stock Exchange", nyse.Name);
        Assert.Equal("XNYS", nyse.Mic);
        Assert.Equal("XNYS", nyse.OperatingMic);
        Assert.Equal("N", nyse.ParticipantId);
        Assert.Equal("exchange", nyse.Type);
        Assert.Equal("US", nyse.Locale);
        Assert.Equal("https://www.nyse.com", nyse.Url);
        Assert.Null(nyse.Acronym);

        Assert.Equal("Nasdaq", page.Results[1].Name);
        Assert.False(page.HasMore);
        Assert.Equal("1", page.RequestId);
    }

    [Fact]
    public async Task EnumerateTraversesTwoPagesFollowingTheCursorVerbatim()
    {
        PagingStubHandler handler = new(FirstPage, Fixtures.StocksExchangesLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string> names = [];

        using (client)
        using (transport)
        {
            await foreach (StockExchange exchange in client.Stocks.EnumerateExchangesAsync(cancellationToken: Ct))
            {
                names.Add(exchange.Name);
            }
        }

        Assert.Equal(["New York Stock Exchange", "Nasdaq", "Investors Exchange"], names);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(Cursor, handler.Requests[1].ToString());
    }

    [Fact]
    public async Task ListReportsMorePagesFromTheCursoredCopy()
    {
        StubHandler handler = new(FirstPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<StockExchange> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListExchangesAsync(cancellationToken: Ct);
        }

        Assert.True(page.HasMore);
        Assert.Equal(2, page.Results.Length);
    }
}
