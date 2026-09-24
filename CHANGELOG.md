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

## [0.4.1] — 2026-09-24

### Fixed

- **`MassiveDotNet`** — the opt-in rate limiter no longer releases a second burst after sitting
  idle. A regression introduced in 0.4.0: replenishment moved off the limiter's own timer onto the
  handler's, and while the bucket is full the limiter returns from a replenish without recording
  that any time has passed. Its clock therefore froze for as long as a caller was idle, and the
  first refill after a request finally drained the bucket credited the whole idle at once, capped
  at the allowance — so a caller got their burst and then the entire allowance again immediately
  behind it. On the free tier's five a minute, a minute of quiet released ten requests inside one
  second. The handler now keeps the limiter's clock moving while the bucket is full, at no cost to
  the burst: after any idle the first five still go out at once, and the sixth, seventh and eighth
  land at 12, 24 and 36 seconds exactly as before. Sustained rates are unaffected — the correction
  cannot engage while requests are flowing, since it only applies to a bucket that is already full.
  0.3.0 and earlier are not affected. No public API change.

## [0.4.0] — 2026-09-24

### Added

- **`MassiveDotNet.WebSocket`** — `MassiveStreamEvictedException`, plus
  `MassiveStockStream.EvictionCount` and `MassiveStockStream.LastEvictionMessage`. Massive answers a
  connection it is about to evict with a `max_connections` status and then closes it, so eviction is
  the one documented disconnect cause that announces itself. The exception carries the server's
  message verbatim and reaches a consumer through the existing `Faulted` event; it is **not**
  terminal, and the reconnect policy is unchanged. Because reconnect is on by default an eviction
  normally ends in a reconnect rather than a stop, which is what the two properties are for: read
  them from a `Reconnected` handler to tell an eviction apart from an ordinary drop. The DI
  package's `LogStreamHealth` bridge reports one at `Warning` beside the reconnect it caused.

### Fixed

- **`MassiveDotNet`** — the opt-in rate limiter now delivers the rate it was configured with. It
  drove replenishment from `System.Threading.Timer`, which is given whole milliseconds and adds a
  flat one permit per firing however much time has actually passed, so the delivered rate was
  whatever that timer could express rather than what the caller asked for. Above 60,000 permits a
  minute the refill period rounds to zero, which the timer stores as *fire once*: the limiter
  refilled a single time at construction and never again, so every request after the initial burst
  waited — forever, on the `QueueLimit` default, with nothing reporting it. A period that was not a
  whole number of milliseconds over-delivered instead, by 7% at 7,000 a minute and 15% at 45,000,
  and a firing delayed by a pause lost its permit for good. Replenishment is now driven from the
  time that actually elapsed, computed at tick precision, so a fractional period is honoured
  exactly, a sub-millisecond one works, and a late tick catches up. Measured from 600 to 120,000 a
  minute, every rate now lands within 1% of what was configured. The free tier's five a minute was
  never affected and is unchanged. No public API change.

- **`MassiveDotNet.WebSocket`** — a `max_connections` status arriving mid-stream is no longer
  discarded. It was parsed and then thrown away unless a subscribe happened to be in flight, so the
  abort that followed carried no close code and read exactly like a slow-consumer close or a network
  drop — an operator would audit their own consumer's throughput when the real problem was a second
  process displacing them on the same key. The status now also records the connection's cause, and
  still refuses the subscribe it arrives during, with that subscribe's acknowledgement bookkeeping
  untouched. A drop no status preceded reports exactly what it reported before.

- **`MassiveDotNet`** — `LocalDateJsonConverter` and `InstantJsonConverter` no longer render an
  arbitrarily long value into their failure messages. Both refused a value past 64 bytes already,
  but only on the copying path taken for an escaped value or one split across buffer segments; the
  unescaped fast path, which is every response the service actually sends, parsed the raw span with
  no length check, so a pathological string in a `format: date` or `format: date-time` field was
  echoed whole — and, through the DI package's log bridge, into a log line. The check now runs
  ahead of both paths. A malformed value short enough to be a plausible date or timestamp still
  names itself, as before; one longer than 64 bytes reports `found a longer value`, which is what
  the copying path has always done.

## [0.3.0] — 2026-09-22

### Added

- **`MassiveDotNet.WebSocket`** — `MassiveTopicSubscription<T>.MalformedCount` and
  `MassiveStockStream.MalformedObserved`, reporting events the wire sent in a shape the SDK's schema
  does not accept. Separate from `DroppedCount`/`DropObserved` on purpose: a drop is the consumer's
  own backpressure and is fixed by raising `TopicBufferCapacity`, while a malformed event is the
  server's wire disagreeing with the SDK and the consumer cannot fix it at all. `MalformedObserved`
  carries the `JsonException`, throttled to at most one raise a second on its own window. The DI
  package's `LogStreamHealth` bridges it to `ILogger` at `Warning`.

### Fixed

- **`MassiveDotNet.WebSocket`** — a value a converter refuses no longer ends the connection. The
  event is dropped and counted, and the read loop carries on. The connection is multiplexed, so one
  unparseable field on one symbol used to take every other topic's live data down with it. A
  malformed `ev` is still terminal: it leaves the reader stranded mid-object with no topic to
  attribute the loss to.
- **`MassiveDotNet`** — a numeric range failure now names the value it refused:
  `The number in StockAggregate.z (12.0) does not fit a 64-bit integer.` `Utf8JsonReader`'s
  `TryGetInt64` returns `false` for three unrelated reasons and the old message distinguished none
  of them. Capped at 32 bytes, and confined to that one failure: the wrong-token-type message names
  a token type and has no value to quote, and the decimal round-trip message reads a string token,
  where "the bytes are provably a number" does not hold.
- **`MassiveDotNet.WebSocket`** — the SDK now tells the server it is leaving. Every teardown used to
  abort the socket, because `ClientWebSocket.Dispose()` does not perform the closing handshake, so a
  server could not tell a deliberate disconnect from a network failure — which matters on a plan
  allowing one connection per cluster. The close is send-only and bounded: collecting a reply would
  need something pumping `ReceiveAsync`, and on both teardown paths nothing is — disposal has
  already stopped the read loop, and the reconnect handover runs on it. No public API change.

## [0.2.0] — 2026-09-09

**The first version published to nuget.org.** The `0.1`/`0.2` numbering follows the project's own
milestones rather than a release history: everything below has been in the repository for some
time, and 0.2.0 is the first version anyone can `dotnet add package`.

### Added

- **`MassiveDotNet.Rest`** — **60 of the platform's 147 REST operations**, generated from Massive's
  OpenAPI description: 21 under `Stocks` (aggregates, trades, quotes, snapshots, indicators) and 39
  under `Reference` (tickers, news, corporate actions, financials, SEC filings, exchanges and
  conditions). Options, crypto, forex, futures, indices and the vendor datasets are not in this
  release. Coverage is contract-tested and cannot regress: an endpoint may be added, never removed,
  except when Massive retires the route from the description itself.
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

[Unreleased]: https://github.com/jerbersoft/massivedotnet/compare/v0.4.1...HEAD
[0.4.1]: https://github.com/jerbersoft/massivedotnet/releases/tag/v0.4.1
[0.4.0]: https://github.com/jerbersoft/massivedotnet/releases/tag/v0.4.0
[0.3.0]: https://github.com/jerbersoft/massivedotnet/releases/tag/v0.3.0
[0.2.0]: https://github.com/jerbersoft/massivedotnet/releases/tag/v0.2.0
