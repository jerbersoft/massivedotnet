# A malformed field drops one event, not the connection — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop one unparseable field on one symbol killing a multiplexed stream connection, and put the value that was refused into the exception message so the cause can be read from a log.

**Architecture:** `TopicSink<T>.Write` catches `JsonException`, counts it on the subscription, and returns — the reader it was handed is a struct copy that `Dispatch` has already finished with, so the outer read loop resumes on the next event with no resync. The drop gets its own counter (`MalformedCount`) and its own throttled signal (`MalformedObserved`), carrying the exception, because once the failure is no longer terminal that signal is the only route the improved message has out of the SDK. In core, `JsonValueReader`'s range-failure message gains the offending token, capped at 32 bytes — confined to the one helper that is reachable only after the token has been proved a JSON number.

**Tech Stack:** .NET 10, C# 14, `System.Text.Json` (source-generated, no reflection), NodaTime, xUnit v3.

**Spec:** `docs/superpowers/specs/2026-09-21-malformed-stream-events-design.md`

**Issue:** #65. Read the *current* description — it was rewritten five minutes after filing and the rewrite reverses the original proposed fix.

## Global Constraints

Copied verbatim from `CLAUDE.md` and the spec. Every task's requirements implicitly include these.

- **Rule 3.** No reflection-based serialization anywhere in shipped code. `System.Text.Json` source generation only. Nothing in this plan introduces a serializer.
- **Rule 5.** Generated files (`*.g.cs`) are never hand-edited. Nothing in this plan touches generated code.
- **Rule 7.** `MassiveDotNet` (core) references no external package other than NodaTime and `System.Threading.RateLimiting`. Task 1 adds `using System.Buffers;` and `using System.Text;` — both BCL, no package.
- **Rule 8.** `Microsoft.Extensions.*` appears only in `MassiveDotNet.Extensions.DependencyInjection`. Task 5 is the only task that may name a logger.
- **Rule 9.** Builds are warning-free. `TreatWarningsAsErrors` is on. A declared-but-never-raised event is `CS0067` and fails the build.
- **Rule 10.** Every public member of a shipped library carries XML documentation. `internal` members here carry it too, matching `ITopicSink` and `EventRaiser`.
- **Rule 11.** **API keys are never logged, echoed in exception messages, or written to disk.** This plan deliberately adds an echo to one exception message. The safety argument is structural and is written down in Task 1; do not widen the echo to any other helper.
- **Rule 12.** No BCL `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, or `TimeSpan` may be **named** anywhere in the repository's source — not in a declaration, not in a typed local, not in a static call. Use `Duration` / `Instant`, and convert inline at a BCL call site (`Duration.FromSeconds(5).ToTimeSpan()`). `TemporalTypeTests` and `BannedSymbols.txt` both fail the build on one. Naming one in a *comment* is fine; the scan strips comments.
- **Rule 13.** CI runs entirely offline. Every test in this plan is in the offline tier and needs no key. Do not add a live test.
- **Rule 14.** Every public member of a shipped library has an entry in that project's `PublicAPI.Unshipped.txt`. Two entries are added, in Task 4. `RS0016` and `RS0017` are errors.
- **D31.** A guard nobody has seen fail is indistinguishable from a clean tree. Every test in this plan is watched failing for the right reason before the code that makes it pass is written.
- **D36.** Streaming converters are hand-written and every one walks its object through `StreamEventWalk`. Nothing here changes what parses.
- **Comments explain *why*, not *what*.** The comments in this plan's code blocks are part of the deliverable, not decoration — they carry the arguments a later reader needs in order to not undo this. Type them in.
- **Commits:** conventional prefixes (`feat:`, `fix:`, `test:`, `docs:`, `refactor:`). Every commit message ends with the trailer every commit in this repository carries:
  `Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB`
- **Never commit `.claude/`.** It is untracked and must stay that way. Stage files explicitly; never `git add -A`.
- **Branch:** `fix/malformed-stream-events`, off `master` at `f34976c`. Create it in Task 1 step 0. Do not commit to `master`. Approving this plan is the authorization to commit on that branch (`CLAUDE.md` otherwise says do not commit unless asked); pushing and opening a PR still needs a separate ask.

### The trap this plan is sequenced around

The two halves of issue #65 work against each other, and the issue does not say so.

Today the message reaches a consumer through `Faulted`, *because* the failure is terminal. Making it non-terminal removes that route. A fix that only stops the outage would therefore swallow the evidence along with the event, and issue #65's step 3 — whether `z` needs a wider read — would become permanently undecidable.

That is why `MalformedObserved` carries the `JsonException` (Task 4) and why the DI bridge interpolates its message (Task 5). **Do not drop the exception from either signature to simplify them.**

### Non-goals — do not do these

- **Do not retype `StockAggregate.AverageTradeSize`.** It stays `long`. Massive documents `z` as an `integer`; the type is not the defect, and retyping it is a breaking public API change made on a guess that contradicts the vendor. Issue #65 says so explicitly.
- **Do not widen any reader to accept `12.0` as an integer.** Nothing in this plan changes what parses.
- **Do not widen the catch past `sink.Write`.** A malformed `ev` stays terminal. Task 3 pins this.
- **Do not touch `max_connections` handling.** That is issue #64.
- **Do not edit `StreamEventWalk.NullableDecimal`'s remarks.** They already describe this behaviour; this change is what makes them true.

---

## File Structure

**Modified — shipped code:**

| File | Change | Task |
|---|---|---|
| `src/MassiveDotNet/Serialization/JsonValueReader.cs` | `OutOfRange` echoes the number token, capped at 32 bytes; class remarks rewritten. | 1 |
| `src/MassiveDotNet.WebSocket/MassiveTopicSubscription.cs` | `MalformedCount` + internal `RecordMalformed`. | 2 |
| `src/MassiveDotNet.WebSocket/Internal/TopicSink.cs` | `Write` catches `JsonException`; `EventMalformed` seam. | 2 |
| `src/MassiveDotNet.WebSocket/Internal/MassiveStreamConnection.cs` | G3's comment in `ReadLoopAsync` rewritten. Comment only. | 3 |
| `src/MassiveDotNet.WebSocket/Internal/EventRaiser.cs` | `Raise<T1, T2, T3>` overload. | 4 |
| `src/MassiveDotNet.WebSocket/MassiveStockStream.cs` | `MalformedObserved`, its own throttle window and lock, wiring in `GetOrCreateSink`. | 4 |
| `src/MassiveDotNet.WebSocket/PublicAPI.Unshipped.txt` | Two entries. | 4 |
| `src/MassiveDotNet.Extensions.DependencyInjection/MassiveStreamServiceCollectionExtensions.cs` | `LogStreamHealth` subscribes `MalformedObserved`; one `[LoggerMessage]` partial. | 5 |

**Modified — tests:**

| File | Change | Task |
|---|---|---|
| `tests/MassiveDotNet.Rest.Tests/JsonValueReaderTests.cs` | The echo, the cap, and the two helpers that must not echo. | 1 |
| `tests/MassiveDotNet.WebSocket.Tests/BackpressureTests.cs` | The sink-level drop, the count, and the exception's contents. | 2 |
| `tests/MassiveDotNet.WebSocket.Tests/DispatchTests.cs` | The cross-topic property; a remark on the existing frame-boundary test. | 3 |
| `tests/MassiveDotNet.WebSocket.Tests/StockStreamTests.cs` | The signal, its throttle, and handler isolation. | 4 |
| `tests/MassiveDotNet.Extensions.DependencyInjection.Tests/LogStreamHealthTests.cs` | The log bridge, its per-topic delta, and the sentinel-key assertion. | 5 |

**Modified — documentation:**

| File | Change | Task |
|---|---|---|
| `CHANGELOG.md` | `### Added` and `### Fixed` under `## [Unreleased]`. | 6 |
| `CLAUDE.md` | Decision row **D39**. | 6 |

**No files are created.** Every change lands in a file that already exists.

---

## Task 1: Core — the range failure names the value it refused

**Files:**
- Modify: `src/MassiveDotNet/Serialization/JsonValueReader.cs` — class remarks at lines 25–29, the three `OutOfRange` call sites at lines 83, 113, 138, and the helper at lines 248–249
- Test: `tests/MassiveDotNet.Rest.Tests/JsonValueReaderTests.cs`

`JsonValueReader` is core, shared with REST, and already in `PublicAPI.Shipped.txt`. This is a **message-text change only** — no signature on the public surface moves, so rule 14 is untouched here.

The core test project is `MassiveDotNet.Rest.Tests`. That is not a mistake: core's serialization is tested from there, and `JsonValueReaderTests.cs` already exists.

**Interfaces:**
- Consumes: nothing from earlier tasks. This is the first task.
- Produces: `JsonValueReader.ReadInt64` / `ReadInt32` / `ReadDouble` keep their exact public signatures, `static X (ref Utf8JsonReader reader, string model, string property)`. Only their failure message changes, to the form
  `The number in {model}.{property} ({token}) does not fit {expected}.`
  Task 2's and Task 5's assertions read that format.

- [ ] **Step 0: Branch**

```bash
git switch -c fix/malformed-stream-events master
git log --oneline -1
```

