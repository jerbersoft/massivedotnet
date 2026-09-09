# Stocks streaming topics: the remaining four, and the shape the other five markets inherit

Issue #21. Depends on #52, merged to `master` as `3fc70e4`.

## Why

Eight stock topics exist. Two ship — `StockTopic.Trades` and `StockTopic.Quotes`, #20's worked
example — one is the market-agnostic fair market value topic held for #58, and one is a launchpad
topic no exposed feed host can reach (#59). The remaining four are this issue: second aggregates,
minute aggregates, net order imbalance, and limit up-limit down.

The point of doing stocks first is not the four topics. It is that #53-#58 add twenty more
converters, and two decisions bind all of them: whether the hand-written `Utf8JsonReader` walk is
extracted before it is copied twenty-four times, and whether these converters are hand-written at
all. Making those decisions against four converters costs four converters to reverse. Making them
against twenty-four does not. That is why #53-#58 each depend on this issue and say so.

The cost of getting this order wrong is already measured. #20's whole-branch review found two defect
shapes that had each been fixed *per member* before anyone generalised them — an unguarded event
raise, fixed four times before `EventRaiser` was extracted, and an unguarded collection iterated
during disposal, fixed three times. Seven redundant fixes. The review named a third shape and could
not extract it, because #20 had no live instance of it left:

> A hand-written `Utf8JsonReader` walk that reads a value without going through `JsonValueReader`,
> and/or that does not `Skip()` an unrecognised property.

This issue creates four live instances of it, and #53-#58 create twenty more.

## What the wire actually says

Read from `https://massive.com/docs/websocket/stocks/{aggregates-per-second,aggregates-per-minute,imbalances,luld}.md`
and verified against the live feed on 2026-09-08 at 09:52 ET, market open, `wss://socket.massive.com/stocks`.

| Topic | Code | Ticker field | Timestamp unit | Acknowledged live |
|---|---|---|---|---|
| Second aggregates | `A` | `sym` | milliseconds (`s`, `e`) | yes |
| Minute aggregates | `AM` | `sym` | milliseconds (`s`, `e`) | yes |
| Net order imbalance | `NOI` | `T` | nanoseconds (`t`) | **no — `not authorized`** |
| Limit up-limit down | `LULD` | `T` | nanoseconds (`t`) | yes |

Four findings from that probe, each of which changes the design:

**1. `A` and `AM` are field-for-field identical.** Seventeen fields, same names, same types. They
differ only in the `ev` code and the length of the window the bar covers. Every other market repeats
the same pair.

**2. The ticker field is not the same across topics.** `A` and `AM` send `sym`, exactly as `T` and
`Q` do. `NOI` and `LULD` send `T` — the same letter that is the *trade topic's* wire code. Nothing
in the transport cares, because dispatch keys on `ev`, but a converter that assumed `sym` would find
no ticker and throw on every event.

**3. The LULD documentation contradicts itself on the timestamp unit, and the wire settles it.** Its
field table says `t` is "The Timestamp in Unix MS". Its own published sample carries
`1764086430905642800` — nineteen digits, which is nanoseconds; read as milliseconds it is a date
roughly fifty-six million years out. The live frame observed on 2026-09-08 carried
`1788877046310003385`, confirming nanoseconds. `A`/`AM` really are milliseconds (`1788877043000`),
and `NOI` is documented and sampled as nanoseconds, so LULD is the only topic where the prose and
the wire disagree.

**4. `A` carries `dv` and `dav` on the wire, and its published sample omits them.** The field table
lists both as decimal-string volumes. The sample response does not contain them; the live frame
does: `"dv":"4989.0","dav":"4332125.038360"`. A fixture drawn only from the published sample would
never exercise those two fields.

One further observation, which this issue pins rather than acts on: `NOI` answered
`{"ev":"status","status":"error","message":"not authorized"}`. See D-W18.

## Decisions

### D-W12 · The property walk is extracted into one cursor, and every converter goes through it

A small mutable `struct` in `MassiveDotNet.WebSocket/Internal`, `StreamEventWalk`, holds the walk's
state and every accessor. A converter reads as a list of properties and nothing else:

```csharp
StreamEventWalk walk = new(ref reader, Model);

while (walk.NextProperty(ref reader))
{
    if (reader.ValueTextEquals("sym"u8))     { ticker = walk.Ticker(ref reader, tickers, "sym"); }
    else if (reader.ValueTextEquals("v"u8))  { volume = walk.Int64(ref reader, "v"); }
    else if (reader.ValueTextEquals("op"u8)) { officialOpen = walk.Double(ref reader, "op"); }
}
```

`NextProperty` skips the previous property's value if no accessor consumed it, then advances to the
next property name, returning `false` at the end of the object. There is no `else` arm, so the
missing-`Skip()` half of the defect has nowhere to live: it is not caught by review, it is
unrepresentable. Every accessor delegates to `JsonValueReader`, so the other half — a value read
straight off the reader, diverging in its number and null handling from every other converter — has
nowhere to live either, because a converter that wants a value asks the walk for it.

**The reader travels as a parameter rather than living in the cursor, and that is not a style
choice.** The natural design is a `ref struct` holding a `ref Utf8JsonReader` field, so a converter
writes `walk.Int64("v")`. The compiler refuses it: `CS9050`, a ref field cannot refer to a ref
struct, and `Utf8JsonReader` is one. Making `StreamEventWalk` itself a `ref struct` and passing the
reader in fails differently — `CS8350`, because a `ref struct` receiver *might* capture the
reference even when it does not. Both were confirmed against the compiler on 2026-09-08 before this
was written down. A plain `struct` receiver is what compiles, because it cannot hold a ref field at
all, so there is nothing for ref-safety analysis to reject. The cost is `ref reader` at every call
site, which is noise; the alternative is no extraction.

`StockTradeConverter` and `StockQuoteConverter` move onto it in the same change. Leaving them on
their own copies would defeat the extraction: two hand-written walks would survive to drift from the
shared one, which is the outcome D15 rejects for filter rendering and D32 rejects for scalar reads.

The rejected alternative is an abstract `StreamEventConverter<T>` with a virtual
`TryReadProperty(ref Utf8JsonReader, ...)`. It centralises the loop but not the per-property
`reader.Read()`, so it closes only the `Skip()` half. Worse, each converter accumulates a dozen
locals before constructing its model, and those cannot cross a virtual call as locals — each event
would need a mutable builder type, which is a new type per event introduced to avoid a duplicated
loop.

The walk is `internal` to `MassiveDotNet.WebSocket` rather than public in core beside
`JsonValueReader`. Every streaming converter lives in this assembly, and the generated REST
converters cannot use it without a change to the generator that rules 5 and 6 make a separate piece
of work. Promoting it later is additive; shipping it public now is a surface that cannot be withdrawn.

### D-W13 · Streaming converters stay hand-written, and `CLAUDE.md` records it as D36

D1's premise is that the generator reads a machine-readable description and emits from it: 147 REST
operations from `specs/openapi.json`. There is no equivalent for the streaming wire. Generating
these would mean introducing a new hand-curated input file that *is* the source of truth for field
names, types, and units — the same facts written in JSON instead of C#, with no upstream to check
them against.

That trade is bad in three directions. It loses compile-time checking of the facts, since a typo in
a JSON type name fails at generation at best and at deserialization at worst, where today it does
not compile. It loses the per-field prose: the reason `TimestampNanoseconds` is named that despite
the documentation is a paragraph of argument that belongs beside the field, not in a map row. And it
buys rules 5 and 6's obligations — never hand-edit the output, CI regenerates and fails on a diff,
byte-identical determinism — for artefacts with no upstream that can drift, which is the entire
justification those rules exist to serve.

Twenty-four converters is genuinely enough repetition to ask the question. D-W12 is most of the
answer: with the walk extracted, a converter is a property list, a model construction, and a
`Write`. What generation would remove is the typing, not the thinking.

`CLAUDE.md` gets this as decision D36, so #53-#58 inherit it instead of re-litigating it once per
market.

### D-W14 · One `StockAggregate` serves both `A` and `AM`

The two shapes are identical, so one model and one converter serve both, with the converter taking
its topic code as a constructor argument for `Write` to emit. `MassiveStockStream` exposes them as
two subscribe methods returning `MassiveTopicSubscription<StockAggregate>`, backed by two sinks —
sinks are keyed by wire code, not by CLR type, so two sinks over one type need nothing new from the
dispatcher.

Two models would let the type name say which window produced the bar. That is worth something, and
it costs a duplicated model and a duplicated converter in *every* market — twelve of each across
six — kept in sync by hand, which is the duplication this issue exists to cut. The window is not
lost: every bar carries its own start and end timestamps, one second apart or sixty.

### D-W15 · LULD's timestamp is nanoseconds, read off the wire against the documentation's prose

`StockLimitUpLimitDown.TimestampNanoseconds`, converted through `Epoch.FromNanoseconds`.

This is the one place in this issue where the SDK contradicts Massive's written description, so the
reasoning has to be worth it. D21 makes the description the contract for *what ships* — which
operations exist, and how they are marked. It does not make a description's prose authoritative
about a unit when that same description's own sample response contradicts it and the live wire
agrees with the sample. There is no reading under which both halves of that page are right.

Choosing the prose would compile, would deserialize without error, and would hand every caller an
`Instant` roughly fifty-six million years in the future. That is precisely the silently-wrong
binding D20 introduced `DateOrNanoseconds` to make unrepresentable on the request side, arriving on
the response side instead.

The observation is dated in the model's remarks and pinned by a live test, so it flips the day
either the wire or the documentation moves.

### D-W16 · The imbalance auction time is stored raw and computed as a `LocalTime?`; its auction type stays a `string`

`NOI`'s `at` is `(hour × 100) + minutes` in Eastern time — `930`, `1600`. Stored raw as
`AuctionTimeCode`, exposed as `AuctionTime`, a NodaTime `LocalTime?`. That is D5's shape applied to a
field that is not an epoch value, and rule 12's vocabulary applied to a wall-clock time, which is
exactly what `LocalTime` is for. It is nullable rather than throwing, because a computed property
must not throw on a value the server chose: a code outside `0000`-`2359` reads as `null` rather than
taking down the parse.

`NOI`'s `a` is a one-character auction type from a closed set — `O`, `M`, `H`, `C`, `P`, `R` — and
stays a `string`. D-W1 made `StockTopic` an enum because the server silently ignores a topic code it
does not recognise, so a caller's typo is unrecoverable and must be made unrepresentable. That
argument is about a value the SDK *sends*. This is a value the SDK *receives*, where an enum has
only two options for a code Massive adds later: throw, failing the whole parse over one field, or
misfile it as an existing member. A string carries what arrived.

### D-W17 · Sink creation and completion stop growing per topic

`MassiveStockStream` currently holds one field, one `GetOrCreate…Sink` method, and one
`?.Complete()` call per topic. At two topics that is invisible. At six it is six near-identical
locking bodies, and #53-#58 repeat the pattern per market.

So: one generic `GetOrCreateSink<T>(ref TopicSink<T>? field, StockTopic topic, JsonConverter<T> converter)`
holding the `_sinkLock` acquisition, the `ObjectDisposedException` check, the drop-event wiring and
the `AddSink` registration once; and disposal iterating the sinks this stream created rather than
naming each one.

This is the same lesson as `EventRaiser` and D-W12, applied before the copies exist rather than
after seven of them do. The eager `Complete()` at the top of `DisposeAsync` is kept rather than
deferred to the connection's own `CompleteAllSinks()`: the connection completes sinks only after
awaiting the read loop, so relying on it would delay every consumer's `await foreach` ending by the
length of that drain. Idempotence makes the second pass a no-op.

### D-W18 · The `not authorized` status is pinned here and fixed in #60

Subscribing to `NOI.*` on a key without the entitlement answers
`{"ev":"status","status":"error","message":"not authorized"}`. `MassiveStreamConnection.OnStatus`
returns early on any status that is not `success`, so that prose is discarded, the acknowledgement
count falls short, and the caller gets a `MassiveStreamSubscriptionException` whose message says the
server "ignores a topic code it does not recognise" — which is not what happened.

That is a real defect and it is filed as #60. It is not fixed here. #21 adds topics; changing what a
refused subscribe throws is a change to the exception contract, reachable today on `T` and `Q` for
any key whose plan lacks them, and it deserves its own diff rather than arriving as a side effect of
adding four enum members.

What this issue owes is the pin: a dated live test asserting the current behaviour, so #60 has to
move it deliberately. That is D21's posture — an observation is pinned, dated, and flips the day the
behaviour changes — applied to the SDK's own behaviour rather than the service's.

**Resolved.** #60 landed as D37: the refusal is recorded against the acknowledgement slot that is
armed and carried on the exception as `ServerMessage`, and `TheImbalanceTopicIsStillNotAuthorizedOnThisKey`
now pins the server's words rather than avoiding them. The pin did its job — it had to be moved
deliberately, and it named what was wrong with the message while the message was still wrong.

## Scope

**New public surface.**

`StockTopic` gains `SecondAggregates`, `MinuteAggregates`, `Imbalances`, `LimitUpLimitDown`, with
`ToCode()` arms returning `A`, `AM`, `NOI`, `LULD`.

Three `readonly record struct` models in `MassiveDotNet.WebSocket.Events`:

| Model | Topics | Ticker field | Notable fields |
|---|---|---|---|
| `StockAggregate` | `A`, `AM` | `sym` | `dv`/`dav` decimal-string volumes; `otc` absent means false |
| `StockImbalance` | `NOI` | `T` | `at` raw code plus `LocalTime?`; `a` auction type as `string` |
| `StockLimitUpLimitDown` | `LULD` | `T` | `i` indicators as `ConditionSet`; nanosecond `t` |

`MassiveStockStream` gains `SubscribeSecondAggregatesAsync`, `SubscribeMinuteAggregatesAsync`,
`SubscribeImbalancesAsync` and `SubscribeLimitUpLimitDownAsync`, each taking
`IReadOnlyCollection<string> tickers` and a defaulted trailing `CancellationToken`, matching the two
that exist.

**New internal surface.** `StreamEventWalk` (D-W12), `StockAggregateConverter`,
`StockImbalanceConverter`, `StockLimitUpLimitDownConverter`.

**Changed.** `StockTradeConverter` and `StockQuoteConverter` move onto `StreamEventWalk`;
`MassiveStockStream`'s sink creation and disposal collapse per D-W17; `CLAUDE.md` gains D36 and the
`Layout` note for the new converters; `docs/performance/2026-09-07-streaming-allocation-figures.md`
gains the new ceilings and their regressions.

**Model field names.** Domain nouns, not wire codes, matching `StockTrade` and `StockQuote`.
`StockAggregate`: `Ticker`, `Volume`, `DecimalVolume`, `AccumulatedVolume`,
`DecimalAccumulatedVolume`, `OfficialOpenPrice`, `VolumeWeightedAveragePrice`, `Open`, `Close`,
`High`, `Low`, `DailyVolumeWeightedAveragePrice`, `AverageTradeSize`, `StartTimestampMilliseconds`,
`EndTimestampMilliseconds`, `Otc`, plus computed `Start` and `End`.
`StockImbalance`: `Ticker`, `TimestampNanoseconds`, `AuctionTimeCode`, `AuctionType`,
`SymbolSequence`, `ExchangeId`, `ImbalanceQuantity`, `PairedQuantity`, `BookClearingPrice`, plus
computed `Timestamp` and `AuctionTime`.
`StockLimitUpLimitDown`: `Ticker`, `HighPrice`, `LowPrice`, `Indicators`, `Tape`,
`TimestampNanoseconds`, `SequenceNumber`, plus computed `Timestamp`.

## Testing

**The walk, tested once rather than per converter.** This is D-W12's whole return, so it is the
test that has to exist: an unknown scalar property, an unknown object property, an unknown array
property, and a nested unknown object, each followed by a known property whose value must still
arrive correctly. Watched failing first by removing `NextProperty()`'s auto-skip, per D31 — a
missing skip does not throw, it silently reads the next property from the wrong token, so a test
that only checks "does not throw" would pass against the defect.

**Fixtures.** The published sample per topic, as the preference order requires, plus two captured
live frames the samples cannot supply: an `A` frame carrying `dv`/`dav`, which the published sample
omits, and the `LULD` frame that settles D-W15's unit. Both reviewed for account identifiers before
committing.

**Per topic.** Sample deserializes; absent optional fields read as null or false; the ticker is
pooled across events; a null or non-string ticker throws `JsonException` naming the model, on both
`sym` and `T` spellings; round trip through `Write` and `Read`.

**Allocation.** A ceiling per new parse path, each set by breaking that path, watching the assertion
go red for the right reason, restoring, and recording the regression beside the number in
`docs/performance/`. `StockAggregate` allocates its two decimal-volume strings when present;
`StockImbalance` allocates its one-character auction type; `StockLimitUpLimitDown` allocates nothing
beyond its indicators when they fit inline. Ceilings carry roughly 20% headroom, except a claim of
"allocates nothing", which is asserted with strict equality.

**Stream façade.** Each subscribe routes to the right wire code; calling one twice returns the same
subscription; a disposed stream refuses with `ObjectDisposedException` naming `MassiveStockStream`;
disposal ends every created topic's sequence, including topics added after the first.

**Live, excluded from CI by category and compiled there (rule 13).** Subscribe to all four codes and
assert the acknowledgements, which is the strongest evidence available that a code is right, since
the server answers an unrecognised topic with silence. Pin `NOI`'s `not authorized` refusal, dated,
per D-W18. Assert the LULD timestamp's magnitude is nanoseconds when an event arrives during
regular hours, per D-W15.

## Non-goals

- **The other five markets.** #53-#58, each depending on this issue for D-W12 and D-W13.
- **Fair market value.** #58. It is a business-feed topic and is not market-scoped.
- **Launchpad topics.** #59, blocked on a host no exposed feed provides.
- **Fixing the `not authorized` message.** #60, per D-W18.
- **Generating streaming converters.** Refused in D-W13, not deferred.
- **Promoting `StreamEventWalk` to core for the generated REST converters.** Additive later;
  rules 5 and 6 make it a separate piece of work.
