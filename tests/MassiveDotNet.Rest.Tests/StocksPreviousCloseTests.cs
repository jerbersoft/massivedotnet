using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The previous close endpoint: an array of one bar, returned as the array the description
/// declares rather than unwrapped.
/// </summary>
public sealed class StocksPreviousCloseTests
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
        StubHandler handler = new(Fixtures.StocksPreviousClose);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListPreviousCloseAsync("AAPL", adjusted: false, cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v2/aggs/ticker/AAPL/prev?adjusted=false", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.StocksPreviousClose);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        PreviousCloseBar[] bars;

        using (client)
        using (transport)
        {
            bars = await client.Stocks.ListPreviousCloseAsync("AAPL", cancellationToken: Ct);
        }

        PreviousCloseBar bar = Assert.Single(bars);
        Assert.Equal(115.55, bar.Open);
        Assert.Equal(117.59, bar.High);
        Assert.Equal(114.13, bar.Low);
        Assert.Equal(115.97, bar.Close);
        Assert.Equal(131704427d, bar.Volume);
        Assert.Equal(116.3058, bar.VolumeWeightedAveragePrice);
        Assert.Null(bar.TransactionCount);
        Assert.Equal(1605042000000, bar.TimestampMilliseconds);
        Assert.Equal(Instant.FromUtc(2020, 11, 10, 21, 0), bar.Timestamp);
    }

    [Fact]
    public void RejectsABlankTickerBeforeIssuingARequest()
    {
        StubHandler handler = new(Fixtures.StocksPreviousClose);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            Assert.Throws<ArgumentException>(() => { _ = client.Stocks.ListPreviousCloseAsync("  ", cancellationToken: Ct); });
        }

        Assert.Null(handler.LastRequestUri);
    }
}
