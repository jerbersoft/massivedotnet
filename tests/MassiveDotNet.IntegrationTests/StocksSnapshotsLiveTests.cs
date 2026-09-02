using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// The snapshot operations against the real service, and the proof D19 deferred to them: only a
/// live call can tell the comma-joined ticker list from the repeated-key form the OpenAPI default
/// implies, because the service reads one ticker from the latter and every ticker from the former.
/// </summary>
public sealed class StocksSnapshotsLiveTests : LiveApiTest
{
    private static readonly string[] Tickers = ["AAPL", "MSFT"];

    [Fact]
    public async Task TwoTickersReturnExactlyTwoSnapshots()
    {
        TickerSnapshot[] snapshots = await Client.Stocks.ListSnapshotsAsync(tickers: Tickers, cancellationToken: Ct);

        Assert.Equal(2, snapshots.Length);
        Assert.Equal(Tickers, snapshots.Select(snapshot => snapshot.Ticker!).Order());
    }

    [Fact]
    public async Task OneTickerReturnsItsSnapshot()
    {
        TickerSnapshot snapshot = await Client.Stocks.GetSnapshotAsync("AAPL", Ct);

        Assert.Equal("AAPL", snapshot.Ticker);
        Assert.NotNull(snapshot.PreviousDay);
        Assert.True(snapshot.PreviousDay.Value.Close > 0);
        Assert.NotNull(snapshot.Updated);
    }

    [Fact]
    public async Task MoversReturnSnapshots()
    {
        TickerSnapshot[] gainers = await Client.Stocks.ListMoversAsync(SnapshotDirection.Gainers, cancellationToken: Ct);

        Assert.NotEmpty(gainers);
        Assert.All(gainers, snapshot => Assert.False(string.IsNullOrEmpty(snapshot.Ticker)));
    }
}
