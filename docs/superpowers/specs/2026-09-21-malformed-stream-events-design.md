# A malformed field drops one event, not the connection

Issue #65. Touches `MassiveDotNet` (core) and `MassiveDotNet.WebSocket`, and adds one bridge to
`MassiveDotNet.Extensions.DependencyInjection`.

## Why

A `z` value the reader will not accept throws out of a converter, the exception escapes the read
loop, and the whole connection dies — taking every other topic on that socket with it. Observed in
production four times in ten minutes on 2026-09-18, each occurrence forcing a full reconnect:

```
Massive trade stream faulted: The number in StockAggregate.z does not fit a 64-bit integer.
```

Two separate defects are visible in that one line, and they have to be fixed in this order.

**The message does not say what the value was**, so the cause cannot be determined from logs.
`Utf8JsonReader.TryGetInt64` returns `false` in three unrelated situations and the message
distinguishes none of them:

1. A non-integral value — `"z":123.45` — which would mean the vendor violates its own schema.
2. An integral value written in a non-integer JSON *form* — `"z":12.0`, `"z":1e3`. Both are valid
   JSON numbers, both are whole, and `TryGetInt64` rejects both because the token is not an integer
   token. A plausible serializer artefact, and the easiest of the three to overlook.
3. A value genuinely outside `Int64`'s range.

**One event's parse failure is terminal**, and the connection it ends is multiplexed. Trades,
quotes and minute aggregates share it, so one unparseable field, on one symbol, in a topic the
consumer may not even have subscribed to, takes down the trade feed.

