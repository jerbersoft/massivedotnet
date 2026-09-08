# MassiveDotNet

A .NET 10 SDK for the **Massive** market data platform (formerly Polygon.io).

[![ci](https://github.com/jerbersoft/massivedotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/jerbersoft/massivedotnet/actions/workflows/ci.yml)

Endpoints are generated from Massive's own OpenAPI description, so the surface tracks the platform
rather than drifting from it. The design goals, in priority order, are **complete endpoint
coverage**, **Native AOT compatibility**, and **minimal allocation** — then ergonomics.

## Status

Pre-release, and **not yet published to NuGet** — packaging is [#19](https://github.com/jerbersoft/massivedotnet/issues/19).
Until it lands, reference the projects directly:

```bash
git clone https://github.com/jerbersoft/massivedotnet.git
cd massivedotnet
dotnet build MassiveDotNet.slnx
```

```xml
<ProjectReference Include="path/to/massivedotnet/src/MassiveDotNet.Rest/MassiveDotNet.Rest.csproj" />
```

**60 of the platform's 147 REST operations** ship today, across two groups. See
[Coverage](#coverage) for what is here and what is not.

## Quickstart

```csharp
using MassiveDotNet.Rest;
using MassiveDotNet.Rest.Models;

string apiKey = Environment.GetEnvironmentVariable("MASSIVE_API_KEY")!;

// One client per application. It is thread-safe, owns a pooled HttpClient, and is meant to be
// long-lived -- creating one per request exhausts sockets under load.
using MassiveRestClient client = new(apiKey);

LastTrade trade = await client.Stocks.GetLastTradeAsync("AAPL");

Console.WriteLine($"AAPL last trade  {trade.Price:N2} x {trade.Size} at {trade.SipTimestamp}");
// AAPL last trade  327.99 x 0 at 2026-09-03T21:00:20Z
```

The key is sent as an `Authorization: Bearer` header, not as the `apiKey` query parameter the
platform's description declares — query strings leak into access logs, proxies, and browser
history. Query-string auth is still available through `MassiveAuthenticationScheme.QueryString`.

`samples/MassiveDotNet.Quickstart` is a runnable version of the walkthrough below. Every snippet in
[The shape of the API](#the-shape-of-the-api) is lifted from it, so the build is what keeps them
honest — a signature change that would silently rot this page breaks that project instead. The
`AddMassive` registrations under [Dependency injection](#dependency-injection) call the same API
that `samples/MassiveDotNet.HostSample` and `samples/MassiveDotNet.WebSample` compile; the
resilience line there is illustrative, since it needs a package neither sample references.

```bash
export MASSIVE_API_KEY=...
dotnet run --project samples/MassiveDotNet.Quickstart
```

## The shape of the API

### Groups

Endpoints hang off groups that mirror the platform's own taxonomy. Groups are `readonly struct`
over the shared transport, so navigating to one allocates nothing.

```csharp
client.Stocks.ListAggregatesAsync(...)
client.Reference.ListTickersAsync(...)
```

### One value, one page, or every page

A singular operation returns the value itself, and throws if the service answers `200` without one:

```csharp
LastTrade trade = await client.Stocks.GetLastTradeAsync("AAPL");
```

A paginated operation gets two methods. `ListXxxAsync` returns a single page and reports whether
more exist:

```csharp
MassivePage<Agg> page = await client.Stocks.ListAggregatesAsync(
    "AAPL",
    multiplier: 1,
    timespan: AggregateTimespan.Day,
    from: new LocalDate(2024, 1, 2),
    to: new LocalDate(2024, 1, 12),
    adjusted: true,
    sort: SortOrder.Ascending);

Console.WriteLine($"{page.Results.Length} bars (more: {page.HasMore})");
```

`EnumerateXxxAsync` follows the server's cursor across every page, holding one page in memory at a
time:

```csharp
await foreach (TickerSummary ticker in client.Reference.EnumerateTickersAsync(
    market: MarketType.Stocks,
    active: true,
    limit: 100))
{
    Console.WriteLine($"{ticker.Ticker,-8} {ticker.Name}");
}
```

The split follows the BCL's own `Directory.EnumerateFiles` / `Directory.GetFiles` distinction.
Operations that do not paginate return `T[]`, or `T` for a singular result. Whether an operation
paginates is detected from the description, never declared by hand.

Cursors are followed **verbatim**, and only when `next_url` names the same origin as the configured
base address. A cursor pointing elsewhere throws rather than being followed, because the SDK
re-attaches your API key to every page and the cursor is chosen by the response body.

### Filters

Massive declares comparator variants as separate query parameters — `strike_price.gt`,
`strike_price.gte`, `strike_price.lte`, and so on. Flattened, that gave one endpoint a 114-argument
method. Here each field collapses to **one** parameter, typed by the comparators it actually
supports:

| Type | Comparators the field declares |
|------|-------------------------------|
| `RangeFilter<T>` | `.gt` `.gte` `.lt` `.lte` |
| `SetFilter<T>` | `.any_of` |
| `Filter<T>` | both of the above |
| `ArrayFilter<T>` | `.any_of` `.all_of` |

A plain value converts implicitly to equality, so the common case reads as if there were no filter
type at all:

```csharp
MassivePage<ReferenceDividend> dividends = await client.Reference.ListDividendsAsync(
    ticker: "AAPL",                                    // implicit equality
    exDividendDate: RangeFilter.Between(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 31)),
    order: SortOrder.Ascending,
    limit: 10);
```

Because the grouping is read from the description rather than declared by hand, asking for a
comparator an endpoint does not support is a **compile error** instead of a parameter the server
silently drops:

```csharp
MassivePage<FinancialRatios> screened = await client.Reference.ListRatiosAsync(
    priceToEarnings: RangeFilter.Between(5d, 15d),
    dividendYield: RangeFilter.Gt(0.03),
    limit: 5);
```

### Dates and times

The SDK uses [NodaTime](https://nodatime.org) throughout. No BCL `DateTime`, `DateTimeOffset`,
`DateOnly`, `TimeOnly`, or `TimeSpan` is named anywhere in its source — not in a signature, not in
a private field, not in a local. The rule is machine-checked by reflection over the exported
surface plus a comment- and literal-aware scan of every tree in the repository.

Market data is unforgiving about temporal ambiguity: an aggregate window is Eastern Time, a tick
timestamp is epoch nanoseconds, and a dividend's ex-date is a calendar date carrying no time or
zone at all. `DateTime` collapses all three into one type whose meaning rides on a `Kind` flag that
is trivially lost. NodaTime keeps them distinct, so the wrong one does not compile.

| Concept | Type | Example |
|---------|------|---------|
| A moment on the global timeline | `Instant` | `Agg.Timestamp`, `LastTrade.SipTimestamp` |
| A calendar date, no time or zone | `LocalDate` | ex-dividend date, IPO date |
| A wall-clock time in a named zone | `ZonedDateTime` | session open and close |
| An elapsed amount of time | `Duration` | `MassiveClientOptions.Timeout` |

Wire timestamps are stored as raw `long` epoch values, with the NodaTime type exposed as a computed
property, so a 50,000-row series pays for the conversion only on the values you actually read:

```csharp
foreach (Agg bar in page.Results)
{
    // TimestampMilliseconds is the raw wire value; Timestamp computes the Instant on demand.
    Console.WriteLine($"  {bar.Timestamp}  o {bar.Open:N2}  h {bar.High:N2}  l {bar.Low:N2}  c {bar.Close:N2}");
}
```

NodaTime's default `LocalDate` formatting is a long culture string. For ISO output use `uuuu-MM-dd`
— in NodaTime `uuuu` is the absolute year, where `yyyy` is the era year:

```csharp
Console.WriteLine($"{dividend.ExDividendDate:uuuu-MM-dd}");   // 2024-02-09
```

### Errors

Failures surface as exceptions rather than a result type, so the allocation happens only on the
failure path:

```csharp
try
{
    LastTrade trade = await client.Stocks.GetLastTradeAsync("AAPL");
}
catch (MassiveRateLimitExceededException exception)
{
    // The free tier allows five requests a minute, so this is the first error most callers meet.
    Console.Error.WriteLine($"Rate limited. Retry after: {exception.RetryAfter}");
}
catch (MassiveApiException exception)
{
    Console.Error.WriteLine($"{(int)exception.StatusCode} {exception.Message} (request {exception.RequestId})");
}
```

`RequestId` is what Massive's support asks for. `RetryAfter` is a NodaTime `Duration`, read from the
response's `Retry-After` header when the server sends one.

Rate limiting and retry are **not** applied automatically — the SDK cannot know your tier, and
silently retrying is not a decision to make on a caller's behalf. Both are available opt-in; see
[Rate limiting and retry](#rate-limiting-and-retry).

## Rate limiting and retry

Off by default, because the SDK never learns your entitlement: a default tuned for the free tier's
five requests a minute would throttle a paid key to a fraction of its allowance, and any other
default would rate-limit the callers it was meant to protect. Each feature is one option.

```csharp
using MassiveRestClient client = new(new MassiveClientOptions
{
    ApiKey = apiKey,

    // Pace requests so you stay inside your tier rather than discovering it through a 429.
    RateLimit = new MassiveRateLimitOptions
    {
        PermitsPerWindow = 5,
        Window = Duration.FromMinutes(1),
    },

    // Ride out a transient 429 or 5xx. Nothing else is retried.
    Retry = new MassiveRetryOptions { MaxAttempts = 3 },
});
```

They are separate options because they solve different problems — on a paid tier you may want to
survive a transient 502 without pacing your requests at all.

**Throttling.** Permits refill continuously rather than all at once on a window boundary, so a
burst up to `PermitsPerWindow` goes out immediately and everything after it is paced. By default a
request over the allowance waits; set `QueueLimit = 0` to have it throw
`MassiveRateLimitExceededException` without reaching the network instead.

**Retry.** Only HTTP 429 and 5xx. Every other 4xx describes a request that fails identically
however often it is sent, so retrying one spends quota to reach the same answer. Backoff is
exponential with jitter, and a server's `Retry-After` takes precedence over the computed delay —
except when it exceeds `MaxBackoff`, where the 429 surfaces with its hint intact rather than
holding your task for a period the SDK did not choose:

```csharp
catch (MassiveRateLimitExceededException exception)
{
    // Retry gave up, or the server asked for longer than MaxBackoff.
    Console.Error.WriteLine($"Still limited. The server asked for {exception.RetryAfter}.");
}
```

Both work identically under `AddMassive`, which reads the same options.

## Streaming

`MassiveDotNet.WebSocket` is the fourth package in the SDK, alongside core, `.Rest`, and
`.Extensions.DependencyInjection`. It streams trades and quotes over a persistent connection to
the platform's stocks feed, reconnecting with backoff and replaying every subscription when the
connection drops.

```csharp
using MassiveDotNet.WebSocket;
using MassiveDotNet.WebSocket.Events;

await using MassiveStreamClient client = new(new MassiveStreamOptions { ApiKey = apiKey });
await using MassiveStockStream stream = await client.ConnectStocksAsync();

MassiveTopicSubscription<StockTrade> trades = await stream.SubscribeTradesAsync(["AAPL"]);

await foreach (StockTrade trade in trades)
{
    Console.WriteLine($"{trade.Ticker}  {trade.Price:N2} x {trade.Size}  at {trade.SipTimestamp}");
}
```

Topics are a typed enum, `StockTopic`, rather than a string: the server silently drops a topic code
it does not recognise — no acknowledgement, no error — so a caller who mistypes a string would see
a healthy connection producing nothing, indefinitely. Every subscribe call is
acknowledgement-counted for the same reason, and throws `MassiveStreamSubscriptionException` if the
server accepted fewer pairs than were requested.

Each topic owns one bounded buffer, `TopicBufferCapacity` events by default (1024). A slow consumer
drops the **oldest** event rather than blocking the read loop — every topic shares one socket, so a
writer that waits would stall every other topic on the same connection, not just its own. A drop is
counted exactly, on `MassiveTopicSubscription<T>.DroppedCount`, and raised as an event:

```csharp
stream.DropObserved += (topicCode, droppedCount) =>
    Console.Error.WriteLine($"{topicCode} dropped an event (total dropped: {droppedCount})");
```

Authentication travels in a message rather than a header, because the wire protocol has no concept
of one. A refused key and a plan without WebSocket access for the market both answer `auth_failed`,
distinguished only by the server's own prose, so `MassiveStreamAuthenticationException.ServerMessage`
carries it verbatim rather than a guessed category. This failure is terminal — the stream does not
retry it — unlike a dropped connection, which reconnects automatically by default.

`AddMassiveStream` registers `MassiveStreamClient` as a singleton the same way `AddMassive` does for
the REST client, and `LogStreamHealth` bridges reconnects and drops onto `ILogger` without core ever
learning that logging exists:

```csharp
builder.Services.AddMassiveStream(options => options.ApiKey = builder.Configuration["Massive:ApiKey"]);
```

```csharp
stream.LogStreamHealth(logger);
```

## Native AOT

The SDK is `IsAotCompatible` and `IsTrimmable`, and uses `System.Text.Json` source generation
exclusively — there is no reflection-based serialization anywhere in shipped code.

`samples/MassiveDotNet.AotSmokeTest` is the enforcement mechanism, not a tutorial: it disables the
reflection fallback outright and is published in CI, where **any** IL trim or AOT warning fails the
build.

```bash
dotnet publish samples/MassiveDotNet.AotSmokeTest -r osx-arm64 -c Release
```

Core takes exactly two external dependencies: NodaTime, verified AOT-clean at 3.3.3 including TZDB
zone resolution, and `System.Threading.RateLimiting`, which backs the opt-in throttle. Neither
carries a transitive dependency of its own, and the smoke test reaches both — an unexercised type
is trimmed away, so a clean publish only proves what the sample actually calls.

## Allocation

Minimal allocation is the third design priority, so it is measured rather than asserted. Two
instruments, because allocation is byte-exact and elapsed time is not:

- **`AllocationTests`** runs with the offline suite and fails the build on a regression. It asserts
  ceilings on request URI building, comparator rendering, aggregate deserialization at 1k / 10k /
  50k rows, and per-page cost across a cursor traversal — and asserts **exactly zero** for group
  navigation, which is the one place where zero is the actual claim.
- **`benchmarks/MassiveDotNet.Benchmarks`** produces the comparative figures against naive
  baselines. It never runs in CI, because a timing assertion on a shared runner is noise.

Headline numbers, measured 2026-09-04 on an M4 Max:

| | SDK | Naive baseline |
|---|---:|---|
| Build an aggregates request URI | **51 ns, 200 B** | `UriBuilder` + `Dictionary`: 410 ns, 1,856 B |
| Read 50,000 aggregate bars | **24.1 ms, 32.0 MB** | same, body buffered into a string: 25.6 ms, 49.5 MB |
| Navigate to a group | **0 B** | — |

The 200 bytes are the returned string and nothing else. Streaming the response body rather than
buffering it saves half the allocation at every payload size.

One figure is not flattering and is documented anyway: reading 50,000 rows allocates between 7.6x
and 8.8x the 4.2 MB array it returns — the range is how warm the JIT is — because
`System.Text.Json` builds a JSON array through a doubling `List<T>`, where every growth slot costs
a full 88-byte struct. That is [#47][issue-47]; the ceilings pin it so it cannot get worse, and
they come down with the fix.

Full tables, baselines, and the reasoning behind every ceiling are in
[`docs/performance/2026-09-04-allocation-figures.md`](docs/performance/2026-09-04-allocation-figures.md).

```bash
dotnet run --project benchmarks/MassiveDotNet.Benchmarks -c Release
```

[issue-47]: https://github.com/jerbersoft/massivedotnet/issues/47

## Client lifetime

`MassiveRestClient` is thread-safe and meant to be **long-lived**. Create one per application and
share it; creating one per request exhausts sockets under load.

```csharp
using MassiveRestClient client = new(new MassiveClientOptions
{
    ApiKey = apiKey,
    BaseAddress = MassiveEndpoints.Production,
    Timeout = Duration.FromSeconds(30),
    UserAgent = "my-app/1.0",
});
```

### Dependency injection

`MassiveDotNet.Extensions.DependencyInjection` wires the client through `IHttpClientFactory`. It is
a separate package so a REST consumer never pulls the DI stack — it is the only project allowed to
reference `Microsoft.Extensions.*`, and CI asserts that.

```csharp
builder.Services.AddMassive(options =>
{
    options.ApiKey = builder.Configuration["Massive:ApiKey"];
    options.UserAgent = "my-app/1.0";
});
```

`MassiveRestClient` then arrives by injection like any other service. It and `MassiveHttpTransport`
are registered as **singletons**, matching the lifetime the client documents for itself.

`AddMassive` returns the `IHttpClientBuilder` for the underlying named client, so resilience,
logging, or any other handler goes on the same pipeline:

```csharp
builder.Services.AddMassive(apiKey)
       .AddStandardResilienceHandler();   // Microsoft.Extensions.Http.Resilience
```

There is deliberately **no** overload that binds an `IConfiguration` section. `Timeout` is a
NodaTime `Duration`, which the configuration binder cannot convert — a bound section compiles,
publishes AOT clean, raises nothing at runtime, and silently keeps the default. Reading the values
yourself costs a line and fails visibly instead.

Registering twice layers another options delegate without attaching the key twice, and the
`Authorization` header's value is redacted in the factory's logs no matter how you configure
logging — the SDK put the key there, so it is the SDK's job to keep it out of your log sink.

Both samples in `samples/` register the SDK this way: `MassiveDotNet.HostSample` on the Generic
Host, `MassiveDotNet.WebSample` on a minimal API. Both publish Native AOT with zero warnings in CI.

### Without dependency injection

Hand the transport an `HttpClient` you own and it will not manage that client's lifetime. Add
`MassiveAuthenticationHandler` so every request — including pages fetched while following a cursor
— carries the key. Both types live in `MassiveDotNet.Http`:

```csharp
HttpClient http = factory.CreateClient("massive");
MassiveHttpTransport transport = new(http);
MassiveRestClient client = new(transport);
```

To get authentication and the resilience handlers in the order the SDK composes them, build the
pipeline rather than assembling it by hand:

```csharp
HttpMessageHandler pipeline = MassiveHttpTransport.CreateHandlerPipeline(options, new SocketsHttpHandler());
HttpClient http = new(pipeline) { BaseAddress = options.BaseAddress };
```

Order matters and its failure mode is silent. `MassiveAuthenticationHandler` rewrites the request
URI under the query-string scheme, so a retry handler placed **outside** it re-authenticates every
attempt and appends the key again — `?apiKey=k&apiKey=k&apiKey=k`. The request still succeeds; the
only symptom is your key reaching access logs once per attempt.

`BaseAddress` is required on that `HttpClient`: pagination checks a cursor's origin against it
before sending your key, and throws if it is unset.

The transport does not own a caller-supplied `HttpClient`, so disposing it leaves the factory's
client — and its pooled connections — alone.

## Coverage

| Group | Operations | Methods |
|-------|-----------:|--------:|
| `Stocks` | 21 | 32 |
| `Reference` | 39 | 69 |
| **Total** | **60 of 147** | **101** |

The method count exceeds the operation count because each of the 40 paginated operations gets both
a `List` and an `Enumerate`. The odd one out is `Reference.DownloadFilingFileAsync`, the SDK's only
hand-written endpoint method: the SEC filing-file route declares JSON and serves `text/html`, so
the generated method ships as declared and a byte-copying download sits beside it.

Not yet mapped, each tracked by its own issue:
[Options](https://github.com/jerbersoft/massivedotnet/issues/10) ·
[Crypto](https://github.com/jerbersoft/massivedotnet/issues/11) ·
[Forex](https://github.com/jerbersoft/massivedotnet/issues/12) ·
[Futures](https://github.com/jerbersoft/massivedotnet/issues/13) ·
[Indices](https://github.com/jerbersoft/massivedotnet/issues/14) ·
[vendor datasets](https://github.com/jerbersoft/massivedotnet/issues/16) ·
[cross-market snapshots](https://github.com/jerbersoft/massivedotnet/issues/17).

WebSocket streams ship today for the stocks market; the other five markets are tracked by
[#21](https://github.com/jerbersoft/massivedotnet/issues/21). S3 flat files are planned for v0.3.

### Deprecated and experimental operations

Nothing in the description is silently omitted. An operation Massive marks deprecated ships with
`[Obsolete]` under diagnostic `MASSIVE0002`, a warning whose message names the method that replaces
it. An operation on a `vX`-prefixed or `dev` route ships with `[Experimental("MASSIVE0001")]`, which
is an **error** until you opt in:

```xml
<NoWarn>$(NoWarn);MASSIVE0001</NoWarn>
```

Both are read from the description itself, never declared by hand, so neither can drift from what
Massive publishes.

## Building and testing

```bash
dotnet build MassiveDotNet.slnx                    # warning-free; warnings are errors
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
```

The suite has two tiers. The offline tier runs against committed fixtures and needs no key — it is
all CI ever runs. The live tier calls the real service, is committed and compiled everywhere, and
runs only locally with a key in a gitignored `.env`:

```bash
dotnet test MassiveDotNet.slnx --filter "Category=Integration"
```

No Massive key is ever placed in CI. A key there would leak through build logs, consume quota on
every push, make the build depend on a third party's uptime, and could not work for pull requests
from forks, where secrets are deliberately withheld.

Endpoints are generated from `specs/openapi.json` plus a curated `specs/endpoints.map.json`, and
the output is committed so diffs stay reviewable and consumers need no build step:

```bash
dotnet run --project tools/MassiveDotNet.CodeGen
git diff --exit-code src/                          # generation is deterministic
```

`CLAUDE.md` carries the full architecture decision record, including the reasoning behind the
choices summarised here.

## License

MIT. See [LICENSE](LICENSE).
