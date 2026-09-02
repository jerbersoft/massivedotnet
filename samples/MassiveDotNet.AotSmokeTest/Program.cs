using System.Net;
using System.Text;
using MassiveDotNet;
using MassiveDotNet.Http;
using MassiveDotNet.Rest;
using MassiveDotNet.Rest.Models;
using NodaTime;
using NodaTime.Text;

// Exercises the full request-building and deserialization path with no network, so that
// `dotnet publish` proves the SDK is Native AOT clean. Reflection-based serialization is
// disabled in this project, so any reflection fallback fails here rather than in production.

StubHandler handler = new();
HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };

using MassiveHttpTransport transport = new(httpClient);
using MassiveRestClient client = new(transport);

MassivePage<Agg> page = await client.Stocks.ListAggregatesAsync(
    "AAPL",
    1,
    AggregateTimespan.Day,
    new LocalDate(2020, 1, 1),
    new LocalDate(2020, 1, 10),
    adjusted: true,
    sort: SortOrder.Ascending);

Agg[] bars = page.Results;

Console.WriteLine($"request : {handler.LastRequestUri}");
Console.WriteLine($"bars    : {bars.Length} (more: {page.HasMore}, request id: {page.RequestId})");

foreach (Agg bar in bars)
{
    Console.WriteLine(Describe(bar));
}

// The traversal is rooted here deliberately. This project is the enforcement mechanism the
// constitution names for rules 3 and 4, and an unreferenced generic instantiation is simply
// trimmed away: with nothing calling EnumerateAsync<TEnvelope, TItem>, a clean publish would
// prove nothing about it, nor about the async-iterator state machine the compiler builds behind
// it. The stub advertises a cursor on the uncursored request, so this really does cross a page
// boundary rather than exercising a single-page shortcut.
Console.WriteLine("\nenumerating every page:");

int enumerated = 0;

await foreach (Agg bar in client.Stocks.EnumerateAggregatesAsync(
    "AAPL",
    1,
    AggregateTimespan.Day,
    new LocalDate(2020, 1, 1),
    new LocalDate(2020, 1, 10),
    adjusted: true,
    sort: SortOrder.Ascending))
{
    enumerated++;
    Console.WriteLine(Describe(bar));
}

// Filters are rooted for the same reason as the traversal above. RequestUriBuilder.AppendQuery<T>
// dispatches on typeof(T) through Unsafe.As, and the LocalDate converter is reached only through
// the generated context: each is a generic instantiation a clean publish says nothing about
// unless something here calls it. This call covers string, LocalDate, and long elements, a range,
// a set, and a date on the way back in.
Console.WriteLine("\ndividends, filtered:");

MassivePage<Dividend> dividends = await client.Stocks.ListDividendsAsync(
    ticker: "AAPL",
    exDividendDate: RangeFilter.Between(new LocalDate(2025, 1, 1), new LocalDate(2025, 12, 31)),
    frequency: RangeFilter.Gte(4L),
    distributionType: SetFilter.AnyOf("recurring", "special"));

Console.WriteLine($"request : {handler.LastRequestUri}");

foreach (Dividend dividend in dividends.Results)
{
    Console.WriteLine($"  {dividend.Ticker}  ex {dividend.ExDividendDate}  {dividend.CashAmount:F2} {dividend.Currency}");
}

const string ExpectedFilterQuery =
    "?ticker=AAPL&ex_dividend_date.gte=2025-01-01&ex_dividend_date.lte=2025-12-31"
    + "&frequency.gte=4&distribution_type.any_of=recurring,special";

if (handler.LastRequestUri?.Query != ExpectedFilterQuery)
{
    Console.Error.WriteLine($"FAIL: expected the filter query {ExpectedFilterQuery}; got {handler.LastRequestUri?.Query}.");
    return 1;
}

