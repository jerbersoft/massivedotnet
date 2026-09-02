# Stocks group: the remaining fifteen operations

Issue: [#8](https://github.com/jerbersoft/massivedotnet/issues/8) · Milestone: v0.1 REST · Date: 2026-09-02

## Why

The Stocks group holds seven operations: aggregates, dividends, the daily open/close, the last
trade, SMA, and their siblings. Issue #8 lists twenty more. Every generator path those twenty need
already exists — array and object results, paginated objects with named items, nested models,
comparator filters, array parameters, and spec-read stability — so this is the first group that is
almost entirely map rows. What it is not is mechanical: the spec is wrong about the deprecated
tick endpoints in ways the generator would faithfully reproduce, the v3 tick endpoints take a
filter at a precision no core type renders, and the snapshot family needs six models whose reuse
D16 has to verify across three operations.

Twenty is also not the number. The issue names seventeen operations and counts twenty; four of the
seventeen were mapped by earlier issues, one of the unnamed three is an indices route, and another
carries a path segment whose meaning for stability is undecided.

## What the spec actually declares

Measured on `specs/openapi.json` on 2026-09-02, with `allOf` merged and `$ref` resolved.

| Operation | Route | Payload | Pages |
|---|---|---|---|
| `GetGroupedStocksAggregates` | `/v2/aggs/grouped/locale/us/market/stocks/{date}` | array under `results`; each row carries `T` and `otc` | no |
| `GetPreviousStocksAggregates` | `/v2/aggs/ticker/{stocksTicker}/prev` | array under `results`; the schema omits `otc` and `T`, the example carries `T` | no |
| `Trades` | `/v3/trades/{stockTicker}` | array under `results` | yes |
| `Quotes` | `/v3/quotes/{stockTicker}` | array under `results` | yes |
| `LastQuote` | `/v2/last/nbbo/{stocksTicker}` | one object under `results` | no |
| `GetStocksSnapshotTickers` | `/v2/snapshot/locale/us/markets/stocks/tickers` | array under `tickers`; envelope has `count` and `status`, no `request_id` | no |
| `GetStocksSnapshotTicker` | `/v2/snapshot/locale/us/markets/stocks/tickers/{stocksTicker}` | one object under `ticker` | no |
| `GetStocksSnapshotDirection` | `/v2/snapshot/locale/us/markets/stocks/{direction}` | array under `tickers`; `direction` is a path enum of `gainers`, `losers` | no |
| `EMA`, `RSI` | `/v1/indicators/{ema,rsi}/{stockTicker}` | identical to SMA, property for property | yes |
| `MACD` | `/v1/indicators/macd/{stockTicker}` | SMA's shape with `short_window`, `long_window`, `signal_window`, and values carrying `histogram` and `signal` | yes |
| `DeprecatedGetHistoricStocksTrades` | `/v2/ticks/stocks/trades/{ticker}/{date}` | array under `results`; envelope has `db_latency`, `results_count`, `success`, `ticker` | no |
| `DeprecatedGetHistoricStocksQuotes` | `/v2/ticks/stocks/nbbo/{ticker}/{date}` | as above | no |
| `get_stocks_v1_splits` | `/stocks/v1/splits` | array under `results` | yes |
| `get_stocks_v1_exchanges` | `/stocks/v1/exchanges` | array under `results` | yes |

The three snapshot operations share one item shape: `day`, `lastQuote`, `lastTrade`, `min`, and
`prevDay` objects, plus `ticker`, `todaysChange`, `todaysChangePerc`, `updated`, and `fmv`. `day`
and `prevDay` differ by one property, `dv`, so they are two shapes.

The v3 `timestamp` filter, on trades and quotes, is documented as "a date with the format
YYYY-MM-DD or a nanosecond timestamp". `DateOrTimestamp` renders an `Instant` as Unix
milliseconds, so it cannot bind here without silently asking for 1970.

Where the description is wrong, and how it is known:

| Site | Defect | Evidence |
|---|---|---|
| Grouped daily example | `request_id` is an object | the schema says string; every other example is a string |
| Splits and exchanges examples | `"request_id": 1` | as above; the dividends example had the same defect and its fixture already corrects it |
| Previous close schema | omits `T`, which the example carries | the model follows the schema, so `T` is not bound; if the live tier shows it consistently, the nightly spec sync is where it gets fixed |
| Historic trades schema | `T`, `f`, `e`, `r` marked required | the example omits all four |
| Historic quotes schema | `T`, `f`, `i` marked required | the example omits all three |
| Grouped daily, historic tick, last quote, and snapshot timestamps | `integer` with no format | every example value overflows `int32`; the v3 tick and MACD timestamps declare `int64` and need no override |
| `MassiveEnumValues.ToWireValueNanoseconds` | returns `ToUnixTimeTicks`, which is 100 ns units | no caller exists yet; the new filter type would have been the first |

## Decisions

### D-G1 · Fifteen operations ship under Stocks; the other five are accounted for

The issue names seventeen operations. Four of them — custom bars, the daily open/close, the last
trade, and SMA — were mapped by earlier issues as reference patterns, and the other thirteen ship
here. Its count of twenty includes three it does not name: `/stocks/v1/exchanges`, which ships
here; `/v1/open-close/{indicesTicker}/{date}`, an indices route no issue owns, which moves to #14;
and `/stocks/dev/trades/{ticker}`, whose `dev` segment is neither `vX` nor
`x-polygon-experimental`, so D18 has no reading of it, and which gets its own issue about what
`dev` means before it can ship. `/stocks/v1/splits`, which the issue neither names nor counts,
ships here because it carries `x-polygon-entitlement-market-type: stocks` and belongs beside
dividends rather than in the reference group #9 owns. Fifteen in all.

`CoverageBaseline` goes from 7 to 22.

### D-G2 · Tick-level timestamp filters bind to `DateOrNanoseconds`, a second core type

```csharp
public readonly struct DateOrNanoseconds : IEquatable<DateOrNanoseconds>
{
    public static DateOrNanoseconds FromDate(LocalDate value);
    public static DateOrNanoseconds FromInstant(Instant value);       // Unix nanoseconds
    public static DateOrNanoseconds FromUnixNanoseconds(long value);
    public static DateOrNanoseconds FromLiteral(string value);
    // implicit from LocalDate, Instant, long, string; ToString renders the wire form
}
```

It is `DateOrTimestamp` with the unit changed, joins the closed element set in `TypeBinding` and
`RequestUriBuilder.AppendElement`, and is the map type for `timestamp` on `Trades` and `Quotes`.
The `Instant` form renders through `ToWireValueNanoseconds`, which is corrected to
`(value - NodaConstants.UnixEpoch).ToInt64Nanoseconds()` with the test that would have caught it.

Two alternatives were rejected. Binding the filter to `LocalDate` alone drops the nanosecond form
the API documents, which is rule 2's silent omission on the request side. Adding nanosecond
factories to `DateOrTimestamp` keeps one type, but its implicit `Instant` conversion would go on
rendering milliseconds, so `timestamp: someInstant` on trades would compile and ask for 1970. Two
types with the unit in the name make that unrepresentable. Recorded in CLAUDE.md as D20.

### D-G3 · `SnapshotDirection` is a core enum, and the direction route is one method

`Gainers` and `Losers`, rendered by a `ToWireValue` overload as the lowercase path segment,
following `AggregateTimespan`, the other enum that lands in a path. `TypeBinding` adds it to the
enum arm. The operation is one map row, `ListMoversAsync(SnapshotDirection direction, ...)`: the
spec has one operation with a path enum, and splitting it into two methods would be two rows for
one operation, which the coverage test cannot express.

### D-G4 · Near-duplicate bar models are accepted, per D-N4

`GroupedDailyBar` adds `Ticker` to `Agg`'s shape; `PreviousCloseBar` removes `IsOtc`;
`SnapshotDay` and `SnapshotPreviousDay` differ by `dv`. D-N4's exact-name check refuses `Agg` at
the first two sites and refuses sharing at the third, and its rationale accepted exactly this cost:
a consumer who sees a permanently null member reads it as absent. A map-level override that lets a
site tolerate a model's extra property was considered and rejected; it reopens D-N4 for an
ergonomic gain the design goals rank last, and would be the option nobody finds until it hides a
real mismatch.

### D-G5 · The map corrects the deprecated pair; their models carry generated members only

The historic tick schemas mark properties required that their own examples omit, and declare
nanosecond timestamps as bare integers. The map types the falsely required properties nullable,
which is the existing mechanism for suppressing the `required` modifier, and the timestamps
`long`, as `LastTrade` already does. Each row's summary says which defect it corrects.

`HistoricTrade` and `HistoricQuote` get no hand-written partial and so no computed `Instant`
properties. A consumer who wants them is meant to move to `Trade` and `Quote`, which the Obsolete
message names. The `date` path parameter carries `format: date` and binds to `LocalDate` on its
own; `timestamp` and `timestampLimit` are nanosecond offsets and bind to `long?` via the map, not
to `DateOrNanoseconds`, because this route takes its date in the path and only an integer here.

The two test projects that call these methods add `MASSIVE0002` to `NoWarn` in their own
`.csproj`, following the Stability convention. Nothing is suppressed in generated code.

### D-G6 · Epoch conversions share one internal helper

`LastTrade` keeps a private `FromNanoseconds`. This batch adds nanosecond conversions on `Trade`,
`Quote`, `LastQuote`, `SnapshotLastQuote`, `SnapshotLastTrade`, and `TickerSnapshot`, and
millisecond ones on `GroupedDailyBar`, `PreviousCloseBar`, `SnapshotMinute`, and `MacdValue`. Ten
copies of a two-line method is where a private helper becomes an internal one:
`MassiveDotNet.Rest.Models.Epoch`, static, with `FromMilliseconds(long)` and
`FromNanoseconds(long)`, and `LastTrade` and `IndicatorValue` call it too. It stays internal to the
Rest project because core has no reason to know about wire epochs.

### D-G7 · Fixtures depart from an invalid published example, and say so

The fixture is the published example verbatim except where the example fails its own schema: the
three `request_id` defects become strings. Each departure is a comment on the fixture naming the
field and the reason, as the dividends fixture already does. The deprecated pair's examples are
used unchanged, because the map, not the fixture, absorbs their requiredness defect.

### D-G8 · The live tier proves D19 and one page boundary, then touches everything once

D19's comma-joined array rendering could only be proven live, and the array-parameter work
deferred that proof to the operation that has one: the all-tickers snapshot with two tickers must
return exactly two. Trades on a fixed 2024 session with `limit=2` must cross a page boundary,
which proves the cursor is followed verbatim on the tick envelope. Every other operation gets one
call asserting shape, not values, so a moved response shape shows up on the next local run. The
deprecated pair is included so the suite reports the day Massive retires them.

## Scope

### Surface

| Operation | Method | Returns |
|---|---|---|
| `GetGroupedStocksAggregates` | `ListGroupedDailyAsync(LocalDate date, bool? adjusted, bool? includeOtc)` | `GroupedDailyBar[]` |
| `GetPreviousStocksAggregates` | `ListPreviousCloseAsync(string ticker, bool? adjusted)` | `PreviousCloseBar[]` |
| `Trades` | `ListTradesAsync(string ticker, RangeFilter<DateOrNanoseconds>? timestamp, SortOrder? order, int? limit, string? sort)`, `EnumerateTradesAsync` | `MassivePage<Trade>`, `IAsyncEnumerable<Trade>` |
| `Quotes` | `ListQuotesAsync(...)` as trades, `EnumerateQuotesAsync` | `MassivePage<Quote>`, `IAsyncEnumerable<Quote>` |
| `LastQuote` | `GetLastQuoteAsync(string ticker)` | `LastQuote` |
| `GetStocksSnapshotTickers` | `ListSnapshotsAsync(string[]? tickers, bool? includeOtc)` | `TickerSnapshot[]` |
| `GetStocksSnapshotTicker` | `GetSnapshotAsync(string ticker)` | `TickerSnapshot` |
| `GetStocksSnapshotDirection` | `ListMoversAsync(SnapshotDirection direction, bool? includeOtc)` | `TickerSnapshot[]` |
| `EMA` | `ListEmaAsync(...)` as SMA, `EnumerateEmaAsync` | `MassivePagedResult<IndicatorSeries>`, `IAsyncEnumerable<IndicatorValue>` |
| `RSI` | `ListRsiAsync(...)` as SMA, `EnumerateRsiAsync` | as EMA |
| `MACD` | `ListMacdAsync(string ticker, RangeFilter<DateOrTimestamp>? timestamp, AggregateTimespan? timespan, bool? adjusted, int? shortWindow, int? longWindow, int? signalWindow, SeriesType? seriesType, bool? expandUnderlying, SortOrder? order, int? limit)`, `EnumerateMacdAsync` | `MassivePagedResult<MacdSeries>`, `IAsyncEnumerable<MacdValue>` |
| `DeprecatedGetHistoricStocksTrades` | `ListHistoricTradesAsync(string ticker, LocalDate date, long? timestamp, long? timestampLimit, bool? reverse, int? limit)` | `HistoricTrade[]`, `[Obsolete]` naming `Stocks.ListTradesAsync` |
| `DeprecatedGetHistoricStocksQuotes` | `ListHistoricQuotesAsync(...)` as above | `HistoricQuote[]`, `[Obsolete]` naming `Stocks.ListQuotesAsync` |
| `get_stocks_v1_splits` | `ListSplitsAsync(Filter<string>? ticker, RangeFilter<LocalDate>? executionDate, SetFilter<string>? adjustmentType, int? limit, string? sort)`, `EnumerateSplitsAsync` | `MassivePage<Split>`, `IAsyncEnumerable<Split>` |
| `get_stocks_v1_exchanges` | `ListExchangesAsync(int? limit)`, `EnumerateExchangesAsync` | `MassivePage<StockExchange>`, `IAsyncEnumerable<StockExchange>` |

`CancellationToken` is last and defaulted on every method. Filter types are what the generator
derives from each field's suffix set; the map names only element types (`LocalDate` on
`execution_date`, `DateOrNanoseconds` on the v3 `timestamp`). The grouped daily `date` path
parameter is a bare string in the spec and takes `LocalDate` from the map. `Trades` and `Quotes`
are mapped before the deprecated pair so the Obsolete messages resolve.

