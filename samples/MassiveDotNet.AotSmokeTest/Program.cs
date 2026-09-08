using System.Net;
using System.Text;
using System.Text.Json;
using MassiveDotNet;
using MassiveDotNet.Http;
using MassiveDotNet.Rest;
using MassiveDotNet.Rest.Models;
using MassiveDotNet.WebSocket;
using MassiveDotNet.WebSocket.Events;
using MassiveDotNet.WebSocket.Internal;
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

// Singular results are new generic instantiations and new context registrations, each of which a
// clean publish says nothing about unless something here reaches it: MassivePagedResult<T>, the
// explicit IPagedEnvelope<IndicatorValue> path that EnumerateAsync walks through Results?.Values,
// a struct payload under results, a body object, and a body array's MarketHoliday[] type info.
Console.WriteLine("\nsma, one page with its underlying:");

MassivePagedResult<IndicatorSeries> sma = await client.Stocks.ListSmaAsync(
    "AAPL",
    timespan: AggregateTimespan.Day,
    window: 10,
    seriesType: SeriesType.Close,
    expandUnderlying: true,
    limit: 1);

Console.WriteLine($"request : {handler.LastRequestUri}");
Console.WriteLine($"values  : {sma.Result.Values?.Length} (more: {sma.HasMore}, underlying: {sma.Result.Underlying?.Aggregates?.Length} aggregates)");

const string ExpectedSmaQuery = "?timespan=day&window=10&series_type=close&expand_underlying=true&limit=1";

if (handler.LastRequestUri?.Query != ExpectedSmaQuery)
{
    Console.Error.WriteLine($"FAIL: expected the SMA query {ExpectedSmaQuery}; got {handler.LastRequestUri?.Query}.");
    return 1;
}

if (!sma.HasMore
    || sma.Result.Values is not [{ TimestampMilliseconds: 1517562000016 }]
    || sma.Result.Underlying?.Aggregates is not { Length: 2 })
{
    Console.Error.WriteLine("FAIL: expected one SMA value at 1517562000016 with two underlying aggregates and more pages.");
    return 1;
}

Console.WriteLine("\nsma, enumerating every value:");

int smaValues = 0;

await foreach (IndicatorValue value in client.Stocks.EnumerateSmaAsync("AAPL", limit: 1))
{
    smaValues++;
    Console.WriteLine($"  {LocalDatePattern.Iso.Format(value.Timestamp.InUtc().Date)}  {value.Value,9:F3}");
}

if (smaValues != 2)
{
    Console.Error.WriteLine($"FAIL: expected 2 SMA values across two pages; got {smaValues}.");
    return 1;
}

Console.WriteLine("\nlast trade:");

LastTrade trade = await client.Stocks.GetLastTradeAsync("AAPL");
Console.WriteLine($"  {trade.Ticker}  {trade.Price:F4} x {trade.Size}  at {InstantPattern.ExtendedIso.Format(trade.SipTimestamp)}");

if (trade.Ticker != "AAPL" || trade.SipTimestampNanoseconds != 1617901342969834000)
{
    Console.Error.WriteLine("FAIL: expected the AAPL trade at 1617901342969834000.");
    return 1;
}

Console.WriteLine("\ndaily open/close:");

DailyOpenClose day = await client.Stocks.GetDailyOpenCloseAsync("AAPL", new LocalDate(2023, 1, 9));
Console.WriteLine($"  {day.Symbol}  {LocalDatePattern.Iso.Format(day.From)}  O {day.Open:F2}  C {day.Close:F2}  {day.Status}");

if (day.From != new LocalDate(2023, 1, 9) || day.Close != 325.12)
{
    Console.Error.WriteLine("FAIL: expected AAPL on 2023-01-09 closing at 325.12.");
    return 1;
}

Console.WriteLine("\nmarket holidays:");

MarketHoliday[] holidays = await client.Reference.ListMarketHolidaysAsync();

foreach (MarketHoliday holiday in holidays)
{
    Console.WriteLine($"  {LocalDatePattern.Iso.Format(holiday.Date)}  {holiday.Exchange,-6}  {holiday.Status,-11}  {holiday.Name}");
}

if (holidays is not [.., { Status: "early-close", Open: not null }])
{
    Console.Error.WriteLine("FAIL: expected the last holiday to be an early close with an open time.");
    return 1;
}

