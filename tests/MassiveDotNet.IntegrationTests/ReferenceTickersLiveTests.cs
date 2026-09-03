using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// The tickers family against the real service: a page boundary at a small limit (D-R13), one
/// shape call for the details, the types, and related companies, and the pin that the events
/// wire spells its discriminator <c>type</c> where the description says <c>event_type</c>.
/// </summary>
public sealed class ReferenceTickersLiveTests : LiveApiTest
{
    [Fact]
    public async Task TickersCrossAPageBoundary()
    {
        // limit is per page, so five tickers at two per page is three requests. A repeated or
        // skipped symbol across the seam is what an incorrectly rebuilt cursor looks like.
        List<string> tickers = [];

        await foreach (TickerSummary ticker in Client.Reference.EnumerateTickersAsync(
            market: MarketType.Stocks,
            active: true,
            order: SortOrder.Ascending,
            sort: "ticker",
            limit: 2,
            cancellationToken: Ct))
        {
            tickers.Add(ticker.Ticker);

            if (tickers.Count == 5)
            {
                break;
            }
        }

        Assert.Equal(5, tickers.Count);
        Assert.Equal(tickers.Order(StringComparer.Ordinal), tickers);
        Assert.Equal(5, tickers.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task TickerDetailsCarryTheCompanyProfile()
    {
        TickerDetails details = await Client.Reference.GetTickerAsync("AAPL", cancellationToken: Ct);

        Assert.Equal("AAPL", details.Ticker);
        Assert.Contains("Apple", details.Name, StringComparison.Ordinal);
        Assert.Equal(new LocalDate(1980, 12, 12), details.ListDate);
        Assert.NotNull(details.Address);
        Assert.False(string.IsNullOrEmpty(details.Address.City));
        Assert.NotNull(details.Branding);
    }

    [Fact]
    public async Task TickerTypesIncludeCommonStock()
    {
        TickerType[] types = await Client.Reference.ListTickerTypesAsync(assetClass: MarketType.Stocks, cancellationToken: Ct);

        Assert.Contains(types, type => type.Code == "CS" && type.AssetClass == "stocks");
    }

    [Fact]
    public async Task TickerEventsSpellTheDiscriminatorType()
    {
        // The description declares a required event_type; the wire carried "type" on 2026-09-03,
        // so the model's EventType reads as absent (D-R10). This pins the drift so it flips the
        // day the service or the description moves; TickerChange is the working discriminator.
        TickerEvents events = await Client.Reference.GetTickerEventsAsync("META", cancellationToken: Ct);

        Assert.False(string.IsNullOrEmpty(events.Name));
        Assert.NotNull(events.Events);
        Assert.NotEmpty(events.Events);
        Assert.All(events.Events, @event => Assert.Null(@event.EventType));
        Assert.Contains(events.Events, @event => @event.TickerChange?.Ticker == "FB");
    }

    [Fact]
    public async Task RelatedCompaniesReturnTickers()
    {
        RelatedCompany[] companies = await Client.Reference.ListRelatedCompaniesAsync("AAPL", Ct);

        Assert.NotEmpty(companies);
        Assert.All(companies, company => Assert.False(string.IsNullOrEmpty(company.Ticker)));
    }
}
