using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The v3 quotes feed: the same nanosecond filter as trades (D20), over NBBO structs with a bid
/// and an ask side.
/// </summary>
public sealed class StocksQuotesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersTheNanosecondRangeAndTheOrder()
    {
        StubHandler handler = new(Fixtures.StocksQuotes);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListQuotesAsync(
                "AAPL",
                timestamp: RangeFilter.Gte(DateOrNanoseconds.FromUnixNanoseconds(1517562000065700400)).Lt(DateOrNanoseconds.FromDate(new LocalDate(2018, 2, 3))),
                order: SortOrder.Descending,
                limit: 2,
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v3/quotes/AAPL?timestamp.gte=1517562000065700400&timestamp.lt=2018-02-03&order=desc&limit=2",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSampleToTheNanosecond()
    {
        StubHandler handler = new(Fixtures.StocksQuotes);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Quote> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListQuotesAsync("AAPL", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);

        Quote first = page.Results[0];
        Assert.Equal(102.7, first.BidPrice);
        Assert.Equal(60d, first.BidSize);
        Assert.Equal(11, first.BidExchangeId);
        Assert.Equal(0d, first.AskPrice);
        Assert.Equal(0d, first.AskSize);
        Assert.Equal(0, first.AskExchangeId);
        Assert.Equal(2060L, first.SequenceNumber);
        Assert.Equal(3, first.Tape);
        Assert.Equal([1], first.Conditions!);
        Assert.Null(first.Indicators);
        Assert.Null(first.TrfTimestampNanoseconds);
        Assert.Null(first.TrfTimestamp);

        Assert.Equal(1517562000065700400, first.SipTimestampNanoseconds);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1517562000065700400), first.SipTimestamp);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1517562000065321200), first.ParticipantTimestamp);

        Assert.Equal(170d, page.Results[1].BidPrice);
    }

    [Fact]
    public async Task ReportsNoFurtherPagesFromAPageWithoutACursor()
    {
        StubHandler handler = new(Fixtures.StocksQuotesLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Quote> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListQuotesAsync("AAPL", cancellationToken: Ct);
        }

        Assert.False(page.HasMore);
        Assert.Equal(2062L, Assert.Single(page.Results).SequenceNumber);
    }

    [Fact]
    public async Task EnumerateWalksTheSinglePage()
    {
        // The published sample advertises a cursor; the stub serves the same body again, so the
        // traversal is bounded here to prove Enumerate starts from the same URI List would.
        PagingStubHandler handler = new(Fixtures.StocksQuotes);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<long> sequenceNumbers = [];

        using (client)
        using (transport)
        {
            await foreach (Quote quote in client.Stocks.EnumerateQuotesAsync("AAPL", limit: 2, cancellationToken: Ct))
            {
                sequenceNumbers.Add(quote.SequenceNumber);

                if (sequenceNumbers.Count == 2)
                {
                    break;
                }
            }
        }

        Assert.Equal([2060L, 2061L], sequenceNumbers);
        Assert.Equal("https://api.massive.com/v3/quotes/AAPL?limit=2", handler.Requests[0].ToString());
        Assert.Single(handler.Requests);
    }
}