// The nanosecond filter type and the bare array parameter are new generic instantiations of the
// builder's element dispatch (D19, D20), and the snapshot item is the first model with five
// optional nested structs; each is reachable only through these two calls.
Console.WriteLine("\ntrades, with a nanosecond range:");

MassivePage<Trade> trades = await client.Stocks.ListTradesAsync(
    "AAPL",
    timestamp: RangeFilter.Between(
        DateOrNanoseconds.FromInstant(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1517562000000000000)),
        DateOrNanoseconds.FromDate(new LocalDate(2018, 2, 3))),
    order: SortOrder.Ascending,
    limit: 2);

Console.WriteLine($"request : {handler.LastRequestUri}");

foreach (Trade tick in trades.Results)
{
    Console.WriteLine($"  {tick.TradeId,-3} {tick.Price,9:F2} x {tick.Size,6:N0}  at {InstantPattern.ExtendedIso.Format(tick.SipTimestamp)}");
}

const string ExpectedTradesQuery = "?timestamp.gte=1517562000000000000&timestamp.lte=2018-02-03&order=asc&limit=2";

if (handler.LastRequestUri?.Query != ExpectedTradesQuery)
{
    Console.Error.WriteLine($"FAIL: expected the trades query {ExpectedTradesQuery}; got {handler.LastRequestUri?.Query}.");
    return 1;
}

if (!trades.HasMore || trades.Results is not [{ SipTimestampNanoseconds: 1517562000016036600 }, _])
{
    Console.Error.WriteLine("FAIL: expected two trades, the first at 1517562000016036600, with more pages.");
    return 1;
}

Console.WriteLine("\nsnapshots, for two tickers:");

TickerSnapshot[] snapshots = await client.Stocks.ListSnapshotsAsync(tickers: ["BCAT", "BRK/B"], includeOtc: false);

Console.WriteLine($"request : {handler.LastRequestUri}");

foreach (TickerSnapshot snapshot in snapshots)
{
    Console.WriteLine($"  {snapshot.Ticker,-6} day close {snapshot.Day?.Close,9:F3}  accumulated volume {snapshot.Minute?.AccumulatedVolume,10:N0}");
}

const string ExpectedSnapshotsQuery = "?tickers=BCAT,BRK%2FB&include_otc=false";

if (handler.LastRequestUri?.Query != ExpectedSnapshotsQuery)
{
    Console.Error.WriteLine($"FAIL: expected the snapshots query {ExpectedSnapshotsQuery}; got {handler.LastRequestUri?.Query}.");
    return 1;
}

if (snapshots is not [{ Ticker: "BCAT", Minute: { AccumulatedVolume: 37216 }, LastTrade: { SipTimestampNanoseconds: 1605192894630916600 }, Updated: not null }])
{
    Console.Error.WriteLine("FAIL: expected one BCAT snapshot with accumulated volume 37216 and a last trade at 1605192894630916600.");
    return 1;
}

// Market status is the first body-object payload with nested models, the tickers page the first
// whose query carries a MarketType and whose rows carry an Instant read from an RFC 3339 string,
// and the contracts page the first ContractType parameter and optional array of nested models;
// each is a generic instantiation or a context entry a clean publish says nothing about unless
// something here reaches it.
Console.WriteLine("\nmarket status:");

MarketStatus status = await client.Reference.GetMarketStatusAsync();
Console.WriteLine(
    $"  market {status.Market}  nyse {status.Exchanges?.Nyse}  fx {status.Currencies?.Fx}"
    + $"  at {(status.ServerTime is { } serverTime ? InstantPattern.ExtendedIso.Format(serverTime) : "unknown")}");

if (status.Market != "extended-hours" || status.Exchanges?.Otc != "closed" || status.ServerTime != Instant.FromUtc(2020, 11, 10, 22, 37, 37))
{
    Console.Error.WriteLine("FAIL: expected an extended-hours market with OTC closed at 2020-11-10T22:37:37Z.");
    return 1;
}

Console.WriteLine("\ntickers, for the stocks market:");

MassivePage<TickerSummary> tickers = await client.Reference.ListTickersAsync(market: MarketType.Stocks, active: true, limit: 1);

Console.WriteLine($"request : {handler.LastRequestUri}");