### Models

Structs, `readonly partial record struct`, per D4:

| Model | Pointer, from | Notes |
|---|---|---|
| `GroupedDailyBar` | `results/items` of `GetGroupedStocksAggregates` | `Agg`'s rows, `t` as `long`, plus `T` as `Ticker` |
| `PreviousCloseBar` | `results/items` of `GetPreviousStocksAggregates` | `Agg`'s rows without `otc` |
| `Trade` | `results/items` of `Trades` | `sip_timestamp`, `participant_timestamp`, `trf_timestamp` as `long` nanoseconds |
| `Quote` | `results/items` of `Quotes` | as `Trade`, with bid and ask sides |
| `LastQuote` | `results` of `LastQuote` | v2 short keys renamed; `t`, `y` as `long`, `f` as `long?` |
| `SnapshotDay` | `ticker/day` of `GetStocksSnapshotTicker` | has `dv` |
| `SnapshotPreviousDay` | `ticker/prevDay` | no `dv` |
| `SnapshotMinute` | `ticker/min` | `t` as `long` milliseconds, `av` as `long` |
| `SnapshotLastQuote` | `ticker/lastQuote` | `t` as `long` nanoseconds |
| `SnapshotLastTrade` | `ticker/lastTrade` | `t` as `long` nanoseconds |
| `MacdValue` | `results/values/items` of `MACD` | `histogram`, `signal`, `value`, `timestamp` as `long` milliseconds |
| `HistoricTrade` | `results/items` of `DeprecatedGetHistoricStocksTrades` | D-G5 corrections; no partial |
| `HistoricQuote` | `results/items` of `DeprecatedGetHistoricStocksQuotes` | D-G5 corrections; no partial |

