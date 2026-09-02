using System.Net;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The first unpaginated singular result: one struct under <c>results</c>, returned as itself
/// rather than as an array of one, with nanosecond timestamps exposed as instants (D-S4).
/// </summary>
public sealed class StocksLastTradeTests
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
        StubHandler handler = new(Fixtures.StocksLastTrade);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.GetLastTradeAsync("AAPL", Ct);
        }

        Assert.Equal("https://api.massive.com/v2/last/trade/AAPL", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.StocksLastTrade);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        LastTrade trade;

        using (client)
        using (transport)
        {
            trade = await client.Stocks.GetLastTradeAsync("AAPL", Ct);
        }

        Assert.Equal("AAPL", trade.Ticker);
        Assert.Equal(129.8473, trade.Price);
        Assert.Equal(25d, trade.Size);
        Assert.Equal("25.0", trade.DecimalSize);
        Assert.Equal(3135876, trade.SequenceNumber);
        Assert.Equal("118749", trade.TradeId);
        Assert.Equal(4, trade.ExchangeId);
        Assert.Equal(202, trade.TrfId);
        Assert.Equal(3, trade.Tape);
        Assert.Equal([37], trade.Conditions!);
        Assert.Null(trade.CorrectionIndicator);

        // Nanosecond precision survives the conversion: 1617901342969834000 ns is 2021-04-08
        // 16:22:22.969834000 UTC, which FromUnixTimeTicks would have rounded to the 100 ns tick.
        Assert.Equal(1617901342969834000, trade.SipTimestampNanoseconds);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1617901342969834000), trade.SipTimestamp);
        Assert.Equal(new LocalDate(2021, 4, 8), trade.SipTimestamp.InUtc().Date);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1617901342968000000), trade.ParticipantTimestamp);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1617901342969796400), trade.TrfTimestamp);
    }

    [Fact]
    public async Task AnAbsentTrfTimestampIsNull()
    {
        string body = Fixtures.StocksLastTrade.Replace("\"f\": 1617901342969796400,", "", StringComparison.Ordinal);
        StubHandler handler = new(body);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        LastTrade trade;

        using (client)
        using (transport)
        {
            trade = await client.Stocks.GetLastTradeAsync("AAPL", Ct);
        }

        Assert.Null(trade.TrfTimestampNanoseconds);
        Assert.Null(trade.TrfTimestamp);
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
                () => client.Stocks.GetLastTradeAsync("AAPL", Ct));

            Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
            Assert.Equal("r", exception.RequestId);
            Assert.Contains("/v2/last/trade/AAPL", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ANullBodyThrowsWithoutARequestId()
    {
        StubHandler handler = new("null");
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Stocks.GetLastTradeAsync("AAPL", Ct));

            Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
            Assert.Null(exception.RequestId);
        }
    }

    [Fact]
    public void RejectsABlankTickerBeforeIssuingARequest()
    {
        StubHandler handler = new(Fixtures.StocksLastTrade);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            // Not awaited, and deliberately not ThrowsAsync: the guard must fire on the call
            // itself, which a faulted task would also satisfy.
            Assert.Throws<ArgumentException>(() => { _ = client.Stocks.GetLastTradeAsync("  ", Ct); });
        }

        Assert.Null(handler.LastRequestUri);
    }
}
