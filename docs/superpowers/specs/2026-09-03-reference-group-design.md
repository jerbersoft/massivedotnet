# Reference group: the remaining thirty-three operations

Issue: [#9](https://github.com/jerbersoft/massivedotnet/issues/9) · Milestone: v0.1 REST · Date: 2026-09-03

## Why

The Reference group holds two operations, news and market holidays. Issue #9 owns thirty-five,
and the other thirty-three are the largest block of coverage left in the milestone: tickers and
everything about them, market status, conditions, exchanges, the v3 corporate actions, options
contracts, IPOs, short data, float, and the whole SEC filings surface, financials included.

Every generator path these need exists, with two small exceptions the description hides: one
response schema wraps its items in a single-branch `oneOf`, which the generator reads today as an
array of strings, and the path parameters of three routes are not marked required, which the generator
reads today as optional. What is not mechanical is the description being wrong about the wire in four
places the generator would faithfully reproduce, two routes existing twice under different
version segments, one route serving a document where the description declares JSON, and a date
filter the server silently misreads in the form the SDK's date type would render.

Thirty-three is twice the Stocks group. This spec covers all of them and is implemented as two
plans on two branches, so each lands as a reviewable unit.

## What the spec actually declares

Measured on `specs/openapi.json` on 2026-09-03, with `allOf` merged and `$ref` resolved. Every
operation below is tagged `reference` or carries the `reference` entitlement, and none is mapped.

| Operation | Route | Payload | Pages |
|---|---|---|---|
| `ListTickers` | `/v3/reference/tickers` | array under `results` | yes |
| `GetTicker` | `/v3/reference/tickers/{ticker}` | one object under `results`; nested `address`, `branding` | no |
| `ListTickerTypes` | `/v3/reference/tickers/types` | array under `results`; no JSON example | no |
| `GetEvents` | `/vX/reference/tickers/{id}/events` | one object under `results`; `events` items behind a one-branch `oneOf` | no |
| `GetRelatedCompanies` | `/v1/related-companies/{ticker}` | array under `results` | no |
| `GetMarketStatus` | `/v1/marketstatus/now` | the body itself; nested `currencies`, `exchanges`, `indicesGroups` | no |
| `ListConditions` | `/v3/reference/conditions` | array under `results`; nested `sip_mapping`, `update_rules` with two identical children | yes |
| `ListExchanges` | `/v3/reference/exchanges` | array under `results`; no JSON example | no |
| `ListDividends` | `/v3/reference/dividends` | array under `results` | yes |
| `ListStockSplits` | `/v3/reference/splits` | array under `results` | yes |
| `ListOptionsContracts` | `/v3/reference/options/contracts` | array under `results`; nested `additional_underlyings` | yes |
| `GetOptionsContract` | `/v3/reference/options/contracts/{options_ticker}` | one object under `results`, the list item's shape | no |
| `ListIPOs` | `/vX/reference/ipos` | array under `results` | yes |
| `get_v1_reference_ipos` | `/v1/reference/ipos` | array under `results`; three dates typed `int64` | yes |
| `get_stocks_v1_short-interest` | `/stocks/v1/short-interest` | array under `results` | yes |
| `get_stocks_v1_short-volume` | `/stocks/v1/short-volume` | array under `results` | yes |
| `get_stocks_vX_float` | `/stocks/vX/float` | array under `results` | yes |
| `ListFilings` | `/v1/reference/sec/filings` | array under `results`; nested `entities` items with `company_data`; no example | yes |
| `GetFiling` | `/v1/reference/sec/filings/{filing_id}` | one object under `results`, the list item's shape; no example | no |
| `ListFilingFiles` | `/v1/reference/sec/filings/{filing_id}/files` | array under `results`; no example | yes |
| `GetFilingFile` | `/v1/reference/sec/filings/{filing_id}/files/{file_id}` | the body itself, the files item's shape; no example | no |
| `get_stocks_filings_10-K_vX_sections` | `/stocks/filings/10-K/vX/sections` | array under `results` | yes |
| `get_stocks_filings_10-K_vX_0_sections` | `/stocks/filings/10-K/vX_0/sections` | identical to the `vX` revision | yes |
| `get_stocks_filings_8-K_vX_disclosures` | `/stocks/filings/8-K/vX/disclosures` | array under `results` | yes |
| `get_stocks_filings_8-K_vX_text` | `/stocks/filings/8-K/vX/text` | array under `results` | yes |
| `get_stocks_filings_vX_13-F` | `/stocks/filings/vX/13-F` | array under `results` | yes |
| `get_stocks_filings_vX_form-3` | `/stocks/filings/vX/form-3` | array under `results`; nested `footnotes` | yes |
| `get_stocks_filings_vX_form-4` | `/stocks/filings/vX/form-4` | array under `results`; nested `footnotes`, the form 3 shape | yes |
| `get_stocks_filings_vX_index` | `/stocks/filings/vX/index` | array under `results` | yes |
| `get_stocks_filings_vX_risk-factors` | `/stocks/filings/vX/risk-factors` | array under `results` | yes |
| `get_stocks_taxonomies_vX_disclosures` | `/stocks/taxonomies/vX/disclosures` | array under `results` | yes |
| `get_stocks_taxonomies_vX_risk-factors` | `/stocks/taxonomies/vX/risk-factors` | array under `results` | yes |
| `ListFinancials` | `/vX/reference/financials` | array under `results`; nested `financials` holding four statements of data points | yes |

Fifteen routes carry a `vX` or `vX_0` segment and ship `[Experimental]` with no map work (D18,
D23). No operation here is deprecated.

Where the description disagrees with the wire, and how it is known:

| Site | Defect | Evidence |
|---|---|---|
| Ticker events items | wrapped in a `oneOf` with one branch | the only response-side `oneOf` in the description; the generator sees no type and binds `string[]` |
| Ticker events `event_type` | required, but the wire spells it `type` | the published example and a live call on 2026-09-03 both carry `type`, never `event_type` |
| SEC v1 path parameters | `filing_id`, `file_id` not marked required | OpenAPI requires path parameters to be required; the description omits the flag on these three |
| SEC filing file | JSON metadata object declared | the route serves `text/html` with `Accept: application/json` and with `Accept: text/html`, 2026-09-03 |
| SEC v1 dates | `filing_date`, `period_of_report_date`, `acceptance_datetime` are bare strings | the wire carries `20260902` and `20260902103943`; a filter of `2026-09-01` returned January filings, `20260901` returned September ones |
| v1 IPO dates | `announced_date`, `last_updated`, `listing_date` typed `int64` | the published example shows `2024-06-01`; the route answers a plain-text 404, so the wire cannot settle it |
| Short interest, short volume, float examples | `"request_id": 1` | the envelope schema declares a string; the dividends fixture already corrects the same defect |
| `/v1/reference/ipos`, `/stocks/filings/10-K/vX_0/sections` | declared, unserved | plain-text `404 page not found` on 2026-09-03 |

## Decisions

### D-R1 · One spec, two plans

Plan A, on `feat/reference-core`, ships seventeen operations: tickers, ticker details, ticker
types, ticker events, related companies, market status, conditions, exchanges, v3 dividends, v3
splits, both options contract operations, both IPO operations, short interest, short volume, and
float. `CoverageBaseline` goes from 23 to 40. Plan B, on `feat/reference-sec`, ships the other
sixteen: the four SEC v1 operations, both 10-K sections revisions, both 8-K operations, 13-F, form
3, form 4, the filing index, risk factors, both taxonomies, and financials. `CoverageBaseline`
goes from 40 to 56. Issue #9 gets a progress comment when A lands and closes when B does.

The split follows the data, not the count: A is the reference data every consumer touches, B is
the SEC surface with its own transport addition, and neither depends on the other's models.

### D-R2 · A single-branch `oneOf` reads as its branch

A private `Unwrap` helper, called from `Spec.Shape` and `Spec.Collect`, treats a schema whose
`oneOf` has exactly one branch as that branch, so `Shape`, `Properties`, and `Navigate` all see
through it. A `oneOf` with more than one branch of which any is an object is refused with a
diagnostic naming the branch count and the rule; none exists today, and a union has no honest model
binding. A union of scalars, which the news parameters declare, stays a scalar as it always has.
Parameter-side `oneOf`, which the news operation uses, is untouched: those parameters
already bind through the map.

Without this the ticker events model would carry `string[]? Events` and fail on every real
response, which is the silent wrong binding D16 exists to prevent.

### D-R3 · A path parameter is required whether or not the description says so

`Spec` reads `Required` as `in == "path" || required`. OpenAPI mandates the flag on path
parameters; the SEC v1 description omits it on `filing_id` and `file_id`, and without this the
generated signatures would default them to `null` and append an empty segment. No other operation
in the description is affected, so no generated file changes.

### D-R4 · The filing file route ships as declared, and a download sits beside it

`GetFilingFileAsync` is generated from the description: a body payload of the `FilingFile` model.
The live tier pins that it throws `MassiveApiException` on the HTML body the service actually
sends, dated 2026-09-03, which is D21 applied to a route that drifted rather than retired.

Beside it, the hand-written `ReferenceGroup` partial adds the method a consumer can use:

```csharp
public Task DownloadFilingFileAsync(
    string filingId,
    string fileId,
    Stream destination,
    CancellationToken cancellationToken = default)
```

It calls the generated `BuildGetFilingFileUri`, so the route cannot drift from the description,
and a new transport method copies the body:

```csharp
public Task DownloadAsync(string requestUri, Stream destination, CancellationToken cancellationToken = default)
```

`DownloadAsync` validates its arguments and the disposed state as `GetAsync` does, sends with
`ResponseHeadersRead`, raises the same exceptions on a non-success status, and copies the content
to the destination. It does not inspect the content type: the caller asked for the bytes, and the
files list already names each file's type and name.

Three alternatives were rejected. A map-level `kind: document` would have the map overriding the
description on a live observation, which is the second source D18 and D21 forbid. Returning a
`Stream` ties the response's lifetime to a value the caller may forget to dispose. Returning a
`string` is wrong for the graphics and PDFs a filing carries.

### D-R5 · Two revisions of one route: the served one keeps the plain name

`/vX/reference/ipos` and `/v1/reference/ipos` are one function; so are the `vX` and `vX_0` 10-K
sections routes. In each pair one revision is served and documented and the other answers 404.
The served revision takes the plain name, `ListIposAsync` and `List10KSectionsAsync`, and the
other carries its version segment, `ListIposV1Async` and `List10KSectionsVx0Async`. Both ship,
both are named after a route, and both are marked from the path (D18).

The alternative, giving the plain name to the versioned `v1` route because the description
promotes it, was rejected: `ListIposAsync` would 404 today, and the cost at the transition is the
same either way. When Massive retires one revision the nightly sync drops it, and the rename lands
in that D21 removal commit, noted as breaking.

### D-R6 · Enums: reuse `MarketType`, add `ContractType`, keep the rest strings

`MarketType` already holds every member that `asset_class` declares on conditions, exchanges, and
ticker types, and that `market` declares on tickers, so those four rows name it and the #37 check
verifies each one way. `order` is `SortOrder` everywhere it appears. `contract_type` gets a new
core enum, `ContractType { Call, Put }`, rendered `call` and `put`, because the options group will
bind the same field and moving a string parameter to an enum later would break callers.

Every other enum parameter stays `string`: the ticker `type` (twenty-three values the Ticker Types
API owns), `sip`, `data_type`, `ipo_status`, `timeframe`, `section`, `dividend_type`, `locale`,
the filing `type`, and every per-endpoint `sort`. The rule: an enum lands in core when its member
set crosses groups or sits in a path. A string passes through, and the server rejects a bad value
with a 400, the same posture as an entitlement.

### D-R7 · Models parallel to `stocks/v1` ones get their own names

`/v3/reference/dividends` carries ten properties where `/stocks/v1/dividends` carries twelve;
`/v3/reference/splits` five where `/stocks/v1/splits` has seven; `/v3/reference/exchanges` adds
`asset_class` to what `/stocks/v1/exchanges` has. D16's exact-name check refuses sharing at every
one of these sites, so they are `ReferenceDividend`, `ReferenceSplit`, and `Exchange`. `Exchange`
goes unprefixed because it spans every asset class, and `StockExchange` already holds the narrower
one. The two IPO models differ in wire types, not just names, so they are `Ipo` and `IpoV1`.

Tickers follow the constitution's own example in D4: `TickerSummary` is the list item (not
`Ticker`: C# forbids a member named after its enclosing type, CS0542, and `Ticker` is the
SDK-wide property name for the wire field), `TickerDetails` the singular, with `CompanyAddress`
and `Branding` beneath it.