foreach (TickerSummary ticker in tickers.Results)
{
    Console.WriteLine($"  {ticker.Ticker,-6} {ticker.Name}  updated {(ticker.LastUpdatedUtc is { } updated ? InstantPattern.ExtendedIso.Format(updated) : "never")}");
}

const string ExpectedTickersQuery = "?market=stocks&active=true&limit=1";

if (handler.LastRequestUri?.Query != ExpectedTickersQuery)
{
    Console.Error.WriteLine($"FAIL: expected the tickers query {ExpectedTickersQuery}; got {handler.LastRequestUri?.Query}.");
    return 1;
}

if (tickers.Results is not [{ Ticker: "A", LastUpdatedUtc: not null }] || !tickers.HasMore)
{
    Console.Error.WriteLine("FAIL: expected one ticker, A, with an update time, and more pages.");
    return 1;
}

Console.WriteLine("\noptions contracts, calls only:");

MassivePage<OptionsContract> contracts = await client.Reference.ListOptionsContractsAsync(
    underlyingTicker: "AAPL",
    contractType: ContractType.Call,
    strikePrice: RangeFilter.Between(80d, 90d),
    limit: 2);

Console.WriteLine($"request : {handler.LastRequestUri}");

foreach (OptionsContract contract in contracts.Results)
{
    Console.WriteLine($"  {contract.Ticker}  strike {contract.StrikePrice,6:F2}  extras {contract.AdditionalUnderlyings?.Length ?? 0}");
}

const string ExpectedContractsQuery = "?underlying_ticker=AAPL&contract_type=call&strike_price.gte=80&strike_price.lte=90&limit=2";

if (handler.LastRequestUri?.Query != ExpectedContractsQuery)
{
    Console.Error.WriteLine($"FAIL: expected the contracts query {ExpectedContractsQuery}; got {handler.LastRequestUri?.Query}.");
    return 1;
}

if (contracts.Results is not [{ AdditionalUnderlyings: null }, { AdditionalUnderlyings: [{ Underlying: "VMW" }, _] }])
{
    Console.Error.WriteLine("FAIL: expected two contracts, the second with two additional underlyings starting with VMW.");
    return 1;
}

// The financials page is the SDK's only dictionary of nested models, so its converter and the
// Dictionary<string, FinancialDataPoint> instantiation are reachable only through this call; the
// download is the only response body read without deserializing, so it is the only thing that
// roots the transport's copy path. A clean publish says nothing about either otherwise.
Console.WriteLine("\nfinancials, one quarterly report:");

MassivePage<FinancialReport> financials = await client.Reference.ListFinancialsAsync(
    ticker: "SITE",
    timeframe: "quarterly",
    includeSources: false,
    limit: 1);

Console.WriteLine($"request : {handler.LastRequestUri}");

foreach (FinancialReport report in financials.Results)
{
    Console.WriteLine(
        $"  {report.CompanyName}  {report.FiscalPeriod} {report.FiscalYear} ({report.Timeframe})"
        + $"  assets {report.Financials.BalanceSheet?["assets"].Value,18:N0}"
        + $"  revenues {report.Financials.IncomeStatement?["revenues"].Value,18:N0}");
}

const string ExpectedFinancialsQuery = "?ticker=SITE&timeframe=quarterly&include_sources=false&limit=1";

if (handler.LastRequestUri?.Query != ExpectedFinancialsQuery)
{
    Console.Error.WriteLine($"FAIL: expected the financials query {ExpectedFinancialsQuery}; got {handler.LastRequestUri?.Query}.");
    return 1;
}

if (financials.Results is not [{ FiscalPeriod: "Q1", Timeframe: "quarterly" } report0]
    || report0.StartDate != new LocalDate(2022, 1, 3)
    || report0.Financials.BalanceSheet is not { Count: 2 } balanceSheet
    || report0.Financials.IncomeStatement is not { Count: 1 } incomeStatement
    || balanceSheet["assets"] is not { Label: "Assets", Unit: "USD", Value: 2407400000d }
    || incomeStatement["revenues"].Value != 805300000d)
{
    Console.Error.WriteLine("FAIL: expected one quarterly Q1 report whose balance sheet holds two data points, assets at 2,407,400,000.");
    return 1;
}

Console.WriteLine("\ndownloading one filing file:");

using MemoryStream file = new();