Expected: `f34976c docs: publish a DocFX site from docs/, ported from fmpdotnet`

- [ ] **Step 1: Write the failing tests**

Append these to `tests/MassiveDotNet.Rest.Tests/JsonValueReaderTests.cs`, inside the class, after `ReadsANullableInt64`. They use the file's existing `Model`, `Property` and `Rejects` helpers — `Rejects` returns the `JsonException` it caught.

```csharp
    // Issue #65. A production log said only "The number in StockAggregate.z does not fit a 64-bit
    // integer". Utf8JsonReader.TryGetInt64 returns false for three unrelated reasons and that
    // message distinguishes none of them: a non-integral value (the vendor violating its own
    // schema), an integral value written in a non-integer JSON FORM -- 12.0, 1e3, both valid JSON
    // numbers, both whole, both refused because the token is not an integer token -- and a value
    // genuinely past long's range. Three causes, three different fixes, and the value itself is the
    // only thing that tells them apart (D-W21).
    [Theory]
    [InlineData("12.0")]
    [InlineData("1e3")]
    [InlineData("123.45")]
    [InlineData("9223372036854775808")]
    public void AnOutOfRangeInt64NamesTheValueItRefused(string json)
    {
        JsonException thrown = Rejects(
            json, (ref Utf8JsonReader reader) => JsonValueReader.ReadInt64(ref reader, Model, Property));

        Assert.Contains(json, thrown.Message, StringComparison.Ordinal);
        Assert.Contains(Model, thrown.Message, StringComparison.Ordinal);
        Assert.Contains(Property, thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOutOfRangeInt32NamesTheValueItRefused()
    {
        JsonException thrown = Rejects(
            "2147483648", (ref Utf8JsonReader reader) => JsonValueReader.ReadInt32(ref reader, Model, Property));

        Assert.Contains("2147483648", thrown.Message, StringComparison.Ordinal);
    }

    // A JSON number token has no length limit, so an unbounded echo would put a value of any size
    // into an exception message -- and from there, through the DI package's log bridge, into a log
    // line. Capped at 32 bytes with a trailing ellipsis.
    [Fact]
    public void ALongNumberIsEchoedOnlyUpToTheCap()
    {
        JsonException thrown = Rejects(
            new string('9', 120),
            (ref Utf8JsonReader reader) => JsonValueReader.ReadInt64(ref reader, Model, Property));

        Assert.Contains(new string('9', 32) + "...", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('9', 33), thrown.Message, StringComparison.Ordinal);
    }

    // The other two failure messages deliberately do NOT echo, for two different reasons, and both
    // are rule 11's argument rather than an oversight. The token-type failure names a token TYPE
    // and has no value in hand at all. The decimal round-trip failure reads a String token, where
    // "the bytes are provably a JSON number" does not hold and the class of value it could quote
    // back is unbounded -- which is exactly the class an API key belongs to (D-W21).
    [Fact]
    public void AWrongTokenTypeDoesNotEchoTheValue()
    {
        JsonException thrown = Rejects(
            "\"secret-value\"",
            (ref Utf8JsonReader reader) => JsonValueReader.ReadInt64(ref reader, Model, Property));

        Assert.DoesNotContain("secret-value", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedDecimalDoesNotEchoTheValue()
    {
        JsonException thrown = Rejects(
            "\"1e5\"",
            (ref Utf8JsonReader reader) => JsonValueReader.ReadDecimal(ref reader, Model, Property));

        Assert.DoesNotContain("1e5", thrown.Message, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~JsonValueReaderTests" -v minimal
```

Expected: `AnOutOfRangeInt64NamesTheValueItRefused` (×4), `AnOutOfRangeInt32NamesTheValueItRefused` and `ALongNumberIsEchoedOnlyUpToTheCap` **FAIL** — `Assert.Contains() Failure`, because today's message is `The number in Row.field does not fit a 64-bit integer.` with no value in it.

`AWrongTokenTypeDoesNotEchoTheValue` and `ARefusedDecimalDoesNotEchoTheValue` **PASS** already. That is correct and is the point: they are the guard that stops the next person generalising the echo across all three helpers.

**This is the D31 moment for this task.** A test asserting only "the message is non-empty" would pass against the defect. Confirm these three fail for the stated reason before continuing.

- [ ] **Step 3: Add the usings**

`JsonValueReader.cs` line 1 currently reads `using System.Globalization;`. Replace the using block at the top of the file with:

```csharp
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
```

`System.Buffers` is for `ReadOnlySequence<byte>.CopyTo`; `System.Text` is for `Encoding.UTF8`. Both are BCL, so rule 7 is untouched.

- [ ] **Step 4: Rewrite the class-level remarks**

Replace the third `<para>` of the class remarks (`JsonValueReader.cs:25-29`) — the paragraph beginning "Every method leaves the reader on the value it read" and ending "the response body is not ours to quote back." — with these three paragraphs:

```csharp
/// <para>
/// Every method leaves the reader on the value it read, so the caller's next
/// <see cref="Utf8JsonReader.Read"/> advances to the following property name.
/// </para>
/// <para>
/// Exactly one failure message echoes the value that caused it: the range failure raised when a
/// token is a number the target type cannot hold. This paragraph used to say that no method echoes
/// anything, and issue #65 is what reversed it. A streaming <c>StockAggregate.z</c> was refused in
/// production and the log said only that the number did not fit -- which cannot distinguish a
/// non-integral value from an integral one written as <c>12.0</c> from one genuinely past the
/// range. Those are three different causes with three different fixes, and the value is the only
/// thing that tells them apart.
/// </para>
/// <para>
/// The echo is confined to that one helper, and the confinement IS the rule 11 argument: it is
/// reachable only after its caller has checked
/// <c>reader.TokenType == JsonTokenType.Number</c>, which every caller does and no other path
/// reaches it, so the bytes it quotes are provably a JSON number -- and an API key is not a JSON
/// number. The safety is structural rather than a matter of care taken at each site. The
/// wrong-token-type failure names a token type and has no value in hand; the decimal round-trip
/// failure reads a <see cref="JsonTokenType.String"/>, where that proof does not hold and the class
/// of value it could quote back is unbounded. Neither echoes, and neither is to be changed to
/// (D-W21).
/// </para>
```

- [ ] **Step 5: Change `OutOfRange` and add the echo helper**

Replace the `OutOfRange` helper at `JsonValueReader.cs:248-249`:

```csharp
    private static JsonException OutOfRange(string expected, string model, string property) =>
        new($"The number in {model}.{property} does not fit {expected}.");
```

with:

```csharp
    /// <summary>
    /// How much of an offending number reaches a range failure's message. A JSON number token has
    /// no length limit, so without a cap a pathological value could produce an exception message
    /// thousands of digits long -- and the DI package bridges these messages to a logger.
    /// </summary>
    private const int MaxEchoedTokenLength = 32;

    private static JsonException OutOfRange(
        string expected, ref Utf8JsonReader reader, string model, string property) =>
        new($"The number in {model}.{property} ({EchoNumber(ref reader)}) does not fit {expected}.");

    /// <summary>Renders the number token the reader is on, capped, for a failure message.</summary>
    /// <param name="reader">A reader positioned on a <see cref="JsonTokenType.Number"/> token.</param>
    /// <returns>The token's text, with a trailing ellipsis when it was longer than the cap.</returns>
    /// <remarks>
    /// Called only from <see cref="OutOfRange"/>, whose every caller has already refused a
    /// non-number token, so every byte here comes from the JSON number grammar and is therefore
    /// ASCII. That is what makes both the decode and the truncation safe: there is no multi-byte
    /// character for a cut at <see cref="MaxEchoedTokenLength"/> to split in half. Do not call this
    /// from anywhere that has not made that check (D-W21, rule 11).
    /// </remarks>
    private static string EchoNumber(ref Utf8JsonReader reader)
    {
        long length = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
        int copied = (int)Math.Min(length, MaxEchoedTokenLength);
        Span<byte> token = stackalloc byte[MaxEchoedTokenLength];

        if (reader.HasValueSequence)
        {
            reader.ValueSequence.Slice(0, copied).CopyTo(token);
        }
        else
        {
            reader.ValueSpan[..copied].CopyTo(token);
        }

        string text = Encoding.UTF8.GetString(token[..copied]);

        return length > MaxEchoedTokenLength ? text + "..." : text;
    }
```

- [ ] **Step 6: Update the three call sites**

All three are one-line ternaries. Add `ref reader` as the second argument.

`JsonValueReader.cs:83`:

```csharp
        return reader.TryGetInt32(out int value)
            ? value
            : throw OutOfRange("a 32-bit integer", ref reader, model, property);
```

`JsonValueReader.cs:113`:

```csharp
        return reader.TryGetInt64(out long value)
            ? value
            : throw OutOfRange("a 64-bit integer", ref reader, model, property);
```

`JsonValueReader.cs:138`:

```csharp
        return reader.TryGetDouble(out double value)
            ? value
            : throw OutOfRange("a double", ref reader, model, property);
```

- [ ] **Step 7: Run the tests to verify they pass**

```bash
dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~JsonValueReaderTests" -v minimal
```

