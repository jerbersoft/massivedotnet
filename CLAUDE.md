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
| 2 | Deprecated operations ship, marked `[Obsolete]`. `vX`-prefixed and `dev` operations ship, marked `[Experimental]`. Nothing is silently omitted, and nothing is removed or re-marked on a live observation (D21). | `EndpointCoverageTests` — every mapped entry point carries the attribute exactly where the spec's `x-polygon-deprecation` extension or `vX`-prefixed/`dev` route says so; build fails |
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
| D15 | Comparator variants (`.gt` `.gte` `.lt` `.lte` `.any_of` `.all_of`) collapse to **one filter-typed parameter per field**: `RangeFilter<T>`, `SetFilter<T>`, `Filter<T>`, or `ArrayFilter<T>`, chosen by the generator from the exact suffix set the spec declares. Equality is the implicit conversion from `T`. Rendering lives in `RequestUriBuilder`, not in generated code. | 1,182 flat parameters gave one endpoint a 114-argument method. The field is the unit the platform documents; typing it by capability makes an unsupported comparator a compile error rather than a silently dropped parameter. Grouping is read from the spec so it cannot drift, and an unrecognised suffix set fails generation instead of guessing. One rendering implementation is tested once rather than in 93 generated files. Element types are a closed set (`string`, `int`, `long`, `double`, `LocalDate`, `DateOrTimestamp`, `DateOrNanoseconds`); `Instant` is excluded because it carries no wire precision. |
| D16 | Nested object schemas bind to **named models in the map**, generated from the spec and verified structurally at every site that names them. An object with no binding fails generation. | 184 nested sites across 55 operations collapse to 60 shapes, and their public names (`Greeks`, `NewsPublisher`) are worth a human's row in the map: path-derived names would give the three stocks snapshot operations three identical `Day` types. A name may cover only one shape, so every reuse site is checked — property names must match exactly and the model may not require what the site makes optional — while scalar types are trusted from the model row, because the description's own formats disagree at sites that are plainly the same thing. Failing on an unbound object is what stops a required nested object from shipping as a `string` that throws at deserialization. |
| D17 | The `result` row has two kinds, `array` and `object`, and an omitted `property` means the body is the payload. A paginated object result returns `MassivePagedResult<T>` and enumerates the array its model row names as `items`; a paginated object with no `items` keeps a `Get` whose cursor, if one ever arrives, throws. Every singular `Get` returns `T`, and a 200 without its payload throws. | Fifty operations are not an array under `results`: 28 return one object there, 20 of which — the indicators — genuinely paginate over `results.values` with a per-page `underlying`, and 22 have no `results` at all. `MassivePage<T>` cannot hold an object with two halves, and discarding `underlying` would silently drop what `expand_underlying` asked for. Pagination stays spec-detected: the map only says where the items are, so it cannot drift. `Task<T?>` on every `Get` was rejected because the description's requiredness is unreliable and every unknown-ticker probe returned 404; a 200 without a payload is the same class of failure as a body that will not deserialize, and is reported the same way. |
| D18 | Stability is **read from the spec**, never declared in the map: `x-polygon-deprecation` marks a deprecated operation, and a `vX`-prefixed or `dev` route segment or `x-polygon-experimental` marks an experimental one (`dev` since D22, the prefix since D23). Deprecated entry points carry `[Obsolete]` with diagnostic `MASSIVE0002`, a warning, whose message names the .NET method that supersedes them; experimental ones carry `[Experimental("MASSIVE0001")]`, an error until a consumer suppresses it. Only the public entry points are marked. | The issue that asked for this had the map carry a `stability` column, but the description already says both things, and a second source is one that drifts. The extension alone is not enough — it appears on two of the fourteen `vX` routes — so the path is the primary signal. Dedicated diagnostic ids let a consumer with warnings-as-errors suppress this SDK's deprecations without hiding every other CS0618, and make the experimental opt-in a single `NoWarn` entry. A replacement is resolved from the docs-site slug the description carries rather than restated in the map, so generation refuses a deprecated operation whose replacement is unmapped instead of emitting a message that names nothing. Models, envelopes, and the JSON context stay unmarked so the SDK's own generated code compiles without suppressions. |
| D19 | A `type: array` query parameter that carries no comparators binds to `T[]?` and renders **comma-joined**, each element escaped, an empty array omitted like `null`. Its element type comes from the same closed set as a filter's, checked at generation. | The description declares neither `style` nor `explode` on these parameters, and the OpenAPI default for that omission is the repeated-key form, `tickers=A&tickers=B`. The service does not honour it: probed live, that form returned one ticker where the comma-joined form returned both, so following the standard would silently drop every value after the first, and the parameter's own prose says comma-separated. It is not a `SetFilter` because the field is the list, not a comparator over a scalar field, so there is no suffix to render. An empty array is omitted because an optional parameter is never sent as empty, and on the one family that has these, `tickers=` and an absent `tickers` mean the same thing anyway. The live proof lands with the snapshot operations that use it, since a fixture cannot tell the two wire forms apart. |
| D20 | Tick-level timestamp filters bind to `DateOrNanoseconds`, a second core value type with the unit in its name. It joins the closed element set beside `DateOrTimestamp`, and the map names whichever the endpoint documents. | The v3 trades and quotes `timestamp` takes "a date or a nanosecond timestamp". `DateOrTimestamp` renders an `Instant` as Unix milliseconds, so binding it there would compile and ask for a moment in 1970; adding nanosecond factories to it would keep one type but leave its implicit `Instant` conversion rendering the wrong unit on half the endpoints. Two types make the wrong unit unrepresentable. Binding the filter to `LocalDate` alone was rejected because it drops the nanosecond form the API documents, which is rule 2's silent omission arriving on the request side. |
| D21 | The description is the contract for what ships. An operation the live tier observes retired, unserved, or otherwise drifted stays mapped and keeps the stability the description gives it; the live test that saw it pins the observation, dated, so it flips the day the service changes. When the nightly sync drops the route, the map row, the generated methods, and `CoverageBaseline` go in one commit — the only sanctioned decrease — noted as breaking in the changelog. | Both retired v2 tick routes and the unserved stocks exchanges route answered 404 on 2026-09-02 while the description still declared them. Removing or re-marking on that evidence needs an exclusion list, which is the second stability source D18 exists to forbid, and a rule keyed on a live observation cannot be enforced, because CI never sees the live service (rule 13). Massive's own signal for removal is the description, which the sync watches nightly, and a pinned 404 costs one live test where a skip would read as green. The cost is a runtime 404 for a consumer who ignores an `[Obsolete]` warning that already names the replacement, or who calls a stable-marked route the service has not yet stood up; both are the description's error to correct, not the SDK's to guess at. |
| D22 | A `dev` route segment reads as experimental exactly as `vX` does: from the path, never from the map. `/stocks/dev/trades/{ticker}` therefore ships as `Stocks.ListDevTradesAsync`, marked `[Experimental]` and named after its route. | The segment is Massive's own marker that the route is unreleased: it is in the description and the MCP catalog, absent from the docs site, and answered a plain-text 404 on 2026-09-03. "May change or be removed without notice" is exactly the experimental message, and an opt-in error is the right default for a route that does not yet answer. Leaving it unmapped was rejected because it needs an exclusion list, which is a second stability source (D18), and is rule 2's silent omission; asking Massive was rejected because the answer would still need a description-side signal before the generator could act on it. The name follows the route rather than a word like "Preview", so a reader can find the row from the path, and the method goes with the segment when the route is released under a version. The cost if wrong is an opt-in method that 404s, the posture D21 already accepts, and its pinned live test flips the day the route is served. |
| D23 | A route segment that **starts with** `vX` reads as experimental, so `vX_0` marks `/stocks/filings/10-K/vX_0/sections` exactly as `vX` marks its sibling. Read from the path, never from the map. | The description carries both a `vX` and a `vX_0` revision of the 10-K sections route, and only the `vX` one is served: `vX_0` answered a plain-text 404 on 2026-09-03. An equality reading ships the `vX_0` operation unmarked and stable, which contradicts Massive's own convention, and marking it from the map is the second stability source D18 forbids. A prefix is the smallest reading that covers every revision Massive might number; `dev` stays an exact match because it is a word, not a version. The cost if wrong is an `[Experimental]` on a route Massive considers released, which the next spec sync corrects the day the segment changes. |
| D24 | A `oneOf` with exactly one branch reads as that branch, in `Spec.Shape` and `Spec.Collect`; a `oneOf` of scalars stays a scalar; a `oneOf` with more than one branch of which any is an object fails generation. | The description uses the one-branch form once, on the ticker events items, and the generator read it as an array of strings, which would have failed on every real response: the silent wrong binding D16 exists to prevent. A scalar union stays a scalar because the news parameters declare one and go through the same classifier. An object union has no honest model binding, so it is refused rather than guessed; none exists today. |
| D26 | When the description declares two revisions of one route, the served, documented revision takes the plain method name and the other carries its version segment: `ListIposAsync` for `/vX/reference/ipos` beside `ListIposV1Async`, `List10KSectionsAsync` for the `vX` sections route beside `List10KSectionsVx0Async`. The rename lands in the D21 removal commit when Massive retires a revision. | Naming is the map's job, so this is not a second stability source: both revisions ship and both are marked from the path (D18). Giving the plain name to the versioned route because the description promotes it was rejected: `ListIposAsync` would 404 today, and the cost at the transition, one breaking rename noted in the changelog, is the same either way. D25 is reserved for the SEC filings plan. |
| D25 | A route that serves a document where the description declares JSON ships **as declared**, and a hand-written download sits beside it: `GetFilingFileAsync` returns the declared `FilingFile` and throws on the HTML the service sends, while `DownloadFilingFileAsync` copies the bytes to a caller's stream through the same generated URI builder. `MassiveHttpTransport.DownloadAsync` inspects no content type. | The SEC filing file route declares a metadata object and serves `text/html` with either `Accept` header, observed 2026-09-03. A map-level `kind: document` would have the map overriding the description on a live observation, which is the second stability source D18 and D21 forbid, so the generated method stays as declared and its pinned live test flips the day either side moves. The download is hand-written because nothing in the description says it exists. Returning a `Stream` was rejected because it ties the response's lifetime to a value the caller may forget to dispose, and returning a `string` is wrong for the graphics and PDFs a filing carries. The content type is not inspected because the caller asked for the bytes and the files listing already names each file's type, name, and size. |