await client.Reference.DownloadFilingFileAsync("0001683168-26-006873", "myx_i10k-053126.htm", file);

Console.WriteLine($"request : {handler.LastRequestUri}");
Console.WriteLine($"  {file.Length} bytes, starting {Encoding.UTF8.GetString(file.ToArray(), 0, 14)}");

if (file.Length == 0 || !Encoding.UTF8.GetString(file.ToArray()).StartsWith("<html", StringComparison.Ordinal))
{
    Console.Error.WriteLine("FAIL: expected the filing file body to be copied to the stream, starting with <html.");
    return 1;
}

Console.WriteLine($"\nrequests: {handler.Requests}");

if (bars.Length != 2 || !page.HasMore)
{
    Console.Error.WriteLine("FAIL: expected a first page of 2 bars reporting more.");
    return 1;
}

// Two pages of the aggregates enumeration, the single-page aggregates call, the dividends call,
// the news call, one SMA page, two SMA pages enumerated, the last trade, the open/close day, the
// holidays, the trades page, the snapshots, the market status, the tickers page, the contracts
// page, the financials page, and the filing file download.
if (enumerated != 3 || handler.Requests != 18)
{
    Console.Error.WriteLine(
        $"FAIL: expected 3 enumerated bars over 18 requests; got {enumerated} over {handler.Requests}.");
    return 1;
}

// The resilience handlers are rooted here for the reason every other section above is rooted:
// System.Threading.RateLimiting is core's second package (D30), and an unreached TokenBucketRateLimiter
// is trimmed away — so a clean publish would say nothing about it unless something here acquires a
// permit and waits on the replenishment timer. A separate stub keeps the request count above intact.
Console.WriteLine("\nresilience: retrying a 503 through a rate-limited pipeline:");

FlakyHandler flaky = new();

MassiveClientOptions resilientOptions = new()
{
    ApiKey = "aot-smoke-test-key",
    AuthenticationScheme = MassiveAuthenticationScheme.QueryString,
    Retry = new MassiveRetryOptions
    {
        MaxAttempts = 3,
        InitialBackoff = Duration.FromMilliseconds(1),
        MaxBackoff = Duration.FromMilliseconds(20),
    },
    RateLimit = new MassiveRateLimitOptions
    {
        PermitsPerWindow = 4,
        Window = Duration.FromSeconds(1),
    },
};

using HttpMessageHandler resilientPipeline =
    MassiveHttpTransport.CreateHandlerPipeline(resilientOptions, flaky);

using HttpClient resilientHttpClient = new(resilientPipeline, disposeHandler: false)
{
    BaseAddress = MassiveEndpoints.Production,
};

using MassiveHttpTransport resilientTransport = new(resilientHttpClient);
using MassiveRestClient resilientClient = new(resilientTransport);

MassivePage<TickerSummary> retried = await resilientClient.Reference.ListTickersAsync(limit: 1);

Console.WriteLine($"request : {flaky.LastRequestUri}");
Console.WriteLine($"attempts: {flaky.Requests} (results: {retried.Results.Length})");

if (flaky.Requests != 3)
{
    Console.Error.WriteLine($"FAIL: expected the 503s to be retried to a third attempt; got {flaky.Requests}.");
    return 1;
}

// The ordering property from D30, asserted where a trimmed or reordered pipeline would show up.
if (flaky.SentQueries.Any(query => query.Split("apiKey=", StringSplitOptions.None).Length - 1 != 1))
{
    Console.Error.WriteLine(
        $"FAIL: expected exactly one apiKey per attempt; got [{string.Join(", ", flaky.SentQueries)}].");
    return 1;
}

// L3 (Task 14 ruling): this project referenced only .Rest through Task 13, so every "AOT publish
// clean" claim made while building the streaming package proved nothing about it. The client is
// constructed and disposed without connecting -- this project publishes in CI, which has no key
// and no network (rule 13) -- and both hand-written event converters are built and read against a
// literal frame each, so ILC roots them and the ConditionSet InlineArray rather than trimming
// instantiations nothing here would otherwise reach.
Console.WriteLine("\nstreaming: rooting MassiveStreamClient, the event converters, and ConditionSet:");

MassiveStreamOptions streamOptions = new() { ApiKey = "aot-smoke-test-key" };

