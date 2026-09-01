# MassiveDotNet

A .NET 10 SDK for the **Massive** market data platform (formerly Polygon.io), covering the
REST API, WebSocket streams, and S3 flat files.

Design goals, in priority order: **complete endpoint coverage**, **Native AOT compatibility**,
**minimal allocation**, then ergonomics. Where the first three conflict with convenience, they win —
but never silently: prefer an additive opt-in surface over degrading the default one.

---

## Non-negotiables

Every rule below names how it is enforced. A rule with no enforcement mechanism is a suggestion,
and does not belong in this section.

| # | Rule | Enforced by |
|---|------|-------------|
| 1 | Every non-deprecated REST operation in `specs/openapi.json` is reachable from the public API. | `EndpointCoverageTests` — build fails |
| 2 | Deprecated operations ship, marked `[Obsolete]`. `vX` operations ship, marked `[Experimental]`. Nothing is silently omitted. | Code review against the map |
| 3 | No reflection-based serialization anywhere in shipped code. `System.Text.Json` source generation only. | AOT smoke publish must emit **zero** IL warnings |
| 4 | Shipped libraries set `IsAotCompatible` and `IsTrimmable`. | `src/Directory.Build.props`, verified by AOT publish |
| 5 | Generated files (`*.g.cs`) are never hand-edited. Hand-written members go in the matching `partial`. | CI regenerates and fails on any diff |
| 6 | The generator is deterministic: same inputs produce byte-identical output. | CI idempotency check |
| 7 | `MassiveDotNet` (core) references **no** external package other than NodaTime (rule 12). Every other project stays dependency-free unless listed here. | CI assertion on the restore graph |
| 8 | `Microsoft.Extensions.*` appears only in `MassiveDotNet.Extensions.DependencyInjection`. | CI assertion on the restore graph |
| 9 | Builds are warning-free. `TreatWarningsAsErrors` is on and is not to be relaxed per-project. | CI build |
| 10 | Every public member carries XML documentation. | `GenerateDocumentationFile` + warnings-as-errors (CS1591) |
| 11 | API keys are never logged, echoed in exception messages, or written to disk. | Code review; see decision D2 |
| 13 | **CI runs entirely offline.** No live API key is ever placed in CI, and no test that calls the service executes there. Integration tests against the live API are committed and run locally; CI excludes them by category but still compiles them, so public API drift breaks the build. | CI holds no credential secret, asserts no workflow references one, and asserts the exclusion actually selected no live test |
| 12 | **NodaTime is the SDK's only temporal vocabulary.** No BCL `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, or `TimeSpan` may be named anywhere in the repository's source. See [Temporal types](#temporal-types) for the vocabulary, the BCL boundary, and the required patterns. | `TemporalTypeTests` — reflection over the public surface, plus a comment- and literal-aware source scan; build fails |

---

## Architecture decisions

Terse by design. The rationale matters more than the restatement — if you are considering
reversing one of these, the "why" column is the argument you need to defeat.

| ID | Decision | Why |
|----|----------|-----|
| D1 | Endpoints are **generated** from `specs/openapi.json` + `specs/endpoints.map.json`; output is **committed**. | 147 operations with 1,182 comparator params. Hand-writing drifts from the spec; committing the output keeps diffs reviewable and adds no consumer build step. |
| D2 | Auth defaults to `Authorization: Bearer`, not the `apiKey` query parameter the spec declares. | Query strings leak into access logs, proxies, and browser history. Query-string auth remains available via `MassiveAuthenticationScheme`. |
| D3 | Errors surface as exceptions (`MassiveApiException`), not `Result<T>`. | Idiomatic for .NET; allocates only on the failure path. |
| D4 | Tick-level types (`Agg`, `Trade`, `Quote`) are `readonly record struct`. Reference types (`Ticker`, `Dividend`, …) are `record` classes. | A 50k-row response allocates one array instead of 50k objects. Structs cost nothing ergonomically where nobody null-checks a row; classes stay where nullability is meaningful. |
| D5 | Timestamps are stored as raw `long` epoch values; `DateTimeOffset` is a computed property. | Conversion happens only when read, so a large series costs nothing until the value is wanted. |
| D6 | Two-level API: `client.Stocks.ListAggregatesAsync(...)`. Groups are `readonly struct` over the shared transport. | Mirrors the platform's own taxonomy; navigation allocates nothing. |
| D7 | `net10.0` only. No multi-targeting. | Keeps `ref struct`, collection expressions, and current AOT behaviour available without `#if` ladders. |
| D8 | Packages: `MassiveDotNet` (core) · `.Rest` · `.WebSocket` · `.FlatFiles` · `.Extensions.DependencyInjection`. | REST consumers never pull streaming or S3 code; core stays dependency-free (rules 7–8). |
| D9 | Vendor datasets (Benzinga, ETF Global, Fed, TMX, Fable — 28 operations) ship in `.Rest` like any other endpoint. | They are ordinary REST operations; entitlement is the server's concern, not the SDK's. |
| D10 | `specs/openapi.json` is normalized (sorted keys, 2-space indent) before committing. | Upstream key ordering is unstable; without this every nightly refresh is a meaningless 20k-line diff. |
| D13 | No Massive key is stored as a CI secret, and no test that calls the live API executes in CI. Live integration tests are committed and run locally, excluded from the CI run by category while still compiling there. | A key in CI leaks through build logs, consumes account quota on every push, makes the build depend on a third party's uptime, and cannot work for pull requests from forks — where secrets are deliberately withheld. Committed fixtures give the same wire fidelity, are reviewable in a diff, make failures reproducible years later, and keep the suite fast and deterministic. Fixtures do drift from the live API, which is what the locally run live tier and the nightly spec sync (D10) exist to catch. Excluding that tier rather than skipping it inside the CI run matters: a skip reports into the same summary and reads as green, whereas a project CI never invokes makes no claim at all. |
| D12 | NodaTime replaces BCL date and time types throughout the public API, and is the single external dependency permitted in core. | Market data is unforgiving about temporal ambiguity: bars are Eastern Time, tick timestamps are epoch nanoseconds, corporate actions are calendar dates with no time or zone, and sessions cross DST boundaries. `DateTime` conflates all of these behind one type whose meaning depends on an easily-lost `Kind` flag, and `DateOnly` cannot express a zone at all. NodaTime makes the distinction between an instant, a local date, and a zoned time unrepresentable-if-wrong rather than merely documented. Verified Native AOT clean at 3.3.3, including TZDB zone resolution, so it costs nothing against rules 3 and 4. The rule is strict rather than public-surface-only because a BCL type in a private field or local is the seed of the next one in a signature; the only sanctioned contact is an inline conversion at a BCL call site, which names no type. |
| D11 | The endpoint catalog served by the Massive MCP server is a **build-time** input to the map only. It is never a runtime dependency, and never a test fixture source for wire formats. | Its `call_api` flattens JSON into DataFrames, so it cannot represent the wire envelope. Its docs *do* carry asset-class ownership and comparator groupings the OpenAPI description lacks. |
| D14 | Pagination cursors are followed **verbatim**, but only when `next_url` names the same origin as the configured `BaseAddress`. A mismatch throws rather than following. | `next_url` is absolute, carries no key, and is chosen by the response body — so following it unconditionally sends the caller's API key to whatever host a server names, which is rule 11's concern arriving by another route. Verbatim matters independently: the aggregates cursor rewrites a path segment (`2024-01-01` becomes `1704776400000`), so rebuilding a cursor from the original arguments silently restarts the traversal. An opt-out was rejected — it is an option nobody finds before filing a bug, and everybody finds after reading a workaround online. The cost is understood: if Massive ever shards pagination onto a second hostname this throws where a naive client would keep working, which is the correct failure. |
| D15 | Comparator variants (`.gt` `.gte` `.lt` `.lte` `.any_of` `.all_of`) collapse to **one filter-typed parameter per field**: `RangeFilter<T>`, `SetFilter<T>`, `Filter<T>`, or `ArrayFilter<T>`, chosen by the generator from the exact suffix set the spec declares. Equality is the implicit conversion from `T`. Rendering lives in `RequestUriBuilder`, not in generated code. | 1,182 flat parameters gave one endpoint a 114-argument method. The field is the unit the platform documents; typing it by capability makes an unsupported comparator a compile error rather than a silently dropped parameter. Grouping is read from the spec so it cannot drift, and an unrecognised suffix set fails generation instead of guessing. One rendering implementation is tested once rather than in 93 generated files. Element types are a closed set (`string`, `int`, `long`, `double`, `LocalDate`, `DateOrTimestamp`); `Instant` is excluded because it carries no wire precision. |

---

## Layout

```
specs/openapi.json          Vendored OpenAPI description. Refreshed by CI; never edited by hand.
specs/endpoints.map.json    Curated map: what the spec does NOT say (asset class, method names,
                            .NET parameter names and types, property names for anonymous schemas).
tools/MassiveDotNet.CodeGen Build-time generator. Never shipped, never a consumer dependency.
src/MassiveDotNet           Core: options, transport, auth, exceptions, enums, pooled URI building.
src/MassiveDotNet.Rest      REST client. Generated/ is machine-owned; everything else is hand-written.
tests/                      Unit tests and the endpoint-coverage contract tests.
samples/MassiveDotNet.AotSmokeTest
                            Publishes Native AOT in CI to prove rules 3 and 4.
```

---

## Working on this

### Adding endpoints

1. Add a row to `specs/endpoints.map.json`. Supply only what the spec lacks: `group`, `method`,
   .NET parameter names and types, and property names for anonymous result schemas.
   Everything else — paths, parameters, requiredness, enum members, nullability, prose — is read
   from the spec so it cannot drift.
2. Regenerate: `dotnet run --project tools/MassiveDotNet.CodeGen`
3. Raise `CoverageBaseline` in `EndpointCoverageTests` to the new count.
4. Add a deserialization test using the endpoint's **published sample response** as the fixture.
5. `dotnet test` and confirm the AOT smoke test still publishes clean.

### Constraints the generator must respect

- `RequestUriBuilder` is a `ref struct`, so it **cannot** appear in an `async` method — not even
  without crossing an `await`. Each endpoint therefore emits three methods: a public entry point,
  a synchronous `Build{Method}Uri`, and an async `Send{Method}Async`.
- `.g.cs` files suppress the project's nullable context, so the generated header re-enables it
  explicitly with `#nullable enable`.
- Prose from the OpenAPI description contains HTML intended for the docs site. Run it through
  `Prose.Clean` before emitting it into XML comments; escape it unless it is map-authored markup.

### Before opening a PR

```bash
dotnet build MassiveDotNet.slnx                                    # must be warning-free
dotnet test MassiveDotNet.slnx
dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/  # must be clean
dotnet publish samples/MassiveDotNet.AotSmokeTest -r <rid> -c Release          # zero IL warnings
```

---

## Temporal types

Market data is unforgiving about temporal ambiguity. An aggregate window is Eastern Time, a tick
timestamp is epoch nanoseconds, a dividend's ex-date is a calendar date carrying no time or zone at
all, and a trading session crosses DST boundaries twice a year. `DateTime` collapses all of these
into one type whose meaning depends on a `Kind` flag that is trivially lost across a serialization
boundary. NodaTime keeps them distinct types, so the wrong one does not compile.

### Vocabulary

| Domain concept | Type | Example in this SDK |
|----------------|------|---------------------|
| A moment on the global timeline | `Instant` | `Agg.Timestamp`, trade and quote SIP timestamps |
| A calendar date with no time or zone | `LocalDate` | Ex-dividend date, split execution date, IPO date |
| A wall-clock time in a named zone | `ZonedDateTime` | Session open and close in `America/New_York` |
| A date and time with no zone attached | `LocalDateTime` | Rare; prefer `Instant` or `ZonedDateTime` |
| An elapsed amount of time | `Duration` | `MassiveClientOptions.Timeout`, `RetryAfter` |
| The current moment | `IClock` / `SystemClock.Instance` | Never `DateTime.UtcNow` — an injected clock is also testable |

On wire DTOs, store the raw epoch value as a `long` and expose the NodaTime type as a computed
property (decision D5), so the conversion is paid only when the value is actually read:

```csharp
[JsonPropertyName("t")]
public long TimestampMilliseconds { get; init; }

[JsonIgnore]
public Instant Timestamp => Instant.FromUnixTimeMilliseconds(TimestampMilliseconds);
```

### The BCL boundary

Rule 12 cannot mean "no `TimeSpan` value ever exists at runtime". Several BCL APIs have `TimeSpan`
in their signatures, and no SDK can change that. What the rule *does* mean is that the type is
**never named in our source** — no declarations, no typed locals, no static calls like
`TimeSpan.FromMinutes`. Every crossing is an inline conversion through NodaTime's own methods.

The distinction is not cosmetic. A `TimeSpan` in a private field or a local is how the next one
ends up in a public signature; forbidding the name removes the gradient.

**Producing** a value for a BCL API — convert at the call site:

```csharp
// correct: the type is never named
handler.PooledConnectionLifetime = Duration.FromMinutes(2).ToTimeSpan();
httpClient.Timeout = options.Timeout.ToTimeSpan();

// wrong: names the type, and drops the domain type on the floor
handler.PooledConnectionLifetime = TimeSpan.FromMinutes(2);
```

**Consuming** a value from a BCL API — pattern match, so the temporary is implicitly typed:

```csharp
// correct: `delta` is inferred, never written down
Duration? retryAfter = response.Headers.RetryAfter?.Delta is { } delta
    ? Duration.FromTimeSpan(delta)
    : null;

// wrong: a typed local, invisible to reflection but caught by the source scan
TimeSpan? delta = response.Headers.RetryAfter?.Delta;
```

### Known boundary points

Every place the SDK touches a BCL temporal signature. Extend this table when a new one appears.

| API | Direction | Pattern | Where |
|-----|-----------|---------|-------|
| `HttpClient.Timeout` | produce | `options.Timeout.ToTimeSpan()` | `MassiveHttpTransport` ctor |
| `SocketsHttpHandler.PooledConnectionLifetime` | produce | `Duration.FromMinutes(2).ToTimeSpan()` | `MassiveHttpTransport` ctor |
| `RetryConditionHeaderValue.Delta` | consume | pattern match, then `Duration.FromTimeSpan` | `MassiveHttpTransport.CreateExceptionAsync` |
| `RetryConditionHeaderValue(TimeSpan)` | produce | `retryAfter.ToTimeSpan()` | `StubHandler` (tests) |

Anticipated, for work not yet written:

| API | Arrives with | Pattern |
|-----|--------------|---------|
| `Task.Delay` | retry backoff (#5) | `Task.Delay(backoff.ToTimeSpan(), ct)` |
| `System.Threading.RateLimiting` window and period options | rate limiting (#5) | `window.ToTimeSpan()` on the options object |
| `CancellationTokenSource.CancelAfter` | per-call deadlines | `cts.CancelAfter(deadline.ToTimeSpan())` |
| `ClientWebSocketOptions.KeepAliveInterval` | WebSockets (#20) | `keepAlive.ToTimeSpan()` |
| SigV4 `x-amz-date` signing timestamp | flat files (#22) | `clock.GetCurrentInstant()` formatted with an `InstantPattern`; never `DateTime.UtcNow` |

### How this is enforced

`TemporalTypeTests` runs two independent layers, because neither is sufficient alone:

1. **Reflection** over the exported surface of both shipped assemblies — properties, fields,
   methods, operators, and constructors. Operators are deliberately included: an implicit
   conversion from a BCL type would reintroduce it into the public API.
2. **A source scan** over `src`, `tests`, `samples`, and `tools`. Reflection cannot see local
   variables or static calls, so this catches what layer 1 structurally cannot. It strips comments,
   string literals (raw and verbatim included), and character literals before matching, preserving
   newlines so reported line numbers stay accurate.

Naming a forbidden type in a **comment is fine** — the scan strips them — which is why the boundary
sites in `MassiveHttpTransport` can explain themselves in prose.

`TemporalTypeTests.cs` is the only file exempt from the scan, since expressing the rule requires
naming the types. Two further tests guard the scanner itself: one asserts it flags an offending
declaration, the other that it ignores the same identifiers inside comments and literals, so it
cannot pass merely because its stripping ate the input.

---

## Testing

The suite has two tiers. **CI runs only the offline tier**, which is rule 13 and is not negotiable
for convenience.

| Tier | Project | Runs in CI | Needs a key |
|------|---------|------------|-------------|
| Offline | `MassiveDotNet.Rest.Tests` | yes | no |
| Live | `MassiveDotNet.IntegrationTests` | **no** — compiled only | yes |

```bash
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"   # what CI runs
dotnet test MassiveDotNet.slnx --filter "Category=Integration"    # live, local only
```

The live project stays in the solution deliberately. It is never executed by CI, but it is still
**compiled** there, so a change to the public API breaks the build rather than rotting unnoticed
until someone next runs it.

Excluded, not skipped. A test that skips inside the CI run still reports into the same summary and
reads as green; a project CI never invokes makes no claim at all. Locally the same tests *do* skip
when no key is present, with a reason naming the variable to set — that is honest feedback to a
person reading the output, not a false signal in an automated gate. CI additionally holds no key,
so a live test could not pass there even if the filter were removed.

### Where fixtures come from

In order of preference:

1. **Massive's published sample responses.** Every endpoint's documentation carries one. These are
   the canonical shape and should be the default fixture for a new endpoint.
2. **Captured live responses.** When a sample is absent, incomplete, or suspected of being out of
   date, a developer with their own key captures the real response locally, reviews it, and commits
   it as a fixture. The key stays in a gitignored `.env` and never leaves the machine.
3. **Hand-written JSON.** Only for cases the service cannot easily be made to produce — a malformed
   body, a truncated stream, an error envelope for a status code you cannot trigger on demand.

A captured fixture is reviewed before committing: confirm it carries no account identifiers, and
that no URL in it embeds a key. Bearer authentication keeps keys out of request URLs (D2), which is
one reason it is the default.

### What this does and does not buy

Committed fixtures give reproducible failures, reviewable diffs, a fast suite, and tests that work
on a fork's pull request where secrets are deliberately unavailable.

What they do not do is notice when Massive changes a response shape. That is the job of the nightly
spec sync (D10), which diffs the OpenAPI description and opens a pull request. If a shape changes
without the description changing, the endpoint's own fixture needs recapturing — treat a surprising
production report as a signal to do that.

### What the live tier is for

Only what fixtures structurally cannot verify:

- **Authentication actually works** against the real service, rather than against a stub that was
  told to accept it.
- **The wire format still matches.** A fixture asserts the SDK agrees with a recording of the past;
  a live call asserts it agrees with the service today.
- **Error envelopes are real.** The OpenAPI description declares every error response with an empty
  schema, so `MassiveErrorPayload` is an informed guess. `ErrorHandlingLiveTests` is what confirms
  the guess, and is the highest-value test in the suite.
- **Assumptions about behaviour the spec does not state**, such as whether an unknown ticker yields
  an empty result or an error status.

Do not port assertions here that a fixture already covers. Every live test costs quota, wall time,
and a dependency on Massive being up.

Run the live tier after finishing an endpoint group, and before tagging a release. If it reveals
that a response shape has moved, recapture that endpoint's fixture (see #27) so the offline tier
learns what the live tier found.

---

## Conventions

- **Naming**: groups are `{Asset}Group`; methods are verb-first and `Async`-suffixed
  (`ListAggregatesAsync`, `GetLastTradeAsync`). `CancellationToken` is always the last parameter,
  always defaulted.
- **Pagination**: the 100 operations whose success schema declares `next_url` get two methods —
  `ListXxxAsync` returning `MassivePage<T>` (one page, reporting whether more exist) and
  `EnumerateXxxAsync` returning `IAsyncEnumerable<T>` (every page, one in flight at a time).
  The 47 that do not paginate keep returning `T[]`. Pagination is detected from the spec, never
  declared in the map. `Enumerate`/`List` follows the BCL's `Directory.EnumerateFiles` /
  `Directory.GetFiles` distinction; avoid "Stream", which in this SDK means WebSockets.
- **Filters**: a field that carries comparator variants becomes one optional parameter typed
  `RangeFilter<T>`, `SetFilter<T>`, `Filter<T>`, or `ArrayFilter<T>` by its suffix set; a plain
  `T` converts implicitly to equality. The map names the **element** type on the base field's row
  (`"ex_dividend_date": { "type": "LocalDate" }`), never the filter type, and never a row keyed by
  a variant. Grouping is detected from the spec, never declared in the map (D15). Calendar dates
  (`format: date`) are `LocalDate` on parameters and models alike, read by
  `LocalDateJsonConverter`.
- **Nullability**: enabled everywhere. Optional query parameters are nullable and omitted from the
  request when `null` — never sent as empty.
- **Temporal**: see [Temporal types](#temporal-types). Rule 12 is strict and machine-checked.
- **Allocation**: build request URIs through `RequestUriBuilder`, never `UriBuilder` or a
  `Dictionary`. Deserialize from the response stream; never buffer a body into a string first.
- **Comments**: explain *why*, not *what*. The generator's output is read by humans in review, so
  emitted comments are held to the same standard as hand-written ones.
- **Tests**: prefer driving the real public API through a stubbed `HttpMessageHandler` over testing
  internals. See [Testing](#testing) for where fixtures come from and why the suite runs offline.
- **Commits**: do not commit or push unless asked.