Classes, `partial record`:

| Model | Pointer, from | Notes |
|---|---|---|
| `TickerSnapshot` | `ticker` of `GetStocksSnapshotTicker` | five nullable struct members; `updated` as `long` nanoseconds; `fmv` as `FairMarketValue` |
| `MacdSeries` | `results` of `MACD` | `items: values`; `underlying` reuses `IndicatorUnderlying` |
| `Split` | `results/items` of `get_stocks_v1_splits` | `execution_date` is `LocalDate` from its format |
| `StockExchange` | `results/items` of `get_stocks_v1_exchanges` | |

The snapshot models are declared once, from the single-ticker operation, and named from the
`tickers/items/...` sites of the other two, where D16's structural check verifies them. `EMA` and
`RSI` reuse `IndicatorSeries`; `MACD` reuses `IndicatorUnderlying`.

Partials add `[JsonIgnore]` computed `Instant` properties, through `Epoch` (D-G6), on
`GroupedDailyBar`, `PreviousCloseBar`, `Trade`, `Quote`, `LastQuote`, `SnapshotMinute`,
`SnapshotLastQuote`, `SnapshotLastTrade`, `TickerSnapshot`, and `MacdValue`.

### Files

```
src/MassiveDotNet/DateOrNanoseconds.cs                 new (D-G2)
src/MassiveDotNet/SnapshotDirection.cs                 new (D-G3)
src/MassiveDotNet/MassiveEnumValues.cs                 + SnapshotDirection.ToWireValue; ToWireValueNanoseconds fixed
src/MassiveDotNet/Http/RequestUriBuilder.cs            AppendElement: + DateOrNanoseconds
tools/MassiveDotNet.CodeGen/TypeBinding.cs             ElementTypes + DateOrNanoseconds; enum arm + SnapshotDirection
specs/endpoints.map.json                               17 models, 15 endpoints
src/MassiveDotNet.Rest/Generated/                      regenerated
src/MassiveDotNet.Rest/Models/Epoch.cs                 new internal helper (D-G6)
src/MassiveDotNet.Rest/Models/{LastTrade,IndicatorValue}.cs   call Epoch
src/MassiveDotNet.Rest/Models/*.cs                     ten new partials
tests/MassiveDotNet.CodeGen.Tests/                     SnapshotDirection path arm; DateOrNanoseconds element
tests/MassiveDotNet.Rest.Tests/                        fixtures, rendering, deserialization, traversal; CoverageBaseline 22
tests/MassiveDotNet.Rest.Tests/*.csproj                NoWarn MASSIVE0002
tests/MassiveDotNet.IntegrationTests/                  D19 proof, trades traversal, one call per operation
tests/MassiveDotNet.IntegrationTests/*.csproj          NoWarn MASSIVE0002
samples/MassiveDotNet.AotSmokeTest/Program.cs          trades with a nanosecond range; snapshots with a ticker array
CLAUDE.md                                              D20; Filters and Stability conventions
```