Expected: PASS, all of them.

- [ ] **Step 8: Run the whole Rest tier, warning-free**

```bash
dotnet build MassiveDotNet.slnx
dotnet test tests/MassiveDotNet.Rest.Tests
```

Expected: build with **zero** warnings (`TreatWarningsAsErrors` makes one an error anyway), and every Rest test passing. A message-format change can break a test elsewhere that asserted on the old text; if one goes red, read it — it is a real finding, not noise.

- [ ] **Step 9: Commit**

```bash
git add src/MassiveDotNet/Serialization/JsonValueReader.cs tests/MassiveDotNet.Rest.Tests/JsonValueReaderTests.cs
git commit -m "$(cat <<'EOF'
fix: name the value in a numeric range failure's message

Utf8JsonReader.TryGetInt64 returns false for three unrelated reasons -- a non-integral value, an
integral value written in a non-integer token form such as 12.0 or 1e3, and a value genuinely past
the range -- and "The number in StockAggregate.z does not fit a 64-bit integer" distinguishes none
of them. Issue #65 saw that message four times in ten minutes in production and could not tell which
had happened.

The echo is confined to OutOfRange, and the confinement is the rule 11 argument: that helper is
reachable only after its caller has checked TokenType == Number, so the bytes it quotes are provably
a JSON number, and an API key is not a JSON number. Expected names a token type and has nothing to
echo; NotExact reads a String token, where the proof does not hold. Two tests pin that both of those
still do not echo. Capped at 32 bytes, because a JSON number token has no length limit.

The class remarks said the opposite and are rewritten rather than left silently contradicted.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB
EOF
)"
```

---

## Task 2: The sink drops one event instead of throwing

**Files:**
- Modify: `src/MassiveDotNet.WebSocket/MassiveTopicSubscription.cs:31` — beside `RecordDrop`
- Modify: `src/MassiveDotNet.WebSocket/Internal/TopicSink.cs` — the `ItemDropped` event and `Write`
- Test: `tests/MassiveDotNet.WebSocket.Tests/BackpressureTests.cs`

This is the task the outage fix actually lives in. Everything after it is reporting.

**Why the catch goes here and not in `Dispatch`.** `MassiveStreamConnection.Dispatch` does this (`MassiveStreamConnection.cs:889-908`):

```csharp
Utf8JsonReader start = reader;             // a struct copy is a free bookmark
string? code = ReadEventCode(ref reader);  // advances `reader` to this object's END token
...
Utf8JsonReader replay = start;             // a second copy
sink.Write(ref replay);                    // the converter throws in here
```

`replay` is a copy. When the converter throws, the outer `reader` is untouched and is already parked on the end of the object that failed, so the `while` loop takes the next event with no resync and no guesswork. `TopicSink<T>` is also the type that knows `T` and owns the subscription holding the count. `Dispatch` and `ITopicSink` are unchanged by this task.

**Interfaces:**
- Consumes: Task 1's message format. `JsonValueReader.ReadInt64` now throws `JsonException` whose message contains the refused token.
- Produces:
  - `public long MassiveTopicSubscription<T>.MalformedCount { get; }`
  - `internal void MassiveTopicSubscription<T>.RecordMalformed()`
  - `internal event Action<JsonException>? TopicSink<T>.EventMalformed`
  Task 4 subscribes `EventMalformed` and reads `MalformedCount`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/MassiveDotNet.WebSocket.Tests/BackpressureTests.cs`. The file already has `using System.Text;`, `using System.Text.Json;`, `using MassiveDotNet.WebSocket.Events;`, `using MassiveDotNet.WebSocket.Internal;` and `using Xunit;`. Add this helper beside the existing `Feed`:

```csharp
    private static void FeedAggregate(TopicSink<StockAggregate> sink, string json)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        reader.Read();
        sink.Write(ref reader);
    }

    private static TopicSink<StockAggregate> CreateAggregateSink(int capacity) =>
        new("AM", capacity, new StockAggregateConverter(new TickerPool(16), "AM"));