await using (MassiveStreamClient streamClient = new(streamOptions))
{
    Console.WriteLine($"  client constructed for feed {streamOptions.Feed}, not connected");
}

const string StreamTradeFrame =
    """[{"ev":"T","sym":"MSFT","x":4,"i":"12345","z":3,"p":114.125,"s":100,"c":[0,12],"t":1536036818784,"pt":1536036818763,"q":3681328}]""";
const string StreamQuoteFrame =
    """[{"ev":"Q","sym":"MSFT","bx":4,"bp":114.125,"bs":100,"ax":7,"ap":114.128,"as":160,"c":0,"i":[604],"t":1536036818784,"q":50385480,"z":3}]""";

TickerPool streamTickers = new(16);

// Matches TopicSink<T>'s own EmptyOptions: JsonSerializerOptions.Default is annotated
// RequiresUnreferencedCode/RequiresDynamicCode, which would fail this very publish (rule 3) --
// and neither converter's Read ever consults its options parameter regardless.
JsonSerializerOptions streamJsonOptions = new();

Utf8JsonReader tradeReader = new(Encoding.UTF8.GetBytes(StreamTradeFrame));
tradeReader.Read();  // [
tradeReader.Read();  // {
StockTrade streamTrade = new StockTradeConverter(streamTickers)
    .Read(ref tradeReader, typeof(StockTrade), streamJsonOptions);

Utf8JsonReader quoteReader = new(Encoding.UTF8.GetBytes(StreamQuoteFrame));
quoteReader.Read();  // [
quoteReader.Read();  // {
StockQuote streamQuote = new StockQuoteConverter(streamTickers)
    .Read(ref quoteReader, typeof(StockQuote), streamJsonOptions);

Console.WriteLine(
    $"  trade   : {streamTrade.Ticker} {streamTrade.Price:F3} x {streamTrade.Size}  "
    + $"conditions [{string.Join(",", streamTrade.Conditions.AsSpan().ToArray())}]");
Console.WriteLine(
    $"  quote   : {streamQuote.Ticker} bid {streamQuote.BidPrice:F3} x {streamQuote.BidSize}  "
    + $"ask {streamQuote.AskPrice:F3} x {streamQuote.AskSize}  "
    + $"indicators [{string.Join(",", streamQuote.Indicators.AsSpan().ToArray())}]");

if (streamTrade.Ticker != "MSFT" || streamTrade.Conditions.AsSpan().ToArray() is not [0, 12])
{
    Console.Error.WriteLine("FAIL: expected the streamed MSFT trade with conditions [0, 12].");
    return 1;
}

