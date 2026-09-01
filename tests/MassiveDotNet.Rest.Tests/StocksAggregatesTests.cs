using System.Net;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

public sealed class StocksAggregatesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(StubHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task BuildsTheDocumentedRequestPath()
    {
        StubHandler handler = new(Fixtures.StocksAggregates);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListAggregatesAsync(
                "AAPL",
                1,
                AggregateTimespan.Day,
                new LocalDate(2020, 1, 1),
                new LocalDate(2020, 1, 10),
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2020-01-01/2020-01-10",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task OmitsOptionalParametersThatWereNotSupplied()
    {
        StubHandler handler = new(Fixtures.StocksAggregates);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListAggregatesAsync("AAPL", 1, AggregateTimespan.Day, "2020-01-01", "2020-01-10", cancellationToken: Ct);
        }

        Assert.DoesNotContain('?', handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task RendersOptionalParametersUsingTheirWireValues()
    {
        StubHandler handler = new(Fixtures.StocksAggregates);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListAggregatesAsync(
                "AAPL",
                5,
                AggregateTimespan.Minute,
                "2020-01-01",
                "2020-01-10",
                adjusted: false,
                sort: SortOrder.Descending,
                limit: 50_000,
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v2/aggs/ticker/AAPL/range/5/minute/2020-01-01/2020-01-10"
                + "?adjusted=false&sort=desc&limit=50000",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task EscapesTickersContainingReservedCharacters()
    {
        StubHandler handler = new(Fixtures.StocksAggregates);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListAggregatesAsync(
                "BRK/B",
                1,
                AggregateTimespan.Day,
                "2020-01-01",
                "2020-01-10",
                cancellationToken: Ct);
        }

        Assert.Contains("BRK%2FB", handler.LastRequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcceptsMillisecondTimestampsAsWellAsDates()
    {
        StubHandler handler = new(Fixtures.StocksAggregates);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListAggregatesAsync("AAPL", 1, AggregateTimespan.Day, 1578114000000L, "2020-01-10", cancellationToken: Ct);
        }

        Assert.Contains("/1578114000000/2020-01-10", handler.LastRequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeserializesTheDocumentedSampleResponse()
    {
        StubHandler handler = new(Fixtures.StocksAggregates);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        Agg[] bars;

        using (client)
        using (transport)
        {
            bars = await client.Stocks.ListAggregatesAsync(
                "AAPL", 1, AggregateTimespan.Day, "2020-01-01", "2020-01-10", cancellationToken: Ct);
        }

        Assert.Equal(2, bars.Length);

        Agg first = bars[0];
        Assert.Equal(74.06, first.Open);
        Assert.Equal(75.15, first.High);
        Assert.Equal(73.7975, first.Low);
        Assert.Equal(75.0875, first.Close);
        Assert.Equal(135647456, first.Volume);
        Assert.Equal(74.6099, first.VolumeWeightedAveragePrice);
        Assert.Equal(1, first.TransactionCount);
        Assert.Equal(1577941200000, first.TimestampMilliseconds);
    }

    [Fact]
    public async Task LeavesOtcFalseWhenTheServerOmitsTheField()
    {
        StubHandler handler = new(Fixtures.StocksAggregates);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        Agg[] bars;

        using (client)
        using (transport)
        {
            bars = await client.Stocks.ListAggregatesAsync(
                "AAPL", 1, AggregateTimespan.Day, "2020-01-01", "2020-01-10", cancellationToken: Ct);
        }

        Assert.False(bars[0].IsOtc);
    }

    [Fact]
    public async Task ComputesTimestampFromTheRawMilliseconds()
    {
        StubHandler handler = new(Fixtures.StocksAggregates);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        Agg[] bars;

        using (client)
        using (transport)
        {
            bars = await client.Stocks.ListAggregatesAsync(
                "AAPL", 1, AggregateTimespan.Day, "2020-01-01", "2020-01-10", cancellationToken: Ct);
        }

        Assert.Equal(
            Instant.FromUnixTimeMilliseconds(1577941200000),
            bars[0].Timestamp);
    }

    [Fact]
    public async Task ReturnsAnEmptyArrayWhenTheServerSendsNoResults()
    {
        StubHandler handler = new("""{"status":"OK","request_id":"abc","resultsCount":0,"queryCount":0,"adjusted":true,"ticker":"AAPL"}""");
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        Agg[] bars;

        using (client)
        using (transport)
        {
            bars = await client.Stocks.ListAggregatesAsync(
                "AAPL", 1, AggregateTimespan.Day, "2020-01-01", "2020-01-10", cancellationToken: Ct);
        }

        Assert.Empty(bars);
    }

    [Fact]
    public async Task RejectsABlankTickerBeforeIssuingARequest()
    {
        StubHandler handler = new(Fixtures.StocksAggregates);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                client.Stocks.ListAggregatesAsync("  ", 1, AggregateTimespan.Day, "2020-01-01", "2020-01-10", cancellationToken: Ct));
        }

        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task SurfacesTheServerErrorMessageAndRequestId()
    {
        StubHandler handler = new(HttpStatusCode.Unauthorized, Fixtures.Unauthorized);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassiveApiException exception;

        using (client)
        using (transport)
        {
            exception = await Assert.ThrowsAsync<MassiveApiException>(() =>
                client.Stocks.ListAggregatesAsync("AAPL", 1, AggregateTimespan.Day, "2020-01-01", "2020-01-10", cancellationToken: Ct));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Equal("Unknown API Key", exception.Message);
        Assert.Equal("b1a2c3d4e5f60718293a4b5c6d7e8f90", exception.RequestId);
    }

    [Fact]
    public async Task RaisesATypedExceptionCarryingRetryAfterOnRateLimit()
    {
        StubHandler handler = new(HttpStatusCode.TooManyRequests, """{"status":"ERROR","error":"Too many requests"}""")
        {
            RetryAfter = Duration.FromSeconds(30),
        };

        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassiveRateLimitExceededException exception;

        using (client)
        using (transport)
        {
            exception = await Assert.ThrowsAsync<MassiveRateLimitExceededException>(() =>
                client.Stocks.ListAggregatesAsync("AAPL", 1, AggregateTimespan.Day, "2020-01-01", "2020-01-10", cancellationToken: Ct));
        }

        Assert.Equal(Duration.FromSeconds(30), exception.RetryAfter);
    }

    [Fact]
    public async Task SendsTheApiKeyAsABearerTokenByDefault()
    {
        StubHandler handler = new(Fixtures.StocksAggregates);
        MassiveClientOptions options = new() { ApiKey = "test-key" };

        using MassiveHttpTransport transport = new(options);

        // Exercise the handler chain directly: the options-based constructor owns its pipeline.
        HttpClient probe = new(new MassiveAuthenticationHandler("test-key", options.AuthenticationScheme)
        {
            InnerHandler = handler,
        })
        {
            BaseAddress = MassiveEndpoints.Production,
        };

        using (probe)
        {
            await probe.GetAsync(new Uri("/v2/aggs/ticker/AAPL/range/1/day/2020-01-01/2020-01-10", UriKind.Relative), Ct);
        }

        Assert.Equal("Bearer test-key", handler.LastAuthorization);
    }
}