```

Then these two tests:

```csharp
    // Issue #65 / D-W19. Before this, a value the converter refused threw out of Write, escaped
    // Dispatch, missed the read loop's reconnect filter (G3, deliberately), and ended the entire
    // connection -- taking every OTHER topic sharing that socket down with it. One symbol's bad
    // field, on a topic the consumer may not even have subscribed to, killed the trade feed. Now
    // the one event is dropped and counted, and the next one parses.
    //
    // Both refusal cases the production log could not tell apart, because the drop must not depend
    // on which one it was. "z":12.0 is the one easiest to overlook -- a valid JSON number, a whole
    // value, refused anyway because the token is not an integer token -- and the second is a value
    // genuinely past long's range.
    [Theory]
    [InlineData("12.0")]
    [InlineData("9223372036854775808")]
    public async Task AnEventTheConverterRefusesIsDroppedAndCountedRatherThanThrown(string badAverageTradeSize)
    {
        TopicSink<StockAggregate> sink = CreateAggregateSink(capacity: 8);

        FeedAggregate(sink, $$"""{"ev":"AM","sym":"MSFT","v":1,"z":{{badAverageTradeSize}},"s":1,"e":2}""");
        FeedAggregate(sink, """{"ev":"AM","sym":"MSFT","v":1,"z":7,"s":3,"e":4}""");
        sink.Complete();

        List<long> sizes = [];
        await foreach (StockAggregate bar in sink.Subscription.WithCancellation(TestContext.Current.CancellationToken))
        {
            sizes.Add(bar.AverageTradeSize);
        }

        // The malformed bar is gone and the well-formed one that followed it arrived.
        Assert.Equal([7L], sizes);

        // Its own counter, not DroppedCount (D-W20). A buffer overflow is the consumer's own
        // backpressure and they fix it by raising TopicBufferCapacity; this is Massive's wire being
        // off-schema and there is nothing they can do about it. Folding the two together would hand
        // a consumer advice that cannot work.
        Assert.Equal(1, sink.Subscription.MalformedCount);
        Assert.Equal(0, sink.Subscription.DroppedCount);
    }

    // The evidence route. A malformed event was terminal until issue #65, so its message reached a
    // consumer through Faulted; now that the connection survives, this seam is the ONLY way the
    // refused value leaves the SDK. Without it, the fix for the outage would swallow the evidence
    // for the fix that is still outstanding (whether `z` needs a wider read at all).
    [Fact]
    public void ARefusedEventRaisesEventMalformedCarryingTheValue()
    {
        TopicSink<StockAggregate> sink = CreateAggregateSink(capacity: 8);

        JsonException? observed = null;
        sink.EventMalformed += error => observed = error;

        FeedAggregate(sink, """{"ev":"AM","sym":"MSFT","v":1,"z":12.0,"s":1,"e":2}""");

        // The `!` is not redundant: `observed` is assigned inside a lambda, which the nullable
        // analyzer cannot follow across the raise, so Assert.NotNull does not narrow it here.
        Assert.NotNull(observed);
        Assert.Contains("StockAggregate.z", observed!.Message, StringComparison.Ordinal);
        Assert.Contains("12.0", observed!.Message, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~BackpressureTests" -v minimal
```

Expected: **compile errors** — `MalformedCount` and `EventMalformed` do not exist. That is a legitimate red for a TDD step; it is the "function not defined" failure. Do not skip ahead on the strength of it: after Step 3 and Step 4 the file compiles, and Step 5 re-runs it to see a genuine assertion failure before the `Write` change lands.

- [ ] **Step 3: Add `MalformedCount` to the subscription**

In `src/MassiveDotNet.WebSocket/MassiveTopicSubscription.cs`, add `private long _malformed;` beside `private long _dropped;`, and add this immediately after `internal void RecordDrop()`:

```csharp
    /// <summary>
    /// How many events were discarded because the wire sent a value this topic's converter would
    /// not accept.
    /// </summary>
    /// <remarks>
    /// Monotonic, and exact rather than estimated. Deliberately separate from
    /// <see cref="DroppedCount"/>, because the two have opposite remedies: a drop is the consumer's
    /// own backpressure and is fixed by raising
    /// <see cref="MassiveStreamOptions.TopicBufferCapacity"/> or doing less work in the loop, while
    /// this is Massive's wire disagreeing with the SDK's schema and there is nothing the consumer
    /// can do about it at all. One counter for both would hand a consumer advice that cannot work
    /// (D-W20). Subscribe to <see cref="MassiveStockStream.MalformedObserved"/> for the exception
    /// naming which field and which value.
    /// </remarks>
    public long MalformedCount => Interlocked.Read(ref _malformed);

    internal void RecordMalformed() => Interlocked.Increment(ref _malformed);
```

- [ ] **Step 4: Catch in `TopicSink<T>.Write`**

In `src/MassiveDotNet.WebSocket/Internal/TopicSink.cs`, add after the existing `ItemDropped` event declaration:

```csharp
    /// <summary>Raised when the converter refused an event and it was dropped.</summary>
    internal event Action<JsonException>? EventMalformed;
```

Then replace the whole body of `Write` with:

```csharp
    public void Write(ref Utf8JsonReader reader)
    {
        T value;

        try
        {
            // The null-forgiving operator matches JsonConverter{T}.Read's own signature, T? Read(...):
            // annotated that way because T is unconstrained and a converter for a reference-typed model
            // COULD return null, but no streaming converter this SDK hands to a TopicSink ever does --
            // each one either returns a genuine value or throws.
            value = _converter.Read(ref reader, typeof(T), EmptyOptions)!;
        }
        catch (JsonException malformed)
        {
            // D-W19, issue #65. The reader handed in here is Dispatch's own `replay` -- a struct
            // COPY of the frame reader, and the real one is already parked on this object's end
            // token -- so whatever position the converter left THIS reader in cannot affect the
            // caller, and the dispatch loop takes the next event with no resync. That property is
            // the whole reason a per-event drop is safe, and it is why the catch is here rather
            // than one level out: widening it to cover ReadEventCode would swallow a failure that
            // strands the REAL reader mid-object, with no topic to attribute the loss to. This
            // drops a bad FIELD. A bad FRAME stays terminal.
            Subscription.RecordMalformed();

            // Two notifications from one place, exactly as the drop callback above does it:
            // RecordMalformed is what makes Subscription.MalformedCount exact, and this is the
            // separate seam MassiveStockStream subscribes to. The exception travels with it
            // because it is now the ONLY route the refused value has out of the SDK -- this used
            // to be terminal, so the message reached a consumer through Faulted.
            EventMalformed?.Invoke(malformed);

            return;
        }

        // TryWrite, never WriteAsync. Every topic shares one read loop: a writer that waits stalls
        // the socket, closes the receive window, and gets the connection dropped for being a slow
        // consumer -- taking down the topics that were keeping up (D-W4). With DropOldest this
        // always succeeds, and the eviction is counted rather than hidden.
        _channel.Writer.TryWrite(value);
    }
```

`value` is definitely assigned at the point of `TryWrite`: the catch clause's end-point is unreachable because it `return`s, so reaching past the `try` statement means the `try` block completed.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~BackpressureTests" -v minimal
```

Expected: PASS.

**Then watch it fail for the right reason (D31).** Comment out the `catch` block's `return` and replace it with `throw;`, re-run, and confirm `AnEventTheConverterRefusesIsDroppedAndCountedRatherThanThrown` goes red with a `JsonException` escaping `FeedAggregate` — not with a count mismatch. Restore the `return`.

- [ ] **Step 6: Run the whole WebSocket tier**

```bash
dotnet build MassiveDotNet.slnx
dotnet test tests/MassiveDotNet.WebSocket.Tests
```

Expected: build warning-free; every test passing. `AllocationTests` is in this project and its ceilings must be untouched — a `try` around a call that does not throw costs nothing. If an allocation ceiling goes red here, stop: that is a real regression and the ceiling is not to be raised to accommodate it.

- [ ] **Step 7: Commit**

```bash
git add src/MassiveDotNet.WebSocket/MassiveTopicSubscription.cs src/MassiveDotNet.WebSocket/Internal/TopicSink.cs tests/MassiveDotNet.WebSocket.Tests/BackpressureTests.cs
git commit -m "$(cat <<'EOF'
fix: drop one malformed event instead of the whole connection

A `z` the reader would not accept threw out of a converter, escaped the read loop, and ended a
connection that multiplexes trades, quotes and every aggregate topic -- observed four times in ten
minutes in production, each one forcing a full reconnect (issue #65).

The catch goes inside TopicSink<T>.Write because Dispatch hands it a struct COPY of the frame
reader, and the real one is already parked on the failed object's end token: the loop takes the next
event with no resync. It also knows T, and owns the subscription holding the count. Dispatch and
ITopicSink are unchanged.

MalformedCount is its own counter rather than a reuse of DroppedCount (D-W20). A buffer overflow is
the consumer's backpressure and they fix it by raising TopicBufferCapacity; an off-schema field is
Massive's wire and they cannot. One counter for both would hand them advice that cannot work.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB
EOF
)"
```

---

## Task 3: The property being protected, and the boundary

**Files:**
- Modify: `src/MassiveDotNet.WebSocket/Internal/MassiveStreamConnection.cs:255-262` — the G3 comment above the read loop's reconnect filter. **Comment only; the filter itself does not change.**
- Test: `tests/MassiveDotNet.WebSocket.Tests/DispatchTests.cs`

Task 2 made the drop work. This task pins the two properties that make it *correct*, at the connection level where a consumer actually experiences them, and rewrites the comment that currently tells a reader the opposite.

**Interfaces:**
- Consumes: `MassiveTopicSubscription<T>.MalformedCount` from Task 2.
- Produces: nothing new. Behaviour only.

- [ ] **Step 1: Write the failing test**

Add to `tests/MassiveDotNet.WebSocket.Tests/DispatchTests.cs`. The file already has every using this needs and already has the `ConnectAsync(FakeWebSocket)` and `SubscribeAfterSendAsync` helpers.

```csharp
    // Issue #65 / D-W19: the property this whole change exists to protect, stated at the level a
    // consumer feels it. The connection is multiplexed -- trades, quotes and both aggregate windows
    // share one socket -- so one symbol's unparseable `z` on a topic the consumer may not even have
    // subscribed to used to take the trade feed down with it, and everything after it on every
    // topic with it.
    //
    // The malformed AM event comes FIRST in the frame on purpose: the trade only arrives if the
    // dispatch loop resumed correctly from the failed object's end token.
    [Fact]
    public async Task AMalformedEventInOneTopicDoesNotCostAnotherTopicItsData()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        TopicSink<StockAggregate> bars = new(
            "AM", capacity: 8, new StockAggregateConverter(new TickerPool(16), "AM"));
        TopicSink<StockTrade> trades = new("T", capacity: 8, new StockTradeConverter(new TickerPool(16)));
        connection.AddSink(bars);
        connection.AddSink(trades);

        socket.EnqueueText(
            """[{"ev":"AM","sym":"MSFT","v":1,"z":12.0,"s":1,"e":2},{"ev":"T","sym":"MSFT","i":"1","p":1,"s":1,"t":1,"q":1}]""");

        // A widening subscribe, once acknowledged, guarantees the frame above has already been
        // dispatched -- the same causality barrier every other test in this file uses.
        Task barrier = await SubscribeAfterSendAsync(
            connection,
            socket,
            "T",
            ["BARRIER"],
            """[{"ev":"status","status":"success","message":"subscribed to: T.BARRIER"}]""");
        await barrier;

        trades.Complete();

        List<string> tickers = [];
        await foreach (StockTrade trade in trades.Subscription.WithCancellation(TestContext.Current.CancellationToken))
        {
            tickers.Add(trade.Ticker);
        }

        Assert.Equal(["MSFT"], tickers);
        Assert.Equal(1, bars.Subscription.MalformedCount);

        // And the connection is still alive. Before this change the read loop's task was faulted by
        // now and every sink had been completed.
        Assert.False(connection.ReadLoopTask.IsCompleted);
    }
```

- [ ] **Step 2: Run it to verify it passes, then prove it would have caught the defect**

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~AMalformedEventInOneTopicDoesNotCostAnotherTopicItsData" -v minimal
```

Expected: PASS — Task 2 already made it true.

**D31 requires more than that.** A test written after its fix has never been seen to fail. Temporarily revert `TopicSink<T>.Write`'s catch to `throw;`, re-run, and confirm this test goes red — the trade never arrives and `ReadLoopTask` is faulted. Restore the catch and re-run to green.

- [ ] **Step 3: Add a remark to the existing frame-boundary test**

`DispatchTests.AFrameWithAMalformedEventCodeFaultsTheReadLoop` already exists and already asserts the boundary. What it does not say is that the boundary is now a deliberate line rather than an absence of handling. Replace the comment directly above it:

```csharp
    // The connection-level shape of the same requirement: a malformed ev surfaces as a
    // JsonException at the point a real frame is dispatched, faulting the read loop's task the same
    // way every other unrecoverable frame does (see ReadLoopTests) -- it is not swallowed.
    //
    // Since issue #65 this is also the FIELD/FRAME boundary, and the reason for it. A malformed
    // field is caught in TopicSink<T>.Write and drops one event, because the reader that failed is
    // a copy and the real one is already parked on the object's end token. A malformed `ev` is
    // different in kind: it throws from ReadEventCode, which is driving the REAL reader, so it
    // strands that reader mid-object with no safe point to resume from -- and there is no topic to
    // attribute the loss to either, because the code is what names the topic. Widening the catch to
    // cover it turns this test red, which is the point of it (D-W19).
```

- [ ] **Step 4: Rewrite the G3 comment in the read loop**

In `src/MassiveDotNet.WebSocket/Internal/MassiveStreamConnection.cs`, replace the comment block immediately above `catch (Exception error) when (error is WebSocketException or MassiveStreamException)` (lines 255–262) with:

```csharp
                // Only WebSocketException and MassiveStreamException are read-loop faults treated as
                // reconnectable -- an unexpected close (D-W7). A JsonException is deliberately NOT one
                // of them, and that argument is unchanged: the socket is healthy, the server will
                // resend the same shape on the next message, and reconnecting to "fix" a parse failure
                // would tear down every other topic's live data and hammer the service in a backoff
                // loop over nothing the drop caused -- the same posture D-W6 refuses for a rejected
                // key. Do not widen this filter to catch it (G3, Task 11 review).
                //
                // What changed with issue #65 is only G3's other half. A JsonException from a
                // FIELD no longer reaches here at all: TopicSink<T>.Write catches it, drops the one
                // event, counts it, and the loop reads on (D-W19). So the SDK now neither reconnects
                // NOR stops over a bad field, which strengthens the argument above rather than
                // weakening it. A JsonException from a malformed `ev` still arrives here, still
                // misses this filter, and is still terminal -- ReadEventCode drives the real reader,
                // so a throw there strands it mid-object with no topic to attribute the loss to.
```

- [ ] **Step 5: Run the whole WebSocket tier**

```bash
dotnet build MassiveDotNet.slnx
dotnet test tests/MassiveDotNet.WebSocket.Tests
```

Expected: build warning-free, every test passing, including `AFrameWithAMalformedEventCodeFaultsTheReadLoop` unchanged.

- [ ] **Step 6: Commit**

```bash
git add src/MassiveDotNet.WebSocket/Internal/MassiveStreamConnection.cs tests/MassiveDotNet.WebSocket.Tests/DispatchTests.cs
git commit -m "$(cat <<'EOF'
test: pin that a malformed event costs only its own topic

The connection is multiplexed, so the property worth asserting is not "the loop survives" but "the
trade in the same frame still arrives". One symbol's unparseable `z`, on a topic the consumer may
not even have subscribed to, used to take the trade feed with it.

Also draws the field/frame line explicitly. A malformed `ev` stays terminal: it throws from
ReadEventCode, which drives the REAL reader rather than Dispatch's copy, so it strands that reader
mid-object -- and there is no topic to attribute the loss to, because the code is what names the
topic. AFrameWithAMalformedEventCodeFaultsTheReadLoop is now that boundary's guard, and says so.

G3's comment said a parse failure is terminal. Its reasoning -- do not reconnect over one -- is
untouched and in fact strengthened, since the SDK now neither reconnects nor stops. Only the
conclusion moved, so the comment is rewritten rather than left contradicting the code under it.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB
EOF
)"
```

---

## Task 4: The consumer-facing signal

**Files:**
- Modify: `src/MassiveDotNet.WebSocket/Internal/EventRaiser.cs` — a `Raise<T1, T2, T3>` overload
- Modify: `src/MassiveDotNet.WebSocket/MassiveStockStream.cs` — event, window, lock, handler, wiring
- Modify: `src/MassiveDotNet.WebSocket/PublicAPI.Unshipped.txt` — two entries
- Test: `tests/MassiveDotNet.WebSocket.Tests/StockStreamTests.cs`

**Interfaces:**
- Consumes: `TopicSink<T>.EventMalformed` and `MassiveTopicSubscription<T>.MalformedCount` from Task 2.
- Produces:
  - `public event Action<string, long, JsonException>? MassiveStockStream.MalformedObserved` — topic wire code, that topic's running malformed count, the exception.
  - `internal static void EventRaiser.Raise<T1, T2, T3>(Action<T1, T2, T3>? handlers, T1 argument1, T2 argument2, T3 argument3)`
  Task 5 subscribes `MalformedObserved`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/MassiveDotNet.WebSocket.Tests/StockStreamTests.cs`, beside `DropObservedIsThrottledToAtMostOncePerSecond`. The file already has `using NodaTime;`, `using NodaTime.Testing;` and the two `ConnectAsync` overloads; add `using System.Text.Json;` to the top of the file for `JsonException`.

These use the file's existing `SubscribeTradesAsync(stream, socket, ticker, ct)` helper as a causality barrier. Add the minute-aggregate equivalent beside it if the file does not already have one:

```csharp
    private static async Task<MassiveTopicSubscription<StockAggregate>> SubscribeMinuteAggregatesAsync(
        MassiveStockStream stream, FakeWebSocket socket, string ticker, CancellationToken cancellationToken)
    {
        TaskCompletionSource sent = socket.SentSignal;
        Task<MassiveTopicSubscription<StockAggregate>> subscribe = stream.SubscribeMinuteAggregatesAsync([ticker], cancellationToken);

        await sent.Task.WaitAsync(cancellationToken);
        socket.EnqueueText($$"""[{"ev":"status","status":"success","message":"subscribed to: AM.{{ticker}}"}]""");

        return await subscribe;
    }
```

```csharp
    // Issue #65 / D-W20. The exception travels with the raise, and that is not decoration: a
    // malformed event was terminal until this change, so its message reached a consumer through
    // Faulted. Now that the connection survives, this event is the only route the refused value has
    // out of the SDK -- and that value is the only evidence about which of TryGetInt64's three
    // rejection cases actually occurs on `z`, which is the question issue #65 defers to a later
    // decision and this work exists to make answerable.
    [Fact]
    public async Task MalformedObservedCarriesTheTopicTheCountAndTheRefusedValue()
    {
        FakeClock clock = new(Instant.FromUnixTimeSeconds(0));
        MassiveStreamOptions options = new() { ApiKey = "k" };

        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync(options, clock);
        await using MassiveStockStream _ = stream;

        await SubscribeMinuteAggregatesAsync(stream, socket, "MSFT", TestContext.Current.CancellationToken);

        string? topicCode = null;
        long count = 0;
        JsonException? error = null;

        stream.MalformedObserved += (topic, malformedCount, exception) =>
        {
            topicCode = topic;
            count = malformedCount;
            error = exception;
        };

        socket.EnqueueText("""[{"ev":"AM","sym":"MSFT","v":1,"z":12.0,"s":1,"e":2}]""");

        await SubscribeMinuteAggregatesAsync(stream, socket, "AAPL", TestContext.Current.CancellationToken);

        Assert.Equal("AM", topicCode);
        Assert.Equal(1, count);
        // The `!` is not redundant: `error` is assigned inside a lambda, so Assert.NotNull does not
        // narrow it for the analyzer here.
        Assert.NotNull(error);
        Assert.Contains("StockAggregate.z", error!.Message, StringComparison.Ordinal);
        Assert.Contains("12.0", error!.Message, StringComparison.Ordinal);
    }

    // Its own window, deliberately not shared with DropObserved's (D-W20). A sustained off-schema
    // field would otherwise produce one raise -- and, through the DI bridge, one log line -- per
    // event; and a burst of buffer overflows must not be able to throttle this into silence, or
    // vice versa, because they are different problems with opposite remedies.
    //
    // Edge-triggered, exactly like DropObserved's: the FIRST qualifying event in a fresh window
    // raises and every later one in that window is discarded outright, so the count carried is the
    // running total at that first event, not the total once the burst ends.
    [Fact]
    public async Task MalformedObservedIsThrottledToAtMostOncePerSecond()
    {
        FakeClock clock = new(Instant.FromUnixTimeSeconds(0));
        MassiveStreamOptions options = new() { ApiKey = "k" };

        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync(options, clock);
        await using MassiveStockStream _ = stream;

        MassiveTopicSubscription<StockAggregate> bars =
            await SubscribeMinuteAggregatesAsync(stream, socket, "MSFT", TestContext.Current.CancellationToken);

        int observed = 0;
        long lastCount = 0;

        stream.MalformedObserved += (_, malformedCount, _) =>
        {
            Interlocked.Increment(ref observed);
            Interlocked.Exchange(ref lastCount, malformedCount);
        };

        socket.EnqueueText(
            """[{"ev":"AM","sym":"MSFT","v":1,"z":12.0,"s":1,"e":2},{"ev":"AM","sym":"MSFT","v":1,"z":13.0,"s":3,"e":4}]""");

        await SubscribeMinuteAggregatesAsync(stream, socket, "AAPL", TestContext.Current.CancellationToken);

        // Both were refused, so the subscription's own total is 2 -- but only the first reached the
        // event, carrying the total as it stood at that moment: 1.
        Assert.Equal(2, bars.MalformedCount);
        Assert.Equal(1, Interlocked.CompareExchange(ref observed, 0, 0));
        Assert.Equal(1, Interlocked.Read(ref lastCount));

        clock.Advance(Duration.FromSeconds(2));

        socket.EnqueueText("""[{"ev":"AM","sym":"MSFT","v":1,"z":14.0,"s":5,"e":6}]""");

        await SubscribeMinuteAggregatesAsync(stream, socket, "TSLA", TestContext.Current.CancellationToken);

        Assert.Equal(2, Interlocked.CompareExchange(ref observed, 0, 0));
        Assert.Equal(3, Interlocked.Read(ref lastCount));
    }

    // F1, the property EventRaiser exists for, applied to the new event. Every handler gets its own
    // try/catch: a multicast delegate stops invoking subscribers the instant one throws, so a single
    // try around the whole invocation would still starve every handler after the throwing one -- and
    // the raise happens on the read-loop thread, so an escaping handler exception would reach the
    // loop's outer catch and end the live feed. A notification that one event was dropped must never
    // be worse than the drop it is reporting.
    [Fact]
    public async Task AThrowingMalformedObservedHandlerDoesNotEndTheStream()
    {
        FakeClock clock = new(Instant.FromUnixTimeSeconds(0));
        MassiveStreamOptions options = new() { ApiKey = "k" };

        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync(options, clock);
        await using MassiveStockStream _ = stream;

        await SubscribeMinuteAggregatesAsync(stream, socket, "MSFT", TestContext.Current.CancellationToken);

        int reachedAfterTheThrower = 0;

        stream.MalformedObserved += (_, _, _) => throw new InvalidOperationException("a rude handler");
        stream.MalformedObserved += (_, _, _) => Interlocked.Increment(ref reachedAfterTheThrower);

        socket.EnqueueText("""[{"ev":"AM","sym":"MSFT","v":1,"z":12.0,"s":1,"e":2}]""");

        // If the throw had escaped, this subscribe would never be acknowledged -- the read loop that
        // delivers the acknowledgement would be dead.
        MassiveTopicSubscription<StockTrade> trades =
            await SubscribeTradesAsync(stream, socket, "AAPL", TestContext.Current.CancellationToken);

        Assert.NotNull(trades);
        Assert.Equal(1, Interlocked.CompareExchange(ref reachedAfterTheThrower, 0, 0));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~StockStreamTests" -v minimal
```

Expected: compile errors — `MalformedObserved` does not exist.

- [ ] **Step 3: Add the three-argument `Raise`**

Append to `src/MassiveDotNet.WebSocket/Internal/EventRaiser.cs`, inside the class, after `Raise<T1, T2>`:

```csharp
    /// <summary>Raises a three-argument event, isolating each subscriber's failure.</summary>
    /// <typeparam name="T1">The first argument's type.</typeparam>
    /// <typeparam name="T2">The second argument's type.</typeparam>
    /// <typeparam name="T3">The third argument's type.</typeparam>
    /// <param name="handlers">The event's invocation list, or <see langword="null"/> if empty.</param>
    /// <param name="argument1">The first argument to pass each subscriber.</param>
    /// <param name="argument2">The second argument to pass each subscriber.</param>
    /// <param name="argument3">The third argument to pass each subscriber.</param>
    public static void Raise<T1, T2, T3>(
        Action<T1, T2, T3>? handlers, T1 argument1, T2 argument2, T3 argument3)
    {
        foreach (Delegate handler in handlers?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action<T1, T2, T3>)handler)(argument1, argument2, argument3);
            }
            catch
            {
                // See the remarks above.
            }
        }
    }
```

`EventRaiser` is `internal`, so this needs no `PublicAPI.Unshipped.txt` entry.

- [ ] **Step 4: Add the event, its window, its lock and its handler**

In `src/MassiveDotNet.WebSocket/MassiveStockStream.cs`:

Add `using System.Text.Json;` to the top of the file, above the existing `using System.Text.Json.Serialization;`.

Beside `DropThrottleWindow`, add:

```csharp
    // D-W20: its OWN window and its own state, deliberately not shared with DropThrottleWindow's.
    // A burst of buffer overflows and a burst of off-schema fields are different problems with
    // opposite remedies, so neither must be able to throttle the other into silence.
    private static readonly Duration MalformedThrottleWindow = Duration.FromSeconds(1);
```

Beside `_dropThrottleLock` and `_lastDropObservedAt`, add:

```csharp
    private readonly object _malformedThrottleLock = new();
    private Instant? _lastMalformedObservedAt;
```

Immediately after the `DropObserved` event declaration, add:

```csharp
    /// <summary>
    /// Raised when the wire sent a value a converter would not accept and the event was dropped,
    /// naming the topic's wire code, that topic's own running malformed count, and the exception
    /// saying which field and which value — throttled to at most once a second so a sustained
    /// off-schema field does not produce an unbounded stream of notifications.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="DropObserved"/> on purpose (D-W20), and not a duplicate of it. A
    /// drop is the consumer's own backpressure, fixed by raising
    /// <see cref="MassiveStreamOptions.TopicBufferCapacity"/> or doing less work in the loop; this
    /// is Massive's wire disagreeing with the SDK's schema, which the consumer cannot fix at all.
    /// Reporting both through one signal would hand them advice that cannot work.
    /// <para>
    /// The exception travels with the raise because it is the only route the refused value has out
    /// of the SDK. A malformed event was terminal until issue #65, so its message reached a
    /// consumer through <see cref="Faulted"/>; now that the connection survives, nothing else
    /// carries it. Every handler is invoked with its own try/catch (F1): a notification about one
    /// dropped event must never itself end the live feed.
    /// </para>
    /// </remarks>
    public event Action<string, long, JsonException>? MalformedObserved;
```

In `GetOrCreateSink`, immediately after the existing `sink.ItemDropped += ...` line:

```csharp
                sink.EventMalformed += error =>
                    OnEventMalformed(topicCode, sink.Subscription.MalformedCount, error);
```

And beside `OnItemDropped`, add:

```csharp
    // The same edge-triggered throttle as OnItemDropped, with its own window and its own lock
    // (D-W20): the first qualifying event in a fresh window raises and every later one in that
    // window is discarded outright, so the count carried is the running total at that first event,
    // not the total once a burst has finished. Sharing the drop throttle's state would let a
    // buffer-overflow burst silence this, and the two report problems with opposite remedies.
    private void OnEventMalformed(string topicCode, long malformedCount, JsonException error)
    {
        Instant now = _clock.GetCurrentInstant();

        lock (_malformedThrottleLock)
        {
            if (_lastMalformedObservedAt is { } last && now - last < MalformedThrottleWindow)
            {
                return;
            }

            _lastMalformedObservedAt = now;
        }

        EventRaiser.Raise(MalformedObserved, topicCode, malformedCount, error);
    }
```

- [ ] **Step 5: Add the two public API entries**

`src/MassiveDotNet.WebSocket/PublicAPI.Unshipped.txt` currently holds only `#nullable enable`. Append these two lines below it, keeping `#nullable enable` first:

```text
MassiveDotNet.WebSocket.MassiveStockStream.MalformedObserved -> System.Action<string!, long, System.Text.Json.JsonException!>?
MassiveDotNet.WebSocket.MassiveTopicSubscription<T>.MalformedCount.get -> long
```

The analyzer wants these sorted; if `dotnet build` reports RS0016 or RS0017 anyway, take the exact text the diagnostic gives rather than reformatting by hand.

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet build MassiveDotNet.slnx
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~StockStreamTests" -v minimal
```

Expected: PASS.

**D31:** temporarily delete the `lock`/window check from `OnEventMalformed` (raise unconditionally) and confirm `MalformedObservedIsThrottledToAtMostOncePerSecond` goes red with `observed == 2`, not with some other failure. Restore it.

- [ ] **Step 7: Run the whole tier**

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests
```

Expected: every test passing, `DropObservedIsThrottledToAtMostOncePerSecond` included and unchanged.

- [ ] **Step 8: Commit**

```bash
git add src/MassiveDotNet.WebSocket/Internal/EventRaiser.cs src/MassiveDotNet.WebSocket/MassiveStockStream.cs src/MassiveDotNet.WebSocket/PublicAPI.Unshipped.txt tests/MassiveDotNet.WebSocket.Tests/StockStreamTests.cs
git commit -m "$(cat <<'EOF'
feat: report a malformed event through its own throttled signal

MalformedObserved carries the topic code, that topic's running malformed count, and the exception.
The exception is the load-bearing part: a malformed event was terminal until the previous commit, so
its message reached a consumer through Faulted, and now that the connection survives this is the
only route the refused value has out of the SDK. Without it the outage fix would swallow the
evidence for the decision still outstanding -- whether `z` needs a wider read at all.

A second signal rather than a reuse of DropObserved, against D33's "a second signal is strictly less
surface for the same information", because it is not the same information: a slow consumer and an
off-schema vendor have opposite remedies. Its own throttle window too, so a burst of one cannot
silence the other.

EventRaiser gains a three-argument overload -- internal, so no public API entry -- and keeps its
per-subscriber try/catch: the raise runs on the read-loop thread, so an escaping handler exception
would end the feed the notification was merely describing (F1).

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB
EOF
)"
```

---

## Task 5: The log bridge

**Files:**
- Modify: `src/MassiveDotNet.Extensions.DependencyInjection/MassiveStreamServiceCollectionExtensions.cs:80-136`
- Test: `tests/MassiveDotNet.Extensions.DependencyInjection.Tests/LogStreamHealthTests.cs`

This is the only project permitted to reference `Microsoft.Extensions.*` (rule 8), and `LogStreamHealth` is the only code in the SDK that formats stream state into log text — which makes it the only place an API key could ever reach a log (rule 11). The existing `NeitherADropNorAReconnectEverLogsTheApiKey` test is that gate; this task adds an interpolated *string* to the templates for the first time, so extend it rather than trusting inspection.

**Interfaces:**
- Consumes: `MassiveStockStream.MalformedObserved` from Task 4.
- Produces: nothing other tasks depend on.

- [ ] **Step 1: Write the failing test**

Add to `tests/MassiveDotNet.Extensions.DependencyInjection.Tests/LogStreamHealthTests.cs`. It reuses the file's existing `ConnectAsync(IClock)` helper, `CapturingLoggerProvider`, `SentinelApiKey` and `Ct`.

```csharp
    // Issue #65 / D-W20. Three claims in one test, because they only hold together.
    //
    // (a) The warning carries the refused VALUE. That message is the only evidence left about which
    //     of TryGetInt64's three rejection cases occurs on `z`, now that the failure is no longer
    //     terminal and Faulted no longer carries it.
    // (b) It does NOT repeat LogDropped's "raise TopicBufferCapacity" advice. That advice is right
    //     for a buffer overflow and useless here -- the wire is off-schema and the consumer cannot
    //     do anything about it. Handing them one cause's remedy for another cause is the confusion
    //     this change and issue #64 both exist to remove.
    // (c) The sentinel key still appears nowhere. This is the first [LoggerMessage] template in the
    //     SDK to interpolate a STRING rather than a count, so the rule 11 gate has to cover it.
    [Fact]
    public async Task AMalformedFieldIsLoggedWithTheValueAndWithoutTheBufferAdvice()
    {
        FakeClock clock = new(Instant.FromUnixTimeSeconds(0));
        (MassiveStockStream stream, FakeWebSocket first, FakeWebSocket _) = await ConnectAsync(clock);
        await using MassiveStockStream owned = stream;

        CapturingLoggerProvider capture = new();
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(capture);
        });

        stream.LogStreamHealth(factory.CreateLogger("stream-health-test"));

        await stream.SubscribeMinuteAggregatesAsync(["MSFT"], Ct);

        first.EnqueueText("""[{"ev":"AM","sym":"MSFT","v":1,"z":12.0,"s":1,"e":2}]""");

        // A widening subscribe, once acknowledged, guarantees the frame above has been dispatched.
        await stream.SubscribeMinuteAggregatesAsync(["AAPL"], Ct);

        string captured = capture.Text;

        Assert.Contains("dropped 1 events on topic AM", captured, StringComparison.Ordinal);
        Assert.Contains("StockAggregate.z (12.0)", captured, StringComparison.Ordinal);
        Assert.DoesNotContain("TopicBufferCapacity or do less work", captured, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelApiKey, captured, StringComparison.Ordinal);
    }

    // The sibling of DropsOnTwoTopicsAreCountedSeparatelyRatherThanContinuingEachOther, one axis
    // over: not two topics sharing one counter, but ONE topic whose two counters share one
    // dictionary. A drop and a malformed event are different running totals for the same topic
    // code, so a shared dictionary has the malformed count measured against the drop count --
    // `malformedCount > previouslyReported` is false, and the warning is silently never logged. A
    // consumer watching the log would conclude the wire was fine while events were being refused,
    // which is precisely the "one cause made indistinguishable from another" this change exists to
    // remove.
    //
    // The two throttles are separate windows (D-W20), so both first events get through inside the
    // same second on the fake clock -- which is what makes this deterministic rather than a race.
    [Fact]
    public async Task ADropAndAMalformedEventOnOneTopicAreCountedSeparately()
    {
        FakeClock clock = new(Instant.FromUnixTimeSeconds(0));
        (MassiveStockStream stream, FakeWebSocket first, FakeWebSocket _) = await ConnectAsync(clock);
        await using MassiveStockStream owned = stream;

        CapturingLoggerProvider capture = new();
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(capture);
        });

        stream.LogStreamHealth(factory.CreateLogger("stream-health-test"));

        await stream.SubscribeMinuteAggregatesAsync(["MSFT"], Ct);

        // TopicBufferCapacity is 1 (ConnectAsync sets it): three well-formed bars with nobody
        // reading evict two, and the malformed fourth is refused. The drop comes FIRST, so a shared
        // dictionary has its total in place by the time the malformed one is measured.
        first.EnqueueText(
            """[{"ev":"AM","sym":"MSFT","v":1,"z":1,"s":1,"e":2},{"ev":"AM","sym":"MSFT","v":1,"z":2,"s":3,"e":4},{"ev":"AM","sym":"MSFT","v":1,"z":3,"s":5,"e":6},{"ev":"AM","sym":"MSFT","v":1,"z":12.0,"s":7,"e":8}]""");

        await stream.SubscribeMinuteAggregatesAsync(["AAPL"], Ct);

        string captured = capture.Text;

        Assert.Contains("because a topic buffer was full", captured, StringComparison.Ordinal);
        Assert.Contains("because the wire sent a value", captured, StringComparison.Ordinal);
    }
```

`ConnectAsync(IClock)` in this file already sets `AutoAcknowledgeSubscribes = true` on both sockets and `TopicBufferCapacity = 1` on the options, so the `SubscribeMinuteAggregatesAsync` calls complete without hand-enqueued acknowledgements and a four-event frame overflows the buffer.

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test tests/MassiveDotNet.Extensions.DependencyInjection.Tests --filter "FullyQualifiedName~AMalformedFieldIsLoggedWithTheValueAndWithoutTheBufferAdvice" -v minimal
```

Expected: both FAIL — `Assert.Contains() Failure`, nothing malformed was logged, because nothing subscribes `MalformedObserved` yet.

- [ ] **Step 3: Subscribe the event in `LogStreamHealth`**

In `MassiveStreamServiceCollectionExtensions.cs`, append this to the body of `LogStreamHealth`, after the existing `stream.DropObserved += ...` block and before the closing brace:

```csharp
        // Its own dictionary, for the same per-topic-delta reason as the drop bridge above, and
        // deliberately not that same dictionary: a malformed count and a drop count are two
        // different running totals for the same topic code, so sharing one would have each measure
        // its delta against the other's total and silently under-report both.
        Dictionary<string, long> reportedMalformed = new(StringComparer.Ordinal);

        stream.MalformedObserved += (topicCode, malformedCount, error) =>
        {
            long previouslyReported = reportedMalformed.TryGetValue(topicCode, out long value) ? value : 0;

            if (malformedCount > previouslyReported)
            {
                LogMalformed(logger, malformedCount - previouslyReported, topicCode, error.Message);
                reportedMalformed[topicCode] = malformedCount;
            }
        };
```

- [ ] **Step 4: Add the `[LoggerMessage]` partial and update the rule 11 comment**

Replace the comment above the existing `[LoggerMessage]` block (`MassiveStreamServiceCollectionExtensions.cs:116-119`) with:

```csharp
    // CA1848: LoggerMessage delegates rather than the plain ILogger.LogWarning(...) extension,
    // which allocates a params array and boxes every argument on every call.
    //
    // Rule 11: three of these interpolate only counts and a topic's wire code. The fourth,
    // LogMalformed, interpolates a JsonException's MESSAGE, which is a string -- so the rule holds
    // structurally rather than by inspection here. That message comes from JsonValueReader, whose
    // only value-echoing helper is reachable only after the token has been proved a
    // JsonTokenType.Number, and an API key is not a JSON number (D-W21). None of the four ever
    // touches options.ApiKey or anything derived from it, and LogStreamHealthTests asserts a
    // sentinel key appears nowhere in what is captured.
```

Then add this partial beside the other three:

```csharp
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The Massive stream dropped {Malformed} events on topic {TopicCode} because the wire sent a "
            + "value the SDK's schema does not accept: {Reason} Raising TopicBufferCapacity will not help -- "
            + "this is a server-side shape, not a slow consumer.")]
    private static partial void LogMalformed(ILogger logger, long malformed, string topicCode, string reason);
