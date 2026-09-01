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
| 12 | **NodaTime is the SDK's only temporal vocabulary.** Every date, time, instant, and duration uses `Instant`, `LocalDate`, `LocalDateTime`, `ZonedDateTime`, or `Duration`. BCL `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, and `TimeSpan` must not be **named anywhere in the repository's source** — not in public API, not in private members, not in locals, not in static calls. Where a BCL API signature itself traffics in `TimeSpan` (`HttpClient.Timeout`, `SocketsHttpHandler.PooledConnectionLifetime`, the `Retry-After` header), produce or consume the value inline through `Duration.ToTimeSpan()` or `Duration.FromTimeSpan()`, so the type is never written down. | `TemporalTypeTests` — reflection over the public surface, plus a comment- and literal-aware source scan; build fails |

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
| D12 | NodaTime replaces BCL date and time types throughout the public API, and is the single external dependency permitted in core. | Market data is unforgiving about temporal ambiguity: bars are Eastern Time, tick timestamps are epoch nanoseconds, corporate actions are calendar dates with no time or zone, and sessions cross DST boundaries. `DateTime` conflates all of these behind one type whose meaning depends on an easily-lost `Kind` flag, and `DateOnly` cannot express a zone at all. NodaTime makes the distinction between an instant, a local date, and a zoned time unrepresentable-if-wrong rather than merely documented. Verified Native AOT clean at 3.3.3, including TZDB zone resolution, so it costs nothing against rules 3 and 4. The rule is strict rather than public-surface-only because a BCL type in a private field or local is the seed of the next one in a signature; the only sanctioned contact is an inline conversion at a BCL call site, which names no type. |
| D11 | The endpoint catalog served by the Massive MCP server is a **build-time** input to the map only. It is never a runtime dependency, and never a test fixture source for wire formats. | Its `call_api` flattens JSON into DataFrames, so it cannot represent the wire envelope. Its docs *do* carry asset-class ownership and comparator groupings the OpenAPI description lacks. |

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

## Conventions

- **Naming**: groups are `{Asset}Group`; methods are verb-first and `Async`-suffixed
  (`ListAggregatesAsync`, `GetLastTradeAsync`). `CancellationToken` is always the last parameter,
  always defaulted.
- **Nullability**: enabled everywhere. Optional query parameters are nullable and omitted from the
  request when `null` — never sent as empty.
- **Temporal**: an *instant* is `Instant`; a calendar date with no time or zone is `LocalDate`; a
  wall-clock time in a named zone is `ZonedDateTime`; an elapsed amount is `Duration`. Store raw
  epoch values on wire DTOs and expose the NodaTime type as a computed property (D5), so conversion
  is paid only when read. Never name a BCL temporal type (rule 12): at a BCL call site write
  `Duration.FromMinutes(2).ToTimeSpan()`, and consume one with a pattern match
  (`x?.Delta is { } d ? Duration.FromTimeSpan(d) : null`) rather than a typed local.
- **Allocation**: build request URIs through `RequestUriBuilder`, never `UriBuilder` or a
  `Dictionary`. Deserialize from the response stream; never buffer a body into a string first.
- **Comments**: explain *why*, not *what*. The generator's output is read by humans in review, so
  emitted comments are held to the same standard as hand-written ones.
- **Tests**: prefer driving the real public API through a stubbed `HttpMessageHandler` over testing
  internals. Fixtures come from Massive's published sample responses, not from invented JSON.
- **Commits**: do not commit or push unless asked.
