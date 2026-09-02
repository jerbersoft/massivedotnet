using System.Net;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The three snapshot operations: a bare array parameter rendered comma-joined (D19), an enum in
/// the path, and one item shape of five optional nested structs shared across all three (D16).
/// </summary>
public sealed class StocksSnapshotsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] TwoTickers = ["BCAT", "BRK/B"];

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersTheTickerListCommaJoinedWithEachElementEscaped()
    {
        StubHandler handler = new(Fixtures.StocksSnapshots);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListSnapshotsAsync(tickers: TwoTickers, includeOtc: true, cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v2/snapshot/locale/us/markets/stocks/tickers?tickers=BCAT,BRK%2FB&include_otc=true",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task AnEmptyTickerListIsOmittedLikeNull()
    {
        StubHandler handler = new(Fixtures.StocksSnapshots);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListSnapshotsAsync(tickers: [], cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v2/snapshot/locale/us/markets/stocks/tickers", handler.LastRequestUri?.ToString());
    }

    [Theory]
    [InlineData(SnapshotDirection.Gainers, "gainers")]
    [InlineData(SnapshotDirection.Losers, "losers")]
    public async Task TheMoversPathCarriesTheDirection(SnapshotDirection direction, string segment)
    {
        StubHandler handler = new(Fixtures.StocksMovers);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListMoversAsync(direction, includeOtc: false, cancellationToken: Ct);
        }

        Assert.Equal(
            $"https://api.massive.com/v2/snapshot/locale/us/markets/stocks/{segment}?include_otc=false",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task BuildsTheSingleTickerPath()
    {
        StubHandler handler = new(Fixtures.StocksSnapshot);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.GetSnapshotAsync("AAPL", Ct);
        }

        Assert.Equal("https://api.massive.com/v2/snapshot/locale/us/markets/stocks/tickers/AAPL", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesTheSingleTickerSampleThroughEveryNestedStruct()
    {
        StubHandler handler = new(Fixtures.StocksSnapshot);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        TickerSnapshot snapshot;

        using (client)
        using (transport)
        {
            snapshot = await client.Stocks.GetSnapshotAsync("AAPL", Ct);
        }

        Assert.Equal("AAPL", snapshot.Ticker);
        Assert.Equal(0.98, snapshot.TodaysChange);
        Assert.Equal(0.82, snapshot.TodaysChangePercent);
        Assert.Null(snapshot.FairMarketValue);
        Assert.Equal(1605195918306274000, snapshot.UpdatedNanoseconds);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1605195918306274000), snapshot.Updated);

        Assert.NotNull(snapshot.Day);
        SnapshotDay day = snapshot.Day.Value;
        Assert.Equal(119.62, day.Open);
        Assert.Equal(120.53, day.High);
        Assert.Equal(118.81, day.Low);
        Assert.Equal(120.4229, day.Close);
        Assert.Equal(28727868d, day.Volume);
        Assert.Equal(119.725, day.VolumeWeightedAveragePrice);
        Assert.Equal("28727868.0", day.DecimalVolume);
        Assert.False(day.IsOtc);

        Assert.NotNull(snapshot.PreviousDay);
        SnapshotPreviousDay previousDay = snapshot.PreviousDay.Value;
        Assert.Equal(119.49, previousDay.Close);
        Assert.Equal(110597265d, previousDay.Volume);

        Assert.NotNull(snapshot.Minute);
        SnapshotMinute minute = snapshot.Minute.Value;
        Assert.Equal(28724441L, minute.AccumulatedVolume);
        Assert.Equal("28724441.0", minute.DecimalAccumulatedVolume);
        Assert.Equal("270796.0", minute.DecimalVolume);
        Assert.Equal(762L, minute.TransactionCount);
        Assert.Equal(120.4201, minute.Close);
        Assert.Equal(1684428720000, minute.TimestampMilliseconds);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1684428720000), minute.Timestamp);

        Assert.NotNull(snapshot.LastQuote);
        SnapshotLastQuote lastQuote = snapshot.LastQuote.Value;
        Assert.Equal(120.47, lastQuote.AskPrice);
        Assert.Equal(4, lastQuote.AskSize);
        Assert.Equal(120.46, lastQuote.BidPrice);
        Assert.Equal(8, lastQuote.BidSize);
        Assert.Equal(1605195918507251700, lastQuote.SipTimestampNanoseconds);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1605195918507251700), lastQuote.SipTimestamp);

        Assert.NotNull(snapshot.LastTrade);
        SnapshotLastTrade lastTrade = snapshot.LastTrade.Value;
        Assert.Equal("4046", lastTrade.TradeId);
        Assert.Equal(120.47, lastTrade.Price);
        Assert.Equal(236, lastTrade.Size);
        Assert.Equal("236.0", lastTrade.DecimalSize);
        Assert.Equal(10, lastTrade.ExchangeId);
        Assert.Equal([14, 41], lastTrade.Conditions);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1605195918306274000), lastTrade.SipTimestamp);
    }

    [Fact]
    public async Task DeserializesTheAllTickersSample()
    {
        StubHandler handler = new(Fixtures.StocksSnapshots);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        TickerSnapshot[] snapshots;

        using (client)
        using (transport)
        {
            snapshots = await client.Stocks.ListSnapshotsAsync(cancellationToken: Ct);
        }

        TickerSnapshot snapshot = Assert.Single(snapshots);
        Assert.Equal("BCAT", snapshot.Ticker);
        Assert.Equal(-0.601, snapshot.TodaysChangePercent);
        Assert.NotNull(snapshot.Minute);
        Assert.Equal(37216L, snapshot.Minute.Value.AccumulatedVolume);
    }

    [Fact]
    public async Task DeserializesTheMoversSample()
    {
        StubHandler handler = new(Fixtures.StocksMovers);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        TickerSnapshot[] snapshots;

        using (client)
        using (transport)
        {
            snapshots = await client.Stocks.ListMoversAsync(SnapshotDirection.Gainers, cancellationToken: Ct);
        }

        TickerSnapshot snapshot = Assert.Single(snapshots);
        Assert.Equal("PDS", snapshot.Ticker);
        Assert.Equal(1849.096, snapshot.TodaysChangePercent);
        Assert.NotNull(snapshot.LastTrade);
        Assert.Equal([63], snapshot.LastTrade.Value.Conditions);
    }

    [Fact]
    public async Task ATickerThatHasNotTradedLeavesEveryNestedStructNull()
    {
        // Each nested object is optional in the schema: a ticker with no activity in a window
        // has no bar for it. A struct member would deserialize to zeros, which reads as a
        // real bar; a nullable struct reads as absent.
        StubHandler handler = new("""
            {
              "request_id": "r",
              "status": "OK",
              "ticker": { "ticker": "AAPL", "todaysChange": 0, "todaysChangePerc": 0, "updated": 1605195918306274000 }
            }
            """);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        TickerSnapshot snapshot;

        using (client)
        using (transport)
        {
            snapshot = await client.Stocks.GetSnapshotAsync("AAPL", Ct);
        }

        Assert.Null(snapshot.Day);
        Assert.Null(snapshot.PreviousDay);
        Assert.Null(snapshot.Minute);
        Assert.Null(snapshot.LastQuote);
        Assert.Null(snapshot.LastTrade);
        Assert.Null(snapshot.FairMarketValue);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1605195918306274000), snapshot.Updated);
    }

    [Fact]
    public async Task AnAbsentUpdatedTimestampIsNull()
    {
        StubHandler handler = new("""{ "request_id": "r", "status": "OK", "ticker": { "ticker": "AAPL" } }""");
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        TickerSnapshot snapshot;

        using (client)
        using (transport)
        {
            snapshot = await client.Stocks.GetSnapshotAsync("AAPL", Ct);
        }

        Assert.Null(snapshot.UpdatedNanoseconds);
        Assert.Null(snapshot.Updated);
    }

    [Fact]
    public async Task ASuccessWithoutItsPayloadThrowsNamingTheTickerProperty()
    {
        StubHandler handler = new(Fixtures.SingularWithoutResults);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Stocks.GetSnapshotAsync("AAPL", Ct));

            Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
            Assert.Equal("r", exception.RequestId);
            Assert.Contains("carried no 'ticker' payload", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnAbsentTickersArrayIsAnEmptyArray()
    {
        StubHandler handler = new("""{ "count": 0, "status": "OK" }""");
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            Assert.Empty(await client.Stocks.ListMoversAsync(SnapshotDirection.Losers, cancellationToken: Ct));
        }
    }
}
