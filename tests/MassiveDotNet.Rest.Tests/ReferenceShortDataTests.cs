using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Short interest, short volume, and float: range-and-set filters over numeric and calendar-date
/// fields (D15), with the three <c>request_id</c> corrections of D-G7 in their fixtures. Float is
/// experimental, which the test project's project file suppresses.
/// </summary>
public sealed class ReferenceShortDataTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task ShortInterestRendersNumericAndDateFilters()
    {
        StubHandler handler = new(Fixtures.ReferenceShortInterest);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListShortInterestAsync(
                ticker: "A",
                daysToCover: RangeFilter.Gte(1.5),
                settlementDate: SetFilter.AnyOf(new LocalDate(2025, 3, 14), new LocalDate(2025, 3, 28)),
                avgDailyVolume: RangeFilter.Gt(1_000_000L),
                limit: 10,
                sort: "settlement_date.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/v1/short-interest"
                + "?ticker=A&days_to_cover.gte=1.5&settlement_date.any_of=2025-03-14,2025-03-28"
                + "&avg_daily_volume.gt=1000000&limit=10&sort=settlement_date.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task ShortInterestDeserializesTheCorrectedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceShortInterest);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<ShortInterest> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListShortInterestAsync(ticker: "A", cancellationToken: Ct);
        }

        ShortInterest row = Assert.Single(page.Results);
        Assert.Equal("A", row.Ticker);
        Assert.Equal(new LocalDate(2025, 3, 14), row.SettlementDate);
        Assert.Equal(3906231L, row.SharesShort);
        Assert.Equal(2340158L, row.AverageDailyVolume);
        Assert.Equal(1.67, row.DaysToCover);
        Assert.False(page.HasMore);
        Assert.Equal("1", page.RequestId);
    }

    [Fact]
    public async Task ShortVolumeRendersItsFilters()
    {
        StubHandler handler = new(Fixtures.ReferenceShortVolume);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListShortVolumeAsync(
                ticker: SetFilter.AnyOf("A", "AAPL"),
                date: new LocalDate(2025, 3, 25),
                shortVolumeRatio: RangeFilter.Lt(50d),
                limit: 1,
                sort: "date",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/v1/short-volume?ticker.any_of=A,AAPL&date=2025-03-25&short_volume_ratio.lt=50&limit=1&sort=date",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task ShortVolumeDeserializesTheCorrectedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceShortVolume);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<ShortVolume> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListShortVolumeAsync(ticker: "A", cancellationToken: Ct);
        }

        ShortVolume row = Assert.Single(page.Results);
        Assert.Equal("A", row.Ticker);
        Assert.Equal(new LocalDate(2025, 3, 25), row.Date);
        Assert.Equal(574084d, row.TotalVolume);
        Assert.Equal(181219d, row.TotalShortVolume);
        Assert.Equal(31.57, row.ShortVolumeRatio);
        Assert.Equal(1d, row.ExemptVolume);
        Assert.Equal(181218d, row.NonExemptVolume);
        Assert.Equal(179943L, row.NasdaqCarteretShortVolume);
        Assert.Equal(1L, row.NasdaqCarteretShortVolumeExempt);
        Assert.Equal(1L, row.NasdaqChicagoShortVolume);
        Assert.Equal(0L, row.NasdaqChicagoShortVolumeExempt);
        Assert.Equal(1275L, row.NyseShortVolume);
        Assert.Equal(0L, row.NyseShortVolumeExempt);
        Assert.Equal(0L, row.AdfShortVolume);
        Assert.Equal(0L, row.AdfShortVolumeExempt);
        Assert.Equal("1", page.RequestId);
    }

    [Fact]
    public async Task FloatRendersItsFilters()
    {
        StubHandler handler = new(Fixtures.ReferenceFloat);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListFloatAsync(
                ticker: "AAPL",
                freeFloatPercent: RangeFilter.Gte(90d),
                limit: 1,
                sort: "effective_date",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/vX/float?ticker=AAPL&free_float_percent.gte=90&limit=1&sort=effective_date",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task FloatDeserializesTheCorrectedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceFloat);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<ShareFloat> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListFloatAsync(ticker: "AAPL", cancellationToken: Ct);
        }

        ShareFloat row = Assert.Single(page.Results);
        Assert.Equal("AAPL", row.Ticker);
        Assert.Equal(new LocalDate(2025, 11, 1), row.EffectiveDate);
        Assert.Equal(15000000000L, row.FreeFloat);
        Assert.Equal(98.5, row.FreeFloatPercent);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task EnumerateWalksASinglePageOfShortInterest()
    {
        StubHandler handler = new(Fixtures.ReferenceShortInterest);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<LocalDate> dates = [];

        using (client)
        using (transport)
        {
            await foreach (ShortInterest row in client.Reference.EnumerateShortInterestAsync(ticker: "A", cancellationToken: Ct))
            {
                dates.Add(row.SettlementDate);
            }
        }

        Assert.Equal(new LocalDate(2025, 3, 14), Assert.Single(dates));
    }
}