if (dividends.Results.Length != 1 || dividends.Results[0].ExDividendDate != new LocalDate(2025, 8, 11))
{
    Console.Error.WriteLine("FAIL: expected one dividend with ex-dividend date 2025-08-11.");
    return 1;
}

// Nested models and the RFC 3339 converter are reachable only through the news envelope, so a
// clean publish says nothing about them unless something here deserializes one. This is the
// first call whose result carries a required nested object, an array of nested objects, and an
// Instant parsed from a string rather than an epoch number.
Console.WriteLine("\nnews, with a nested publisher and insights:");

MassivePage<NewsArticle> news = await client.Reference.ListNewsAsync(
    ticker: "UBS",
    publishedUtc: RangeFilter.Gte(new LocalDate(2024, 6, 1)),
    limit: 1);

Console.WriteLine($"request : {handler.LastRequestUri}");

foreach (NewsArticle item in news.Results)
{
    Console.WriteLine($"  {InstantPattern.ExtendedIso.Format(item.PublishedUtc)}  {item.Publisher.Name}  {item.Title}");
}

const string ExpectedNewsQuery = "?ticker=UBS&published_utc.gte=2024-06-01&limit=1";

if (handler.LastRequestUri?.Query != ExpectedNewsQuery)
{
    Console.Error.WriteLine($"FAIL: expected the news query {ExpectedNewsQuery}; got {handler.LastRequestUri?.Query}.");
    return 1;
}

if (news.Results is not [NewsArticle article]
    || article.Publisher.Name != "Investing.com"
    || article.PublishedUtc != Instant.FromUtc(2024, 6, 24, 18, 33, 53)
    || article.Insights is not [{ Ticker: "UBS", Sentiment: "positive" }]
    || article.Keywords is not { Length: 3 })
{
    Console.Error.WriteLine("FAIL: expected one article from Investing.com published 2024-06-24T18:33:53Z with one positive UBS insight and three keywords.");
    return 1;
}

Console.WriteLine($"\nrequests: {handler.Requests}");

if (bars.Length != 2 || !page.HasMore)
{
    Console.Error.WriteLine("FAIL: expected a first page of 2 bars reporting more.");
    return 1;
}

// Two pages of the enumeration, the single-page aggregates call, the dividends call, and the
// news call.
if (enumerated != 3 || handler.Requests != 5)
{
    Console.Error.WriteLine(
        $"FAIL: expected 3 enumerated bars over 5 requests; got {enumerated} over {handler.Requests}.");
    return 1;
}

Console.WriteLine("\nAOT smoke test passed.");
return 0;

static string Describe(Agg bar) =>
    $"  {LocalDatePattern.Iso.Format(bar.Timestamp.InUtc().Date)}  O {bar.Open,9:F4}  H {bar.High,9:F4}  "
    + $"L {bar.Low,9:F4}  C {bar.Close,9:F4}  V {bar.Volume,12:N0}";

internal sealed class StubHandler : HttpMessageHandler
{
    private const string FirstPage = """
        {
          "adjusted": true,
          "next_url": "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/1578027600000/2020-01-10?cursor=next",
          "queryCount": 2,
          "request_id": "6a7e466379af0a71039d60cc78e72282",
          "results": [
            { "c": 75.0875, "h": 75.15, "l": 73.7975, "n": 1, "o": 74.06, "t": 1577941200000, "v": 135647456, "vw": 74.6099 },
            { "c": 74.3575, "h": 75.145, "l": 74.125,  "n": 1, "o": 74.2875, "t": 1578027600000, "v": 146535512, "vw": 74.7026 }
          ],
          "resultsCount": 2,
          "status": "OK",
          "ticker": "AAPL"
        }
        """;

    private const string FinalPage = """
        {
          "adjusted": true,
          "queryCount": 1,
          "request_id": "6a7e466379af0a71039d60cc78e72282",
          "results": [
            { "c": 74.9499, "h": 75.225, "l": 74.37, "n": 1, "o": 74.955, "t": 1578286800000, "v": 118578580, "vw": 74.8358 }
          ],
          "resultsCount": 1,
          "status": "OK",
          "ticker": "AAPL"
        }
        """;

