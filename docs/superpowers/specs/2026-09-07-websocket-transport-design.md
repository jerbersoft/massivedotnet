# WebSocket transport: connection, auth, reconnect, backpressure

Issue: [#20](https://github.com/jerbersoft/massivedotnet/issues/20) · Milestone: v0.2 WebSockets · Date: 2026-09-07

## Why

The SDK has no streaming surface at all. `src/` holds core, `.Rest`, and
`.Extensions.DependencyInjection`; D8 has always named `.WebSocket` among the SDK's packages,
and this is it.

This is also the first subsystem in the repository that is **not generated**. `specs/openapi.json`
declares two HTTPS servers and says nothing about streaming, so there is no description to read, no
map row to add, and no `EndpointCoverageTests` count to raise. Every fact below was established by
probing the live service on 2026-09-07, because the alternative was inventing it.

#21 — 34 event types across 6 markets — is the milestone's bulk and inherits whatever shape is
chosen here. That is the reason to get the consumer surface right before a single event type exists.

## What the wire actually does

Verified 2026-09-07 (a US exchange holiday, so the stock market was closed) with a throwaway
`ClientWebSocket` probe. Findings that contradict the issue are marked.

### The handshake

Three exchanges, all of them JSON **arrays**, never bare objects:

```
connect   wss://socket.massive.com/stocks
<--       [{"ev":"status","status":"connected","message":"Connected Successfully"}]
-->       {"action":"auth","params":"<key>"}
<--       [{"ev":"status","status":"auth_success","message":"authenticated"}]
-->       {"action":"subscribe","params":"T.AAPL,A.AAPL"}
<--       [{"ev":"status","status":"success","message":"subscribed to: T.AAPL"},
           {"ev":"status","status":"success","message":"subscribed to: A.AAPL"}]
```

Outbound control messages are single objects; everything inbound is an array. One frame carried both
subscription acknowledgements, which is the same batching the data feed uses, so the parser cannot
assume one event per frame.

### Findings

| Question | Finding |
|---|---|
| How many feed hosts exist? | **8 stems, not the 18 the issue claims.** See below. |
| Is `launchpad` among them? | **No** — verified three times on both domains. The issue names it. |
| Do the massive.com and polygon.io names both work? | Yes, the same 8 stems on each. `business.massive.com` is a CNAME to `business.polygon.io`. |
| Is auth a header? | No. It is a message, sent after the socket is open (unlike REST, D2). |
| What does a bad key produce? | `auth_failed`, then the server **closes without a close handshake** — `ReceiveAsync` throws rather than reporting a Close frame. |
| What does an unentitled market produce? | Also `auth_failed`, with a different message: *"Your plan doesn't include websocket access."* |
| Which markets does the test key reach? | **`stocks` only.** `options`, `crypto`, `forex`, `indices`, and `futures` all answer `auth_failed` with the entitlement message. |
| Are wildcards accepted? | Yes — `T.*` is acknowledged like any other subscription. |
| What does an unknown **ticker** do? | `T.NOTATICKER` is **acknowledged successfully** and simply never produces data. |
| What does an unknown **topic** do? | `ZZ.AAPL` and `QQ.AAPL` are **silently dropped** — no acknowledgement, no error, nothing. |

### The host list

DNS cannot enumerate these: `polygon.io` and `massive.com` both answer with **wildcard records**, so
`definitely-not-a-feed.polygon.io` resolves to an address like any other. What distinguishes a real
host is the certificate — a provisioned host presents one naming itself, and an unprovisioned name
falls through to the ingress default, `CN=Kubernetes Ingress Controller Fake Certificate`.

By that test, eight stems are present, identically on both domains:

```
socket   delayed   business   polyfeed   polyfeedplus   nasdaqfeed   starterfeed   delayed-business
```

Absent: `launchpad`, `delayed-launchpad`, `delayed-polyfeed`, `delayed-polyfeedplus`,
`delayed-nasdaqfeed`, `delayed-starterfeed`, and every `business-*` variant.

The issue's "18 feed hosts" is not what the service presents. Eight stems across two domains is
sixteen names, and the two lists differ in composition, not just in count.

### The finding that changed the design

A valid topic with a nonsense ticker is acknowledged. A nonsense topic is dropped in silence. So the
one mistake the server *will not* tell a caller about is a mistyped topic code — the caller sees a
successful subscribe, an open connection, and no data, forever, with nothing anywhere reporting a
problem.

That is the failure this repository refuses everywhere else, and it arrives here through the
subscribe path. It is also the reason the usual posture — let the server reject a bad value, as D9
does for entitlements and the enum conventions do for `sort` fields — **cannot** be used here. The
server does not reject. It ignores.

## Decisions

### D-W1 · A topic is a typed enum, a ticker is a string

Topic codes never appear as caller-supplied strings. `SubscribeTradesAsync` and
`StockTopic.Trades` render `"T"`; there is no overload taking `"T"` as text. Tickers stay strings and
are passed through untouched.

This is drawn directly from the asymmetry above. A mistyped topic is invisible, so the design makes
it unrepresentable rather than validated — the same move D20 makes with `DateOrNanoseconds`, where
two types exist so the wrong unit cannot be written. A mistyped ticker is acknowledged by the server
and yields no data, which is the server's business and not something the SDK can check without
carrying a ticker universe it would have to keep fresh.

Rejected: accepting a raw topic string with client-side validation against a known set. It is the
same protection with a worse failure mode — a caller who passes an unrecognised string gets an
exception at runtime where the enum gives them a compile error, and the validation set is a second
source of truth that drifts from the enum beside it.

### D-W2 · Every subscription is acknowledged, and an unacknowledged one throws

The client counts the `(topic, ticker)` pairs it sends and the `status: success` acknowledgements
that come back. If the acknowledgements are short within the subscribe window, `SubscribeAsync`
throws naming the pairs that went unacknowledged.

D-W1 removes the only cause of a silent drop observed today. This is the guard for the ones not
observed: a topic Massive retires, a market that stops serving a shape, an entitlement enforced per
topic rather than at auth. A subscription that silently does not exist is indistinguishable from a
quiet market, which is D29's argument for throwing on a self-referential cursor rather than stopping
politely.

Counting rather than parsing `"subscribed to: T.AAPL"` is deliberate: the message is prose, and prose
is the one part of a wire contract nobody versions.

The accepted cost is a spurious throw if Massive ever coalesces acknowledgements — four subscriptions
were observed acknowledged one-for-one, which is evidence and not a guarantee. It fails loudly at
subscribe time, where it is cheap to notice and correct, rather than quietly forever.

### D-W3 · One sequence per topic, one consumer, created on subscribe

`SubscribeTradesAsync` returns `IAsyncEnumerable<Trade>` backed by that topic's own bounded channel.
Calling it again widens the ticker set and returns the **same** sequence rather than minting a second
one. The sequence permits a single enumeration and throws `InvalidOperationException` on a second
`GetAsyncEnumerator` call, because `SingleReader` alone is a performance contract the runtime does
not police -- violating it is undefined behaviour, not an exception, so the guard is the SDK's to
enforce.

This is what bounds memory. Buffers are allocated per topic actually subscribed — at most 8 for
stocks — so buffer count cannot grow with how many times a caller subscribes.

Rejected: a single ordered sequence carrying every event. The natural implementation is a
discriminated struct, and it cannot be built: the event structs hold `string` and array references,
and .NET forbids overlapping a reference field under `LayoutKind.Explicit`, so the union would be the
*sum* of every shape rather than the maximum. The alternative — yielding a struct holding the
discriminator and a slice of the frame buffer — keeps wire ordering and allocates nothing, but its
validity ends when the iteration advances, and nothing in the type system stops a caller storing one.

The cost is real and is accepted: **ordering between topics is lost.** A trade and the quote beside it
arrive on separate sequences with no way to recover which came first. Within a topic, order holds.

### D-W4 · A full buffer drops the oldest event and counts it; the read loop never blocks

Each topic's channel is bounded with `BoundedChannelFullMode.DropOldest` and an `itemDropped`
callback, so evictions are reported by the runtime rather than inferred from a racy count:

```csharp
Channel.CreateBounded<Trade>(
    new BoundedChannelOptions(capacity)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = true,
    },
    itemDropped: _ => metrics.RecordDrop());
```

Never blocking the read loop is the load-bearing half. Every topic shares one socket, so a writer
that waits for space stalls the loop, closes the TCP receive window, and starves every other topic
behind the slow one — and then Massive drops the connection for being a slow consumer, losing the
topics that were keeping up. Resolving overflow at the buffer confines the loss to the topic that
caused it.

Rejected: throwing on overflow. It is louder and consistent with D29, but market data is bursty by
nature and the open auction alone would trip it, so the loud option would fire routinely on healthy
connections.

The accepted cost is that a consumer who never reads `DroppedCount` loses data quietly. D-W5 is what
narrows that.

### D-W5 · The DI package bridges drops and reconnects to `ILogger`

`AddMassive` subscribes to the drop and reconnect counters and logs a warning. Core gains nothing:
`Microsoft.Extensions.*` is legal in `.Extensions.DependencyInjection` and nowhere else (rule 8), so
the bridge lives there and core continues not to know that logging exists.

This is what keeps D-W4's cost from being the whole story. A consumer wiring the SDK through DI —
which is most of them — is told they are behind, on the logger they already have, without the
counters becoming mandatory reading for anyone.

### D-W6 · Authentication failure is terminal and is never retried

`ConnectAsync` does not return until `auth_success` arrives, and throws
`MassiveStreamAuthenticationException` on `auth_failed`, carrying the server's message **verbatim**.
Reconnect (D-W7) does not apply: an authentication failure ends the connection permanently.

Verbatim matters because `auth_failed` carries two unrelated failures. A bad key answers
*"authentication failed"*; an unentitled market answers *"Your plan doesn't include websocket
access."* — and on the test key, five of the six markets answer the latter. A caller's response to
those differs completely, and the SDK cannot tell them apart without reading prose, so it reports
what the server said instead of inventing a category.

Never retrying is what stops the SDK reconnecting against a bad key until the account is rate-limited.
The server's own behaviour reinforces it: after `auth_failed` it closes abruptly, without a close
handshake, so a retry loop would be reconnecting into a refusal.

### D-W7 · Reconnect is transparent to the sequences, and counted

On an unexpected close the client reconnects with exponential backoff and jitter, re-authenticates,
and replays the subscription registry. The topic sequences do not end and do not throw — a reconnect
the caller has to handle is not a reconnect.

A reconnect does mean missed messages, so it is reported the same way a drop is: `ReconnectCount` and
`LastReconnected`, bridged to `ILogger` by D-W5. The design that counts drops has no business hiding
gaps.

`LastReconnected` comes from an injected `IClock`, defaulting to `SystemClock.Instance` — CLAUDE.md's
vocabulary table is explicit that the current moment never comes from the BCL clock, and an injected
one lets the backoff tests run without real delays.

### D-W8 · Feed hosts are named statics, and the list is what the service presents

`MassiveFeeds` mirrors `MassiveEndpoints`: static `Uri` properties, one per stem, defaulting to the
`massive.com` names with the `polygon.io` equivalents beside them as `MassiveEndpoints.Legacy` already
does for REST. The market is a path segment, so `MassiveFeeds.RealTime` with `MassiveMarket.Stocks`
gives `wss://socket.massive.com/stocks`.

Eight stems ship, because eight is what presents a certificate. `launchpad` does not ship, despite the
issue naming it: shipping a property that resolves through wildcard DNS to an ingress default would
give a caller a TLS failure from a name the SDK told them was real.

Invalid host and market pairings are not modelled out. That one *is* the server's business, and it
answers clearly — `auth_failed` with the entitlement message, which D-W6 surfaces verbatim.

This decision is the one most likely to age. It is written from a certificate probe on a single date,
and D21's posture applies unchanged: the live tier pins what was observed, dated, and flips the day
Massive provisions `launchpad` or retires a stem.

### D-W9 · Authentication is a message, so rule 11 extends to frames

Rule 11 says a key is never logged, echoed in an exception message, or written to disk. Under D2 that
was mostly free, because the key lived in a header the SDK never rendered. Here it is a frame body,
which is exactly the sort of thing a diagnostic hook would print.

Three constraints follow, and they are testable rather than aspirational: the auth frame is never
rendered into an exception message, never traced, and its buffer is cleared after the send. A test
drives a failing handshake and asserts the key appears in no exception message and no diagnostic
output.

### D-W10 · The parse path allocates nothing per event

Per event, in the steady state: nothing on the heap.

- **Frames** are read into a rented buffer, grown across continuation frames to a hard
  `MaxMessageBytes` and then refused. An uncapped reassembly buffer is the one place a server could
  make the SDK allocate without limit.
- **The discriminator** is read by copying the `Utf8JsonReader` struct at the element start, scanning
  for `ev`, and restoring the copy. `ev` was first in every message observed; the copy is what makes
  the design not depend on that.
- **Scalars** go through `JsonValueReader`, unchanged from core. This is D32's primitive doing the job
  it was built for.
- **Tickers** are interned. `reader.CopyString` unescapes into a `stackalloc char[16]`, which probes a
  `Dictionary<string, string>` through `GetAlternateLookup<ReadOnlySpan<char>>()` — so the probe
  allocates no string, and a repeat ticker returns the instance already held.
- **Condition codes** bind to an `[InlineArray]`, not a rented array. `PooledArrayConverter` works for
  REST because the array dies with the response; here it would escape to the caller and could never be
  returned to the pool.

The ticker pool is **capped**. Past the cap it stops interning and allocates normally — degraded,
bounded, and never a leak. An uncapped intern table is an unbounded cache wearing a helpful hat, and
it must hold tickers only: interning trade ids would key it on something genuinely unbounded.

The trade id is the one field that still allocates, because it is unique per event and cannot be
pooled. It ships as a `string` and is measured, rather than contorting the model around a field
consumers read.

### D-W11 · `Epoch` moves to core

`Epoch` is `internal` to `.Rest` and its own comment gives the reason — *"core has no reason to know
about wire epochs"*. That premise expires with a second package needing the same conversion, so it
moves to core and becomes public.

Rejected: duplicating it into `.WebSocket`, which puts two copies of a subtle nanosecond argument in
the tree and guarantees they drift; and `InternalsVisibleTo`, which has no precedent anywhere in this
repository and would be a strange first one to set between two shipped packages.

Worth carrying into #21: **the streaming wire uses milliseconds where REST v3 uses nanoseconds** for
the same conceptual field. That is D20's concern arriving on the streaming side, and the unit is a
per-topic fact to read from Massive's documentation, never inferred from the REST model of the same
name.

## Scope

**Delivered by #20:**

- `src/MassiveDotNet.WebSocket`, referencing core only. `ClientWebSocket` is in-box, so no package is
  added and rules 7 and 8 are untouched.
- `MassiveFeeds`, `MassiveMarket`, `MassiveStreamOptions` (`Duration` throughout, per rule 12).
- `MassiveStreamConnection` — socket ownership, handshake, auth, subscribe/unsubscribe with
  acknowledgement counting, reconnect with backoff and replay, and the read/dispatch loop.
- The capped ticker pool, and `Epoch` promoted to core.
- The bounded per-topic buffers, drop counting, and the reconnect counters.
- `MassiveStockStream`, proven end-to-end on **trades and quotes** — two topics, so dispatch on `ev`
  is genuinely exercised rather than degenerate.
- The `ILogger` bridge in `.Extensions.DependencyInjection`.

**Left to #21:** the other 32 event types, across the five markets this key cannot currently reach.

**Housekeeping this work lands:**

- `TemporalTypeTests.ShippedAssemblies` gains the fourth assembly. `EveryShippedLibraryIsInspected`
  fails the moment the project directory exists, which is what that test was written for.
- That test compares *counts*, not identities, so listing an existing assembly twice would satisfy
  it. Tighten it to a set comparison of assembly names against project directory names.
- CLAUDE.md line 244 says the reflection layer covers "both shipped assemblies" when it already covers
  three. Correct it to four.
- CLAUDE.md's boundary table gains `ClientWebSocketOptions.KeepAliveInterval` and
  `CancellationTokenSource.CancelAfter`, both currently listed as anticipated.
- Issue #21's body specifies "computed `DateTimeOffset`", which predates D12. Correct it before anyone
  implements against it.
- Issue #20's host list is wrong; record the certificate finding on the issue.

## Testing

**The seam.** `ClientWebSocket` is sealed, so the connection talks to an internal `IMassiveWebSocket`
with a real implementation and a fake. This is the same shape as `StubHandler` at the
`HttpMessageHandler` seam, and it keeps the offline tier offline: reconnect, replay, acknowledgement
shortfall, overflow, and drop counting all test against the fake with no socket and no clock.

**Allocation gates**, in the offline tier per D31: a per-event ceiling, and
`RetainsNoMemoryProportionalToTheEventsReceived`, which is "no unbounded buffering" written so it can
fail a build. Both are regressed deliberately before they are committed — D31 is emphatic that a guard
nobody has watched go red is indistinguishable from a clean tree, and the traversal ceiling earned its
place exactly that way.

**The live tier** can only cover what the key reaches, which is `stocks`:

- Connect, authenticate, subscribe, and receive the acknowledgement.
- The five unentitled markets, pinned with their `auth_failed` message and the date observed — D21's
  pattern, so the pin flips the day the entitlement changes rather than reading as green.
- `launchpad` pinned as absent, for the same reason.

No live test asserts data arrives: the stock market is closed on the day this was written, and a test
that only passes during market hours is a test that fails for a reason unrelated to the SDK.

## Non-goals

- **Cross-topic ordering.** Given up by D-W3, deliberately and with the cost stated. If a consumer
  ever needs it, the additive answer is a merged sequence built on top, not a change to the per-topic
  ones.
- **Correlating acknowledgements to specific subscriptions.** D-W2 counts them; matching each to its
  pair would mean parsing prose.
- **A reconnect that replays missed data.** The protocol offers no cursor, so a gap is a gap. It is
  reported, not repaired.
- **Client-side ticker validation.** The server accepts any ticker for a valid topic; checking would
  need a ticker universe kept fresh, and D-W1 already removes the mistake the server hides.
- **The other five markets.** Not scope discipline but entitlement: they cannot be exercised on this
  key, and shipping a market whose handshake has never been observed is what D21 exists to prevent.