---

## Layout

```
specs/openapi.json          Vendored OpenAPI description. Refreshed by CI; never edited by hand.
specs/endpoints.map.json    Curated map: what the spec does NOT say (asset class, method names,
                            .NET parameter names and types, property names for anonymous schemas).
tools/MassiveDotNet.CodeGen Build-time generator. Never shipped, never a consumer dependency.
src/MassiveDotNet           Core: options, transport, auth, exceptions, enums, pooled URI building.
src/MassiveDotNet.Rest      REST client. Generated/ is machine-owned; everything else is hand-written.
tests/                      Unit tests, the endpoint-coverage contract tests, and the generator's diagnostic tests.
samples/MassiveDotNet.AotSmokeTest
                            Publishes Native AOT in CI to prove rules 3 and 4.
```

---

## Working on this

### Adding endpoints

1. Add a row to `specs/endpoints.map.json`. Supply only what the spec lacks: `group`, `method`,
   .NET parameter names and types, and property names for anonymous result schemas.
   A nested object, or the element of a nested array of objects, needs its own `models` row with a
   pointer through its parent and a `model` reference on the parent's property row (D16).
   The `result` row says whether the payload is one model or an array of it (`kind`) and where it
   sits (`property`, omitted when the body itself is the payload); a paginated object's model row
   names the array it enumerates as `items` (D17).
   Everything else — paths, parameters, requiredness, enum members, nullability, prose — is read
   from the spec so it cannot drift. So is stability: a deprecated operation's `[Obsolete]` message
   names the method that replaces it, so map the replacement before the operation it supersedes,
   or generation refuses (D18). A row that names a core enum, such as `SortOrder`, is checked
   against the members the spec declares for that parameter: a member the enum lacks fails
   generation, while a member the operation omits is accepted, since the server rejects it (#37).
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
- A **path parameter is required whether or not the description flags it**. OpenAPI mandates the
  flag; the SEC v1 description omits it on `filing_id` and `file_id`, and reading it literally
  would default those to `null` and append an empty segment. `Spec.Parameters` therefore reads
  requiredness as `in == "path" || required` (D-R3).
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
| A moment on the global timeline | `Instant` | `Agg.Timestamp`, `LastTrade.SipTimestamp`, `NewsArticle.PublishedUtc` |
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
| Offline | `MassiveDotNet.CodeGen.Tests` | yes | no |
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
learns what the live tier found. If it reveals a route the service no longer serves, or has not
yet stood up, pin the status it answers with and the date you saw it, and leave the operation
mapped as the description declares it (D21); the pin flips the day the service changes, where a
skip would read as green.

---

## Conventions

- **Naming**: groups are `{Asset}Group`; methods are verb-first and `Async`-suffixed
  (`ListAggregatesAsync`, `GetLastTradeAsync`). `CancellationToken` is always the last parameter,
  always defaulted.
  A model or method named after an SEC form spells a leading form number, because C# forbids a
  leading digit — `TenKSection`, `EightKDisclosure`, `ThirteenFHolding` — and keeps a trailing
  one: `Form3Filing`, `Form4Filing`, `List10KSectionsAsync`, `List13FHoldingsAsync`.
- **Pagination**: the 100 operations whose success schema declares `next_url` get two methods —
  `ListXxxAsync` returning `MassivePage<T>` (one page, reporting whether more exist) and
  `EnumerateXxxAsync` returning `IAsyncEnumerable<T>` (every page, one in flight at a time).
  When the page is one object rather than an array, `List` returns `MassivePagedResult<T>` and
  `Enumerate` yields the elements of the array the model row names as `items` (D17). A paginated
  object whose model names no `items` has nothing to enumerate, so it keeps a single `Get`
  returning `T` that throws if the server ever sends a cursor it cannot follow. The 47 that do
  not paginate return `T[]`, or `T` for a singular result. Pagination is detected from the spec,
  never declared in the map. `Enumerate`/`List` follows the BCL's `Directory.EnumerateFiles` /
  `Directory.GetFiles` distinction; avoid "Stream", which in this SDK means WebSockets.
- **Filters**: a field that carries comparator variants becomes one optional parameter typed
  `RangeFilter<T>`, `SetFilter<T>`, `Filter<T>`, or `ArrayFilter<T>` by its suffix set; a plain
  `T` converts implicitly to equality. The map names the **element** type on the base field's row
  (`"ex_dividend_date": { "type": "LocalDate" }`), never the filter type, and never a row keyed by
  a variant. Grouping is detected from the spec, never declared in the map (D15). Element types
  are a closed set: `string`, `int`, `long`, `double`, `LocalDate`, `DateOrTimestamp`, and
  `DateOrNanoseconds`; the last two differ only in the unit an `Instant` renders as, and the map
  names whichever the endpoint documents (D20). Calendar dates (`format: date`) are `LocalDate` on
  parameters and models alike, read by `LocalDateJsonConverter`, and date-times are `Instant`,
  read by `InstantJsonConverter`. A field whose own schema is `type: array` and carries no
  comparators is not a filter: it binds to `T[]?` and renders comma-joined as the plain field,
  with an empty array omitted (D19). A map override names the C# type, `string[]`, the same way it
  does for any plain parameter.
- **Enums**: an enum parameter binds a core enum when its member set crosses groups or sits in a
  path: `MarketType` for `asset_class` and `market`, `SortOrder` for `order`, `ContractType` for
  `contract_type`, and `AggregateTimespan`, `SeriesType`, and `SnapshotDirection` where they
  already apply. Every other enum parameter, including per-endpoint `sort` fields and sets one
  operation owns such as the ticker `type`, stays `string`; the server rejects a bad value with a
  400, the same posture as an entitlement (D9). A row naming a core enum is checked one way
  against the members the spec declares (#37).
- **Models**: a nested object, or the element of a nested array of objects, is its own `models`
  row with a pointer through its parent (`results/items/publisher`,
  `results/items/insights/items`), and the parent's property row names it with `model`; the
  generator composes `T`, `T?`, `T[]`, or `T[]?` from the spec, so the map never restates
  requiredness or array-ness. A free-form object with no declared properties takes an explicit
  `type` of `Dictionary<string, T>`. `format: date-time` properties are `Instant`, read by
  `InstantJsonConverter`. A model may also be an endpoint's whole payload: `kind: object` with a
  `property` binds one object on the envelope, and an omitted `property` binds the body itself,
  whose model row omits `pointer` (or points at `items` for a body array). A paginated object's
  model row names its `items` once, and the generator refuses one that is absent, not an array of
  objects, or unbound (D17). Names are domain nouns, prefixed by family only where it
  disambiguates; reuse a model across operations only where the generator's structural check
  passes (D16).
- **Stability**: a deprecated operation's entry points carry
  `[Obsolete("…", DiagnosticId = "MASSIVE0002")]`, a warning whose message names the replacement;
  a `vX`-prefixed or `dev` operation's carry `[Experimental("MASSIVE0001")]`, an error until a consumer opts in
  with `<NoWarn>$(NoWarn);MASSIVE0001</NoWarn>` or a `#pragma`. Both are read from the spec, never
  declared in the map (D18). A test or sample project that exercises such an endpoint suppresses
  the id in its own `.csproj`, as the REST and integration test projects do for the deprecated
  tick endpoints; nothing is ever suppressed inside generated code.
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