```

Warning, not Error: the socket is healthy and every other event is still being delivered, so this describes a gap the consumer can see rather than a topic gone silent — which is the line `LogSubscriptionsLost` sits on the other side of. `{Reason}` is the exception's message and already ends in a full stop, so no punctuation follows it.

- [ ] **Step 5: Run the test to verify it passes**

```bash
dotnet build MassiveDotNet.slnx
dotnet test tests/MassiveDotNet.Extensions.DependencyInjection.Tests --filter "FullyQualifiedName~LogStreamHealthTests" -v minimal
```

Expected: PASS, both new tests.

**D31:** temporarily point the new handler at `reported` — the drop bridge's dictionary — instead of `reportedMalformed`, and re-run. `ADropAndAMalformedEventOnOneTopicAreCountedSeparately` must go red on the second assertion: the drop sets `reported["AM"] = 1`, so the malformed count of 1 is not greater than it and the warning is never logged. Confirm that is the failure you see, not some other one, then restore `reportedMalformed`.

- [ ] **Step 6: Run the whole DI tier**

```bash
dotnet test tests/MassiveDotNet.Extensions.DependencyInjection.Tests
```

Expected: every test passing, including `NeitherADropNorAReconnectEverLogsTheApiKey` and `DropsOnTwoTopicsAreCountedSeparatelyRatherThanContinuingEachOther`.

- [ ] **Step 7: Commit**

```bash
git add src/MassiveDotNet.Extensions.DependencyInjection/MassiveStreamServiceCollectionExtensions.cs tests/MassiveDotNet.Extensions.DependencyInjection.Tests/LogStreamHealthTests.cs
git commit -m "$(cat <<'EOF'
feat: log a malformed stream event with the value that was refused

