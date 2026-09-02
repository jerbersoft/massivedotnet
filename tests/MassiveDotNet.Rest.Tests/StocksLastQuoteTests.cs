using System.Net;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The last NBBO quote: one struct under <c>results</c> whose v2 single-letter keys the map names,
/// and whose timestamps the description declares as bare integers.
/// </summary>
public sealed class StocksLastQuoteTests
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
        StubHandler handler = new(Fixtures.StocksLastQuote);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.GetLastQuoteAsync("AAPL", Ct);
        }

        Assert.Equal("https://api.massive.com/v2/last/nbbo/AAPL", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.StocksLastQuote);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        LastQuote quote;

        using (client)
        using (transport)
        {
            quote = await client.Stocks.GetLastQuoteAsync("AAPL", Ct);
        }

        Assert.Equal("AAPL", quote.Ticker);
        Assert.Equal(127.98, quote.AskPrice);
        Assert.Equal(7, quote.AskSize);
        Assert.Equal(19, quote.AskExchangeId);
        Assert.Equal(127.96, quote.BidPrice);
        Assert.Equal(1, quote.BidSize);
        Assert.Equal(11, quote.BidExchangeId);
        Assert.Equal(83480742L, quote.SequenceNumber);
        Assert.Equal(3, quote.Tape);
        Assert.Null(quote.Conditions);
        Assert.Null(quote.Indicators);

        // Nanosecond precision survives: 1617827221349730300 ns is 2021-04-07 20:27:01.349730300
        // UTC, whose last two digits FromUnixTimeTicks would have dropped.
        Assert.Equal(1617827221349730300, quote.SipTimestampNanoseconds);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1617827221349730300), quote.SipTimestamp);
        Assert.Equal(new LocalDate(2021, 4, 7), quote.SipTimestamp.InUtc().Date);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1617827221349366000), quote.ParticipantTimestamp);
        Assert.Null(quote.TrfTimestampNanoseconds);
        Assert.Null(quote.TrfTimestamp);
    }

    [Fact]
    public async Task ASuccessWithoutItsPayloadThrowsWithStatusOkAndTheRequestId()
    {
        StubHandler handler = new(Fixtures.SingularWithoutResults);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Stocks.GetLastQuoteAsync("AAPL", Ct));

            Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
            Assert.Equal("r", exception.RequestId);
            Assert.Contains("/v2/last/nbbo/AAPL", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RejectsABlankTickerBeforeIssuingARequest()
    {
        StubHandler handler = new(Fixtures.StocksLastQuote);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            Assert.Throws<ArgumentException>(() => { _ = client.Stocks.GetLastQuoteAsync("  ", Ct); });
        }

        Assert.Null(handler.LastRequestUri);
    }
}
