using System.Text.Json;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The v3 trades feed: the first operation whose timestamp filter takes a nanosecond count (D20),
/// and a paginated array of tick-level structs.
/// </summary>
public sealed class StocksTradesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // 2018-02-02T09:00:00.016036600Z, the sample's first SIP timestamp, exactly.
    private static readonly Instant FirstSipTimestamp = NodaConstants.UnixEpoch + Duration.FromNanoseconds(1517562000016036600);

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersAnInstantBoundAsNineteenDigitsAndADateBoundAsIso()
    {
        StubHandler handler = new(Fixtures.StocksTrades);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListTradesAsync(
                "AAPL",
                timestamp: RangeFilter.Between(
                    DateOrNanoseconds.FromInstant(FirstSipTimestamp),
                    DateOrNanoseconds.FromDate(new LocalDate(2018, 2, 3))),
                order: SortOrder.Ascending,
                limit: 2,
                sort: "timestamp",
                cancellationToken: Ct);
        }

        // Nineteen digits: a millisecond render would have been thirteen, and would have asked
        // for a moment in 1970 that the service answers with an empty page rather than an error.
        Assert.Equal(
            "https://api.massive.com/v3/trades/AAPL?timestamp.gte=1517562000016036600&timestamp.lte=2018-02-03&order=asc&limit=2&sort=timestamp",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task ADateEqualityRendersThePlainField()
    {
        StubHandler handler = new(Fixtures.StocksTrades);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListTradesAsync("AAPL", timestamp: DateOrNanoseconds.FromDate(new LocalDate(2018, 2, 2)), cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v3/trades/AAPL?timestamp=2018-02-02", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSampleToTheNanosecond()
    {
        StubHandler handler = new(Fixtures.StocksTrades);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Trade> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListTradesAsync("AAPL", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);
        Assert.Equal("a47d1beb8c11b6ae897ab76cdbbf35a3", page.RequestId);

        Trade first = page.Results[0];
        Assert.Equal("1", first.TradeId);
        Assert.Equal(171.55, first.Price);
        Assert.Equal(100d, first.Size);
        Assert.Equal("100.0", first.DecimalSize);
        Assert.Equal(11, first.ExchangeId);
        Assert.Equal(1063L, first.SequenceNumber);
        Assert.Equal(3, first.Tape);
        Assert.Equal([12, 41], first.Conditions!);
        Assert.Null(first.CorrectionIndicator);
        Assert.Null(first.TrfId);
        Assert.Null(first.TrfTimestampNanoseconds);
        Assert.Null(first.TrfTimestamp);

        Assert.Equal(1517562000016036600, first.SipTimestampNanoseconds);
        Assert.Equal(FirstSipTimestamp, first.SipTimestamp);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1517562000015577000), first.ParticipantTimestamp);

        Assert.Equal("2", page.Results[1].TradeId);
        Assert.Equal(1517562000016038100, page.Results[1].SipTimestampNanoseconds);
    }

    [Fact]
    public async Task ReportsNoFurtherPagesFromAPageWithoutACursor()
    {
        StubHandler handler = new(Fixtures.StocksTradesLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Trade> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListTradesAsync("AAPL", cancellationToken: Ct);
        }

        Assert.False(page.HasMore);
        Assert.Equal("3", Assert.Single(page.Results).TradeId);
    }

    [Fact]
    public async Task EnumerateCrossesThePageBoundaryFollowingTheCursorVerbatim()
    {
        PagingStubHandler handler = new(Fixtures.StocksTrades, Fixtures.StocksTradesLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string> ids = [];

        using (client)
        using (transport)
        {
            await foreach (Trade trade in client.Stocks.EnumerateTradesAsync("AAPL", limit: 2, cancellationToken: Ct))
            {
                ids.Add(trade.TradeId);
            }
        }

        Assert.Equal(["1", "2", "3"], ids);
        Assert.Equal(2, handler.Requests.Count);

        // The cursor is followed verbatim (D14): the second request must match the fixture's own
        // next_url exactly.
        using JsonDocument firstPage = JsonDocument.Parse(Fixtures.StocksTrades);
        Uri nextUrl = new(firstPage.RootElement.GetProperty("next_url").GetString()!);

        Assert.Equal(nextUrl.PathAndQuery, handler.Requests[1].PathAndQuery);
    }

    [Fact]
    public void RejectsABlankTickerBeforeIssuingARequest()
    {
        StubHandler handler = new(Fixtures.StocksTrades);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            Assert.Throws<ArgumentException>(() => { _ = client.Stocks.ListTradesAsync("  ", cancellationToken: Ct); });
            Assert.Throws<ArgumentException>(() => client.Stocks.EnumerateTradesAsync("  ", cancellationToken: Ct));
        }

        Assert.Null(handler.LastRequestUri);
    }
}