LogStreamHealth gains a fourth bridge, at Warning. It carries the exception's message -- which names
the model, the property and the token -- because that is the only evidence left about which of
TryGetInt64's three rejection cases occurs, now that the failure is no longer terminal.

It deliberately does not repeat LogDropped's "raise TopicBufferCapacity" advice. That advice is right
for a buffer overflow and useless for an off-schema field, and handing a consumer one cause's remedy
for another is the confusion this change exists to remove.

Its own per-topic dictionary, not the drop bridge's: two different running totals for the same topic
code would each measure their delta against the other's.

This is the first [LoggerMessage] template in the SDK to interpolate a string rather than a count, so
the rule 11 argument moves from inspection to structure -- the message comes from JsonValueReader,
whose one echoing helper is reachable only after the token is proved a JSON number -- and the
sentinel-key assertion now covers this path too.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB
EOF
)"
```

---

## Task 6: Record the decision, and run every gate

**Files:**
- Modify: `CHANGELOG.md` — the `## [Unreleased]` section, currently empty
- Modify: `CLAUDE.md` — the architecture decision table

**Interfaces:**
- Consumes: everything. This task adds no code.
- Produces: nothing.

- [ ] **Step 1: Add the CHANGELOG entries**

Replace the line `## [Unreleased]` in `CHANGELOG.md` with:

