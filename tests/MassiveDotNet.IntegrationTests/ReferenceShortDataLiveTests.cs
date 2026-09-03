using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>One shape call each for short interest, short volume, and float (D-R13).</summary>
public sealed class ReferenceShortDataLiveTests : LiveApiTest
{
    [Fact]
    public async Task ShortInterestReturnsASettledReport()
    {
        MassivePage<ShortInterest> page = await Client.Reference.ListShortInterestAsync(ticker: "AAPL", limit: 1, cancellationToken: Ct);

        ShortInterest row = Assert.Single(page.Results);
        Assert.Equal("AAPL", row.Ticker);
        Assert.True(row.AverageDailyVolume > 0);
        Assert.True(row.DaysToCover > 0);
    }

    [Fact]
    public async Task ShortVolumeReturnsADay()
    {
        MassivePage<ShortVolume> page = await Client.Reference.ListShortVolumeAsync(ticker: "AAPL", limit: 1, cancellationToken: Ct);

        ShortVolume row = Assert.Single(page.Results);
        Assert.Equal("AAPL", row.Ticker);
        Assert.True(row.TotalVolume > 0);
    }

    [Fact]
    public async Task FloatReturnsTheFreeFloat()
    {
        MassivePage<ShareFloat> page = await Client.Reference.ListFloatAsync(ticker: "AAPL", limit: 1, cancellationToken: Ct);

        ShareFloat row = Assert.Single(page.Results);
        Assert.Equal("AAPL", row.Ticker);
        Assert.True(row.FreeFloat > 0);
        Assert.NotNull(row.EffectiveDate);
    }
}
