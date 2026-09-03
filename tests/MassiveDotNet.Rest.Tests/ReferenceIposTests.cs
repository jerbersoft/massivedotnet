using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Two revisions of one route (D-R5): the served <c>vX</c> one with plain parameters and a
/// calendar-date range, and the declared <c>v1</c> one with comparator groups and a filter that
/// takes a date or a nanosecond timestamp (D20). The <c>vX</c> methods are experimental, which
/// the test project's project file suppresses.
/// </summary>
public sealed class ReferenceIposTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task TheServedRevisionRendersEveryParameterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceIpos);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListIposAsync(
                ticker: "RAPP",
                usCode: "75383L102",
                isin: "US75383L1026",
                listingDate: RangeFilter.Between(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 31)),
                ipoStatus: "history",
                order: SortOrder.Descending,
                limit: 1,
                sort: "listing_date",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/vX/reference/ipos"
                + "?ticker=RAPP&us_code=75383L102&isin=US75383L1026"
                + "&listing_date.gte=2024-01-01&listing_date.lte=2024-12-31"
                + "&ipo_status=history&order=desc&limit=1&sort=listing_date",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task TheServedRevisionDeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceIpos);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Ipo> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListIposAsync(cancellationToken: Ct);
        }

        Ipo ipo = Assert.Single(page.Results);
        Assert.Equal("RAPP", ipo.Ticker);
        Assert.Equal("Rapport Therapeutics Inc.", ipo.IssuerName);
        Assert.Equal("history", ipo.IpoStatus);
        Assert.Equal("CS", ipo.SecurityType);
        Assert.Equal(new LocalDate(2024, 6, 27), ipo.LastUpdated);
        Assert.Equal(new LocalDate(2024, 6, 1), ipo.AnnouncedDate);
        Assert.Equal(new LocalDate(2024, 6, 7), ipo.ListingDate);
        Assert.Equal("US75383L1026", ipo.Isin);
        Assert.Equal("75383L102", ipo.UsCode);
        Assert.Equal("USD", ipo.CurrencyCode);
        Assert.Equal("XNAS", ipo.PrimaryExchange);
        Assert.Equal("Ordinary Shares", ipo.SecurityDescription);
        Assert.Equal(17d, ipo.FinalIssuePrice);
        Assert.Equal(17d, ipo.LowestOfferPrice);
        Assert.Equal(17d, ipo.HighestOfferPrice);
        Assert.Equal(1000000d, ipo.MinSharesOffered);
        Assert.Equal(8000000d, ipo.MaxSharesOffered);
        Assert.Equal(35376457d, ipo.SharesOutstanding);
        Assert.Equal(136000000d, ipo.TotalOfferSize);
        Assert.Equal(100d, ipo.LotSize);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task TheDeclaredRevisionRendersItsComparatorGroups()
    {
        StubHandler handler = new(Fixtures.ReferenceIposV1);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListIposV1Async(
                ticker: "RAPP",
                usCode: RangeFilter.Gte("75383L102"),
                isin: SetFilter.AnyOf("US75383L1026", "US0378331005"),
                listingDate: RangeFilter.Between(
                    DateOrNanoseconds.FromInstant(Instant.FromUtc(2024, 6, 1, 0, 0)),
                    DateOrNanoseconds.FromDate(new LocalDate(2024, 12, 31))),
                ipoStatus: SetFilter.AnyOf("new", "pending"),
                limit: 1,
                sort: "listing_date.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v1/reference/ipos"
                + "?ticker=RAPP&us_code.gte=75383L102&isin.any_of=US75383L1026,US0378331005"
                + "&listing_date.gte=1717200000000000000&listing_date.lte=2024-12-31"
                + "&ipo_status.any_of=new,pending&limit=1&sort=listing_date.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task TheDeclaredRevisionDeserializesTheCorrectedSampleWithEpochDates()
    {
        StubHandler handler = new(Fixtures.ReferenceIposV1);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<IpoV1> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListIposV1Async(cancellationToken: Ct);
        }

        IpoV1 ipo = Assert.Single(page.Results);
        Assert.Equal("RAPP", ipo.Ticker);
        Assert.Equal("history", ipo.IpoStatus);
        Assert.Equal(1717200000000000000L, ipo.AnnouncedDateEpoch);
        Assert.Equal(1719446400000000000L, ipo.LastUpdatedEpoch);
        Assert.Equal(1717718400000000000L, ipo.ListingDateEpoch);
        Assert.Equal(100L, ipo.LotSize);
        Assert.Equal(8000000L, ipo.MaxSharesOffered);
        Assert.Equal(35376457L, ipo.SharesOutstanding);
        Assert.Equal(17d, ipo.FinalIssuePrice);
        Assert.False(page.HasMore);
    }
}