```markdown
## [Unreleased]

### Added

- **`MassiveDotNet.WebSocket`** — `MassiveTopicSubscription<T>.MalformedCount` and
  `MassiveStockStream.MalformedObserved`, reporting events the wire sent in a shape the SDK's schema
  does not accept. Separate from `DroppedCount`/`DropObserved` on purpose: a drop is the consumer's
  own backpressure and is fixed by raising `TopicBufferCapacity`, while a malformed event is the
  server's wire disagreeing with the SDK and the consumer cannot fix it at all. `MalformedObserved`
  carries the `JsonException`, throttled to at most one raise a second on its own window. The DI
  package's `LogStreamHealth` bridges it to `ILogger` at `Warning`.

### Fixed

- **`MassiveDotNet.WebSocket`** — a value a converter refuses no longer ends the connection. The
  event is dropped and counted, and the read loop carries on. The connection is multiplexed, so one
  unparseable field on one symbol — on a topic the consumer may not even have subscribed to — used
  to take every other topic's live data down with it. A malformed `ev` is still terminal: it leaves
  the reader stranded mid-object with no topic to attribute the loss to.
- **`MassiveDotNet`** — a numeric range failure now names the value it refused:
  `The number in StockAggregate.z (12.0) does not fit a 64-bit integer.` `Utf8JsonReader`'s
  `TryGetInt64` returns `false` for three unrelated reasons and the old message distinguished none
  of them. Capped at 32 bytes, and confined to that one failure: the wrong-token-type message names
  a token type and has no value to quote, and the decimal round-trip message reads a string token,
  where "the bytes are provably a number" does not hold.
```

