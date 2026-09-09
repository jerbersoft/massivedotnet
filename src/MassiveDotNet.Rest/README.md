# MassiveDotNet.Rest

REST client for the **Massive** market data platform (formerly Polygon.io), for .NET 10.

```bash
dotnet add package MassiveDotNet.Rest
```

## Quick start

```csharp
using MassiveDotNet;
using MassiveDotNet.Rest;
using MassiveDotNet.Rest.Models;

// One client per application. It is thread-safe, owns a pooled HttpClient, and is meant to be
// long-lived: creating one per request exhausts sockets under load.
using MassiveRestClient client = new(Environment.GetEnvironmentVariable("MASSIVE_API_KEY")!);

await foreach (TickerSummary ticker in client.Reference.EnumerateTickersAsync(
    market: MarketType.Stocks,
    active: true,
    limit: 100))
{
    Console.WriteLine($"{ticker.Ticker,-8} {ticker.Name}");
}
```

Every snippet above is lifted from `samples/MassiveDotNet.Quickstart`, which the build compiles, so
a signature change breaks that project rather than silently rotting this page.

## The shape of it

Endpoints hang off groups that mirror the platform's own taxonomy, and groups are `readonly struct`
over the shared transport, so navigating to one allocates nothing:

```csharp
client.Stocks.ListAggregatesAsync(...)
client.Reference.ListTickersAsync(...)
```

Every paginated endpoint gets two methods, following the BCL's own
`Directory.EnumerateFiles`/`GetFiles` distinction:

- `ListXxxAsync` returns one page and reports whether more exist.
- `EnumerateXxxAsync` returns `IAsyncEnumerable<T>` and walks every page, one in flight at a time,
  retaining no memory proportional to the pages traversed.

Comparator parameters collapse to one filter-typed argument per field rather than six flat ones, so
an unsupported comparator is a compile error instead of a silently dropped query string.

## Why it is built this way

The whole REST surface is **generated from Massive's own OpenAPI description**, and the generated
code is committed — so the surface tracks the platform rather than drifting from it, and every
change to it arrives as a reviewable diff. Deprecated operations ship marked `[Obsolete]` with the
replacement named; unreleased ones ship marked `[Experimental]`. Nothing is silently omitted.

Native AOT clean, source-generated JSON only, and deliberately allocation-conscious: a 50,000-row
page deserializes into one array rather than 50,000 objects.

Licensed MIT. Issues and source at
[github.com/jerbersoft/massivedotnet](https://github.com/jerbersoft/massivedotnet).