## Testing

**Core**, in `tests/MassiveDotNet.Rest.Tests` beside the filter rendering tests:

- `DateOrNanoseconds` renders a date as `yyyy-MM-dd`, an instant one microsecond after the epoch as
  `1000`, a `long` unchanged, and a literal verbatim; equality follows the value.
- `ToWireValueNanoseconds` on the same instant returns `1000`, the test D-G2 says was missing.
- `SnapshotDirection.ToWireValue` renders `gainers` and `losers`.

**Generator**, in `tests/MassiveDotNet.CodeGen.Tests`, through the harness:

- A path parameter typed `SnapshotDirection` emits `AppendPathLiteral(direction.ToWireValue())`.
- A range comparator group typed `DateOrNanoseconds` emits `RangeFilter<DateOrNanoseconds>?`.

**REST**, driven through the public API against the stub handler, one class per family:

- Grouped daily, previous close, last quote: path and query rendering, and the corrected example
  deserializing with the wide integers as `long` and the computed instants at the right precision.
- Trades and quotes: `timestamp.gte` renders nineteen digits from an `Instant` and `yyyy-MM-dd`
  from a `LocalDate`; `order` renders `asc`; the fixture round-trips with `SipTimestamp` asserted
  to the nanosecond; `HasMore` is true from the sample and false from a derived last page.
