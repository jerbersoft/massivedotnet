# Singular results: object and body payloads, paginated or not

Issue: [#31](https://github.com/jerbersoft/massivedotnet/issues/31) · Milestone: v0.1 REST · Date: 2026-09-02

## Why

The endpoint `result` row knows one kind, `array`: a named property of the envelope holding an
array of a model. Fifty of the 147 operations do not fit it. Twenty-eight return one object under
`results`, and twenty-two have no `results` at all: the payload sits under a property named
`ticker`, `tickers`, `last`, or `ticks`, or the body itself is the payload, as it is for market
status, the four open/close endpoints, and market holidays, which is a bare JSON array.

Since #32 the emitter refuses every one of these with a message pointing here, rather than mapping
the object to `string`. That was the right interim behaviour and it is now the blocker: the Stocks
group (#8) needs every singular shape within its first few operations, and every other group has
at least one.

The issue as filed was narrower. It found that `Spec.IsPaginated` reads `next_url` from the spec
(D-P6) and so would force `MassivePage<T>` onto the options contract snapshot, whose `results` is
one object. Measuring the spec shows that shape is not one operation but twenty-one, and twenty of
them, the technical indicators, paginate for real. So D-P6 does not need an escape hatch for a
singular envelope; it needs a definition of what a page *is* when the page body is an object.

## What the spec and the wire actually do

Measured on `specs/openapi.json` and probed against the live API on 2026-09-02.

| Shape of the success schema | Ops | Of which declare `next_url` |
|---|---|---|
| `results` is an array | 97 | 79 |
| `results` is one object | 28 | 21: the 20 indicators and `OptionContract` |
| No `results` property | 22 | 0 |

The 22 without `results` split 15 to 7. Fifteen name the payload property: `ticker` (3 single-ticker
snapshots), `tickers` (6 snapshot lists and gainers/losers), `last` (two last trade/quote endpoints and
currency conversion), `data` (a deprecated order book), and `ticks` (2 deprecated tick endpoints
that also carry a `map` legend). Seven have no payload property because the body is the payload:
`GetMarketStatus`, the four `*OpenClose` operations, `GetFilingFile`, and `GetMarketHolidays`,
whose root is an array.

| Question | Finding |
|---|---|
| Does an indicator endpoint really paginate? | Yes. SMA on AAPL with `limit=2` returned two values and a cursor; following it returned the next two values and a new cursor. |
| Where are the page items? | `results.values`. `results` itself is the same object on every page. |
| What is `results.underlying`? | Per page. Each page carried its own `url` and, with `expand_underlying=true`, the seven aggregates that page's values were computed from. Page two's set overlapped page one's by six, as a windowed indicator should. |
| Does `underlying.url` carry a key? | No, on either page, under bearer authentication. |
| Does the options contract snapshot send `next_url`? | The published sample does not. The live endpoint is not entitled on the plan used, so the wire could not be checked. The shape, one contract, cannot page. |
| What does a singular endpoint return for an unknown ticker? | 404 with an error envelope, on last trade, the single-ticker snapshot, ticker details, and an open/close date with no session. A 200 always carried its payload. |
| What does an indicator return for an unknown ticker? | 200 with `results` present, `values` empty, and no cursor. |

The official Python client answers the indicator question by returning the whole object from a
single call and exposing `next_url` as a string. This SDK already rejected exposing a raw cursor
(D-P4), and its `Enumerate` surface is the reason.

## Decisions

### D-S1 · The result row has two kinds, and an omitted `property` means the body

```jsonc
"result": { "kind": "array",  "model": "Agg",             "property": "results" }   // today
"result": { "kind": "object", "model": "IndicatorSeries", "property": "results" }   // one object
"result": { "kind": "object", "model": "DailyOpenClose" }                           // the body is the object
"result": { "kind": "array",  "model": "MarketHoliday" }                            // the body is the array
```

`kind` says whether the payload is one model or an array of it. `property` names where the payload
sits on the envelope, and when it is absent there is no envelope: the body deserializes as the
model itself, or as an array of it. The matching model row for a body object omits `pointer`,
which `Spec.Navigate` already reads as the success schema root; the model row for a body array
points at `items`.

A third kind was considered, `envelope`, for the body-is-the-payload case. It would have said the
same thing as an absent `property` with one more word to learn, and it would have needed its own
array variant for market holidays. Two kinds crossed with an optional location cover every shape
the spec has.

What each combination generates, with the spec's `next_url` as the only pagination signal:

| Spec says | Map says | `List`/`Get` returns | `Enumerate` |
|---|---|---|---|
| `results` array, `next_url` | `array` | `MassivePage<T>` | `IAsyncEnumerable<T>` |
| `results` array | `array` | `T[]` | none |
| object, `next_url`, model has `items` | `object` | `MassivePagedResult<T>` (D-S2) | `IAsyncEnumerable<TItem>` over `items` |
| object, `next_url`, no `items` | `object` | `T`, guarded (D-S3) | none |
| object | `object` | `T` (D-S4) | none |
| body is the object or array | no `property` | `T` or `T[]` (D-S5) | none |

### D-S2 · A paginated singular result returns `MassivePagedResult<T>`, and its items are named on the model

```csharp
namespace MassiveDotNet;

public readonly record struct MassivePagedResult<T>
{
    public T Result { get; }          // the page's single object, never null
    public bool HasMore { get; }      // next_url was present
    public string? RequestId { get; }
}
```

An indicator page is an object with two halves: the values, which continue across pages, and the
underlying aggregates, which belong to this page alone. `MassivePage<T>` cannot hold that, because
its payload is `T[]`. `MassivePagedResult<T>` is its exact sibling with `T` in place of `T[]`, and
the two methods keep their contracts from D-P2:

```csharp
Task<MassivePagedResult<IndicatorSeries>> ListSmaAsync(...)   // one page, both halves, HasMore
IAsyncEnumerable<IndicatorValue>          EnumerateSmaAsync(...) // every value across every page
```

`Enumerate` flattens `results.values`. The per-page `underlying` is not observable through it, and
the remark says so and points at `List`; a flat sequence has nowhere to attach per-page data, and
page-level iteration remains a non-goal from the pagination design.

Which array inside the object carries the page items is a fact about the shape, so the model row
declares it once rather than each of twenty endpoint rows repeating it:

```jsonc
"IndicatorSeries": {
  "schema": { "operationId": "SMA", "pointer": "results" },
  "items": "values",
  "properties": {
    "values":     { "name": "Values",     "model": "IndicatorValue" },
    "underlying": { "name": "Underlying", "model": "IndicatorUnderlying" }
  }
}
```

`items` must name a property that is an array of objects at the model's schema and whose own row
names a `model`; that model is `TItem`. The generated envelope implements `IPagedEnvelope<TItem>`
explicitly, so `MassiveHttpTransport.EnumerateAsync` walks it unchanged:

```csharp
IndicatorValue[]? IPagedEnvelope<IndicatorValue>.Results => Results?.Values;
```

Two alternatives were rejected. Synthesizing `HasMore` and `RequestId` onto the model itself gives
the flattest call site but puts envelope state on a wire type, forbids reusing that model anywhere
else, and costs a record copy per call to fill the ignored members. Returning a plain
`MassivePage<IndicatorValue>` and discarding `underlying` needs nothing new and silently throws
away what `expand_underlying` asked for, which the constitution forbids.

`items` on a model that no paginated operation uses is harmless and ignored. The `List` prefix
rule in `Naming.Enumerate` applies to a paginated singular exactly as to an array: `ListSma`
derives `EnumerateSma`, and a `Get` name is refused.

### D-S3 · Pagination stays spec-detected; a cursor the SDK cannot follow throws

D-P6 is unchanged: an operation paginates when its success schema declares `next_url`, and the map
never says otherwise. What the map adds is only *where the items are* once the spec has said there
is a cursor.

That leaves an object result on a paginated operation whose model declares no `items`. Today that
is exactly one operation, `OptionContract`, whose envelope declares `next_url` that its shape can
never honour. The generator emits a plain `Get` for it and keeps the envelope's `NextUrl` member,
and the `Send` method guards it:

```csharp
MassiveHttpTransport.ThrowIfUnfollowableCursor(response.NextUrl, requestUri, response.RequestId);
```

A blank cursor passes, as everywhere else (D-P5). A real one throws `MassiveApiException`, naming
the request URI and carrying the request id, because a page the SDK cannot fetch is missing data,
and missing data is loud in this SDK. The helper is one static method on the transport, tested
directly, so the generated line carries no logic of its own (D-P1). Such an operation emits no
`Enumerate`, so the `List` prefix rule does not apply to it; `GetContractSnapshot` is a fine name.

A map acknowledgement key was the alternative: refuse generation until the author writes something
like `"cursor": "ignored"`. It is loud at generation time and silent forever after, it declares a
pagination fact in the map against D-P6, and it needs a staleness check for when the spec drops
the field. The runtime guard needs nothing and cannot drift.

### D-S4 · A singular `Get` returns `T`; a 200 without its payload throws

Every singular endpoint returns `Task<T>`, not `Task<T?>`, whether or not the description marks the
payload required. The description marks `results` optional on last trade, ticker details, and the
snapshots, and required on every indicator; it is no more reliable about requiredness than it is
about formats (D16), and `Task<T?>` on every `Get` would tax every caller, on every call, for a
case the wire does not produce: every unknown-ticker probe returned 404, which is already an
exception.

When a 200 nonetheless carries no payload, the `Send` method throws `MassiveApiException` with
status 200, the request URI, and the response's request id, so support can trace it. That is the
same treatment a body that fails to deserialize already receives, and the same class of failure:
a successful status with an unusable body.

```csharp
LastTrade result = response?.Results
    ?? throw new MassiveApiException(
        HttpStatusCode.OK,
        $"The response from '{requestUri}' carried no 'results' payload.",
        response?.RequestId);
```

For a paginated singular the same line precedes constructing the `MassivePagedResult<T>`.

### D-S5 · Body payloads deserialize the model directly

When `property` is absent no envelope class is emitted. The model, or `T[]` for a body array, is
registered in `MassiveRestJsonContext` alongside the envelopes, and the `Send` method deserializes
it with the model's own type info. A body object that deserializes to `null` (an empty body)
throws as in D-S4, with no request id because there is no envelope to carry one. A body array
coalesces `null` to empty, as arrays do today.

Envelope-only members such as `status` and `request_id` become model properties when the
description declares them at the root, because that is the wire. `DailyOpenClose.Status` is what
Massive's own documentation shows.

Because the model is the body, the existing rule that an envelope-level object outside the result
is unbound now says to declare the body as the result, instead of pointing at this issue. The
refusal stays: a `ticker` snapshot with its payload under `ticker` binds with
`"property": "ticker"`, and an operation with several top-level objects binds the body.

### D-S6 · The structural check extends to every result site

`ValidateResultReuse` (D-N4) already verifies an `array` result against the model's origin at
`{property}/items`. It now resolves the site by kind and location: `object` checks `{property}`
or the root, `array` checks `{property}/items` or `items`. A model named as a singular result is
verified against the same names-and-requiredness rules as a nested reuse, so `Agg` may serve as
`IndicatorUnderlying.Aggregates` only because the check passes: the aggregates origin requires
`o h l c v t` and the indicator site requires those and `n vw` besides.

`items` is verified at the same time: the property exists at the model's schema, is an array of
objects, and has a row naming a `model`.

Refusals, each naming the fix:

- `kind: object` where the site is not an object, or `kind: array` where it is not an array.
- `items` naming a property that is absent, not an array of objects, or whose row lacks `model`.
- Any `kind` other than `array` or `object`.

A body row naming a model generated from `results`, or the reverse, needs no rule of its own: the
origin and the site are different schemas, and the structural check reports the differences.

A body model reused as a nested property is allowed. The structural check guarantees the shape
matches, and nothing breaks if someone does it; a rule with no failure to prevent is policy.

### D-S7 · `SeriesType` is a core enum; `timespan` reuses `AggregateTimespan`

`series_type` on every indicator is `open | high | low | close`. It becomes `SeriesType` in core
with a `ToWireValue` in `MassiveEnumValues`, registered in `TypeBinding`'s enum list beside
`SortOrder`. `timespan` on an indicator omits `second` but is otherwise `AggregateTimespan`, and it
reuses that type. A seven-member `IndicatorTimespan` would buy a compile error for one value nobody
reaches for on an indicator, at the cost of a second timespan type in every indicator signature and
a conversion for anyone passing one through. The remark names the omission, and the server's 400
surfaces as `MassiveApiException`.

Checking that a map-named enum covers every member the spec declares is worth its own issue. It
would not have changed anything here.

## Scope

Four proof endpoints, one per new generator path, following #32's pattern of proving nested
binding on News. `CoverageBaseline` goes from 3 to 7.

| Path proven | Operation | Method | Returns |
|---|---|---|---|
| object + cursor + `items` | `SMA` | `Stocks.ListSmaAsync`, `EnumerateSmaAsync` | `MassivePagedResult<IndicatorSeries>`, `IAsyncEnumerable<IndicatorValue>` |
| object, no cursor | `LastTrade` | `Stocks.GetLastTradeAsync` | `LastTrade` |
| body object | `GetStocksOpenClose` | `Stocks.GetDailyOpenCloseAsync` | `DailyOpenClose` |
| body array | `GetMarketHolidays` | `Reference.ListMarketHolidaysAsync` | `MarketHoliday[]` |

Models: `IndicatorSeries` (class, `items: values`), `IndicatorValue` (struct: `TimestampMilliseconds`
as `long`, `Value`, with `Timestamp` computed in the partial per D5), `IndicatorUnderlying`
(`Url`, `Aggregates` reusing `Agg`), `LastTrade` (struct over the v2 short keys, renamed in the
map; nanosecond timestamps stored as `long` with `Instant` computed in the partial), `DailyOpenClose`
(class; `from` binds to `LocalDate` from its format; `preMarket` typed `double?` on its row because
the description calls a price an integer), and `MarketHoliday` (class; `date` typed `LocalDate`,
`open` and `close` typed `Instant?`).

SMA parameters: `timestamp` with its four comparators becomes `RangeFilter<DateOrTimestamp>`;
`timespan` is `AggregateTimespan`; `series_type` is `SeriesType`; `order` is `SortOrder`;
`adjusted`, `window`, `expand_underlying`, and `limit` take their default bindings.

```
src/MassiveDotNet/MassivePagedResult.cs                new
src/MassiveDotNet/SeriesType.cs                        new
src/MassiveDotNet/MassiveEnumValues.cs                 + SeriesType.ToWireValue
src/MassiveDotNet/Http/MassiveHttpTransport.cs         + ThrowIfUnfollowableCursor
tools/MassiveDotNet.CodeGen/Map.cs                     Property nullable, Items, omitted pointer
tools/MassiveDotNet.CodeGen/TypeBinding.cs             + SeriesType
tools/MassiveDotNet.CodeGen/Emitter.cs                 kinds, body payloads, guard, throw, context
specs/endpoints.map.json                               6 models, 4 endpoints
src/MassiveDotNet.Rest/Generated/                      regenerated
src/MassiveDotNet.Rest/Models/{IndicatorValue,LastTrade}.cs   partials with computed Instants
tests/MassiveDotNet.CodeGen.Tests/                     shape and refusal tests
tests/MassiveDotNet.Rest.Tests/                        fixtures, page, traversal, throw tests
tests/MassiveDotNet.IntegrationTests/                  SMA traversal, open/close
samples/MassiveDotNet.AotSmokeTest/Program.cs          one call per new shape
CLAUDE.md                                              D17, Pagination bullet, Adding endpoints
```

## Testing

**Generator**, in `tests/MassiveDotNet.CodeGen.Tests`, through the harness:

- One generated-shape test per row of the D-S1 table: the envelope's result property is `Model?`
  for `object`; a paginated object with `items` implements `IPagedEnvelope<TItem>` through
  `Results?.Values`; one without emits the guard call and no `Enumerate`; body kinds emit no
  envelope, register `T` or `T[]` in the context, and return the model or array; return types match
  the table.
- One refusal test per rule in D-S6, asserting the message names the fix.
- `Naming.Enumerate` refuses a `Get`-named paginated singular, as it does an array.

**REST**, driven through the public API against the stub handler:

- The four fixtures, taken from the published samples, deserialize into the expected members.
  Names and epoch conversions are asserted on the values the samples carry.
- SMA reports `HasMore` true from the sample, which carries a cursor, and false from a derived last
  page without one.
- An SMA traversal over two stub pages yields every value in order, issues two requests, and
  requests page two only after page one is consumed. `PagingStubHandler` already records that.
- `GetLastTradeAsync` against a 200 whose body is `{"status":"OK","request_id":"r"}` throws
  `MassiveApiException` with status 200 and request id `r`.
- `ThrowIfUnfollowableCursor` passes `null`, empty, and whitespace, and throws for a URL, naming the
  request URI and carrying the request id.
- An empty body on holidays yields an empty array.

`TemporalTypeTests` covers `MassivePagedResult<T>` and every new model automatically. The AOT smoke
test adds one call per new shape and must publish with zero IL warnings. The regenerate-and-diff
check runs as before.

**Live**, in `MassiveDotNet.IntegrationTests`, excluded from CI by category (rule 13): an SMA
traversal at `limit=2` that must cross a page boundary, which no fixture can prove for the singular
shape, and one call to open/close, the body kind whose published sample is likeliest to have
drifted.

## Non-goals

- **The other 16 indicators, MACD's four-field value, market status, the snapshots, and the options
  contract snapshot itself.** Those are their group issues (#8 through #14, and #10 for the
  contract). This lands the binding they need.
- **Page-level iteration** over `IndicatorSeries`. The pagination design declined
  `IAsyncEnumerable<MassivePage<T>>` for arrays; the same holds here.
- **Resume from a cursor.** Still its own issue and its own threat model.
- **CSV content negotiation.** Many singular endpoints also offer `text/csv`; the SDK requests JSON.
- **Verifying map-named enums against spec enum members.** Worth an issue; see D-S7.