### D-R8 · SEC form names: a leading form number is spelled, a trailing one is kept

C# forbids a leading digit, so `TenKSection`, `EightKDisclosure`, `EightKText`, and
`ThirteenFHolding`; a number after a word keeps its digits, so `Form3Filing`, `Form4Filing`, and
every method, where the digits follow the verb: `List10KSectionsAsync`, `List8KDisclosuresAsync`,
`List8KTextAsync`, `List13FHoldingsAsync`, `ListForm3FilingsAsync`, `ListForm4FilingsAsync`.
Spelling the trailing numbers too, `FormThreeFiling`, was rejected because nobody writes the
form's name that way.

### D-R9 · The SEC v1 dates bind `string`, on filters and models alike

The wire form is `yyyyMMdd`, and `yyyyMMddHHmmss` for `acceptance_datetime`. `LocalDate` renders
`yyyy-MM-dd`, which the server accepted and silently misread, returning the wrong year's filings.
That is D20's problem arriving in a third form, and the answer is the same: a type that renders
the wrong wire form must not be bindable there. `filing_date` and `period_of_report_date` are
`RangeFilter<string>`, and the three model properties are `string`, with the map's summaries
naming the compact form. A `CompactDate` core type was considered and deferred until a second
family needs it; the Financials group (#15) is the likely one.

Everywhere else a bare-string date whose published example shows `yyyy-MM-dd` binds `LocalDate`
from the map: `filing_date` on the 8-K disclosures and risk factors, `list_date` on ticker
details, `expiration_date` and `as_of` on contracts, the four dates on v3 dividends,
`execution_date` on v3 splits, `settlement_date` on short interest, `date` on short volume, and
`start_date`, `end_date`, `filing_date` on financials.

### D-R10 · The map corrects the description where the example and the wire agree against it

- Ticker events `event_type` is typed `string?` in the map, which suppresses the `required`
  modifier as D-G5 did for the historic tick models. The published example spells it `type`, the
  live wire does too, and a required property that never arrives would make every call throw.
  The live test pins that `EventType` is null on the wire, dated, so it flips when either side
  moves. The map cannot rename a wire key, and adding one the schema lacks is a second source.
- The v1 IPO model follows the schema: `announced_date`, `last_updated`, and `listing_date` are
  `long?`, no partial, no computed instant, because the description declares no unit and the
  route is unserved. The fixture converts the example's three date strings to Unix nanoseconds,
  the unit the operation's own `listing_date` filter documents, and says so in a comment. The
  live pin is what corrects this the day the route answers.
- The three `"request_id": 1` examples become strings in their fixtures, commented, per D-G7.
- `serverTime` on market status is a bare string in the description and an RFC 3339 timestamp
  with an offset on the wire; the map types it `Instant?`, which `InstantJsonConverter` reads.

### D-R11 · Financials binds four dictionaries of one data point model

`financials` is an object with four declared properties. `balance_sheet` declares one property
literally named `*`, the description's way of documenting a data point shape under any key; the
other three are free-form. All four rows take the verbatim type
`Dictionary<string, FinancialDataPoint>?`, which `Emitter.PropertyType` honours before it asks
whether the site needs a model, and `FinancialDataPoint` is generated from the pointer
`results/items/financials/balance_sheet/*`, which `Spec.Navigate` reaches as an ordinary
property name. A generator test pins that a `type` row on an object with declared properties is
taken verbatim, since D-N6 only spoke of free-form ones.

### D-R12 · Fixtures: published examples, captured where none exists

Six operations publish no JSON example: filings, filing, filing files, the filing file itself,
exchanges, and ticker types. The filing file is a document, not JSON, and gets no fixture: its
offline test stubs an HTML body. The other five are captured live with the local key during the
plan, reviewed for account identifiers and embedded keys, and committed with a doc comment naming
the capture date. Every other fixture is the published example, departing only as D-R10 says.

### D-R13 · The live tier pins what fixtures cannot, then touches everything once

Plan A: tickers at `limit: 2` cross a page boundary through `EnumerateTickersAsync`;
`ListIposV1Async` answers 404, pinned dated; ticker events show `EventType` null on the wire;
market status deserializes its body; the contract get takes its ticker from the list, since a
hard-coded contract expires. Plan B: `GetFilingFileAsync` throws on the HTML body and
`DownloadFilingFileAsync` copies a body starting with `<`; `List10KSectionsVx0Async` answers 404;
the compact-date filter returns only filings on or after the date given; filings at `limit: 2`
cross a page boundary. Every other operation gets one call asserting shape, not values.

## Scope

### Surface, Plan A

Every method takes `CancellationToken cancellationToken = default` last. Filter types are what
the generator derives from each field's suffix set; the map names element types only. Methods
on `vX` routes carry `[Experimental("MASSIVE0001")]`.

| Operation | Method | Returns |
|---|---|---|
| `ListTickers` | `ListTickersAsync(RangeFilter<string>? ticker, string? type, MarketType? market, string? exchange, string? cusip, string? cik, LocalDate? date, string? search, bool? active, SortOrder? order, int? limit, string? sort)`, `EnumerateTickersAsync` | `MassivePage<TickerSummary>`, `IAsyncEnumerable<TickerSummary>` |
| `GetTicker` | `GetTickerAsync(string ticker, LocalDate? date)` | `TickerDetails` |
| `ListTickerTypes` | `ListTickerTypesAsync(MarketType? assetClass, string? locale)` | `TickerType[]` |
| `GetEvents` | `GetTickerEventsAsync(string id, string? types)` | `TickerEvents` |
| `GetRelatedCompanies` | `ListRelatedCompaniesAsync(string ticker)` | `RelatedCompany[]` |
| `GetMarketStatus` | `GetMarketStatusAsync()` | `MarketStatus` |
| `ListConditions` | `ListConditionsAsync(MarketType? assetClass, string? dataType, int? id, string? sip, SortOrder? order, int? limit, string? sort)`, `EnumerateConditionsAsync` | `MassivePage<Condition>`, `IAsyncEnumerable<Condition>` |
| `ListExchanges` | `ListExchangesAsync(MarketType? assetClass, string? locale)` | `Exchange[]` |
| `ListDividends` | `ListDividendsAsync(RangeFilter<string>? ticker, RangeFilter<LocalDate>? exDividendDate, RangeFilter<LocalDate>? recordDate, RangeFilter<LocalDate>? declarationDate, RangeFilter<LocalDate>? payDate, int? frequency, RangeFilter<double>? cashAmount, string? dividendType, SortOrder? order, int? limit, string? sort)`, `EnumerateDividendsAsync` | `MassivePage<ReferenceDividend>`, `IAsyncEnumerable<ReferenceDividend>` |
| `ListStockSplits` | `ListSplitsAsync(RangeFilter<string>? ticker, RangeFilter<LocalDate>? executionDate, bool? reverseSplit, SortOrder? order, int? limit, string? sort)`, `EnumerateSplitsAsync` | `MassivePage<ReferenceSplit>`, `IAsyncEnumerable<ReferenceSplit>` |
| `ListOptionsContracts` | `ListOptionsContractsAsync(RangeFilter<string>? underlyingTicker, string? ticker, ContractType? contractType, RangeFilter<LocalDate>? expirationDate, LocalDate? asOf, RangeFilter<double>? strikePrice, bool? expired, SortOrder? order, int? limit, string? sort)`, `EnumerateOptionsContractsAsync` | `MassivePage<OptionsContract>`, `IAsyncEnumerable<OptionsContract>` |
| `GetOptionsContract` | `GetOptionsContractAsync(string optionsTicker, LocalDate? asOf)` | `OptionsContract` |
| `ListIPOs` | `ListIposAsync(string? ticker, string? usCode, string? isin, RangeFilter<LocalDate>? listingDate, string? ipoStatus, SortOrder? order, int? limit, string? sort)`, `EnumerateIposAsync` | `MassivePage<Ipo>`, `IAsyncEnumerable<Ipo>` |
| `get_v1_reference_ipos` | `ListIposV1Async(Filter<string>? ticker, Filter<string>? usCode, Filter<string>? isin, RangeFilter<DateOrNanoseconds>? listingDate, SetFilter<string>? ipoStatus, int? limit, string? sort)`, `EnumerateIposV1Async` | `MassivePage<IpoV1>`, `IAsyncEnumerable<IpoV1>` |
| `get_stocks_v1_short-interest` | `ListShortInterestAsync(Filter<string>? ticker, Filter<double>? daysToCover, Filter<LocalDate>? settlementDate, Filter<long>? avgDailyVolume, int? limit, string? sort)`, `EnumerateShortInterestAsync` | `MassivePage<ShortInterest>`, `IAsyncEnumerable<ShortInterest>` |
| `get_stocks_v1_short-volume` | `ListShortVolumeAsync(Filter<string>? ticker, Filter<LocalDate>? date, Filter<double>? shortVolumeRatio, int? limit, string? sort)`, `EnumerateShortVolumeAsync` | `MassivePage<ShortVolume>`, `IAsyncEnumerable<ShortVolume>` |
| `get_stocks_vX_float` | `ListFloatAsync(Filter<string>? ticker, RangeFilter<double>? freeFloatPercent, int? limit, string? sort)`, `EnumerateFloatAsync` | `MassivePage<ShareFloat>`, `IAsyncEnumerable<ShareFloat>` |

The `ticker` parameter on `ListOptionsContracts` is one the description itself calls deprecated;
it ships as the plain string it is, and its prose says so. `listing_date` on the v1 IPO route is
documented as a date or a nanosecond timestamp, so it binds `DateOrNanoseconds` (D20).

### Surface, Plan B

| Operation | Method | Returns |
|---|---|---|
| `ListFilings` | `ListFilingsAsync(string? type, RangeFilter<string>? filingDate, RangeFilter<string>? periodOfReportDate, bool? hasXbrl, string? companyName, string? companyCik, string? companyTicker, string? companySic, string? companyNameSearch, SortOrder? order, int? limit, string? sort)`, `EnumerateFilingsAsync` | `MassivePage<Filing>`, `IAsyncEnumerable<Filing>` |
| `GetFiling` | `GetFilingAsync(string filingId)` | `Filing` |
| `ListFilingFiles` | `ListFilingFilesAsync(string filingId, RangeFilter<long>? sequence, RangeFilter<string>? filename, SortOrder? order, int? limit, string? sort)`, `EnumerateFilingFilesAsync` | `MassivePage<FilingFile>`, `IAsyncEnumerable<FilingFile>` |
| `GetFilingFile` | `GetFilingFileAsync(string filingId, string fileId)` | `FilingFile` (D-R4) |
| hand-written | `DownloadFilingFileAsync(string filingId, string fileId, Stream destination)` | `Task` (D-R4) |
| `get_stocks_filings_10-K_vX_sections` | `List10KSectionsAsync(Filter<string>? cik, Filter<string>? ticker, SetFilter<string>? section, RangeFilter<LocalDate>? filingDate, RangeFilter<LocalDate>? periodEnd, int? limit, string? sort)`, `Enumerate10KSectionsAsync` | `MassivePage<TenKSection>`, `IAsyncEnumerable<TenKSection>` |
| `get_stocks_filings_10-K_vX_0_sections` | `List10KSectionsVx0Async(...)` as above, `Enumerate10KSectionsVx0Async` | as above |
| `get_stocks_filings_8-K_vX_disclosures` | `List8KDisclosuresAsync(SetFilter<string>? cik, ArrayFilter<string>? tickers, Filter<LocalDate>? filingDate, string? tertiaryCategory, int? limit, string? sort)`, `Enumerate8KDisclosuresAsync` | `MassivePage<EightKDisclosure>`, `IAsyncEnumerable<EightKDisclosure>` |
| `get_stocks_filings_8-K_vX_text` | `List8KTextAsync(Filter<string>? cik, Filter<string>? ticker, Filter<string>? formType, RangeFilter<LocalDate>? filingDate, int? limit, string? sort)`, `Enumerate8KTextAsync` | `MassivePage<EightKText>`, `IAsyncEnumerable<EightKText>` |
| `get_stocks_filings_vX_13-F` | `List13FHoldingsAsync(SetFilter<string>? filerCik, RangeFilter<LocalDate>? filingDate, int? limit, string? sort)`, `Enumerate13FHoldingsAsync` | `MassivePage<ThirteenFHolding>`, `IAsyncEnumerable<ThirteenFHolding>` |
| `get_stocks_filings_vX_form-3` | `ListForm3FilingsAsync(SetFilter<string>? issuerCik, SetFilter<string>? ownerCik, ArrayFilter<string>? tickers, string? formType, RangeFilter<LocalDate>? filingDate, int? limit, string? sort)`, `EnumerateForm3FilingsAsync` | `MassivePage<Form3Filing>`, `IAsyncEnumerable<Form3Filing>` |
| `get_stocks_filings_vX_form-4` | `ListForm4FilingsAsync(SetFilter<string>? issuerCik, SetFilter<string>? ownerCik, ArrayFilter<string>? tickers, string? formType, RangeFilter<LocalDate>? filingDate, string? transactionCode, int? limit, string? sort)`, `EnumerateForm4FilingsAsync` | `MassivePage<Form4Filing>`, `IAsyncEnumerable<Form4Filing>` |
| `get_stocks_filings_vX_index` | `ListFilingIndexAsync(Filter<string>? cik, Filter<string>? ticker, Filter<string>? formType, RangeFilter<LocalDate>? filingDate, int? limit, string? sort)`, `EnumerateFilingIndexAsync` | `MassivePage<FilingIndexEntry>`, `IAsyncEnumerable<FilingIndexEntry>` |
| `get_stocks_filings_vX_risk-factors` | `ListRiskFactorsAsync(Filter<LocalDate>? filingDate, Filter<string>? ticker, Filter<string>? cik, int? limit, string? sort)`, `EnumerateRiskFactorsAsync` | `MassivePage<RiskFactor>`, `IAsyncEnumerable<RiskFactor>` |
| `get_stocks_taxonomies_vX_disclosures` | `ListDisclosureTaxonomyAsync(Filter<string>? taxonomy, Filter<string>? primaryCategory, Filter<string>? secondaryCategory, Filter<string>? tertiaryCategory, int? limit, string? sort)`, `EnumerateDisclosureTaxonomyAsync` | `MassivePage<DisclosureTaxonomyEntry>`, `IAsyncEnumerable<DisclosureTaxonomyEntry>` |
| `get_stocks_taxonomies_vX_risk-factors` | `ListRiskFactorTaxonomyAsync(RangeFilter<double>? taxonomy, Filter<string>? primaryCategory, Filter<string>? secondaryCategory, Filter<string>? tertiaryCategory, int? limit, string? sort)`, `EnumerateRiskFactorTaxonomyAsync` | `MassivePage<RiskFactorTaxonomyEntry>`, `IAsyncEnumerable<RiskFactorTaxonomyEntry>` |
| `ListFinancials` | `ListFinancialsAsync(string? ticker, string? cik, string? companyName, string? sic, RangeFilter<LocalDate>? filingDate, RangeFilter<LocalDate>? periodOfReportDate, string? timeframe, bool? includeSources, string? companyNameSearch, SortOrder? order, int? limit, string? sort)`, `EnumerateFinancialsAsync` | `MassivePage<FinancialReport>`, `IAsyncEnumerable<FinancialReport>` |

The dotted `entities.company_data.*` and `.search` parameters are plain strings the generator
does not group, because `search` and `name` are not comparator suffixes; the map names them
`companyName`, `companyCik`, `companyTicker`, `companySic`, and `companyNameSearch`.

### Models

All `partial record` classes, per D4: nothing here is tick-level. Property names are PascalCase
from the wire name, booleans take an `Is` prefix where the wire name is an adjective
(`IsAfterHours`, `IsLegacy`, `IsDirector`), and the `sip_mapping` keys become `Cta`, `Opra`,
`Utp`.

Plan A:

| Model | Pointer, from | Notes |
|---|---|---|
| `TickerSummary` | `results/items` of `ListTickers` | `delisted_utc`, `last_updated_utc` are `Instant?` from their format |
| `TickerDetails` | `results` of `GetTicker` | `list_date` as `LocalDate?`; `delisted_utc` as `Instant?`; numbers as the schema declares |
| `CompanyAddress` | `results/address` of `GetTicker` | |
| `Branding` | `results/branding` of `GetTicker` | |
| `TickerType` | `results/items` of `ListTickerTypes` | captured fixture |
| `TickerEvents` | `results` of `GetEvents` | `events` as `TickerEvent[]?` |
| `TickerEvent` | `results/events/items` of `GetEvents` | through the `oneOf` (D-R2); `event_type` as `string?` (D-R10); `date` is `LocalDate` from its format |
| `TickerChange` | `results/events/items/ticker_change` of `GetEvents` | |
| `RelatedCompany` | `results/items` of `GetRelatedCompanies` | one property |
| `MarketStatus` | body of `GetMarketStatus`, no pointer | `serverTime` as `Instant?` (D-R10) |
| `MarketStatusCurrencies` | `currencies` of `GetMarketStatus` | |
| `MarketStatusExchanges` | `exchanges` of `GetMarketStatus` | |
| `MarketStatusIndexGroups` | `indicesGroups` of `GetMarketStatus` | ten string properties |
| `Condition` | `results/items` of `ListConditions` | `data_types` as `string[]` |
| `SipMapping` | `results/items/sip_mapping` of `ListConditions` | |
| `ConditionUpdateRules` | `results/items/update_rules` of `ListConditions` | |
| `UpdateRule` | `results/items/update_rules/consolidated` of `ListConditions` | reused at `market_center`, where D16 verifies it |
| `Exchange` | `results/items` of `ListExchanges` | captured fixture |
| `ReferenceDividend` | `results/items` of `ListDividends` | four dates as `LocalDate` (D-R9) |
| `ReferenceSplit` | `results/items` of `ListStockSplits` | `execution_date` as `LocalDate` |
| `OptionsContract` | `results/items` of `ListOptionsContracts` | `expiration_date` as `LocalDate?`; reused at `results` of `GetOptionsContract` |
| `AdditionalUnderlying` | `results/items/additional_underlyings/items` of `ListOptionsContracts` | |
| `Ipo` | `results/items` of `ListIPOs` | dates are `LocalDate` from their format |
| `IpoV1` | `results/items` of `get_v1_reference_ipos` | three dates as `long?` (D-R10); no partial |
| `ShortInterest` | `results/items` of `get_stocks_v1_short-interest` | `settlement_date` as `LocalDate` |
| `ShortVolume` | `results/items` of `get_stocks_v1_short-volume` | `date` as `LocalDate` |
| `ShareFloat` | `results/items` of `get_stocks_vX_float` | `effective_date` is `LocalDate` from its format |

Plan B:

| Model | Pointer, from | Notes |
|---|---|---|
| `Filing` | `results/items` of `ListFilings` | dates as `string` (D-R9); reused at `results` of `GetFiling`; captured fixture |
| `FilingEntity` | `results/items/entities/items` of `ListFilings` | |
| `FilingCompany` | `results/items/entities/items/company_data` of `ListFilings` | |
| `FilingFile` | `results/items` of `ListFilingFiles` | reused as the body of `GetFilingFile`; captured fixture |
| `TenKSection` | `results/items` of `get_stocks_filings_10-K_vX_sections` | reused by the `vX_0` revision |
| `EightKDisclosure` | `results/items` of `get_stocks_filings_8-K_vX_disclosures` | `filing_date` as `LocalDate?`; `tickers` as `string[]?` |
| `EightKText` | `results/items` of `get_stocks_filings_8-K_vX_text` | |
| `ThirteenFHolding` | `results/items` of `get_stocks_filings_vX_13-F` | `other_managers` as `string[]?` |
| `Form3Filing` | `results/items` of `get_stocks_filings_vX_form-3` | |
| `Form4Filing` | `results/items` of `get_stocks_filings_vX_form-4` | |
| `FilingFootnote` | `results/items/footnotes/items` of `get_stocks_filings_vX_form-3` | reused at the same site of form 4 |
| `FilingIndexEntry` | `results/items` of `get_stocks_filings_vX_index` | |
| `RiskFactor` | `results/items` of `get_stocks_filings_vX_risk-factors` | `filing_date` as `LocalDate?` |
| `DisclosureTaxonomyEntry` | `results/items` of `get_stocks_taxonomies_vX_disclosures` | `taxonomy` is a required `string` |
| `RiskFactorTaxonomyEntry` | `results/items` of `get_stocks_taxonomies_vX_risk-factors` | `taxonomy` is a required `double` |
| `FinancialReport` | `results/items` of `ListFinancials` | `start_date`, `end_date`, `filing_date` as `LocalDate?`; `tickers` as `string[]?` |
| `FinancialStatements` | `results/items/financials` of `ListFinancials` | four rows typed `Dictionary<string, FinancialDataPoint>?` (D-R11) |
| `FinancialDataPoint` | `results/items/financials/balance_sheet/*` of `ListFinancials` | `derived_from` as `string[]?` |

No model in this group gets a hand-written partial: every timestamp arrives as a calendar date or
an RFC 3339 string the converters read, so there is no epoch to compute from.

### Files

Plan A:

```
src/MassiveDotNet/ContractType.cs                      new (D-R6)
src/MassiveDotNet/MassiveEnumValues.cs                 + ContractType.ToWireValue
tools/MassiveDotNet.CodeGen/Spec.cs                    IsObject and Collect see through a one-branch oneOf (D-R2)
tools/MassiveDotNet.CodeGen/TypeBinding.cs             CoreEnums + ContractType
specs/endpoints.map.json                               27 models, 17 endpoints
src/MassiveDotNet.Rest/Generated/                      regenerated
tests/MassiveDotNet.CodeGen.Tests/                     oneOf unwrap and refusal; ContractType arm
tests/MassiveDotNet.Rest.Tests/                        fixtures, rendering, deserialization, traversal; ContractTypeTests; CoverageBaseline 40
tests/MassiveDotNet.IntegrationTests/                  one class per family; the D-R13 pins
samples/MassiveDotNet.AotSmokeTest/Program.cs          market status body; tickers with a MarketType; a contract with a ContractType
CLAUDE.md                                              D24, D25 (see Bookkeeping); Conventions
```

Plan B:

```
src/MassiveDotNet/Http/MassiveHttpTransport.cs         + DownloadAsync (D-R4)
src/MassiveDotNet.Rest/ReferenceGroup.cs               + DownloadFilingFileAsync (D-R4)
tools/MassiveDotNet.CodeGen/Spec.cs                    path parameters always required (D-R3)
specs/endpoints.map.json                               18 models, 16 endpoints
src/MassiveDotNet.Rest/Generated/                      regenerated
tests/MassiveDotNet.CodeGen.Tests/                     path requiredness; verbatim type on a declared object (D-R11)
tests/MassiveDotNet.Rest.Tests/                        fixtures, rendering, deserialization, traversal, download; CoverageBaseline 56
tests/MassiveDotNet.IntegrationTests/                  one class per family; the D-R13 pins
samples/MassiveDotNet.AotSmokeTest/Program.cs          financials with a data point dictionary; a download into a MemoryStream
CLAUDE.md                                              D26 (see Bookkeeping); the generator constraint on path parameters
```

## Testing

**Generator**, in `tests/MassiveDotNet.CodeGen.Tests`, through the harness:

- An array whose items are a one-branch `oneOf` of an object binds through a model row; a
  two-branch `oneOf` on a response schema is refused with the operation and pointer named.
- A `ContractType` parameter renders `contractType?.ToWireValue()`, and a member the enum lacks
  is refused by the #37 check.
- A path parameter with no `required` flag is emitted required, with no default.
- A `type` row on an object property that declares properties is taken verbatim.

**Core**, in `tests/MassiveDotNet.Rest.Tests`: `ContractType.ToWireValue` renders `call` and
`put`; `DownloadAsync` copies a stubbed body to the destination, raises `MassiveApiException`
with the status on a failure, and refuses a null destination and a disposed transport.

**REST**, driven through the public API against the stub handler, one class per family:

- Tickers: `market` renders `stocks`, `date` renders `yyyy-MM-dd`, the list fixture round-trips
  with `LastUpdatedUtc` as an `Instant`, the details fixture deserializes through `CompanyAddress`
  and `Branding`, and tickers traverse two stub pages.
- Ticker types, related companies, exchanges: each fixture round-trips; `assetClass` renders.
- Ticker events: the published example deserializes with `EventType` null and
  `TickerChange.Ticker` set; the path carries the identifier.
- Market status: the documented path; the example deserializes with `ServerTime` at the offset
  the string carries and every nested object populated.
- Conditions: `assetClass` renders; the fixture deserializes both update rules through one model.
- Dividends and splits: the date and amount filters render; each fixture round-trips.
- Options contracts: `contractType` renders `call`, `strikePrice` and `expirationDate` ranges
  render; the list and get fixtures round-trip through `AdditionalUnderlying`.
- IPOs: the `vX` fixture round-trips; the `v1` fixture deserializes its converted dates as
  `long`; `listingDate` renders nineteen digits from an `Instant` on `v1` and `yyyy-MM-dd` on
  `vX`; `ipoStatus.any_of` renders on `v1`.
- Short interest, short volume, float: numeric filters render; corrected fixtures round-trip.
- SEC v1: the dotted parameter names render verbatim, `filingDate` renders the compact string
  unchanged, the captured fixtures round-trip through `FilingEntity` and `FilingCompany`,
  `GetFilingFileAsync` throws `MassiveApiException` on a stubbed HTML body, and
  `DownloadFilingFileAsync` copies that body to a `MemoryStream`; filings traverse two stub pages.
- 10-K, 8-K, 13-F, forms 3 and 4, index, risk factors: `section.any_of`, `tickers.all_of`,
  and `filingDate` ranges render; every fixture round-trips, the form 4 one through
  `FilingFootnote`; the `vX_0` revision builds its own path.
- Taxonomies: `taxonomy.gte` renders a double on risk factors and a string on disclosures.
- Financials: `includeSources` and `companyNameSearch` render; the fixture deserializes four
  dictionaries with `Assets` reachable by key.
- `EndpointCoverageTests` at baseline 40 after A and 56 after B;
  `StabilityAttributesMatchTheSpecification` covers the fifteen experimental routes.

`TemporalTypeTests` covers every new type automatically. The AOT smoke test adds the calls named
in Files and must publish with zero IL warnings. The regenerate-and-diff check runs as before.

**Live**, in `tests/MassiveDotNet.IntegrationTests`, excluded from CI by category (rule 13):
D-R13.

## Non-goals

- **`/stocks/financials/v1/*`.** Issue #15.
- **A `Locale` enum, or any of the per-endpoint enums.** D-R6.
- **A `CompactDate` core type.** D-R9 defers it until a second family needs one.
- **Computed instants on `IpoV1`.** D-R10.
- **Any change to how the `stocks/v1` dividends, splits, and exchanges are reached.** They stay
  under `Stocks`; nothing is re-exported here.
- **Content-type handling on `DownloadAsync`.** D-R4.

## Issue bookkeeping

- #9 gets a comment when Plan A lands naming the seventeen and the baseline, and closes when
  Plan B lands with the sixteen and the transport addition.
- `CLAUDE.md` gains D24 (a one-branch `oneOf` reads as its branch; D-R2), D25 (a route that
  serves a document where the description declares JSON ships as declared, with a download beside
  it; D-R4), and D26 (two revisions of one route: the served one keeps the plain name; D-R5).
  The Conventions section gains the enum rule from D-R6 and the form-name rule from D-R8, and the
  generator constraints gain the path-parameter rule from D-R3.