The first version of this issue claimed `z` is conceptually fractional and should be retyped to
`double`. That is wrong: [Massive documents `z` as an `integer`](https://massive.com/docs/websocket/stocks/aggregates-per-minute),
alongside `v`, `av`, `s` and `e`. `StockAggregate.AverageTradeSize` being `long` matches the
published schema, the type is not the defect, and retyping it would be a breaking public API change
made on a guess that contradicts the vendor. It stays `long` here.

## What is established, and what is not

**Established, from production logs:** a `z` value arrives that `TryGetInt64` rejects, and the
resulting exception kills the connection.

**Not established, and not obtainable from the current logs:** which of the three rejections it was.

That asymmetry sets the order of the work, and it also sets up a trap. Today the improved message
would reach a consumer through `Faulted`, because the failure is terminal. Making the failure
non-terminal removes that route — so a fix that only stops the outage would swallow the evidence
with the event, and the question of what `z` should be would become permanently undecidable. **The
two halves have to land together**, or the second undoes the first.

## Decisions

### D-W19 · A malformed field drops one event and is counted; a malformed frame stays terminal

`Dispatch` already holds the property that makes a per-event drop safe
(`Internal/MassiveStreamConnection.cs:889-908`):

```csharp
Utf8JsonReader start = reader;             // a struct copy is a free bookmark
string? code = ReadEventCode(ref reader);  // advances `reader` to this object's END token
...
Utf8JsonReader replay = start;             // a second copy
sink.Write(ref replay);                    // the converter throws in here
```

`replay` is a copy. When the converter throws, the outer `reader` is untouched and already parked
on the end of the object that failed, so the `while` loop takes the next event with no resync and
no guesswork. The catch therefore goes **inside `TopicSink<T>.Write`**, which knows `T` and owns
the subscription that holds the count; `Dispatch` and `ITopicSink` are unchanged.

The boundary is drawn at `sink.Write` and nowhere wider. If `ReadEventCode` itself throws — a
malformed `ev` — the reader is stranded mid-object, there is no safe point to resume from, and
there is no topic to attribute the loss to. That stays terminal. This fixes a bad *field*, not a
bad *frame*.

Catching a `JsonException` and continuing does not weaken the read loop's existing refusal to treat
a parse failure as reconnectable (G3, `MassiveStreamConnection.cs:255-262`). That argument — the
socket is healthy, the server will resend the same shape, and reconnecting would tear down every
other topic and hammer the service in a backoff loop over nothing the drop caused — survives
intact and is in fact strengthened: we now neither reconnect *nor* stop. Only G3's conclusion that
the failure is terminal changes, and its comment is rewritten to say so.

One existing doc becomes true rather than aspirational. `StreamEventWalk.NullableDecimal`'s remarks
already claim that a decimal refused under D38 means "the frame is dropped by the read loop's own
malformed-message handling rather than delivering a number the server did not send"
(`Internal/StreamEventWalk.cs:204-205`). There is no such handling today — the connection dies.
This is what that sentence has been describing.

### D-W20 · The drop gets its own counter and its own signal, not the buffer-overflow one

`MassiveTopicSubscription<T>.DroppedCount` and `MassiveStockStream.DropObserved` already exist and
already have the right shape. Reusing them was rejected.

D33 argues that "a second signal is strictly less surface for the same information", and this is
not the same information. A buffer overflow is the consumer's own backpressure: it is *their*
problem and they fix it by raising `TopicBufferCapacity` or doing less work in the loop, which is
exactly what `LogStreamHealth` tells them. A malformed field is Massive's wire being off-schema,
and there is nothing the consumer can do about it at all. Folding the two into one counter hands
that consumer advice that cannot work, which is issue #64's complaint — one cause made
indistinguishable from another — reproduced on purpose.

```csharp
// MassiveTopicSubscription<T>, beside DroppedCount
public long MalformedCount { get; }

// MassiveStockStream, beside DropObserved
public event Action<string, long, JsonException>? MalformedObserved;
```

The exception travels with the raise because of the trap named above: it is the only route the
improved message has out of the SDK once the failure is no longer terminal. Flat arguments follow
`DropObserved`'s own style rather than introducing a payload type for one event, at the cost of a
third argument and a three-argument `EventRaiser.Raise` overload — internal, so no public API entry.

Wiring follows drops exactly: `TopicSink<T>` raises an internal `EventMalformed`, `GetOrCreateSink`
subscribes it at the one place sinks are created, and `MassiveStockStream` throttles before raising
through `EventRaiser` so a sustained bad field cannot produce an unbounded notification stream.
The throttle keeps its **own** window, separate from `DropObserved`'s, so a burst of one cannot
mask the other.

`LogStreamHealth` gains a fourth bridge at `Warning`, tracking per-topic deltas the way it already
does for drops (F7). Its message says the wire is off-schema and names the field; it must not say
"raise `TopicBufferCapacity`", which is the misdirection this issue and #64 both exist to remove.

### D-W21 · `OutOfRange` echoes the offending token; `Expected` and `NotExact` do not

```
The number in StockAggregate.z (12.0) does not fit a 64-bit integer.
```

This reverses a standing contract in core. `JsonValueReader`'s class documentation says "No method
echoes the offending value into its message; the model and property name are enough to locate it,
and the response body is not ours to quote back" (`Serialization/JsonValueReader.cs:26-29`). That
paragraph is rewritten rather than left silently contradicted.

The reversal is confined to `OutOfRange`, and the confinement *is* the rule 11 argument.
`OutOfRange` is reachable only after its caller has already checked
`reader.TokenType == JsonTokenType.Number` — every call site does, and there is no other path to
it. The echoed bytes are therefore provably a JSON number, and an API key is not a JSON number. The
safety is structural, not a matter of care taken at each site.

The other two helpers keep their current messages, and the reasons differ:

- `NotExact` reads a `JsonTokenType.String` (D38's fractional-share family), so the "it is provably
  a number" proof does not hold there. The class of value it could quote back is unbounded.
- `Expected` names a token *type*, never a value, so it has nothing to echo in the first place.

The echoed text is capped at 32 bytes with an ellipsis. A JSON number token has no length limit, so
a pathological input could otherwise produce an exception message thousands of digits long.

This is a message-text change, not a signature change: `JsonValueReader` is already in
`PublicAPI.Shipped.txt` and none of its entries move.

### D39 · The row for `CLAUDE.md`

The three decisions above collapse to one row in the repository's decision table, next free id
**D39**, covering: the per-event drop and its counter, the field/frame boundary, why this is a
second signal rather than a reuse of `DroppedCount`, and why the echo is numbers-only.

## Scope

**`MassiveDotNet` (core)**

- `JsonValueReader.OutOfRange` renders the token text, capped; helper to read it from `ValueSpan`
  or `ValueSequence`. Class-level remarks rewritten per D-W21.

**`MassiveDotNet.WebSocket`**

- `TopicSink<T>.Write` catches `JsonException`, records it, raises `EventMalformed`, returns.
- `MassiveTopicSubscription<T>.MalformedCount` + internal `RecordMalformed`.
- `MassiveStockStream.MalformedObserved`, its throttle and window, wired in `GetOrCreateSink`.
- `EventRaiser.Raise<T1, T2, T3>`.
- G3's comment in `ReadLoopAsync` updated; `StreamEventWalk.NullableDecimal`'s remark left as-is,
  since it becomes accurate.
- Two entries in `PublicAPI.Unshipped.txt` (rule 14), XML docs on both (rule 10).

**`MassiveDotNet.Extensions.DependencyInjection`**

- `LogStreamHealth` subscribes `MalformedObserved`; one more `[LoggerMessage]` partial.

**`CHANGELOG.md`** — an `### Added` bullet under `## [Unreleased]`.

## Testing

Offline tier only. `FakeWebSocket.EnqueueText` feeds synthetic frames, so every property here is
testable without a key (rule 13).

**The property being protected**, and the test that has to exist: a frame carrying a malformed `AM`
event *and* a valid `T` event delivers the trade. A malformed event in one topic must not cost
another topic its data.

**Per-event drop.** `"z":12.0` — integral value, non-integer token — drops one event, the
connection survives, `MalformedCount` is 1 and `DroppedCount` is 0. A genuinely out-of-range `z`
does the same. A normal integral `z` still parses unchanged.

**The boundary.** A malformed `ev` still terminates the connection, pinning D-W19's field/frame
line so a later change cannot widen the catch without a test going red.

**The signal.** `MalformedObserved` carries the topic code, the running count, and an exception
whose message names the value; it is throttled to at most one raise per second; a throwing handler
does not take down the stream (the `EventRaiser` property, per F1).

**`JsonValueReader`, in the REST test project.** `OutOfRange` echoes the token and caps a long one;
`Expected` and `NotExact` do not echo. Watched failing first per D31 — a test asserting only "the
message is non-empty" would pass against the defect.

**Allocation.** The happy path must not regress: a `try` around `Write` costs nothing when nothing
throws. The existing ceilings stand unchanged and are the assertion.

## Non-goals

- **Retyping `AverageTradeSize`.** Issue #65 step 3, deferred by the issue itself until there is
  evidence about which rejection case occurs. `long` matches the vendor's published schema, and
  this work is what produces the evidence to revisit it.
- **Auditing `s`, `e`, `v`, `av` for the same strictness.** Once D-W19 lands they fail softly and
  D-W21's message identifies them, which is a better instrument than reading the converters.
- **`max_connections` handling.** Issue #64. Adjacent in theme — one disconnect cause made
  indistinguishable from another — but a connection-level status, not a field.
- **Widening any reader to accept `12.0` as an integer.** That is step 3's decision and needs the
  evidence this work produces. Nothing here changes what parses.
