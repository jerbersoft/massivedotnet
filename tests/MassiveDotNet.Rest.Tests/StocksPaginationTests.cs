using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

public sealed class StocksPaginationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>One aggregate bar, with a distinguishing close price.</summary>
    private static string Bar(double close) =>
        $$"""{"c":{{close}},"h":1,"l":1,"o":1,"t":1577941200000,"v":1}""";

    private static string Page(double close, string? nextUrl) =>
        nextUrl is null
            ? $$"""{"ticker":"AAPL","results":[{{Bar(close)}}],"status":"OK"}"""
            : $$"""{"ticker":"AAPL","next_url":"{{nextUrl}}","results":[{{Bar(close)}}],"status":"OK"}""";

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(PagingStubHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task EnumerateWalksEveryPageOfBars()
    {
        PagingStubHandler handler = new(
            Page(1.0, "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/1/2?cursor=a"),
            Page(2.0, "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/1/2?cursor=b"),
            Page(3.0, nextUrl: null));

        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<double> closes = [];

        using (client)
        using (transport)
        {
            await foreach (Agg bar in client.Stocks.EnumerateAggregatesAsync(
                "AAPL", 1, AggregateTimespan.Day, "2020-01-01", "2020-01-10", cancellationToken: Ct))
            {
                closes.Add(bar.Close);
            }
        }

        Assert.Equal([1.0, 2.0, 3.0], closes);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task EnumerateStartsFromTheSameUriThatListWouldRequest()
    {
        PagingStubHandler handler = new(Page(1.0, nextUrl: null));
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await foreach (Agg _ in client.Stocks.EnumerateAggregatesAsync(
                "AAPL", 1, AggregateTimespan.Day, "2020-01-01", "2020-01-10",
                sort: SortOrder.Ascending, limit: 5, cancellationToken: Ct))
            {
            }
        }

        Assert.Equal(
            "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2020-01-01/2020-01-10?sort=asc&limit=5",
            handler.Requests[0].ToString());
    }

    [Fact]
    public void EnumerateRejectsABlankTickerBeforeIssuingARequest()
    {
        PagingStubHandler handler = new(Page(1.0, nextUrl: null));
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            // Not awaited: the guard must fire on the call, not on the first iteration.
            Assert.Throws<ArgumentException>(() => client.Stocks.EnumerateAggregatesAsync(
                "  ", 1, AggregateTimespan.Day, "2020-01-01", "2020-01-10", cancellationToken: Ct));
        }

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ListReportsNoFurtherPagesWhenTheServerOmitsTheCursor()
    {
        PagingStubHandler handler = new(Page(1.0, nextUrl: null));
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Agg> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListAggregatesAsync(
                "AAPL", 1, AggregateTimespan.Day, "2020-01-01", "2020-01-10", cancellationToken: Ct);
        }

        Assert.False(page.HasMore);
        Assert.Single(page.Results);
        Assert.Single(handler.Requests);
    }
}
