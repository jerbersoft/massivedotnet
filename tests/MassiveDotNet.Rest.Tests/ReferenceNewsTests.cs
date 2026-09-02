using System.Text.Json;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The first mapped operation with nested objects: a required <c>publisher</c>, an optional array
/// of <c>insights</c>, arrays of strings, and an RFC 3339 timestamp, end to end through the stub.
/// </summary>
public sealed class ReferenceNewsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Instant Published = Instant.FromUtc(2024, 6, 24, 18, 33, 53);

    /// <summary>An article carrying only the schema-required scalars: no publisher at all.</summary>
    private const string ArticleWithoutPublisher = """
        {
          "results": [
            {
              "id": "1",
              "title": "Title",
              "author": "Author",
              "article_url": "https://example.com/a",
              "published_utc": "2024-06-24T18:33:53Z",
              "tickers": ["UBS"]
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>An article with its required publisher and nothing optional.</summary>
    private const string ArticleWithoutInsights = """
        {
          "results": [
            {
              "id": "1",
              "title": "Title",
              "author": "Author",
              "article_url": "https://example.com/a",
              "published_utc": "2024-06-24T18:33:53Z",
              "tickers": ["UBS"],
              "publisher": { "name": "Example", "homepage_url": "https://example.com/", "logo_url": "https://example.com/l.png" }
            }
          ],
          "status": "OK"
        }
        """;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task BuildsTheDocumentedRequestPathWithNoFilters()
    {
        StubHandler handler = new(Fixtures.ReferenceNews);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListNewsAsync(cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v2/reference/news", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task RendersATickerAndADateWindowInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceNews);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListNewsAsync(
                ticker: "AAPL",
                publishedUtc: RangeFilter.Between(new LocalDate(2024, 6, 1), new LocalDate(2024, 6, 30)),
                order: SortOrder.Descending,
                limit: 10,
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v2/reference/news"
                + "?ticker=AAPL"
                + "&published_utc.gte=2024-06-01&published_utc.lte=2024-06-30"
                + "&order=desc"
                + "&limit=10",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceNews);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<NewsArticle> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListNewsAsync(ticker: "UBS", cancellationToken: Ct);
        }

        NewsArticle article = Assert.Single(page.Results);

        Assert.Equal("8ec638777ca03b553ae516761c2a22ba2fdd2f37befae3ab6fdab74e9e5193eb", article.Id);
        Assert.Equal("Markets are underestimating Fed cuts: UBS By Investing.com - Investing.com UK", article.Title);
        Assert.Equal("Sam Boughedda", article.Author);
        Assert.Equal("https://uk.investing.com/news/stock-market-news/markets-are-underestimating-fed-cuts-ubs-3559968", article.ArticleUrl);
        Assert.Equal("https://m.uk.investing.com/news/stock-market-news/markets-are-underestimating-fed-cuts-ubs-3559968?ampMode=1", article.AmpUrl);
        Assert.Equal("https://i-invdn-com.investing.com/news/LYNXNPEC4I0AL_L.jpg", article.ImageUrl);
        Assert.StartsWith("UBS analysts warn", article.Description, StringComparison.Ordinal);
        Assert.Equal(Published, article.PublishedUtc);
        Assert.Equal(["UBS"], article.Tickers);
        Assert.Equal(["Federal Reserve", "interest rates", "economic data"], article.Keywords!);

        Assert.Equal("Investing.com", article.Publisher.Name);
        Assert.Equal("https://www.investing.com/", article.Publisher.HomepageUrl);
        Assert.Equal("https://s3.massive.com/public/assets/news/logos/investing.png", article.Publisher.LogoUrl);
        Assert.Equal("https://s3.massive.com/public/assets/news/favicons/investing.ico", article.Publisher.FaviconUrl);

        NewsInsight insight = Assert.Single(article.Insights!);
        Assert.Equal("UBS", insight.Ticker);
        Assert.Equal("positive", insight.Sentiment);
        Assert.StartsWith("UBS analysts are providing a bullish outlook", insight.SentimentReasoning, StringComparison.Ordinal);

        Assert.True(page.HasMore);
        Assert.Equal("831afdb0b8078549fed053476984947a", page.RequestId);
    }

    [Fact]
    public async Task AnArticleWithoutAPublisherIsRejected()
    {
        // publisher is required by the schema, so NewsArticle.Publisher carries the required
        // modifier (D-F10 through D-N2). Its absence is a JsonException wrapped in a
        // MassiveApiException, not a null reference discovered later by the caller.
        StubHandler handler = new(ArticleWithoutPublisher);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Reference.ListNewsAsync(cancellationToken: Ct));

            Assert.IsType<JsonException>(exception.InnerException);
        }
    }

    [Fact]
    public async Task OptionalNestedMembersAreNullWhenAbsent()
    {
        StubHandler handler = new(ArticleWithoutInsights);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<NewsArticle> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListNewsAsync(cancellationToken: Ct);
        }

        NewsArticle article = Assert.Single(page.Results);
        Assert.Null(article.Insights);
        Assert.Null(article.Keywords);
        Assert.Null(article.Publisher.FaviconUrl);
        Assert.Equal("Example", article.Publisher.Name);
    }

    [Fact]
    public async Task AMalformedTimestampIsRejected()
    {
        string body = Fixtures.ReferenceNews.Replace("2024-06-24T18:33:53Z", "2024-06-24 18:33:53", StringComparison.Ordinal);
        StubHandler handler = new(body);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Reference.ListNewsAsync(cancellationToken: Ct));

            Assert.IsType<JsonException>(exception.InnerException);
        }
    }

    [Fact]
    public async Task EnumerateFollowsThePublishedCursorVerbatim()
    {
        PagingStubHandler handler = new(Fixtures.ReferenceNews, Fixtures.ReferenceNewsLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string> ids = [];

        using (client)
        using (transport)
        {
            await foreach (NewsArticle article in client.Reference.EnumerateNewsAsync(ticker: "UBS", cancellationToken: Ct))
            {
                ids.Add(article.Id);
            }
        }

        Assert.Equal(["8ec638777ca03b553ae516761c2a22ba2fdd2f37befae3ab6fdab74e9e5193eb", "second"], ids);
        Assert.Equal(2, handler.Requests.Count);

        // The cursor is followed verbatim (D14): the second request's path and query must match
        // the fixture's own next_url exactly, not merely start with its cursor.
        using JsonDocument firstPage = JsonDocument.Parse(Fixtures.ReferenceNews);
        Uri nextUrl = new(firstPage.RootElement.GetProperty("next_url").GetString()!);

        Assert.Equal(nextUrl.PathAndQuery, handler.Requests[1].PathAndQuery);
    }
}
