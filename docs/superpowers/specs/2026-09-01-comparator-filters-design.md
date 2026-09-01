# Comparator filters: one typed parameter per field

Issue: [#2](https://github.com/jerbersoft/massivedotnet/issues/2) · Milestone: v0.1 REST · Date: 2026-09-01

## Why

The OpenAPI description exposes every comparator as its own flat query parameter: `ticker`,
`ticker.gt`, `ticker.gte`, `ticker.lt`, `ticker.lte`, `ticker.any_of`. Across the 93 operations
that use them there are 1,182 such parameters, and emitting them one-to-one gives
`/stocks/financials/v1/ratios` a 114-parameter method.

Issue #2 proposed collapsing each comparator family into a value type — `RangeFilter<T>` beside
the plain field, `AnyOf<T>` beside that — for roughly 414 typed parameters. This design goes one
step further and makes the **field** the unit. Each field is one parameter whose type says exactly
which comparators the endpoint accepts, and equality is an implicit conversion, so the call shape
callers already know keeps compiling.

Only one endpoint is mapped today and it has no comparators, so this lands before the ten endpoint
groups (#8–#17) rather than rewriting them. Every one of those groups inherits the shape chosen
here.

## What the spec actually declares

Measured on `specs/openapi.json` on 2026-09-01, not inferred from the prose.

| Question | Finding |
|---|---|
| Which suffixes exist? | `gt`, `gte`, `lt`, `lte` (254 each), `any_of` (150), `all_of` (10). |
| Is every variant an optional query parameter? | **Yes.** None is required, and no base field that carries comparators is required either, so every filter parameter can default to `null`. |
| Does a variant's type ever differ from its base field's? | Yes, in 9 of 286 field groups: every `.any_of` variant in the Benzinga earnings/guidance and short-interest/short-volume operations is declared `string` over an `integer` or `number` base. |
| Where is a variant declared? | Usually immediately after its base field, but not always: 45 of 286 groups are not contiguous. For example, `/v3/reference/dividends` declares `ticker` at index 0 and its variants at indices 8-11. |
| Does every group have a plain base field? | All but one: `/v1/summaries` declares `ticker.any_of` and no `ticker`. |
| Are all dotted names comparators? | **No.** `/v1/reference/sec/filings` declares nested field paths such as `entities.company_data.name` and `entities.company_data.name.search`. Grouping must key on a suffix allowlist, not on the presence of a dot. |
| Which suffix combinations occur? | Exactly four, tabulated below. |
| What element types occur? | `string` in 192 groups, `number` in 38, `integer`/`int64` in 15, `string`/`date` in 8, and a `oneOf` of date-time and date in 4 (news `published_utc`). Many `string` groups are temporal by name: `timestamp`, `date`, `filing_date`, `last_updated`. |
| Does the catalog (D11) know groupings the spec does not? | No. On the three endpoints compared, the catalog's "Supports .gt/.gte/.lt/.lte/.any_of" notes match the spec's parameter lists exactly. |

Neither a variant's type nor its position can be trusted, which is why `Spec.Slots` keys grouping on
the base field's name and reads the element type and prose from that field by name, never from a
variant or from declaration order.

| Suffix set | Field groups | Meaning |
|---|---|---|
| `gt gte lt lte` | 136 | a range over an ordered value |
| `gt gte lt lte any_of` | 118 | a range, or membership in a set |
| `any_of` | 22 | membership in a set |
| `any_of all_of` | 10 | an array-valued field; the plain form is "arrays that contain the value" |

What each candidate shape does to the worst signatures:

| Endpoint | Flat | Issue's model | One filter per field |
|---|---|---|---|
| `/stocks/financials/v1/ratios` | 114 | 48 | 24 |
| `/etf-global/v1/analytics` | 69 | 30 | 15 |
| `/v3/reference/dividends` | 35 | 17 | 11 |
| operations still over 12 parameters | 65 | 31 | 2 |

## Decisions

### D-F1 · The field is the unit

```csharp
await client.Stocks.ListDividendsAsync(
    ticker: "AAPL",                                        // equality, by implicit conversion
    exDividendDate: RangeFilter.Between(from, to),         // ex_dividend_date.gte / .lte
    frequency: RangeFilter.Gte(4L),                        // frequency.gte
    distributionType: SetFilter.AnyOf("recurring", "special"),
    cancellationToken: ct);
```

One parameter per field, typed by what the endpoint accepts. This is how the platform documents
itself — the catalog lists `ticker` once and notes which variants it supports — and it is the
"unrepresentable-if-wrong" argument D12 makes for temporal types, applied to comparators: a
range on a set-only field does not compile.

Signatures return to their plain-field count. Nothing is allocated per call beyond the array a
set's caller supplies. The shipped `ListAggregatesAsync` keeps its convention, and a plain-field
call such as `ticker: "AAPL"` compiles unchanged on every endpoint.

Rejected: the issue's own model, a plain field plus separate `xxxRange` and `xxxAnyOf`
parameters. Thirty-one operations would still exceed twelve parameters, and `ticker` together with
`tickerAnyOf` on one call is expressible but meaningless. Also rejected: a request object per
endpoint. It allocates per call, introduces a second calling convention beside the one already
shipped, and adds 147 generated types for a problem that flat parameters solve on all but two
operations.

Recorded in CLAUDE.md as **decision D15**.

### D-F2 · Four value types, chosen by the exact suffix set

| Type | Suffix set | Modes | Groups |
|---|---|---|---|
| `RangeFilter<T>` | `gt gte lt lte` | equal; a lower bound; an upper bound; both | 136 |
| `SetFilter<T>` | `any_of` | equal; any-of | 22 |
| `Filter<T>` | `gt gte lt lte any_of` | everything the two above express | 118 |
| `ArrayFilter<T>` | `any_of all_of` | contains; any-of; all-of | 10 |

All four are `readonly struct`s in namespace `MassiveDotNet`, in the core package beside
`DateOrTimestamp`. Each holds at most two `T` values, an optional `T[]`, and a mode; there is no
reference to anything else.

The generator selects the type from the group's suffix set, exactly. **Any other set fails
generation**, naming the operation, the field, and the set it found. A new combination needs a
design, not a guess — and this is what makes a future spec sync that introduces, say, a `.ne`
suffix fail loudly rather than emit something plausible.

Why four types rather than one `Filter<T>` for everything: a single type would let a caller hand
`any_of` to a range-only field. The generated code would then have to drop it silently or throw at
runtime, and both are worse than a compile error.

Equality members are deliberately absent. Filters are arguments, not values anyone compares, and
`SetFilter<T>` holds an array, for which synthesized record equality would be reference equality
and therefore a lie.

### D-F3 · Factories on non-generic classes; implicit conversions do the composing

```csharp
RangeFilter.Gt(x)   RangeFilter.Gte(x)   RangeFilter.Lt(x)   RangeFilter.Lte(x)
RangeFilter.Between(lower, upper)          // gte and lte, inclusive at both ends
RangeFilter.Gte(start).Lt(end)             // chained: a half-open window
SetFilter.AnyOf(a, b, c)                   // params T[]
ArrayFilter.Contains(x)   ArrayFilter.AnyOf(a, b)   ArrayFilter.AllOf(a, b)
```

Factories live on non-generic static classes so `T` is inferred from the argument. A bounded
`RangeFilter<T>` exposes instance `Gt`, `Gte`, `Lt`, and `Lte` that add the other side, so
half-open windows — the norm for timestamps — need no extra factory.

| From | To | Meaning |
|---|---|---|
| `T` | any of the four | equality; `Contains` for `ArrayFilter<T>` |
| `RangeFilter<T>` | `Filter<T>` | unchanged |
| `SetFilter<T>` | `Filter<T>` | unchanged |
| `SetFilter<T>` | `ArrayFilter<T>` | any-of stays any-of; equality becomes `Contains` |

Callers learn three factory classes and meet `Filter<T>` and `ArrayFilter<T>` only in signatures.
The conversions are what let `RangeFilter.Between(a, b)` be passed to a `Filter<LocalDate>?`
parameter and `"AAPL"` to any of them; C# applies the user-defined conversion and then lifts into
the nullable parameter type in one step, and a `null` argument takes the standard nullable
conversion without ever consulting the user-defined one.

Validation happens at construction, where the stack trace names the caller: an empty set or a
null element throws `ArgumentException`; chaining a bound onto a side already set, or onto an
equality, throws `InvalidOperationException`. `Between` does not check that `lower <= upper`. The
check would need a comparison constraint that `string` cannot honour sensibly, and the server
rejects an empty range on its own.

One exception: the implicit `T` → filter conversion treats a null *reference* as an unset filter
rather than throwing, because C# lifts a user-defined conversion only for a nullable value-type
source, so for a reference-type `T` the operator receives `null` directly instead of being
skipped. Throwing `ArgumentNullException` there would name a parameter (`value`) the caller never
wrote, so an unset filter instead matches the SDK's convention that a null optional argument means
omit it. The factories above and the chained bounds still throw: only the conversion treats null
as absence.

Known wart, accepted: `RangeFilter.Gt(1)` infers `RangeFilter<int>`, so a `long` field needs
`1L` and a `double` field `1.0`. The compiler error names both types, and widening conversions
between filter instantiations would double the conversion surface for a one-character fix.

### D-F4 · Element types are a closed set

`string` · `int` · `long` · `double` · `LocalDate` · `DateOrTimestamp`

Rendering is per element type and reflection-free, so the set must be closed. The generator
validates the element type before the builder ever sees it; the builder's own fallback throws
`NotSupportedException` and is unreachable from generated code.

| Element | Rendered as | How |
|---|---|---|
| `string` | percent-escaped | as the existing `AppendQuery(string?)` does |
| `int`, `long` | decimal | formatted in place, as the existing numeric overloads do |
| `double` | shortest round-trip, invariant culture | `TryFormat` into stack space; never `1,5` under a comma-decimal culture |
| `LocalDate` | `YYYY-MM-DD` | the same `ToWireValue` the path segments use |
| `DateOrTimestamp` | date or Unix milliseconds | its own `ToString` |

`Instant` is deliberately absent. An instant carries no wire precision: the aggregates family takes
milliseconds, trades and quotes take nanoseconds, and Benzinga's `last_updated` takes seconds or
ISO 8601. `DateOrTimestamp` covers the millisecond family today; the nanosecond type arrives with
#8 and joins the set then. Adding an element type is one dispatch case, one allowlist entry in the
generator, and one builder test.

### D-F5 · Rendering lives in the URI builder, not in generated code

`RequestUriBuilder` gains four generic overloads, one per filter type:

```csharp
public void AppendQuery<T>(scoped ReadOnlySpan<char> name, RangeFilter<T>? filter)
public void AppendQuery<T>(scoped ReadOnlySpan<char> name, SetFilter<T>? filter, bool hasExactForm = true)
public void AppendQuery<T>(scoped ReadOnlySpan<char> name, Filter<T>? filter)
public void AppendQuery<T>(scoped ReadOnlySpan<char> name, ArrayFilter<T>? filter)
```

Generated code stays at one line per field, and the emitted file for `ratios` remains something a
reviewer can read:

```csharp
builder.AppendQuery("ex_dividend_date", exDividendDate);
```

| Filter | Wire form |
|---|---|
| equality, or `Contains` | `field=v` |
| a bound | `field.gt=v`, `field.gte=v`, `field.lt=v`, `field.lte=v` |
| `Between(a, b)` | `field.gte=a&field.lte=b` |
| `AnyOf(a, b)` | `field.any_of=a,b` |
| `AllOf(a, b)` | `field.all_of=a,b` |
| `null` | nothing, like every other optional parameter |

Within one field the order is fixed — `field`, `.gt`, `.gte`, `.lt`, `.lte`, `.any_of`, `.all_of` —
whatever order the spec happened to declare. The suffix is appended after the name from a literal,
never by string concatenation, so no name is allocated. Set elements are percent-escaped
individually and joined by a literal comma, so a value that itself contains a comma is
distinguishable from the separator.

Element formatting dispatches on `typeof(T)`, which is a constant per generic instantiation that
both the JIT and the Native AOT compiler fold away: no boxing, no reflection, and no cost the
single-value overloads do not already pay.

Two alternatives were rejected. Emitting one line per comparator into generated code, reading
typed accessors off the filter, would give `ratios` roughly two hundred builder lines per method
and spread rendering across 93 files; one implementation has one set of tests, the same argument
D-P1 made for the page loop. One overload per element type per filter type is twenty-four overloads
of near-identical code.

Overload resolution is safe by construction: generated code passes an exactly typed identifier,
and the identity conversion beats a user-defined one, so a `RangeFilter<T>?` argument never lands
on the `Filter<T>?` overload.

### D-F6 · Grouping is derived from the spec, never declared in the map

The generator groups an operation's parameters by base name, recognising a variant only when its
suffix is in the allowlist `gt gte lt lte any_of all_of`. A group takes the position of its base
field in the signature. The SEC filings' nested names have no allowlisted suffix and stay plain
parameters.

This follows D-P6: the map carries only what the spec cannot express, and the spec expresses this
completely. The catalog was checked and agrees. It keeps its D11 role as a build-time aid to the
*author* — it is the catalog, not the spec, that says `last_updated` is a timestamp — which informs
the element type a map row names, never the grouping.

Consequence for the nightly spec sync: a comparator added to an existing field regenerates the
surface on its own, and a suffix set the generator does not recognise fails the run (D-F2).

### D-F7 · The map names the element type on the base field

```json
"parameters": {
  "ticker":            { "name": "ticker" },
  "ex_dividend_date":  { "name": "exDividendDate", "type": "LocalDate" },
  "frequency":         { "name": "frequency" },
  "distribution_type": { "name": "distributionType" },
  "limit":             { "name": "limit" },
  "sort":              { "name": "sort" }
}
```

A row is keyed by the field's base name and, when it supplies a `type`, names the **element** type.
Authors never write `RangeFilter<...>`; the generator wraps the element in the shape D-F2 selects.
A type outside D-F4's closed set fails generation, naming the parameter and the allowed set.

The default element type comes from the base field's schema, with one addition to
`TypeBinding.FromSchema`: `string` with `format: date` becomes `LocalDate`, for parameters and
model properties alike. Nothing shipped declares a date, so nothing changes.

Two map mistakes become generation failures rather than silent no-ops: a row keyed by a variant
name such as `ticker.gte`, and a row keyed by a name the operation does not declare. Today an
unknown key is ignored; with grouping, a row keyed on a variant would be ignored while looking
correct.

### D-F8 · The one base-less group

`/v1/summaries` declares `ticker.any_of` and no plain `ticker`. Its group is still keyed `ticker`,
because a group's identity is its base name, and the map row for it is keyed `ticker` even though
no parameter of that exact name exists. The parameter is a `SetFilter<string>`, and its generated
line passes `hasExactForm: false`:

```csharp
builder.AppendQuery("ticker", tickers, hasExactForm: false);
```

Equality then renders as `ticker.any_of=AAPL`. A one-element set is equality, and the exact form is
undeclared, so this is the only honest rendering. The parameter's documentation comes from the
variant, since it is the only prose the spec offers, and says the field accepts one or more values.

### D-F9 · Wire dates on models deserialize to `LocalDate`

The proof endpoint is the first mapped operation whose result carries calendar dates, so this is
decided here and #9 inherits it.

A `string` property with `format: date` becomes `LocalDate?` (D-F7), deserialized by
`LocalDateJsonConverter` in the core package and registered once on the generated
`MassiveRestJsonContext` through `[JsonSourceGenerationOptions(Converters = [...])]`. The
converter parses `YYYY-MM-DD` from the reader's UTF-8 span and constructs the date directly: no
intermediate string, and none of the `ParseResult<T>` allocation NodaTime's pattern API incurs per
value. Anything else throws `JsonException`, which System.Text.Json annotates with the property
path. Serialization writes the ISO form.

The D5 pattern — a raw wire property plus a computed one — was considered and rejected for dates.
It is right for a `long` timestamp, which costs nothing to hold, but System.Text.Json allocates a
`string` for a wire string regardless, so the raw-plus-computed form costs more than parsing in
the converter, and it doubles every date property on every reference model.

### D-F10 · Schema-required reference properties carry the C# `required` modifier

A model property the schema marks required gets the C# `required` modifier when its resolved type
is a reference type — `string`, an array, or another mapped `class` model, `Dividend.DistributionType`
being the first case — while struct models and other value types are exempt, since a value type
already has a non-null default and needs nothing. The alternative is `= null!`, which pretends a
value exists before one is read, or a nullable `string?`, which pretends the schema allows absence;
either compiles, but only `required` states the fact the schema is actually promising, and without
it the missing assignment is CS8618 under this repository's warnings-as-errors build. At runtime,
System.Text.Json's source generation honours `required` and throws `JsonException` when the member
is absent from the payload, which `MassiveHttpTransport` wraps into `MassiveApiException` like any
other deserialization failure — pinned by
`StocksDividendsTests.AResponseMissingASchemaRequiredFieldIsRejected`. Response envelopes keep every
property nullable regardless of the schema's own required list, because the generated `Send*`
methods reach the result array through null-conditional navigation; that asymmetry is intentional
and is not to be "fixed" by extending this rule to envelopes.

## Scope

This PR lands the mechanism and proves it on one endpoint.

```
src/MassiveDotNet/RangeFilter.cs                          new: struct + factory class
src/MassiveDotNet/SetFilter.cs                            new
src/MassiveDotNet/Filter.cs                               new
src/MassiveDotNet/ArrayFilter.cs                          new
src/MassiveDotNet/Http/RequestUriBuilder.cs               + four filter overloads, + double, + element dispatch
src/MassiveDotNet/Serialization/LocalDateJsonConverter.cs new (D-F9)
tools/MassiveDotNet.CodeGen/Spec.cs                       + comparator grouping
tools/MassiveDotNet.CodeGen/Argument.cs                   + filter arguments
tools/MassiveDotNet.CodeGen/TypeBinding.cs                + element allowlist, + format:date → LocalDate
tools/MassiveDotNet.CodeGen/Emitter.cs                    + group emission, + param docs, + converter registration
specs/endpoints.map.json                                  + get_stocks_v1_dividends, + Dividend model
src/MassiveDotNet.Rest/Generated/                         regenerated
samples/MassiveDotNet.AotSmokeTest/Program.cs             + a filtered dividends call
tests/MassiveDotNet.Rest.Tests/                           filter, dividends, converter tests
tests/MassiveDotNet.IntegrationTests/                     + one live filter test
CLAUDE.md                                                 + D15, + Filters convention
```

**Proof endpoint.** `/stocks/v1/dividends` (`get_stocks_v1_dividends`) maps into the Stocks group
as `ListDividendsAsync`, with `EnumerateDividendsAsync` following from its `next_url`. It exercises
three of the four shapes and three element types in one signature, and the spec carries its
published sample response:

```csharp
public Task<MassivePage<Dividend>> ListDividendsAsync(
    Filter<string>? ticker = null,
    RangeFilter<LocalDate>? exDividendDate = null,
    RangeFilter<long>? frequency = null,
    SetFilter<string>? distributionType = null,
    int? limit = null,
    string? sort = null,
    CancellationToken cancellationToken = default)
```

`Dividend` is a `record` class per D4. Its four `format: date` properties become `LocalDate?`
without a map override; the remaining eight follow the schema. `CoverageBaseline` rises to 2.

**Parameter documentation.** A filter parameter's `<param>` is the base field's description plus
one generated sentence naming the accepted forms. The variants' own descriptions are boilerplate
("Range by ticker.") and are dropped.

| Type | Appended sentence |
|---|---|
| `RangeFilter<T>` | Accepts an exact value or a range. |
| `SetFilter<T>` | Accepts an exact value or a set of values. |
| `SetFilter<T>`, base-less (D-F8) | Accepts one or more values. |
| `Filter<T>` | Accepts an exact value, a range, or a set of values. |
| `ArrayFilter<T>` | Matches arrays containing the value, any of the values, or all of the values. |

**CLAUDE.md.** D15 records D-F1 and D-F6 in one row. The Conventions section gains a **Filters**
bullet beside Pagination, stating the shape rule and that grouping is read from the spec.

## Testing

All offline tests drive public API. `RequestUriBuilder` is public, so builder-level tests are not
tests of internals.

1. **Builder rendering.** Every filter type against representative element types renders the exact
   documented wire string: equality, each bound, `Between`, a chained half-open window, `AnyOf`
   with an element needing escaping (`BRK/B` becomes `BRK%2FB`, the comma stays literal), `AllOf`,
   `Contains`, the base-less equality-as-`any_of` form, and `null` for each type emitting nothing.
   There is no comma-decimal-culture test: the repository builds with `InvariantGlobalization`,
   so such a test would be vacuous; CA1305 is the guard, and the `double` path names
   `CultureInfo.InvariantCulture` explicitly.
2. **Construction guards.** Empty sets and null elements throw `ArgumentException`; chaining onto a
   set side or onto an equality throws `InvalidOperationException`.
3. **Endpoint through the stub.** `ListDividendsAsync` with equality, a range, and a set produces
   the documented query string; with nothing supplied the request carries no `?`.
4. **Deserialization.** The published sample deserializes into `Dividend`, including the four
   `LocalDate?` properties; a malformed date throws `JsonException`.
5. **Generator contract.** `EndpointCoverageTests` continues to guard the map; CI's idempotency
   check covers rule 6 unchanged.
6. **Public surface.** `TemporalTypeTests` reflects over the new generic types on its own; nothing
   to add.

The AOT smoke sample must call the new endpoint with a range and a set. An unreferenced generic
instantiation is trimmed away and a clean publish would prove nothing about it — the same reason
the sample already roots `EnumerateAsync`. This is what turns "no boxing, no reflection" from a
claim into an IL-warning-free publish.

The live tier gains one test: a dividends call with a date range and a two-ticker set, asserting
every result's ticker is in the set and its ex-dividend date within the range. It confirms the
comma encoding and the inclusive-bound semantics the spec does not state. Local only, excluded from
CI by category (rule 13).

## Non-goals

- **Typed enums for query fields.** `distribution_type` and its peers stay `string`; an SDK enum
  as an element type needs generated rendering and is its own decision.
- **Typed sort specifications** such as `ticker.asc`. `sort` stays `string`.
- **A nanosecond timestamp element type.** Arrives with #8, where trades and quotes need it.
- **The news endpoint's `oneOf` date field.** Defaults to `string`; its group's map row chooses
  when the Reference group is mapped.
- **Proving `all_of` through an endpoint.** No mapped operation has an array field yet; the builder
  test covers `ArrayFilter<T>` until one does.
- **Widening conversions between filter instantiations.** See the wart in D-F3.