    private const string Dividends = """
        {
          "request_id": "1",
          "results": [
            {
              "cash_amount": 0.26,
              "currency": "USD",
              "declaration_date": "2025-07-31",
              "distribution_type": "recurring",
              "ex_dividend_date": "2025-08-11",
              "frequency": 4,
              "historical_adjustment_factor": 0.997899,
              "id": "Ed2c9da60abda1e3f0e99a43f6465863c137b671e1f5cd3f833d1fcb4f4eb27fe",
              "pay_date": "2025-08-14",
              "record_date": "2025-08-11",
              "split_adjusted_cash_amount": 0.26,
              "ticker": "AAPL"
            }
          ],
          "status": "OK"
        }
        """;

    private const string News = """
        {
          "count": 1,
          "next_url": "https://api.massive.com:443/v2/reference/news?cursor=eyJsaW1pdCI6MSwic29ydCI6InB1Ymxpc2hlZF91dGMiLCJvcmRlciI6ImFzY2VuZGluZyIsInRpY2tlciI6e30sInB1Ymxpc2hlZF91dGMiOnsiZ3RlIjoiMjAyMS0wNC0yNiJ9LCJzZWFyY2hfYWZ0ZXIiOlsxNjE5NDA0Mzk3MDAwLG51bGxdfQ",
          "request_id": "831afdb0b8078549fed053476984947a",
          "results": [
            {
              "amp_url": "https://m.uk.investing.com/news/stock-market-news/markets-are-underestimating-fed-cuts-ubs-3559968?ampMode=1",
              "article_url": "https://uk.investing.com/news/stock-market-news/markets-are-underestimating-fed-cuts-ubs-3559968",
              "author": "Sam Boughedda",
              "description": "UBS analysts warn that markets are underestimating the extent of future interest rate cuts by the Federal Reserve, as the weakening economy is likely to justify more cuts than currently anticipated.",
              "id": "8ec638777ca03b553ae516761c2a22ba2fdd2f37befae3ab6fdab74e9e5193eb",
              "image_url": "https://i-invdn-com.investing.com/news/LYNXNPEC4I0AL_L.jpg",
              "insights": [
                {
                  "sentiment": "positive",
                  "sentiment_reasoning": "UBS analysts are providing a bullish outlook on the extent of future Federal Reserve rate cuts, suggesting that markets are underestimating the number of cuts that will occur.",
                  "ticker": "UBS"
                }
              ],
              "keywords": [
                "Federal Reserve",
                "interest rates",
                "economic data"
              ],
              "published_utc": "2024-06-24T18:33:53Z",
              "publisher": {
                "favicon_url": "https://s3.massive.com/public/assets/news/favicons/investing.ico",
                "homepage_url": "https://www.investing.com/",
                "logo_url": "https://s3.massive.com/public/assets/news/logos/investing.png",
                "name": "Investing.com"
              },
              "tickers": [
                "UBS"
              ],
              "title": "Markets are underestimating Fed cuts: UBS By Investing.com - Investing.com UK"
            }
          ],
          "status": "OK"
        }
        """;

    public Uri? LastRequestUri { get; private set; }

    public int Requests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        Requests++;

        string body;

        if (request.RequestUri?.AbsolutePath == "/stocks/v1/dividends")
        {
            body = Dividends;
        }
        else if (request.RequestUri?.AbsolutePath == "/v2/reference/news")
        {
            body = News;
        }
        else
        {
            // Keyed on the cursor rather than on a request counter, so the single-page call and the
            // traversal stay independent of the order they happen to run in.
            bool cursored = request.RequestUri?.Query.Contains("cursor=", StringComparison.Ordinal) == true;
            body = cursored ? FinalPage : FirstPage;
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}