if (streamQuote.Ticker != "MSFT" || streamQuote.Indicators.AsSpan().ToArray() is not [604])
{
    Console.Error.WriteLine("FAIL: expected the streamed MSFT quote with indicator [604].");
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

    private const string Sma = """
        {
          "next_url": "https://api.massive.com/v1/indicators/sma/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "a47d1beb8c11b6ae897ab76cdbbf35a3",
          "results": {
            "underlying": {
              "aggregates": [
                { "c": 75.0875, "h": 75.15, "l": 73.7975, "n": 1, "o": 74.06, "t": 1577941200000, "v": 135647456, "vw": 74.6099 },
                { "c": 74.3575, "h": 75.145, "l": 74.125, "n": 1, "o": 74.2875, "t": 1578027600000, "v": 146535512, "vw": 74.7026 }
              ],
              "url": "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2003-01-01/2022-07-25"
            },
            "values": [
              { "timestamp": 1517562000016, "value": 140.139 }
            ]
          },
          "status": "OK"
        }
        """;

    private const string SmaLastPage = """
        {
          "request_id": "0d5b6f1e9f3c4a7b8e2d1c0f9a8b7c6d",
          "results": {
            "underlying": {
              "url": "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2003-01-01/2022-07-24"
            },
            "values": [
              { "timestamp": 1517475600016, "value": 139.871 }
            ]
          },
          "status": "OK"
        }
        """;

    private const string LastTradeBody = """
        {
          "request_id": "f05562305bd26ced64b98ed68b3c5d96",
          "results": {
            "T": "AAPL",
            "c": [ 37 ],
            "ds": "25.0",
            "f": 1617901342969796400,
            "i": "118749",
            "p": 129.8473,
            "q": 3135876,
            "r": 202,
            "s": 25,
            "t": 1617901342969834000,
            "x": 4,
            "y": 1617901342968000000,
            "z": 3
          },
          "status": "OK"
        }
        """;

    private const string OpenClose = """
        {
          "afterHours": 322.1,
          "close": 325.12,
          "from": "2023-01-09",
          "high": 326.2,
          "low": 322.3,
          "open": 324.66,
          "preMarket": 324.5,
          "status": "OK",
          "symbol": "AAPL",
          "volume": 26122646
        }
        """;

    private const string Holidays = """
        [
          { "date": "2020-11-26", "exchange": "NYSE", "name": "Thanksgiving", "status": "closed" },
          { "date": "2020-11-26", "exchange": "NASDAQ", "name": "Thanksgiving", "status": "closed" },
          { "close": "2020-11-27T18:00:00.000Z", "date": "2020-11-27", "exchange": "NYSE", "name": "Thanksgiving", "open": "2020-11-27T14:30:00.000Z", "status": "early-close" }
        ]
        """;

    private const string Trades = """
        {
          "next_url": "https://api.massive.com/v3/trades/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "a47d1beb8c11b6ae897ab76cdbbf35a3",
          "results": [
            { "conditions": [ 12, 41 ], "decimal_size": "100.0", "exchange": 11, "id": "1", "participant_timestamp": 1517562000015577000, "price": 171.55, "sequence_number": 1063, "sip_timestamp": 1517562000016036600, "size": 100, "tape": 3 },
            { "conditions": [ 12, 41 ], "decimal_size": "100.0", "exchange": 11, "id": "2", "participant_timestamp": 1517562000015577600, "price": 171.55, "sequence_number": 1064, "sip_timestamp": 1517562000016038100, "size": 100, "tape": 3 }
          ],
          "status": "OK"
        }
        """;

    private const string Snapshots = """
        {
          "count": 1,
          "status": "OK",
          "tickers": [
            {
              "day": { "c": 20.506, "dv": "37216.0", "h": 20.64, "l": 20.506, "o": 20.64, "v": 37216, "vw": 20.616 },
              "lastQuote": { "P": 20.6, "S": 22, "p": 20.5, "s": 13, "t": 1605192959994246100 },
              "lastTrade": { "c": [ 14, 41 ], "ds": "2416.0", "i": "71675577320245", "p": 20.506, "s": 2416, "t": 1605192894630916600, "x": 4 },
              "min": { "av": 37216, "c": 20.506, "dav": "37216.0", "dv": "5000.0", "h": 20.506, "l": 20.506, "n": 1, "o": 20.506, "t": 1684428600000, "v": 5000, "vw": 20.5105 },
              "prevDay": { "c": 20.63, "h": 21, "l": 20.5, "o": 20.79, "v": 292738, "vw": 20.6939 },
              "ticker": "BCAT",
              "todaysChange": -0.124,
              "todaysChangePerc": -0.601,
              "updated": 1605192894630916600
            }
          ]
        }
        """;

    private const string MarketStatusBody = """
        {
          "afterHours": true,
          "currencies": { "crypto": "open", "fx": "open" },
          "earlyHours": false,
          "exchanges": { "nasdaq": "extended-hours", "nyse": "extended-hours", "otc": "closed" },
          "market": "extended-hours",
          "serverTime": "2020-11-10T17:37:37-05:00"
        }
        """;

    private const string Tickers = """
        {
          "count": 1,
          "next_url": "https://api.massive.com/v3/reference/tickers?cursor=next",
          "request_id": "e70013d92930de90e089dc8fa098888e",
          "results": [
            {
              "active": true,
              "cik": "0001090872",
              "composite_figi": "BBG000BWQYZ5",
              "currency_name": "usd",
              "last_updated_utc": "2021-04-25T00:00:00Z",
              "locale": "us",
              "market": "stocks",
              "name": "Agilent Technologies Inc.",
              "primary_exchange": "XNYS",
              "share_class_figi": "BBG001SCTQY4",
              "ticker": "A",
              "type": "CS"
            }
          ],
          "status": "OK"
        }
        """;

    private const string Contracts = """
        {
          "request_id": "603902c0-a5a5-406f-bd08-f030f92418fa",
          "results": [
            {
              "cfi": "OCASPS",
              "contract_type": "call",
              "exercise_style": "american",
              "expiration_date": "2021-11-19",
              "primary_exchange": "BATO",
              "shares_per_contract": 100,
              "strike_price": 85,
              "ticker": "O:AAPL211119C00085000",
              "underlying_ticker": "AAPL"
            },
            {
              "additional_underlyings": [
                { "amount": 44, "type": "equity", "underlying": "VMW" },
                { "amount": 6.53, "type": "currency", "underlying": "USD" }
              ],
              "cfi": "OCASPS",
              "contract_type": "call",
              "exercise_style": "american",
              "expiration_date": "2021-11-19",
              "primary_exchange": "BATO",
              "shares_per_contract": 100,
              "strike_price": 90,
              "ticker": "O:AAPL211119C00090000",
              "underlying_ticker": "AAPL"
            }
          ],
          "status": "OK"
        }
        """;

    // Trimmed to two balance-sheet points and one income-statement point: the sample proves the
    // dictionary deserializes and is reachable by key, not that the statement is complete.
    private const string Financials = """
        {
          "count": 1,
          "next_url": "https://api.massive.com/vX/reference/financials?cursor=next",
          "request_id": "55eb92ed43b25568ab0cce159830ea34",
          "results": [
            {
              "cik": "0001650729",
              "company_name": "SiteOne Landscape Supply, Inc.",
              "end_date": "2022-04-03",
              "filing_date": "2022-05-04",
              "financials": {
                "balance_sheet": {
                  "assets": { "label": "Assets", "order": 100, "unit": "USD", "value": 2407400000 },
                  "equity": { "label": "Equity", "order": 1400, "unit": "USD", "value": 1099200000 }
                },
                "income_statement": {
                  "revenues": { "label": "Revenues", "order": 100, "unit": "USD", "value": 805300000 }
                }
              },
              "fiscal_period": "Q1",
              "fiscal_year": "2022",
              "source_filing_url": "https://api.massive.com/v1/reference/sec/filings/0001650729-22-000010",
              "start_date": "2022-01-03",
              "tickers": [ "SITE" ],
              "timeframe": "quarterly"
            }
          ],
          "status": "OK"
        }
        """;

    private const string FilingFileBody = "<html><body><p>Item 1A. Risk Factors</p></body></html>";

    public Uri? LastRequestUri { get; private set; }

    public int Requests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        Requests++;

        // Keyed on the cursor rather than on a request counter, so the single-page calls and the
        // traversals stay independent of the order they happen to run in.
        bool cursored = request.RequestUri?.Query.Contains("cursor=", StringComparison.Ordinal) == true;

        string body = request.RequestUri?.AbsolutePath switch
        {
            "/stocks/v1/dividends" => Dividends,
            "/v2/reference/news" => News,
            "/v1/indicators/sma/AAPL" => cursored ? SmaLastPage : Sma,
            "/v2/last/trade/AAPL" => LastTradeBody,
            "/v1/open-close/AAPL/2023-01-09" => OpenClose,
            "/v1/marketstatus/upcoming" => Holidays,
            "/v3/trades/AAPL" => Trades,
            "/v2/snapshot/locale/us/markets/stocks/tickers" => Snapshots,
            "/v1/marketstatus/now" => MarketStatusBody,
            "/v3/reference/tickers" => Tickers,
            "/v3/reference/options/contracts" => Contracts,
            "/vX/reference/financials" => Financials,
            "/v1/reference/sec/filings/0001683168-26-006873/files/myx_i10k-053126.htm" => FilingFileBody,
            _ => cursored ? FinalPage : FirstPage,
        };

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}

/// <summary>
/// Answers 503 twice and then 200, so the retry handler actually retries. The shared
/// <see cref="StubHandler"/> above cannot express this, and its request count is asserted.
/// </summary>
internal sealed class FlakyHandler : HttpMessageHandler
{
    private const string Body = """{"status":"OK","request_id":"aot","results":[]}""";

    public Uri? LastRequestUri { get; private set; }

    public int Requests { get; private set; }

    /// <summary>Each attempt's query as it was sent; retry re-sends one request instance.</summary>
    public List<string> SentQueries { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        Requests++;
        SentQueries.Add(request.RequestUri?.Query ?? string.Empty);

        HttpStatusCode status = Requests < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;

        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(Body, Encoding.UTF8, "application/json"),
        });
    }
}
