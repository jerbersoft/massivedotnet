# Docs site vs. OpenAPI description

Reconciliation of `massive.com/docs/llms.txt` against `specs/openapi.json`, run 2026-09-04.
Closes [#4](https://github.com/jerbersoft/massivedotnet/issues/4).

Reproduce with `python3 docs/reconciliation/reconcile-docs-site.py`. It reads the network,
so it is not part of the suite (rule 13); it needs no key, and exits non-zero if the docs
site ever documents a route the description omits.

## The question

Rule 1 promises every non-deprecated REST operation in the description is reachable from the
public API. The promise is only as wide as the description. The docs site listed 150 REST
pages against 147 declared operations, and if any of those three extra pages named a route
the description omits, the coverage guarantee has a hole in it that no test in this repo
could see.

## The answer

**No page documents a route the description omits.** All 150 join to a declared operation,
so rule 1's guarantee is as wide as Massive's own documentation. Nothing needs to change in
the map, the generator, or the coverage baseline.

The 150-against-147 gap was never one effect. It is two much larger ones that nearly cancel:

```
150 documented pages
 -23 pages that re-document a route already documented elsewhere
────
127 distinct routes documented
 +20 operations the description declares and the docs site does not
────
147 declared operations
```

The issue guessed doc-side duplication, and that half is right — 7 shared routes are
documented once per asset class, which is 23 extra pages. What it did not predict is that
the description declares 20 operations with no page at all. Read only from the totals, the
two look like a 3-page discrepancy.

## How the join works

Every docs page carries the route it documents on one line:

```
**Endpoint:** `GET /v2/aggs/ticker/{stocksTicker}/range/{multiplier}/{timespan}/{from}/{to}`
```

That template matches the description's path key character for character, **including the
parameter name** — which is what makes the join exact rather than approximate. It has to be:
the description declares the same wire route once per asset class, distinguished only by that
name. 41 operations sit on 10 such routes.

```
/v3/quotes/{stockTicker}    /v3/quotes/{optionsTicker}    /v3/quotes/{fxTicker}
```

All three serve `/v3/quotes/X`. OpenAPI says path templates must be unique after templating,
so the description is invalid on this point, but the shape is deliberate: it lets Massive give
each asset class its own prose, parameters, and examples. It suits this SDK for the same
reason — each becomes its own method on its own group (D6), which is the ergonomics we want
anyway. Normalizing the parameter name away, as a first pass here did, silently collapses
those 41 operations to 10 and invents a coverage hole that is not there.

## The 23 duplicate pages

Seven routes, each documented once per asset class the platform sells it under:

| Route | Documented under |
|---|---|
| `/v1/marketstatus/now` | crypto, forex, indices, options, stocks |
| `/v1/marketstatus/upcoming` | crypto, forex, indices, options, stocks |
| `/v3/snapshot` | crypto, forex, indices, options, stocks |
| `/v3/reference/exchanges` | crypto, forex, options, stocks |
| `/v3/reference/tickers` | crypto, forex, indices, stocks |
| `/v3/reference/tickers/{ticker}` | crypto, forex, indices, stocks |
| `/v3/reference/conditions` | crypto, options, stocks |

This is the asset-class ownership D11 credits the MCP catalog with carrying, arriving from a
second source and agreeing with it. A route documented under four asset classes is a
`Reference` operation, not a per-asset one, which is how all seven are already mapped.

## The 20 undocumented operations

Eleven are already mapped and shipping; nine are not yet mapped. Being undocumented changes
nothing about whether they ship — the description is the contract (D21) — but it does change
where their fixtures come from, so each was probed live to see what the service actually does.

| Route | Live, 2026-09-04 | Reading |
|---|---|---|
| `/futures/v1/exchanges` *(control: documented)* | 200 JSON | served |
| `/stocks/v1/exchanges` | 404 `text/plain` | not routed |
| `/crypto/v1/exchanges` | 404 `text/plain` | not routed |
| `/forex/v1/exchanges` | 404 `text/plain` | not routed |
| `/options/v1/exchanges` | 404 `text/plain` | not routed |
| `/options/v3/trades/{ticker}` | 404 `text/plain` | not routed |
| `/options/v3/quotes/{ticker}` | 404 `text/plain` | not routed |
| `/stocks/dev/trades/{ticker}` | 404 `text/plain` | not routed — re-pins D22 |
| `/stocks/filings/10-K/vX_0/sections` | 404 `text/plain` | not routed — re-pins D23 |
| `/v1/reference/ipos` | 404 `text/plain` | not routed — confirms D26 |
| `/v2/snapshot/…/crypto/tickers/{ticker}/book` | 404 `text/plain` | not routed |
| `/v2/ticks/stocks/trades/{ticker}/{date}` | 404 JSON deprecation notice | routed, deliberately retired |
| `/v2/ticks/stocks/nbbo/{ticker}/{date}` | 404 JSON deprecation notice | routed, deliberately retired |
| `/v1/summaries` | 403 `NOT_AUTHORIZED` | routed, entitlement-gated |
| `/v1/historic/crypto/{from}/{to}/{date}` | 403 `NOT_AUTHORIZED` | routed, entitlement-gated |
| `/v1/historic/forex/{from}/{to}/{date}` | 403 `NOT_AUTHORIZED` | routed, entitlement-gated |
| `/v1/reference/sec/filings` | 200 JSON | served |
| `/v1/reference/sec/filings/{filing_id}` | 200 JSON | served |
| `/v1/reference/sec/filings/{filing_id}/files` | 200 JSON | served |
| `/v1/reference/sec/filings/{filing_id}/files/{file_id}` | 200 `text/html` | served, but not as declared — D25 |
| `/vX/reference/financials` | 200 JSON | served |

Controls confirming the probe distinguishes what it claims to: `/v3/reference/exchanges` 200,
`/stocks/filings/10-K/vX/sections` 200, `/v3/trades/{optionsTicker}` and
`/v3/quotes/{optionsTicker}` 403.

### Three kinds of 404

The probe separates them, and the distinction is what D21's posture rests on:

- **`404` with `text/plain` `404 page not found`** — the router has no such route. The
  description declares something the service has not stood up.
- **`404` with a JSON deprecation envelope** naming the successor — the route exists and
  answers deliberately. Massive retired it and says so on the wire.
- **`403` with `NOT_AUTHORIZED`** — the route exists and works; this key is not entitled.
  Entitlement is the server's concern, not the SDK's (D9), so this is a live route.

Reading all three as "gone" would have removed four working routes from the map.

## What this changes

**Nothing in the SDK's contract.** No doc-only operation exists, so rule 1 has no hole, the
coverage baseline is unmoved at 60/147, and no map row changes.

Three things are worth acting on separately, none of them in scope here:

1. **[#40](https://github.com/jerbersoft/massivedotnet/issues/40) is wider than stocks.** The
   `{asset}/v1/exchanges` family is declared for five asset classes. Futures is documented and
   served; stocks, crypto, forex, and options are undocumented and all four answer a plain-text
   404. The remark already shipped for stocks applies verbatim to the other three when they are
   mapped. The futures sibling also reframes the family: this is not a dead legacy route but a
   new-generation one Massive has rolled out for futures first, which is why "prefer
   `/v3/reference/exchanges` until this route is stood up" is the right wording rather than
   "superseded by".

2. **`/options/v3/{trades,quotes}/{ticker}` are second declarations of routes that already
   ship.** `/v3/trades/{optionsTicker}` and `/v3/quotes/{optionsTicker}` are documented and
   routed; the `/options/v3/…` pair is neither. Both must still be mapped (rule 2 forbids
   silent omission), and whoever maps the Options group inherits the naming question — the
   D26 pattern applies, with the served, documented revision taking the plain name.

3. **Fixtures for four operations cannot come from a published sample.** The description
   carries a response example for 16 of the 20 undocumented operations, so the usual
   preference order holds for those. It carries none for
   `/options/v3/quotes/{ticker}`, `/options/v3/trades/{ticker}`, `/stocks/dev/trades/{ticker}`,
   or `/v1/reference/sec/filings/{filing_id}/files/{file_id}` — and the docs site carries no
   page either. Three of those four are not routed, so a capture is impossible too; the fourth
   is D25's, already pinned. Their fixtures have to be hand-written against the declared schema
   (tier 3), and that is the honest floor rather than a gap to close.

For completeness on the other side: `/v3/reference/dividends` is the one documented operation
whose description carries no response example, and its docs page has one.

## Standing exposure

This is a dated observation, not a gate. CI cannot check it — rule 13 keeps the suite offline,
and this reads two live sources. The nightly sync (D10) watches the description and would catch
a new operation; nothing watches the docs site, so a future doc-only route would go unnoticed
until someone re-runs the script. That is the accepted cost today, on the evidence that the gap
in that direction is currently zero. Re-running it after a spec sync that adds operations is
cheap, and the script's non-zero exit is built for exactly that.

---

## Appendix: generated report

- docs-site REST pages: **150**
- description GET operations: **147**
- pages whose route matches no operation: **0**
- pages that re-document a route already documented: **23**
- operations with no page: **20**

### Declared, undocumented

| Route | Operation | Stability | SDK |
|---|---|---|---|
| `/crypto/v1/exchanges` | `get_crypto_v1_exchanges` | stable | — |
| `/forex/v1/exchanges` | `get_forex_v1_exchanges` | stable | — |
| `/options/v1/exchanges` | `get_options_v1_exchanges` | stable | — |
| `/options/v3/quotes/{ticker}` | `get_options_v3_quotes_ticker` | stable | — |
| `/options/v3/trades/{ticker}` | `get_options_v3_trades_ticker` | stable | — |
| `/stocks/dev/trades/{ticker}` | `get_stocks_dev_trades_ticker` | experimental | Stocks.ListDevTrades |
| `/stocks/filings/10-K/vX_0/sections` | `get_stocks_filings_10-K_vX_0_sections` | experimental | Reference.List10KSectionsVx0 |
| `/stocks/v1/exchanges` | `get_stocks_v1_exchanges` | stable | Stocks.ListExchanges |
| `/v1/historic/crypto/{from}/{to}/{date}` | `DeprecatedGetHistoricCryptoTrades` | deprecated | — |
| `/v1/historic/forex/{from}/{to}/{date}` | `DeprecatedGetHistoricForexQuotes` | deprecated | — |
| `/v1/reference/ipos` | `get_v1_reference_ipos` | stable | Reference.ListIposV1 |
| `/v1/reference/sec/filings` | `ListFilings` | stable | Reference.ListFilings |
| `/v1/reference/sec/filings/{filing_id}` | `GetFiling` | stable | Reference.GetFiling |
| `/v1/reference/sec/filings/{filing_id}/files` | `ListFilingFiles` | stable | Reference.ListFilingFiles |
| `/v1/reference/sec/filings/{filing_id}/files/{file_id}` | `GetFilingFile` | stable | Reference.GetFilingFile |
| `/v1/summaries` | `SnapshotSummary` | stable | — |
| `/v2/snapshot/locale/global/markets/crypto/tickers/{ticker}/book` | `DeprecatedGetCryptoSnapshotTickerBook` | deprecated | — |
| `/v2/ticks/stocks/nbbo/{ticker}/{date}` | `DeprecatedGetHistoricStocksQuotes` | deprecated | Stocks.ListHistoricQuotes |
| `/v2/ticks/stocks/trades/{ticker}/{date}` | `DeprecatedGetHistoricStocksTrades` | deprecated | Stocks.ListHistoricTrades |
| `/vX/reference/financials` | `ListFinancials` | experimental | Reference.ListFinancials |

### Routes documented once per asset class

- `/v1/marketstatus/now` — `crypto/market-operations/market-status`, `forex/market-operations/market-status`, `indices/market-operations/market-status`, `options/market-operations/market-status`, `stocks/market-operations/market-status`
- `/v1/marketstatus/upcoming` — `crypto/market-operations/market-holidays`, `forex/market-operations/market-holidays`, `indices/market-operations/market-holidays`, `options/market-operations/market-holidays`, `stocks/market-operations/market-holidays`
- `/v3/reference/conditions` — `crypto/market-operations/condition-codes`, `options/market-operations/condition-codes`, `stocks/market-operations/condition-codes`
- `/v3/reference/exchanges` — `crypto/market-operations/exchanges`, `forex/market-operations/exchanges`, `options/market-operations/exchanges`, `stocks/market-operations/exchanges`
- `/v3/reference/tickers` — `crypto/tickers/all-tickers`, `forex/tickers/all-tickers`, `indices/tickers/all-tickers`, `stocks/tickers/all-tickers`
- `/v3/reference/tickers/{ticker}` — `crypto/tickers/ticker-overview`, `forex/tickers/ticker-overview`, `indices/tickers/ticker-overview`, `stocks/tickers/ticker-overview`
- `/v3/snapshot` — `crypto/snapshots/unified-snapshot`, `forex/snapshots/unified-snapshot`, `indices/snapshots/unified-snapshot`, `options/snapshots/unified-snapshot`, `stocks/snapshots/unified-snapshot`

### Every page, joined

| Page | Route | Operation | SDK |
|---|---|---|---|
| `alternative/consumer-spending/merchant-aggregates` | `/consumer-spending/eu/v1/merchant-aggregates` | `get_consumer-spending_eu_v1_merchant-aggregates` | — |
| `alternative/consumer-spending/merchant-hierarchy` | `/consumer-spending/eu/v1/merchant-hierarchy` | `get_consumer-spending_eu_v1_merchant-hierarchy` | — |
| `crypto/aggregates/custom-bars` | `/v2/aggs/ticker/{cryptoTicker}/range/{multiplier}/{timespan}/{from}/{to}` | `GetCryptoAggregates` | — |
| `crypto/aggregates/daily-market-summary` | `/v2/aggs/grouped/locale/global/market/crypto/{date}` | `GetGroupedCryptoAggregates` | — |
| `crypto/aggregates/daily-ticker-summary` | `/v1/open-close/crypto/{from}/{to}/{date}` | `GetCryptoOpenClose` | — |
| `crypto/aggregates/previous-day-bar` | `/v2/aggs/ticker/{cryptoTicker}/prev` | `GetPreviousCryptoAggregates` | — |
| `crypto/market-operations/condition-codes` | `/v3/reference/conditions` | `ListConditions` | Reference.ListConditions |
| `crypto/market-operations/exchanges` | `/v3/reference/exchanges` | `ListExchanges` | Reference.ListExchanges |
| `crypto/market-operations/market-holidays` | `/v1/marketstatus/upcoming` | `GetMarketHolidays` | Reference.ListMarketHolidays |
| `crypto/market-operations/market-status` | `/v1/marketstatus/now` | `GetMarketStatus` | Reference.GetMarketStatus |
| `crypto/snapshots/full-market-snapshot` | `/v2/snapshot/locale/global/markets/crypto/tickers` | `GetCryptoSnapshotTickers` | — |
| `crypto/snapshots/single-ticker-snapshot` | `/v2/snapshot/locale/global/markets/crypto/tickers/{ticker}` | `GetCryptoSnapshotTicker` | — |
| `crypto/snapshots/top-market-movers` | `/v2/snapshot/locale/global/markets/crypto/{direction}` | `GetCryptoSnapshotDirection` | — |
| `crypto/snapshots/unified-snapshot` | `/v3/snapshot` | `Snapshots` | — |
| `crypto/technical-indicators/exponential-moving-average` | `/v1/indicators/ema/{cryptoTicker}` | `CryptoEMA` | — |
| `crypto/technical-indicators/moving-average-convergence-divergence` | `/v1/indicators/macd/{cryptoTicker}` | `CryptoMACD` | — |
| `crypto/technical-indicators/relative-strength-index` | `/v1/indicators/rsi/{cryptoTicker}` | `CryptoRSI` | — |
| `crypto/technical-indicators/simple-moving-average` | `/v1/indicators/sma/{cryptoTicker}` | `CryptoSMA` | — |
| `crypto/tickers/all-tickers` | `/v3/reference/tickers` | `ListTickers` | Reference.ListTickers |
| `crypto/tickers/ticker-overview` | `/v3/reference/tickers/{ticker}` | `GetTicker` | Reference.GetTicker |
| `crypto/trades/last-trade` | `/v1/last/crypto/{from}/{to}` | `LastTradeCrypto` | — |
| `crypto/trades/trades` | `/v3/trades/{cryptoTicker}` | `TradesCrypto` | — |
| `economy/funding-conditions` | `/fed/v1/funding-conditions` | `get_fed_v1_funding-conditions` | — |
| `economy/inflation` | `/fed/v1/inflation` | `get_fed_v1_inflation` | — |
| `economy/inflation-expectations` | `/fed/v1/inflation-expectations` | `get_fed_v1_inflation-expectations` | — |
| `economy/labor-market` | `/fed/v1/labor-market` | `get_fed_v1_labor-market` | — |
| `economy/treasury-yields` | `/fed/v1/treasury-yields` | `get_fed_v1_treasury-yields` | — |
| `forex/aggregates/custom-bars` | `/v2/aggs/ticker/{forexTicker}/range/{multiplier}/{timespan}/{from}/{to}` | `GetForexAggregates` | — |
| `forex/aggregates/daily-market-summary` | `/v2/aggs/grouped/locale/global/market/fx/{date}` | `GetGroupedForexAggregates` | — |
| `forex/aggregates/previous-day-bar` | `/v2/aggs/ticker/{forexTicker}/prev` | `GetPreviousForexAggregates` | — |
| `forex/currency-conversion` | `/v1/conversion/{from}/{to}` | `RealTimeCurrencyConversion` | — |
| `forex/market-operations/exchanges` | `/v3/reference/exchanges` | `ListExchanges` | Reference.ListExchanges |
| `forex/market-operations/market-holidays` | `/v1/marketstatus/upcoming` | `GetMarketHolidays` | Reference.ListMarketHolidays |
| `forex/market-operations/market-status` | `/v1/marketstatus/now` | `GetMarketStatus` | Reference.GetMarketStatus |
| `forex/quotes/last-quote` | `/v1/last_quote/currencies/{from}/{to}` | `LastQuoteCurrencies` | — |
| `forex/quotes/quotes` | `/v3/quotes/{fxTicker}` | `QuotesFx` | — |
| `forex/snapshots/full-market-snapshot` | `/v2/snapshot/locale/global/markets/forex/tickers` | `GetForexSnapshotTickers` | — |
| `forex/snapshots/single-ticker-snapshot` | `/v2/snapshot/locale/global/markets/forex/tickers/{ticker}` | `GetForexSnapshotTicker` | — |
| `forex/snapshots/top-market-movers` | `/v2/snapshot/locale/global/markets/forex/{direction}` | `GetForexSnapshotDirection` | — |
| `forex/snapshots/unified-snapshot` | `/v3/snapshot` | `Snapshots` | — |
| `forex/technical-indicators/exponential-moving-average` | `/v1/indicators/ema/{fxTicker}` | `ForexEMA` | — |
| `forex/technical-indicators/moving-average-convergence-divergence` | `/v1/indicators/macd/{fxTicker}` | `ForexMACD` | — |
| `forex/technical-indicators/relative-strength-index` | `/v1/indicators/rsi/{fxTicker}` | `ForexRSI` | — |
| `forex/technical-indicators/simple-moving-average` | `/v1/indicators/sma/{fxTicker}` | `ForexSMA` | — |
| `forex/tickers/all-tickers` | `/v3/reference/tickers` | `ListTickers` | Reference.ListTickers |
| `forex/tickers/ticker-overview` | `/v3/reference/tickers/{ticker}` | `GetTicker` | Reference.GetTicker |
| `futures/aggregates` | `/futures/v1/aggs/{ticker}` | `AggregatesV1` | — |
| `futures/contracts` | `/futures/v1/contracts` | `get_futures_v1_contracts` | — |
| `futures/market-operations/exchanges` | `/futures/v1/exchanges` | `get_futures_v1_exchanges` | — |
| `futures/market-operations/market-status` | `/futures/v1/market-status` | `get_futures_v1_market-status` | — |
| `futures/products` | `/futures/v1/products` | `get_futures_v1_products` | — |
| `futures/schedules` | `/futures/v1/schedules` | `get_futures_v1_schedules` | — |
| `futures/snapshots/contracts-snapshot` | `/futures/v1/snapshot` | `get_futures_v1_snapshot` | — |
| `futures/trades-quotes/quotes` | `/futures/v1/quotes/{ticker}` | `get_futures_v1_quotes_ticker` | — |
| `futures/trades-quotes/trades` | `/futures/v1/trades/{ticker}` | `get_futures_v1_trades_ticker` | — |
| `indices/aggregates/custom-bars` | `/v2/aggs/ticker/{indicesTicker}/range/{multiplier}/{timespan}/{from}/{to}` | `GetIndicesAggregates` | — |
| `indices/aggregates/daily-ticker-summary` | `/v1/open-close/{indicesTicker}/{date}` | `GetIndicesOpenClose` | — |
| `indices/aggregates/previous-day-bar` | `/v2/aggs/ticker/{indicesTicker}/prev` | `GetPreviousIndicesAggregates` | — |
| `indices/market-operations/market-holidays` | `/v1/marketstatus/upcoming` | `GetMarketHolidays` | Reference.ListMarketHolidays |
| `indices/market-operations/market-status` | `/v1/marketstatus/now` | `GetMarketStatus` | Reference.GetMarketStatus |
| `indices/snapshots/indices-snapshot` | `/v3/snapshot/indices` | `IndicesSnapshot` | — |
| `indices/snapshots/unified-snapshot` | `/v3/snapshot` | `Snapshots` | — |
| `indices/technical-indicators/exponential-moving-average` | `/v1/indicators/ema/{indicesTicker}` | `IndicesEMA` | — |
| `indices/technical-indicators/moving-average-convergence-divergence` | `/v1/indicators/macd/{indicesTicker}` | `IndicesMACD` | — |
| `indices/technical-indicators/relative-strength-index` | `/v1/indicators/rsi/{indicesTicker}` | `IndicesRSI` | — |
| `indices/technical-indicators/simple-moving-average` | `/v1/indicators/sma/{indicesTicker}` | `IndicesSMA` | — |
| `indices/tickers/all-tickers` | `/v3/reference/tickers` | `ListTickers` | Reference.ListTickers |
| `indices/tickers/ticker-overview` | `/v3/reference/tickers/{ticker}` | `GetTicker` | Reference.GetTicker |
| `options/aggregates/custom-bars` | `/v2/aggs/ticker/{optionsTicker}/range/{multiplier}/{timespan}/{from}/{to}` | `GetOptionsAggregates` | — |
| `options/aggregates/daily-ticker-summary` | `/v1/open-close/{optionsTicker}/{date}` | `GetOptionsOpenClose` | — |
| `options/aggregates/previous-day-bar` | `/v2/aggs/ticker/{optionsTicker}/prev` | `GetPreviousOptionsAggregates` | — |
| `options/contracts/all-contracts` | `/v3/reference/options/contracts` | `ListOptionsContracts` | Reference.ListOptionsContracts |
| `options/contracts/contract-overview` | `/v3/reference/options/contracts/{options_ticker}` | `GetOptionsContract` | Reference.GetOptionsContract |
| `options/market-operations/condition-codes` | `/v3/reference/conditions` | `ListConditions` | Reference.ListConditions |
| `options/market-operations/exchanges` | `/v3/reference/exchanges` | `ListExchanges` | Reference.ListExchanges |
| `options/market-operations/market-holidays` | `/v1/marketstatus/upcoming` | `GetMarketHolidays` | Reference.ListMarketHolidays |
| `options/market-operations/market-status` | `/v1/marketstatus/now` | `GetMarketStatus` | Reference.GetMarketStatus |
| `options/snapshots/option-chain-snapshot` | `/v3/snapshot/options/{underlyingAsset}` | `OptionsChain` | — |
| `options/snapshots/option-contract-snapshot` | `/v3/snapshot/options/{underlyingAsset}/{optionContract}` | `OptionContract` | — |
| `options/snapshots/unified-snapshot` | `/v3/snapshot` | `Snapshots` | — |
| `options/technical-indicators/exponential-moving-average` | `/v1/indicators/ema/{optionsTicker}` | `OptionsEMA` | — |
| `options/technical-indicators/moving-average-convergence-divergence` | `/v1/indicators/macd/{optionsTicker}` | `OptionsMACD` | — |
| `options/technical-indicators/relative-strength-index` | `/v1/indicators/rsi/{optionsTicker}` | `OptionsRSI` | — |
| `options/technical-indicators/simple-moving-average` | `/v1/indicators/sma/{optionsTicker}` | `OptionsSMA` | — |
| `options/trades-quotes/last-trade` | `/v2/last/trade/{optionsTicker}` | `LastTradeOptions` | — |
| `options/trades-quotes/quotes` | `/v3/quotes/{optionsTicker}` | `QuotesOptions` | — |
| `options/trades-quotes/trades` | `/v3/trades/{optionsTicker}` | `TradesOptions` | — |
| `partners/benzinga/analyst-details` | `/benzinga/v1/analysts` | `get_benzinga_v1_analysts` | — |
| `partners/benzinga/analyst-insights` | `/benzinga/v1/analyst-insights` | `get_benzinga_v1_analyst-insights` | — |
| `partners/benzinga/analyst-ratings` | `/benzinga/v1/ratings` | `get_benzinga_v1_ratings` | — |
| `partners/benzinga/bulls-bears-say` | `/benzinga/v1/bulls-bears-say` | `get_benzinga_v1_bulls-bears-say` | — |
| `partners/benzinga/consensus-ratings` | `/benzinga/v1/consensus-ratings/{ticker}` | `get_benzinga_v1_consensus-ratings_ticker` | — |
| `partners/benzinga/corporate-guidance` | `/benzinga/v1/guidance` | `get_benzinga_v1_guidance` | — |
| `partners/benzinga/earnings` | `/benzinga/v1/earnings` | `get_benzinga_v1_earnings` | — |
| `partners/benzinga/firm-details` | `/benzinga/v1/firms` | `get_benzinga_v1_firms` | — |
| `partners/benzinga/news` | `/benzinga/v2/news` | `get_benzinga_v2_news` | — |
| `partners/etf-global/analytics` | `/etf-global/v1/analytics` | `get_etf-global_v1_analytics` | — |
| `partners/etf-global/constituents` | `/etf-global/v1/constituents` | `get_etf-global_v1_constituents` | — |
| `partners/etf-global/fundflows` | `/etf-global/v1/fund-flows` | `get_etf-global_v1_fund-flows` | — |
| `partners/etf-global/profiles` | `/etf-global/v1/profiles` | `get_etf-global_v1_profiles` | — |
| `partners/etf-global/taxonomies` | `/etf-global/v1/taxonomies` | `get_etf-global_v1_taxonomies` | — |
| `partners/tmx/corporate-events` | `/tmx/v1/corporate-events` | `get_tmx_v1_corporate-events` | — |
| `stocks/aggregates/custom-bars` | `/v2/aggs/ticker/{stocksTicker}/range/{multiplier}/{timespan}/{from}/{to}` | `GetStocksAggregates` | Stocks.ListAggregates |
| `stocks/aggregates/daily-market-summary` | `/v2/aggs/grouped/locale/us/market/stocks/{date}` | `GetGroupedStocksAggregates` | Stocks.ListGroupedDaily |
| `stocks/aggregates/daily-ticker-summary` | `/v1/open-close/{stocksTicker}/{date}` | `GetStocksOpenClose` | Stocks.GetDailyOpenClose |
| `stocks/aggregates/previous-day-bar` | `/v2/aggs/ticker/{stocksTicker}/prev` | `GetPreviousStocksAggregates` | Stocks.ListPreviousClose |
| `stocks/corporate-actions/dividends` | `/stocks/v1/dividends` | `get_stocks_v1_dividends` | Stocks.ListDividends |
| `stocks/corporate-actions/ipos` | `/vX/reference/ipos` | `ListIPOs` | Reference.ListIpos |
| `stocks/corporate-actions/reference-dividends` | `/v3/reference/dividends` | `ListDividends` | Reference.ListDividends |
| `stocks/corporate-actions/reference-splits` | `/v3/reference/splits` | `ListStockSplits` | Reference.ListSplits |
| `stocks/corporate-actions/splits` | `/stocks/v1/splits` | `get_stocks_v1_splits` | Stocks.ListSplits |
| `stocks/corporate-actions/ticker-events` | `/vX/reference/tickers/{id}/events` | `GetEvents` | Reference.GetTickerEvents |
| `stocks/filings/10-k-sections` | `/stocks/filings/10-K/vX/sections` | `get_stocks_filings_10-K_vX_sections` | Reference.List10KSections |
| `stocks/filings/13-f-filings` | `/stocks/filings/vX/13-F` | `get_stocks_filings_vX_13-F` | Reference.List13FHoldings |
| `stocks/filings/8-k-disclosures` | `/stocks/filings/8-K/vX/disclosures` | `get_stocks_filings_8-K_vX_disclosures` | Reference.List8KDisclosures |
| `stocks/filings/8-k-text` | `/stocks/filings/8-K/vX/text` | `get_stocks_filings_8-K_vX_text` | Reference.List8KText |
| `stocks/filings/disclosure-categories` | `/stocks/taxonomies/vX/disclosures` | `get_stocks_taxonomies_vX_disclosures` | Reference.ListDisclosureTaxonomy |
| `stocks/filings/form-3` | `/stocks/filings/vX/form-3` | `get_stocks_filings_vX_form-3` | Reference.ListForm3Filings |
| `stocks/filings/form-4` | `/stocks/filings/vX/form-4` | `get_stocks_filings_vX_form-4` | Reference.ListForm4Filings |
| `stocks/filings/index` | `/stocks/filings/vX/index` | `get_stocks_filings_vX_index` | Reference.ListFilingIndex |
| `stocks/filings/risk-categories` | `/stocks/taxonomies/vX/risk-factors` | `get_stocks_taxonomies_vX_risk-factors` | Reference.ListRiskFactorTaxonomy |
| `stocks/filings/risk-factors` | `/stocks/filings/vX/risk-factors` | `get_stocks_filings_vX_risk-factors` | Reference.ListRiskFactors |
| `stocks/fundamentals/balance-sheets` | `/stocks/financials/v1/balance-sheets` | `get_stocks_financials_v1_balance-sheets` | Reference.ListBalanceSheets |
| `stocks/fundamentals/cash-flow-statements` | `/stocks/financials/v1/cash-flow-statements` | `get_stocks_financials_v1_cash-flow-statements` | Reference.ListCashFlowStatements |
| `stocks/fundamentals/float` | `/stocks/vX/float` | `get_stocks_vX_float` | Reference.ListFloat |
| `stocks/fundamentals/income-statements` | `/stocks/financials/v1/income-statements` | `get_stocks_financials_v1_income-statements` | Reference.ListIncomeStatements |
| `stocks/fundamentals/ratios` | `/stocks/financials/v1/ratios` | `get_stocks_financials_v1_ratios` | Reference.ListRatios |
| `stocks/fundamentals/short-interest` | `/stocks/v1/short-interest` | `get_stocks_v1_short-interest` | Reference.ListShortInterest |
| `stocks/fundamentals/short-volume` | `/stocks/v1/short-volume` | `get_stocks_v1_short-volume` | Reference.ListShortVolume |
| `stocks/market-operations/condition-codes` | `/v3/reference/conditions` | `ListConditions` | Reference.ListConditions |
| `stocks/market-operations/exchanges` | `/v3/reference/exchanges` | `ListExchanges` | Reference.ListExchanges |
| `stocks/market-operations/market-holidays` | `/v1/marketstatus/upcoming` | `GetMarketHolidays` | Reference.ListMarketHolidays |
| `stocks/market-operations/market-status` | `/v1/marketstatus/now` | `GetMarketStatus` | Reference.GetMarketStatus |
| `stocks/news` | `/v2/reference/news` | `ListNews` | Reference.ListNews |
| `stocks/snapshots/full-market-snapshot` | `/v2/snapshot/locale/us/markets/stocks/tickers` | `GetStocksSnapshotTickers` | Stocks.ListSnapshots |
| `stocks/snapshots/single-ticker-snapshot` | `/v2/snapshot/locale/us/markets/stocks/tickers/{stocksTicker}` | `GetStocksSnapshotTicker` | Stocks.GetSnapshot |
| `stocks/snapshots/top-market-movers` | `/v2/snapshot/locale/us/markets/stocks/{direction}` | `GetStocksSnapshotDirection` | Stocks.ListMovers |
| `stocks/snapshots/unified-snapshot` | `/v3/snapshot` | `Snapshots` | — |
| `stocks/technical-indicators/exponential-moving-average` | `/v1/indicators/ema/{stockTicker}` | `EMA` | Stocks.ListEma |
| `stocks/technical-indicators/moving-average-convergence-divergence` | `/v1/indicators/macd/{stockTicker}` | `MACD` | Stocks.ListMacd |
| `stocks/technical-indicators/relative-strength-index` | `/v1/indicators/rsi/{stockTicker}` | `RSI` | Stocks.ListRsi |
| `stocks/technical-indicators/simple-moving-average` | `/v1/indicators/sma/{stockTicker}` | `SMA` | Stocks.ListSma |
| `stocks/tickers/all-tickers` | `/v3/reference/tickers` | `ListTickers` | Reference.ListTickers |
| `stocks/tickers/related-tickers` | `/v1/related-companies/{ticker}` | `GetRelatedCompanies` | Reference.ListRelatedCompanies |
| `stocks/tickers/ticker-overview` | `/v3/reference/tickers/{ticker}` | `GetTicker` | Reference.GetTicker |
| `stocks/tickers/ticker-types` | `/v3/reference/tickers/types` | `ListTickerTypes` | Reference.ListTickerTypes |
| `stocks/trades-quotes/last-quote` | `/v2/last/nbbo/{stocksTicker}` | `LastQuote` | Stocks.GetLastQuote |
| `stocks/trades-quotes/last-trade` | `/v2/last/trade/{stocksTicker}` | `LastTrade` | Stocks.GetLastTrade |
| `stocks/trades-quotes/quotes` | `/v3/quotes/{stockTicker}` | `Quotes` | Stocks.ListQuotes |
| `stocks/trades-quotes/trades` | `/v3/trades/{stockTicker}` | `Trades` | Stocks.ListTrades |
