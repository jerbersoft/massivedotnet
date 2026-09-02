# Nested objects: named models generated from the spec

Issue: [#32](https://github.com/jerbersoft/massivedotnet/issues/32) · Milestone: v0.1 REST · Date: 2026-09-02

## Why

`TypeBinding.FromSchema` maps a `type: object` schema to `string`. Until #2 that was harmless,
because no mapped model had a nested object. Since a8e421f the generator emits the C# `required`
modifier on schema-required reference-type properties (D-F10), so the first mapped model with a
required nested object would emit `public required string Publisher { get; init; }` and fail at
deserialization with a `JsonException`, not at generation. The failure is late, silent at build
time, and wrong in kind: the SDK would be claiming a field is a string when the service documents
it as a structure.

Only two endpoints are mapped and neither has a nested object, so this lands before the ten endpoint
groups (#8–#17) rather than rewriting them. Every one of those groups inherits the map contract
chosen here, and most of them meet a nested object within their first few operations.

## What the spec actually declares

Measured on `specs/openapi.json` on 2026-09-02, not inferred from the prose. "Nested" excludes the
result array itself (`results` and `results/items`), which the map already binds.

| Question | Finding |
|---|---|
| How many operations have a nested object? | 55 of 147. |
| How many nested sites? | 184: 116 single objects and 68 arrays of objects. |
| How many distinct shapes? | 60, comparing property names, formats, and required sets. 25 occur exactly once. |
| How deep? | Up to four levels: `results/items/financials/balance_sheet/*`. |
| How many are required? | 50 sites across 18 operations. Each of these is a `JsonException` today. |
| Any with no declared properties? | 7: two deprecated `map` key legends, two crypto order-book exchange maps (`x`), and three financial statements documented as "keys can be any field in the glossary". `balance_sheet` declares a literal `*` property describing the value shape. |
| Any at envelope level, outside the result? | 12 sites across 10 operations: market status (`currencies`, `exchanges`, `indicesGroups`), the three single-ticker snapshots (`ticker`), three `last` objects, and three deprecated. These are the singular results #31 is about. |
| Is reuse real? | Yes. The three snapshot operations per asset class share their `day`, `min`, `prevDay`, `lastTrade`, and `lastQuote` shapes exactly. Indicator `values/items` recurs 15 times and `underlying/aggregates/items` 20 times, identically. |
| Is reuse clean? | No. The indicator aggregates differ from `GetStocksAggregates` items only in declared formats (`number/float` against `number/double`, `t` as `number` against `integer`) and in `n` being required. A stocks snapshot `day` carries `dv` and no `t` or `n`, so it is a different shape from `Agg` by name, not just by format. |
| Any `format: date-time` strings on models? | 13 sites across 11 operations, including `ListTickers` (`delisted_utc`, `last_updated_utc`) and `ListNews` (`published_utc`). All bind to `string` today. |
| Any `format: date-time` parameters? | Only `ListNews` `published_utc` and its four comparator variants, each a `oneOf` of date-time and date with no top-level `type`, so they default to `string`. |

The official Python SDK (`massive-com/client-python`) is hand-written and answers the naming
question the way this design does: every nested shape is a class named by a human (`Publisher`,
`Insight`, `Greeks`, `IndicatorUnderlying`), reused across operations by judgment (`Agg` for
snapshot days and indicator aggregates). What it does not do is detect drift: unknown keys are
dropped by its `modelclass` decorator, nothing is required, and financial statements enumerate the
glossary as fixed fields so an unlisted key vanishes. Those are the parts this design refuses.

## Decisions

### D-N1 · Nested objects are named models in the map, generated from the spec

A nested object, or the element of a nested array of objects, is an ordinary `models` row whose
`schema.pointer` runs through its parent: `results/items/publisher`,
`results/items/insights/items`. `Spec.Navigate` already walks property and `items` segments, and
`Emitter.EmitModel` already reads properties, requiredness, and prose from whatever schema the
pointer lands on. The nested model is generated exactly like a top-level one.

The alternative is an inline record emitted alongside the parent with a path-derived name. It
needs no curation and cannot drift, but the names come out as `StocksSnapshotTickerTickerDay`, and
the three stocks snapshot operations would each get their own identical `Day` type, so a consumer
could not write one function over a snapshot bar. The public surface is the product; naming it is
worth a human's row in the map, which is the same argument that names top-level models today.

Names are domain nouns, prefixed by family only where it disambiguates (`NewsPublisher`,
`NewsInsight`, `Greeks`). An asset-class prefix is used only when the shapes actually differ, which
D-N4 decides rather than the author's impression.

### D-N2 · A property row names the model; the spec supplies the rest

A property row in a model gains one optional key, `model`, naming a row in `models`. The generator
composes the C# type from the spec at that site:

| Schema at the site | Required | Emitted type |
|---|---|---|
| object | yes | `NewsPublisher` (with the `required` modifier per D-F10) |
| object | no | `NewsPublisher?` |
| array of objects | yes | `required NewsInsight[]` |
| array of objects | no | `NewsInsight[]?` |

```json
"NewsArticle": {
  "schema": { "operationId": "ListNews", "pointer": "results/items" },
  "properties": {
    "publisher": { "name": "Publisher", "model": "NewsPublisher" },
    "insights":  { "name": "Insights",  "model": "NewsInsight" }
  }
},
"NewsPublisher": { "schema": { "operationId": "ListNews", "pointer": "results/items/publisher" } },
"NewsInsight":   { "schema": { "operationId": "ListNews", "pointer": "results/items/insights/items" } }
```

`model` is a separate key from `type` rather than a model name inside a `type` string, for two
reasons. Array-ness and requiredness are spec facts and the map should not restate them, whereas
`type` is verbatim by contract (`"double?"`). And a distinct key tells the generator, without
parsing a type string, that this row is a reference to a model it must verify (D-N4). A row
carrying both `model` and `type` fails generation; a `model` naming a row that does not exist
fails generation; a `model` on a property whose schema is neither an object nor an array of
objects fails generation. Each message names the operation, the model, and the property.

### D-N3 · An unbound object fails generation

`FromSchema` no longer has a default for an object. A schema that is an object, or an array whose
items are objects, with no `model` and no `type` on its map row, stops the run. This applies in all
three places a schema is bound:

- **A model property.** The message names the operation, the model, the property, and the exact
  `schema` block to paste into a new row:

  ```
  Operation 'ListNews': property 'publisher' of model 'NewsArticle' is an object with no binding.
  Add a row to "models" in specs/endpoints.map.json:
    "<Name>": { "schema": { "operationId": "ListNews", "pointer": "results/items/publisher" } }
  and set "model": "<Name>" on the 'publisher' row of 'NewsArticle'.
  ```

- **An envelope property other than the result.** Envelopes have no map rows, so the message says
  the operation's envelope carries an object the generator cannot bind and points at #31, where
  singular results get a home. The 12 sites this affects are exactly #31's; none is mapped today.
- **A parameter.** No parameter in the description is an object, so this is a guard against a
  future spec sync, not a live case. The message says to set `type` on the parameter's row.

The alternative, defaulting to something plausible, is what this issue exists to remove. A silent
`string` compiled and shipped; the bug surfaced as a `JsonException` in a consumer's process.

### D-N4 · Reuse is verified structurally, deeply, and on names

Every site that names a model through `model` is compared against the schema at the model's own
pointer. The comparison is a structural signature computed the same way for both:

- the set of property names must be **identical**;
- every property the model's schema marks required must be required at the site too (the reverse
  is allowed: a site may promise more than the model relies on);
- for each property that is an object or an array of objects, the signature **recurses**, comparing the
  site's nested schema against the corresponding nested schema under the model's own pointer, so a
  reused parent proves its children at that site as well;
- scalar types and formats are **not** compared.

A mismatch fails generation, naming the site, the model, and the properties that differ, split into
"at the site but not on the model" and "on the model but not at the site".

Names are exact because the two failure modes are asymmetric and both bad. A site with a property
the model lacks means the SDK silently drops a field the service documents, which is rule 2's
"nothing is silently omitted" arriving on the response side. A site missing a property the model
has means a consumer sees a permanently null member and reads it as "absent from this response"
when it was never there. Requiredness is one-directional because only one direction throws: a model
that requires what the site makes optional is a `JsonException` waiting for the first sparse
response.

Types are trusted from the model's own row because the description disagrees with itself at sites
that are plainly the same thing. The indicator aggregates are `Agg` in every sense a consumer
cares about, and the spec declares their `t` as `number`. Comparing formats would force an
`IndicatorAgg` that differs from `Agg` by nothing a user can observe. The Python SDK reuses `Agg`
there by hand; this design allows the same reuse and keeps it honest on the axis that matters.

Where names genuinely differ the check refuses, and that is the correct outcome. A stocks snapshot
`day` carries `dv` and no `t` or `n`. It is not an `Agg`, whatever the Python SDK says, and the
Stocks group will declare a `SnapshotBar` or similar. The cost of this decision is understood: a
sloppy spec forces near-duplicate models where a human would have shared one. The map author can
always add a second row; nobody can undo a silently dropped field.

### D-N5 · Arrays bind by their element

`FromSchema` recurses into `items`: an array of strings is `string[]`, of numbers `double[]`, of
`format: date` strings `LocalDate[]`. Today every array is `string[]` regardless of its element,
which is wrong in the same silent way as the object default. An array of objects has no default and
needs `model` (D-N3). No array of arrays exists in the description; if one arrives it fails
generation like any other shape without a binding.

On parameters this changes nothing observable: the only array-typed parameters are arrays of
strings, and their rendering is #33's concern.

### D-N6 · Free-form objects bind through `type` to a dictionary

An object schema with no declared properties has nothing to generate a model from, so D-N3 fails
it and the map author supplies `type` explicitly, following one rule: `Dictionary<string, T>`,
where `T` is what the description documents the values to be. The three financial statements are
`Dictionary<string, FinancialDataPoint>`, with `FinancialDataPoint` an ordinary model whose pointer
is `results/items/financials/balance_sheet/*`, since the description literally declares a property
named `*` to describe the value shape. The crypto order-book exchange maps are
`Dictionary<string, double>`; the deprecated key legends are `Dictionary<string, string>`.

`JsonElement` is not sanctioned. It would put the serializer in the public surface and defer to the
consumer a question the description already answers.

The generator's only new obligation is the `required` modifier. D-F10's check enumerates reference
types (`string`, arrays, class models) and would miss a dictionary. It inverts to a closed set of
value types — `int`, `long`, `double`, `bool`, `LocalDate`, `Instant`, any `struct` model, and
their nullable forms — and everything else is a reference type. An unfamiliar type gets `required`
when the schema requires it, which is the safe direction: a spurious modifier is a compile error
in the SDK's own build, a missing one is CS8618 there too.

System.Text.Json source generation handles string-keyed dictionaries and discovers the value type
through the property graph, so no `[JsonSerializable]` entry is needed and Native AOT is unaffected.
The same discovery covers nested models: the context lists envelopes only, as it does today.

### D-N7 · `format: date-time` on a model is an `Instant`

`FromSchema` binds a `string` with `format: date-time` to `Instant`, read by a new
`InstantJsonConverter` beside `LocalDateJsonConverter` and registered on the generated context the
same way. Thirteen response sites use the format, three of them on the tickers endpoints that will
be among the first mapped in #9. Shipping `PublishedUtc` as `string` on the first Reference model
and correcting it later would be a breaking change for no saving now.

This does not conflict with D5. D5 stores epoch numbers raw because the `long` is already the
cheapest representation and the `Instant` costs a conversion only some readers want. An RFC 3339
string has no cheap raw form: keeping it costs a heap string per value, while the parsed `Instant`
is eight bytes inline. Parsing on read is the minimal-allocation choice here.

The converter parses from the reader's UTF-8 bytes with no intermediate string: `YYYY-MM-DDTHH:MM:SS`,
an optional fraction of one to nine digits, then `Z` or a numeric `±HH:MM` offset. Anything else is
a `JsonException` naming the value. NodaTime's `OffsetDateTimePattern` would accept the same
grammar, but its `ParseResult` is an allocation per value, and a 1,000-article page is 1,000
objects discarded on the spot, the argument `LocalDateJsonConverter` already makes. Writing uses
`InstantPattern.ExtendedIso`.

On the **parameter** side the rule is the opposite. The parameter-side `Instant` renders Unix
milliseconds (`ToWireValue`), which is not RFC 3339, so `TypeBinding.Resolve` refuses to default a
`date-time` parameter and tells the map to choose. No such parameter exists: the News
`published_utc` family is a `oneOf` with no `type`, defaults to `string` as the comparator spec
recorded, and the map binds it to `LocalDate`, the form the service's own pagination cursor shows
(`"published_utc":{"gte":"2021-04-26"}`). An RFC 3339 filter element is #9's call, as before.

### D-N8 · The generator gets a test project

Every decision above ends in "fails generation, naming ...", and the acceptance criterion in #32 is
a specific message. None of that is verifiable without a test that feeds the generator a fragment
and reads the exception. Issue #34 already asks for `tests/MassiveDotNet.CodeGen.Tests`; this
design establishes the project and scopes it to the diagnostics and checks defined here. #34 then
fills in the older refusal paths against an existing harness instead of designing one.

`Spec` and `Map` gain a way to load from a string so tests hand in inline JSON rather than temp
files. The project references the generator project directly, with `InternalsVisibleTo`, since
everything in it is `internal`. It runs in CI: it needs no key and no network, so it is in the
offline tier by nature.

## Scope

This PR lands the mechanism and proves it on one endpoint.

```
src/MassiveDotNet/Serialization/InstantJsonConverter.cs   new (D-N7)
tools/MassiveDotNet.CodeGen/Map.cs                         + model key on property rows; + load from string
tools/MassiveDotNet.CodeGen/Spec.cs                        + load from string; + structural signature (D-N4)
tools/MassiveDotNet.CodeGen/TypeBinding.cs                 objects unbound, arrays by element, date-time → Instant; parameter refusals
tools/MassiveDotNet.CodeGen/Emitter.cs                     + model composition (D-N2), diagnostics (D-N3), drift check (D-N4),
                                                           value-type set (D-N6), converter registration (D-N7)
specs/endpoints.map.json                                   + Reference group, + ListNews, + NewsArticle, NewsPublisher, NewsInsight
src/MassiveDotNet.Rest/Generated/                          regenerated
samples/MassiveDotNet.AotSmokeTest/Program.cs              + a news call
tests/MassiveDotNet.CodeGen.Tests/                         new project (D-N8)
tests/MassiveDotNet.Rest.Tests/                            news and converter tests; CoverageBaseline 3
tests/MassiveDotNet.IntegrationTests/                      + one live news test
MassiveDotNet.slnx                                         + the test project
CLAUDE.md                                                  + D16, + Models convention, + Instant vocabulary row
```

**Proof endpoint.** `/v2/reference/news` (`ListNews`) maps into a new `Reference` group as
`ListNewsAsync`, with `EnumerateNewsAsync` following from its `next_url`. It carries one required
nested object, one optional array of objects, arrays of strings, a `date-time` field, and its
published sample:

```csharp
public Task<MassivePage<NewsArticle>> ListNewsAsync(
    RangeFilter<string>? ticker = null,
    RangeFilter<LocalDate>? publishedUtc = null,
    SortOrder? order = null,
    int? limit = null,
    string? sort = null,
    CancellationToken cancellationToken = default)
```

All three models are `record` classes per D4.

| Model | Required members | Optional members |
|---|---|---|
| `NewsArticle` | `Id`, `Title`, `Author`, `ArticleUrl`, `Tickers` (`string[]`), `Publisher` (`NewsPublisher`); `PublishedUtc` is an `Instant` and needs no modifier | `AmpUrl`, `Description`, `ImageUrl`, `Keywords` (`string[]?`), `Insights` (`NewsInsight[]?`) |
| `NewsPublisher` | `Name`, `HomepageUrl`, `LogoUrl` | `FaviconUrl` |
| `NewsInsight` | `Ticker`, `Sentiment`, `SentimentReasoning` | — |

`ticker` carries `gt gte lt lte` and binds to `RangeFilter<string>` without a map type.
`published_utc` carries the same set and is bound to `LocalDate` by the map (D-N7). `order` is
`SortOrder` by the map; `sort` stays `string` per the comparator spec's non-goals.
`CoverageBaseline` rises to 3.

**CLAUDE.md.** D16 records D-N1, D-N3, and D-N4 in one row. The Conventions section gains a
**Models** bullet beside Filters: a nested object is a `models` row with a pointer through its
parent, named from the parent's property row by `model`, with nullability and `[]` composed from
the spec; free-form objects take an explicit `Dictionary<string, T>`; `format: date-time`
properties are `Instant`, read by `InstantJsonConverter`. The temporal vocabulary table's `Instant`
row gains `NewsArticle.PublishedUtc` as an example, and the Filters bullet's converter sentence
names both converters.

## Testing

All offline tests drive public API except the generator tests, whose subject is the generator.

1. **Generator diagnostics** (`MassiveDotNet.CodeGen.Tests`). Inline spec and map fragments, one
   per case, asserting the message names what the decision says it names: an unbound object on a
   model, on an envelope, and on a parameter (D-N3); `model` with `type`, `model` naming nothing,
   `model` on a scalar (D-N2); a `date-time` parameter with no map type (D-N7).
2. **Generator composition.** The four emitted forms in D-N2's table; `double[]` and `LocalDate[]`
   from arrays (D-N5); `required` on a `Dictionary<string, double>` and not on a `LocalDate`
   (D-N6).
3. **Drift check.** A reuse site identical to the model passes; an extra property at the site, a
   missing one, a model-required property optional at the site, and a mismatch two levels down
   each fail naming the property (D-N4). A site requiring more than the model passes.
4. **Deserialization.** The published sample deserializes into `NewsArticle` with its publisher,
   its one insight, its three keywords, and `PublishedUtc` equal to the instant for
   `2024-06-24T18:33:53Z`. A body whose article lacks `publisher` surfaces as
   `MassiveApiException`, the way the dividends test pins D-F10. A body without `insights` yields
   null. Two pages traverse through the paging stub and `EnumerateNewsAsync` yields both articles.
5. **Converter.** `Z`, a `+00:00` offset, a non-zero offset, one and nine fraction digits, and
   rejection of a date-only string, a number token, and a malformed offset.
6. **Endpoint through the stub.** A ticker range and a date range render the documented query
   string; with nothing supplied the request carries no `?`.
7. **Generator contract.** `EndpointCoverageTests` at baseline 3; CI's idempotency check unchanged.
8. **Public surface.** `TemporalTypeTests` reflects over the new models and converter on its own.

The AOT smoke sample must call `ListNewsAsync` against its stub. Nested types and the converter are
reachable only through the news envelope, and a clean publish proves nothing about types the linker
removed. This is what shows the nested graph and the byte-level parser are AOT clean.

The live tier gains one test: a news call with `limit: 5` and a fixed `publishedUtc` window,
asserting every article has a non-empty publisher name and a `PublishedUtc` inside the window. It
confirms the date form of the filter is accepted and that the service's timestamps parse. Local
only, excluded from CI by category (rule 13).

## Non-goals

- **Singular results.** The 12 envelope-level objects fail loudly here and get their binding in
  #31, which also owns `MapResult.Kind`.
- **Array-typed query parameters.** #33.
- **An RFC 3339 filter element type** for `published_utc` and its peers. Deferred, as the
  comparator spec already recorded, to the Reference group.
- **`oneOf` handling in `FromSchema`.** A typeless schema still defaults to `string` on
  parameters; the map row chooses.
- **Struct-kind nested models.** Nothing here prevents `"kind": "struct"` on a nested row; the
  snapshot group decides when it has a tick-level nested shape to bind.
- **A live proof of dictionaries.** Financials (#15) is the first endpoint that needs one; the
  generator test in item 2 covers the modifier until then.
- **The older refusal paths.** #34, against the project this design creates.