- Snapshots: `tickers` renders comma-joined and escaped, an empty array is omitted, the movers path
  contains `gainers`, one fixture deserializes through every nested struct, and a ticker with no
  `min` leaves it null.
- Indicators: EMA and RSI reuse the SMA fixture shape; MACD renders its three windows and
  deserializes `histogram` and `signal`.
- Historic trades and quotes: the path carries the date, the `long` query parameters render, and
  the unchanged published example deserializes with its absent fields null.
- Splits and exchanges: `execution_date.gte` and `adjustment_type.any_of` render; both fixtures
  round-trip; exchanges traverses two stub pages through `Enumerate`.
- `EndpointCoverageTests` at baseline 22; `StabilityAttributesMatchTheSpecification` covers the
  deprecated pair automatically.

`TemporalTypeTests` covers every new type automatically. The AOT smoke test adds the two calls in
Scope and must publish with zero IL warnings. The regenerate-and-diff check runs as before.

**Live**, in `tests/MassiveDotNet.IntegrationTests`, excluded from CI by category (rule 13): D-G8.

## Non-goals

- **`/stocks/dev/trades/{ticker}`.** Its own issue, about what `dev` means for D18.
- **Snapshot models for crypto, forex, and options.** Their schemas differ; D16's check decides
  reuse when those groups arrive.
- **Any change to `DateOrTimestamp`.** D-G2 is a second type, not a parameter on the first.
- **Computed instants on the deprecated models.** D-G5.
- **A map-level reuse override for near-duplicate models.** D-G4.

## Issue bookkeeping

- #8 closes with the merge; the closing comment lists the fifteen and where the three unnamed
  operations of its twenty went.
- #9 gets a comment that splits and exchanges landed under Stocks; its count drops from 37 to 35.
- #14 gets a comment adopting `/v1/open-close/{indicesTicker}/{date}`.
- A new issue records the `/stocks/dev/trades/{ticker}` stability question.
