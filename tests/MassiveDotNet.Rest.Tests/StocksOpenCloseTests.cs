using System.Net;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The first body-object result: no envelope, the model deserialized straight from the body, with
/// the envelope-level <c>status</c> as one of its members (D-S5).
/// </summary>
public sealed class StocksOpenCloseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly LocalDate Session = new(2023, 1, 9);

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task BuildsTheDocumentedRequestPath()
    {
        StubHandler handler = new(Fixtures.StocksOpenClose);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.GetDailyOpenCloseAsync("AAPL", Session, adjusted: true, cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v1/open-close/AAPL/2023-01-09?adjusted=true", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.StocksOpenClose);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        DailyOpenClose day;

        using (client)
        using (transport)
        {
            day = await client.Stocks.GetDailyOpenCloseAsync("AAPL", Session, cancellationToken: Ct);
        }

        Assert.Equal("AAPL", day.Symbol);
        Assert.Equal(Session, day.From);
        Assert.Equal(324.66, day.Open);
        Assert.Equal(326.2, day.High);
        Assert.Equal(322.3, day.Low);
        Assert.Equal(325.12, day.Close);
        Assert.Equal(26122646, day.Volume);
        Assert.Equal(324.5, day.PreMarket);
        Assert.Equal(322.1, day.AfterHours);
        Assert.Equal("OK", day.Status);
        Assert.False(day.IsOtc);
    }

    [Fact]
    public async Task ANullBodyThrowsWithStatusOkAndNoRequestId()
    {
        StubHandler handler = new("null");
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Stocks.GetDailyOpenCloseAsync("AAPL", Session, cancellationToken: Ct));

            Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
            Assert.Null(exception.RequestId);
            Assert.Contains("carried no payload", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnErrorStatusSurfacesAsAnException()
    {
        // The live probe found that a date with no session is a 404. The singular path must
        // let the transport's error handling run before it ever looks for a payload.
        StubHandler handler = new(HttpStatusCode.NotFound, """{"status":"NOT_FOUND","request_id":"r","message":"Data not found."}""");
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Stocks.GetDailyOpenCloseAsync("AAPL", new LocalDate(2023, 1, 7), cancellationToken: Ct));

            Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
            Assert.Equal("r", exception.RequestId);
        }
    }
}