- [ ] **Step 2: Add decision D39 to `CLAUDE.md`**

Append one row to the architecture decision table, after the `D38` row:

```markdown
| D39 | A malformed **field** drops one event, counted on its own `MalformedCount` and reported through its own throttled `MalformedObserved` carrying the `JsonException`; a malformed **frame** stays terminal. `JsonValueReader`'s range failure echoes the offending token, capped at 32 bytes; its two sibling messages do not. | One `z` the reader refused ended a connection that multiplexes trades, quotes and both aggregate windows — four times in ten minutes in production — so one symbol's off-schema field on a topic the consumer had not subscribed to took down the trade feed. The catch sits in `TopicSink<T>.Write` because `Dispatch` hands it a struct **copy** of the frame reader while the real one is already parked on the failed object's end token: the loop resumes with no resync, and the sink is also the type that knows `T` and owns the count. The boundary is drawn there and nowhere wider — a malformed `ev` throws from `ReadEventCode`, which drives the real reader, stranding it mid-object with no topic to attribute the loss to, so that stays terminal. G3's reasoning is untouched and strengthened: the SDK now neither reconnects **nor** stops over a parse failure; only its conclusion that the failure is terminal changed. A second counter and a second signal were chosen against D33's "a second signal is strictly less surface for the same information", because it is **not** the same information — a slow consumer raises `TopicBufferCapacity` and an off-schema vendor cannot do anything, so one counter would hand a consumer advice that cannot work, which is #64's complaint reproduced deliberately; each throttle keeps its own window so a burst of one cannot mask the other. The exception travels with the raise because the two halves of #65 work against each other: the improved message reached consumers through `Faulted` precisely **because** the failure was terminal, so making it non-terminal without carrying the exception would swallow the evidence and leave the question of what `z` should be permanently undecidable. The echo reverses `JsonValueReader`'s standing "no method echoes the offending value" contract, and the confinement is the rule 11 argument rather than care taken per site: `OutOfRange` is reachable only after a caller has checked `TokenType == Number`, so its bytes are provably a JSON number and an API key is not one — where `NotExact` reads a String token, whose class of value is unbounded, and `Expected` names a token type and has nothing to quote. `StockAggregate.AverageTradeSize` stays `long`: Massive documents `z` as an `integer`, so retyping it would be a breaking public change made on a guess contradicting the vendor, and this work is what produces the evidence to revisit it. |
```

Keep it on one line — every other row in that table is one line.

- [ ] **Step 3: Run every gate the repository has**

```bash
dotnet build MassiveDotNet.slnx
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/
dotnet publish samples/MassiveDotNet.AotSmokeTest -r osx-arm64 -c Release
```

Expected, in order: a build with zero warnings; every offline test passing; `git diff --exit-code src/` clean — nothing in this plan touches generated code, so a diff here means something went wrong; and an AOT publish with **zero** IL warnings.

`TemporalTypeTests` and `BuildGateTests` run as part of the second command. If `TemporalTypeTests` fails, a BCL temporal type got named somewhere — find it and convert at the call site.

- [ ] **Step 4: Commit**

```bash
git add CHANGELOG.md CLAUDE.md
git commit -m "$(cat <<'EOF'
docs: record D39 -- a malformed field drops one event, not the connection

Collapses D-W19, D-W20 and D-W21 into one row: the per-event drop and its counter, the field/frame
boundary and why it is drawn at sink.Write, why this is a second signal rather than a reuse of
DroppedCount, and why the echo is numbers-only.

Also records what this change deliberately does NOT do. StockAggregate.AverageTradeSize stays long,
because Massive documents `z` as an integer and retyping it would be a breaking public change made
on a guess that contradicts the vendor. This work is what produces the evidence to revisit that.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB
EOF
)"
```

- [ ] **Step 5: Report**

Summarise for the user: what landed, which tests were watched failing and for what reason, the exact figure any allocation ceiling moved by (expected: none), and the one thing Task 5 step 5 could not pin. Do **not** push or open a PR without being asked.

---

## Verification summary

| Property | Test | File |
|---|---|---|
| The range message names the refused value | `AnOutOfRangeInt64NamesTheValueItRefused` | `JsonValueReaderTests.cs` |
| …and for `int` too | `AnOutOfRangeInt32NamesTheValueItRefused` | `JsonValueReaderTests.cs` |
| The echo is capped | `ALongNumberIsEchoedOnlyUpToTheCap` | `JsonValueReaderTests.cs` |
| `Expected` does not echo (rule 11) | `AWrongTokenTypeDoesNotEchoTheValue` | `JsonValueReaderTests.cs` |
| `NotExact` does not echo (rule 11) | `ARefusedDecimalDoesNotEchoTheValue` | `JsonValueReaderTests.cs` |
| One refused event is dropped and counted, both refusal cases | `AnEventTheConverterRefusesIsDroppedAndCountedRatherThanThrown` | `BackpressureTests.cs` |
| The exception reaches the sink's seam | `ARefusedEventRaisesEventMalformedCarryingTheValue` | `BackpressureTests.cs` |
| **A malformed event costs only its own topic** | `AMalformedEventInOneTopicDoesNotCostAnotherTopicItsData` | `DispatchTests.cs` |
| A malformed frame is still terminal | `AFrameWithAMalformedEventCodeFaultsTheReadLoop` (existing) | `DispatchTests.cs` |
| The signal carries topic, count and exception | `MalformedObservedCarriesTheTopicTheCountAndTheRefusedValue` | `StockStreamTests.cs` |
| Its own throttle window | `MalformedObservedIsThrottledToAtMostOncePerSecond` | `StockStreamTests.cs` |
| A throwing handler cannot end the stream | `AThrowingMalformedObservedHandlerDoesNotEndTheStream` | `StockStreamTests.cs` |
| The log names the value, not the buffer advice | `AMalformedFieldIsLoggedWithTheValueAndWithoutTheBufferAdvice` | `LogStreamHealthTests.cs` |
| One topic's two counters do not shadow each other | `ADropAndAMalformedEventOnOneTopicAreCountedSeparately` | `LogStreamHealthTests.cs` |
| No key ever reaches a log | `NeitherADropNorAReconnectEverLogsTheApiKey` (existing) + the assertion above | `LogStreamHealthTests.cs` |
| The happy path allocates no more | existing ceilings, unchanged | `AllocationTests.cs` |

The bolded row is the one the whole change exists for. If exactly one test could survive, it is that one.
