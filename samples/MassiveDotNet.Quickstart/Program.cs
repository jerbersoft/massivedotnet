using MassiveDotNet;
using MassiveDotNet.Rest;
using MassiveDotNet.Rest.Models;
using NodaTime;

// A runnable tour of the REST surface against the live service. Every snippet in the README is
// lifted from this file, so the build is what keeps those snippets honest: a signature change
// that would silently rot the documentation breaks this project instead.
//
// The key is read from the environment and never printed (constitution rule 11).

if (Environment.GetEnvironmentVariable("MASSIVE_API_KEY") is not { Length: > 0 } apiKey)
{
    Console.Error.WriteLine("Set MASSIVE_API_KEY to a Massive API key and run again.");
    return 1;
}

// One client per application. It is thread-safe, owns a pooled HttpClient, and is meant to be
// long-lived -- creating one per request exhausts sockets under load.
using MassiveRestClient client = new(apiKey);

try
{
    await OneValueAsync(client);
    await OnePageAsync(client);
    await EveryPageAsync(client);
    await FilteringAsync(client);
}
catch (MassiveRateLimitExceededException exception)
{
    // The free tier allows five requests a minute, so this is the first error most callers meet.
    Console.Error.WriteLine($"Rate limited. Retry after: {exception.RetryAfter}");
    return 2;
}
catch (MassiveApiException exception)
{
    // Errors surface as exceptions rather than a result type (decision D3). The request id is
    // what Massive's support asks for.
    Console.Error.WriteLine($"{(int)exception.StatusCode} {exception.Message} (request {exception.RequestId})");
    return 3;
}

return 0;

// A singular Get returns the value itself, and throws if the service answers 200 without one.
static async Task OneValueAsync(MassiveRestClient client)
{
    LastTrade trade = await client.Stocks.GetLastTradeAsync("AAPL");

    // Timestamps are stored as raw epoch longs and converted only when read (decision D5).
    // SipTimestamp is an Instant: a moment on the global timeline, with no zone to lose.
    Console.WriteLine($"AAPL last trade  {trade.Price:N2} x {trade.Size} at {trade.SipTimestamp}");
}

// A paginated operation's List returns one page and reports whether more exist.
static async Task OnePageAsync(MassiveRestClient client)
{
    MassivePage<Agg> page = await client.Stocks.ListAggregatesAsync(
        "AAPL",
        multiplier: 1,
        timespan: AggregateTimespan.Day,
        from: new LocalDate(2024, 1, 2),
        to: new LocalDate(2024, 1, 12),
        adjusted: true,
        sort: SortOrder.Ascending);

    Console.WriteLine($"\ndaily bars       {page.Results.Length} (more: {page.HasMore})");

    foreach (Agg bar in page.Results)
    {
        // TimestampMilliseconds is the raw wire value; Timestamp computes the Instant on demand.
        Console.WriteLine($"  {bar.Timestamp}  o {bar.Open:N2}  h {bar.High:N2}  l {bar.Low:N2}  c {bar.Close:N2}");
    }
}

// Enumerate follows the server's cursor across every page, holding one page at a time. The
// cursor is followed verbatim and only when it names the configured origin (decision D14).
static async Task EveryPageAsync(MassiveRestClient client)
{
    Console.WriteLine("\nactive tickers");

    int seen = 0;

    await foreach (TickerSummary ticker in client.Reference.EnumerateTickersAsync(
        market: MarketType.Stocks,
        active: true,
        limit: 100))
    {
        Console.WriteLine($"  {ticker.Ticker,-8} {ticker.Name}");

        // Bounded on purpose: the full traversal is thousands of pages, and this is a sample.
        if (++seen == 5)
        {
            break;
        }
    }
}

// A field that carries comparator variants collapses to one filter-typed parameter (decision
// D15). A plain value converts implicitly to equality, so filters cost nothing until wanted.
static async Task FilteringAsync(MassiveRestClient client)
{
    MassivePage<ReferenceDividend> dividends = await client.Reference.ListDividendsAsync(
        ticker: "AAPL",
        exDividendDate: RangeFilter.Between(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 31)),
        order: SortOrder.Ascending,
        limit: 10);

    Console.WriteLine($"\nAAPL 2024 dividends  {dividends.Results.Length}");

    foreach (ReferenceDividend dividend in dividends.Results)
    {
        // NodaTime's default LocalDate formatting is a long culture string; "uuuu-MM-dd" is the
        // ISO pattern. In NodaTime "uuuu" is the absolute year, where "yyyy" is the era year.
        Console.WriteLine($"  ex {dividend.ExDividendDate:uuuu-MM-dd}  {dividend.CashAmount:N4}");
    }

    // Screening reads the same way: every ratio the endpoint reports is a range you can filter on.
    MassivePage<FinancialRatios> screened = await client.Reference.ListRatiosAsync(
        priceToEarnings: RangeFilter.Between(5d, 15d),
        dividendYield: RangeFilter.Gt(0.03),
        limit: 5);

    Console.WriteLine($"\nvalue screen         {screened.Results.Length}");

    foreach (FinancialRatios row in screened.Results)
    {
        Console.WriteLine($"  {row.Ticker,-8} p/e {row.PriceToEarnings,7:N2}  yield {row.DividendYield,6:P2}");
    }
}
