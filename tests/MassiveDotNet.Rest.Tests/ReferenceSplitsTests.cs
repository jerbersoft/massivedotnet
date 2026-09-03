using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The v3 splits: a lexical ticker range, a calendar-date range, and a boolean, on a model
/// separate from the stocks/v1 <c>Split</c> (D-R7).
/// </summary>
public sealed class ReferenceSplitsTests
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
        StubHandler handler = new(Fixtures.ReferenceSplits);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListSplitsAsync(
                ticker: RangeFilter.Between("A", "B"),
                executionDate: RangeFilter.Gte(new LocalDate(2020, 1, 1)),
                reverseSplit: false,
                order: SortOrder.Ascending,
                limit: 2,
                sort: "execution_date",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v3/reference/splits"
                + "?ticker.gte=A&ticker.lte=B&execution_date.gte=2020-01-01&reverse_split=false&order=asc&limit=2&sort=execution_date",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceSplits);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<ReferenceSplit> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListSplitsAsync(ticker: "AAPL", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);

        ReferenceSplit split = page.Results[0];
        Assert.Equal("AAPL", split.Ticker);
        Assert.Equal("E36416cce743c3964c5da63e1ef1626c0aece30fb47302eea5a49c0055c04e8d0", split.Id);
        Assert.Equal(new LocalDate(2020, 8, 31), split.ExecutionDate);
        Assert.Equal(1d, split.SplitFrom);
        Assert.Equal(4d, split.SplitTo);

        Assert.Equal(new LocalDate(2005, 2, 28), page.Results[1].ExecutionDate);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task EnumerateYieldsTheFirstPageInOrder()
    {
        PagingStubHandler handler = new(Fixtures.ReferenceSplits);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<double> ratios = [];

        using (client)
        using (transport)
        {
            await foreach (ReferenceSplit split in client.Reference.EnumerateSplitsAsync(ticker: "AAPL", cancellationToken: Ct))
            {
                ratios.Add(split.SplitTo / split.SplitFrom);

                if (ratios.Count == 2)
                {
                    break;
                }
            }
        }

        Assert.Equal([4d, 2d], ratios);
        Assert.Single(handler.Requests);
    }
}
