using System.Net;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Both IPO routes against the real service: one shape call on the served <c>vX</c> revision,
/// and the pin that the declared <c>v1</c> revision answers 404 (D21, D-R13).
/// </summary>
public sealed class ReferenceIposLiveTests : LiveApiTest
{
    private static readonly LocalDate WindowStart = new(2024, 1, 1);
    private static readonly LocalDate WindowEnd = new(2024, 12, 31);

    [Fact]
    public async Task TheServedRevisionHonoursAStatusAndAListingWindow()
    {
        MassivePage<Ipo> page = await Client.Reference.ListIposAsync(
            ipoStatus: "history",
            listingDate: RangeFilter.Between(WindowStart, WindowEnd),
            limit: 2,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (Ipo ipo in page.Results)
        {
            Assert.Equal("history", ipo.IpoStatus);
            Assert.NotNull(ipo.ListingDate);
            Assert.InRange(ipo.ListingDate.Value, WindowStart, WindowEnd);
            Assert.False(string.IsNullOrEmpty(ipo.IssuerName));
        }
    }

    [Fact]
    public async Task TheDeclaredRevisionAnswersNotFound()
    {
        // The description declares GET /v1/reference/ipos beside the vX route, so rule 1 keeps
        // it mapped, and D21 keeps it that way until the description drops it: the service
        // answered a plain-text 404 on 2026-09-03. This pins the drift so it flips the day the
        // route is served, at which point D26 says the plain name moves here.
        MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(() =>
            Client.Reference.ListIposV1Async(limit: 1, cancellationToken: Ct));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }
}
