---
_layout: landing
---

<div class="text-center my-5">
  <h1 class="display-4 fw-bold">MassiveDotNet</h1>
  <p class="lead">
    A .NET 10 SDK for the <strong>Massive</strong> market data platform (formerly Polygon.io) — REST,
    WebSocket streaming, NodaTime throughout, and Native AOT clean.
  </p>
  <p>
    <a class="btn btn-primary btn-lg" href="../README.md">Reference</a>
    <a class="btn btn-outline-secondary btn-lg" href="api/index.md">API reference</a>
    <a class="btn btn-outline-secondary btn-lg" href="../CHANGELOG.md">Changelog</a>
  </p>
</div>

```csharp
using MassiveDotNet.Rest;
using MassiveDotNet.Rest.Models;

using MassiveRestClient client = new(Environment.GetEnvironmentVariable("MASSIVE_API_KEY")!);

LastTrade trade = await client.Stocks.GetLastTradeAsync("AAPL");
```

That is the whole shape of it. Endpoint groups hang off `MassiveRestClient`; every one of them
speaks NodaTime, throws on failure rather than returning a result type, and reads its rows straight
off the JSON tokens.

## Install

Four packages, all `net10.0`, published together to **nuget.org**. Most consumers want
`MassiveDotNet.Rest`.

```bash
dotnet add package MassiveDotNet.Rest
```

`MassiveDotNet.WebSocket` is live streaming, `MassiveDotNet.Extensions.DependencyInjection` is
`AddMassive` and brings both, and `MassiveDotNet` is the shared core the others depend on.
[Install](../README.md#install) in the README is the full account, including the `-ci.N` prerelease
stream that every green push to `master` publishes.

## The three things worth knowing up front

**The endpoints are generated, and coverage is a contract test.** They come from Massive's own
OpenAPI description plus a curated map of what the description does not say — asset class, method
names, .NET parameter types. A contract test holds the surface to the description, so an endpoint
can be added but never quietly dropped. [Coverage](../README.md#coverage) is the current state:
60 of the platform's 147 REST operations, across Stocks and Reference.

**Time is NodaTime, everywhere, and the BCL date types are banned by a test.** An aggregate window
is Eastern Time, a tick timestamp is epoch nanoseconds, and a dividend's ex-date is a calendar date
with no zone at all — `DateTime` collapses all three into one type whose meaning rides on a `Kind`
flag. So `Instant`, `LocalDate`, `ZonedDateTime` and `Duration` are the vocabulary, and no BCL
temporal type is *named* anywhere in this repository's source.
[Dates and times](../README.md#dates-and-times) has the mapping.

**Allocation is a gate, not an aspiration.** Tick-level rows are structs read by generated
converters straight off the JSON tokens, arrays come from a pool, and request URIs are built without
a dictionary. A 50,000-row page of aggregates allocates the array and about 2.8 KB besides. The
ceilings are asserted in the offline test suite; the measured figures are in
[docs/performance](performance/2026-09-04-allocation-figures.md), and
[Allocation](../README.md#allocation) explains what buys what.

## Where things are

| | |
|---|---|
| [Reference](../README.md) | The README — install, the shape of the API, filters, errors, rate limiting, streaming, AOT, allocation, client lifetime, coverage. Where this site and the README disagree, the README wins. |
| [API reference](api/index.md) | Every public member of all four packages, generated from the same XML documentation comments IntelliSense shows you. |
| [Changelog](../CHANGELOG.md) | What changed in each version, and what broke. |
| [GitHub](https://github.com/jerbersoft/massivedotnet) | Source, issues, and the decision record in `CLAUDE.md`. |
