# Changelog

Notable changes to the MassiveDotNet packages. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html) — with the usual `0.x` caveat that the
public API can still change between minor versions, which is what `0.x` is for.

All four packages share one version and are published together from one run, so a version number
here means the same thing for every package.

Two conventions worth knowing before reading a breaking-change entry:

- **Endpoints are generated from Massive's own OpenAPI description.** When Massive retires a route,
  the map row, the generated methods and the coverage baseline go in one commit, noted as breaking
  here. That is the only sanctioned decrease in endpoint coverage.
- **A deprecated operation ships marked `[Obsolete]`** with the replacement named, and an
  unreleased one ships marked `[Experimental]`. Neither is removed, and nothing is ever silently
  omitted.

## [Unreleased]

## [0.2.0] — unreleased

**The first version published to nuget.org.** The `0.1`/`0.2` numbering follows the project's own
milestones rather than a release history: everything below has been in the repository for some
time, and 0.2.0 is the first version anyone can `dotnet add package`.

### Added

- **`MassiveDotNet.Rest`** — 147 REST operations across stocks, reference data, financials and SEC
  filings, generated from Massive's OpenAPI description. Every non-deprecated operation is reachable
  from the public API, and a contract test fails the build if one is not.
  - Paginated endpoints get `ListXxxAsync` (one page) and `EnumerateXxxAsync`
    (`IAsyncEnumerable<T>`, every page, one in flight at a time, retaining no memory proportional to
    the pages traversed).
  - Comparator parameters collapse to one filter-typed argument per field — `RangeFilter<T>`,
    `SetFilter<T>`, `Filter<T>`, `ArrayFilter<T>` — chosen from the suffix set the description
    declares, so an unsupported comparator is a compile error rather than a silently dropped query
    string. This replaced 1,182 flat parameters, one endpoint of which had a 114-argument method.
  - Cursors are followed verbatim, and only when `next_url` names the same origin as the configured
    `BaseAddress`. A mismatch throws rather than sending the caller's key to a host the response
    body chose.
- **`MassiveDotNet.WebSocket`** — six stock topics (trades, NBBO quotes, second and minute
  aggregates, net order imbalances, limit up-limit down) over a persistent connection, with backoff
  reconnect and subscription replay.
  - Topics are a typed enum. The server silently drops an unrecognised topic code, so every
    subscribe is acknowledgement-counted and throws when the server accepted fewer pairs than were
    asked for.
  - Each topic owns a bounded buffer that drops the oldest event and counts it exactly, so a slow
    consumer never stalls the read loop shared by every other topic.
- **`MassiveDotNet.Extensions.DependencyInjection`** — `AddMassive`, wiring the client through
  `IHttpClientFactory`. The only package permitted to reference `Microsoft.Extensions.*`.
- **`MassiveDotNet`** — the core the three share: transport, both authentication schemes,
  exceptions, and the allocation-conscious serialization helpers.
- Opt-in rate limiting and retry, both off unless configured, composed auth outermost then retry
  then the limiter. The ordering is load-bearing: a retry handler placed outside authentication
  appends the API key once per attempt under the query-string scheme.
- Native AOT support across all four packages, verified by a publish that must emit zero IL
  warnings and by running the resulting native binary.

### Notes for consumers

- **`net10.0` only.** No multi-targeting.
- **NodaTime is the temporal vocabulary.** `Instant`, `LocalDate`, `Duration` and `ZonedDateTime`
  rather than `DateTime`, because a bar window, a tick timestamp and an ex-dividend date are three
  different things that `DateTime` collapses into one. This is the single largest adjustment for a
  caller coming from another SDK.
- **`AddMassive` has no `IConfiguration` overload**, deliberately. `MassiveClientOptions.Timeout` is
  a `Duration`, which the configuration binder cannot convert — and it fails silently, binding clean
  and leaving the default in place. Read the values yourself:
  `options.ApiKey = configuration["Massive:ApiKey"]`.
- The DI package's `Microsoft.Extensions.*` dependencies are published as minimum-version ranges at
  `10.0.0`, as every NuGet dependency is, so a consumer on a lower version is floated upward.
- Some operations that the description declares are not served by the API and answer `404`. They
  ship as declared, with the status and the date observed pinned in a test, because the description
  is the contract for what ships.

[Unreleased]: https://github.com/jerbersoft/massivedotnet/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/jerbersoft/massivedotnet/releases/tag/v0.2.0
