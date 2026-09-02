using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Exercises the first nested-object endpoint against the live service.
/// </summary>
/// <remarks>
/// The fixture proves the SDK reads the published sample. This is what proves the service still
/// sends a publisher on every article, that its timestamps parse, and that the calendar-date form
/// of <c>published_utc</c> is accepted as a filter, none of which the OpenAPI description states.
/// </remarks>
public sealed class ReferenceNewsLiveTests : LiveApiTest
{
    // A fixed historical window, so the assertions do not depend on when the suite is run.
    private static readonly LocalDate WindowStart = new(2024, 6, 1);
    private static readonly LocalDate WindowEnd = new(2024, 6, 30);

    [Fact]
    public async Task ReturnsArticlesWithPublishersInsideADateWindow()
    {
        NewsArticle[] articles = (await Client.Reference.ListNewsAsync(
            ticker: "AAPL",
            publishedUtc: RangeFilter.Between(WindowStart, WindowEnd),
            limit: 5,
            cancellationToken: Ct)).Results;

        Assert.NotEmpty(articles);

        Instant lower = WindowStart.AtMidnight().InUtc().ToInstant();
        Instant upper = WindowEnd.PlusDays(1).AtMidnight().InUtc().ToInstant();

        foreach (NewsArticle article in articles)
        {
            Assert.False(string.IsNullOrWhiteSpace(article.Publisher.Name), "Every article names its publisher.");
            Assert.Contains("AAPL", article.Tickers);
            Assert.InRange(article.PublishedUtc, lower, upper);
        }
    }
}
