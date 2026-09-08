# Stocks streaming topics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the four remaining stock streaming topics — second aggregates, minute aggregates, net order imbalance, and limit up-limit down — on a shared property-walk cursor that every streaming converter goes through.

**Architecture:** Each topic gets a `readonly record struct` model in `MassiveDotNet.WebSocket.Events`, a hand-written `JsonConverter<T>` in `Internal`, a `StockTopic` member, and a `Subscribe…Async` method on `MassiveStockStream`. The converters are built on one new cursor, `StreamEventWalk`, which auto-skips any property a converter does not consume and reads every value through `JsonValueReader` — the two existing converters move onto it in the same branch, so no copy of the walk survives.

**Tech Stack:** .NET 10, C# 14, `System.Text.Json` (source-generated, no reflection), NodaTime, xUnit v3.

**Spec:** `docs/superpowers/specs/2026-09-08-stocks-topics-design.md`

## Global Constraints

Copied verbatim from `CLAUDE.md`. Every task's requirements implicitly include these.

- **Rule 3.** No reflection-based serialization anywhere in shipped code. `System.Text.Json` source generation only.
- **Rule 5.** Generated files (`*.g.cs`) are never hand-edited. Nothing in this plan touches generated code.
- **Rule 9.** Builds are warning-free. `TreatWarningsAsErrors` is on. A declared-but-never-raised event is `CS0067` and fails the build.
- **Rule 10.** Every public member of a shipped library carries XML documentation. `internal` members here carry it too, matching `ITopicSink`.
- **Rule 11.** API keys are never logged, echoed in exception messages, or written to disk.
- **Rule 12.** **No BCL `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, or `TimeSpan` may be named anywhere in the repository's source** — not in a declaration, not in a typed local, not in a static call like `TimeSpan.FromMinutes`. `TemporalTypeTests` fails the build on one, by reflection over the shipped surface and by a source scan that sees locals and static calls. Naming one in a *comment* is fine; the scan strips comments.
- **Rule 13.** CI runs entirely offline. Live tests are committed, compiled in CI, and excluded by `Category=Integration`.
- **D4.** Tick-level types are `readonly record struct`. **D5.** Store the raw epoch value as a `long`; expose the NodaTime type as a computed property.
- **D31.** A guard nobody has seen fail is indistinguishable from a clean tree. Every allocation ceiling is set by breaking its path, watching the assertion go red for the right reason, restoring, and recording the regression beside the number.
- **Commits:** conventional prefixes (`feat:`, `fix:`, `test:`, `docs:`, `refactor:`). Every commit message ends with `Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB`.
- **Never commit `.claude/`.** It is untracked and must stay that way. Stage files explicitly; never `git add -A`.
- **Branch:** `feat/stocks-topics`, already created off `master` at `3fc70e4`.

### Wire facts, read from documentation and verified live on 2026-09-08 at 09:52 ET

| Topic | Code | Ticker field | Timestamp field and unit |
|---|---|---|---|
| Second aggregates | `A` | `sym` | `s`, `e` — Unix **milliseconds** |
| Minute aggregates | `AM` | `sym` | `s`, `e` — Unix **milliseconds** |
| Net order imbalance | `NOI` | `T` | `t` — Unix **nanoseconds** |
| Limit up-limit down | `LULD` | `T` | `t` — Unix **nanoseconds** |

`NOI` and `LULD` send the ticker as `T`, which is also the *trade topic's* wire code. Do not assume `sym`.

LULD's documentation says its `t` is "Unix MS". It is not. Its own published sample and the live wire both carry nanoseconds. See D-W15 in the spec.

---

## File Structure

**Created:**

| File | Responsibility |
|---|---|
| `src/MassiveDotNet.WebSocket/Internal/StreamEventWalk.cs` | The one property-walk cursor every streaming converter uses. |
| `src/MassiveDotNet.WebSocket/Events/StockAggregate.cs` | The `A` and `AM` bar model. |
| `src/MassiveDotNet.WebSocket/Events/StockImbalance.cs` | The `NOI` model. |
| `src/MassiveDotNet.WebSocket/Events/StockLimitUpLimitDown.cs` | The `LULD` model. |
| `src/MassiveDotNet.WebSocket/Internal/StockAggregateConverter.cs` | Reads and writes `StockAggregate`, parameterised by wire code. |
| `src/MassiveDotNet.WebSocket/Internal/StockImbalanceConverter.cs` | Reads and writes `StockImbalance`. |
| `src/MassiveDotNet.WebSocket/Internal/StockLimitUpLimitDownConverter.cs` | Reads and writes `StockLimitUpLimitDown`. |
| `tests/MassiveDotNet.WebSocket.Tests/StreamEventWalkTests.cs` | The cursor's own tests — the walk tested once, not per converter. |
| `tests/MassiveDotNet.IntegrationTests/StreamTopicsLiveTests.cs` | Live acknowledgement pins for all four codes. |

**Modified:**

| File | Change |
|---|---|
| `src/MassiveDotNet.WebSocket/StockTopic.cs` | Four enum members and four `ToCode()` arms. |
| `src/MassiveDotNet.WebSocket/Internal/StockTradeConverter.cs` | Moved onto `StreamEventWalk`. |
| `src/MassiveDotNet.WebSocket/Internal/StockQuoteConverter.cs` | Moved onto `StreamEventWalk`. |
| `src/MassiveDotNet.WebSocket/MassiveStockStream.cs` | Four subscribe methods; sink creation and disposal collapsed. |
| `tests/MassiveDotNet.WebSocket.Tests/Fixtures.cs` | Published samples plus two live captures. |
| `tests/MassiveDotNet.WebSocket.Tests/EventParsingTests.cs` | Per-topic parsing tests. |
| `tests/MassiveDotNet.WebSocket.Tests/StockStreamTests.cs` | Façade tests for the new topics. |
| `tests/MassiveDotNet.WebSocket.Tests/AllocationTests.cs` | Ceilings for the three new parse paths. |
| `CLAUDE.md` | Decision D36; `Layout` note. |
| `docs/performance/2026-09-07-streaming-allocation-figures.md` | New ceilings and the regressions that proved them. |

---

## Task 1: `StreamEventWalk`, the shared property cursor

This is the task the whole issue is sequenced around. Do not start any converter before it lands.

**Files:**
- Create: `src/MassiveDotNet.WebSocket/Internal/StreamEventWalk.cs`
- Test: `tests/MassiveDotNet.WebSocket.Tests/StreamEventWalkTests.cs`

**Interfaces:**
- Consumes: `MassiveDotNet.Serialization.JsonValueReader` (core, public static); `MassiveDotNet.WebSocket.Internal.TickerPool.Intern(ref Utf8JsonReader)`; `MassiveDotNet.WebSocket.Internal.ConditionSetSerialization.Read(ref Utf8JsonReader, string model, string property)`.
- Produces: `internal struct StreamEventWalk` with constructor `StreamEventWalk(ref Utf8JsonReader reader, string model)` and instance methods `bool NextProperty(ref Utf8JsonReader)`, `string Ticker(ref Utf8JsonReader, TickerPool, string)`, `string? String(ref Utf8JsonReader, string)`, `bool Boolean(ref Utf8JsonReader, string)`, `int Int32(ref Utf8JsonReader, string)`, `int? NullableInt32(ref Utf8JsonReader, string)`, `long Int64(ref Utf8JsonReader, string)`, `long? NullableInt64(ref Utf8JsonReader, string)`, `double Double(ref Utf8JsonReader, string)`, `ConditionSet Conditions(ref Utf8JsonReader, string)`.

**Why it is a plain `struct` and not a `ref struct`.** Both alternatives were tried against the compiler on 2026-09-08 and both are rejected: a `ref Utf8JsonReader` field gives `CS9050` (a ref field cannot refer to a ref struct), and a `ref struct` receiver taking `ref Utf8JsonReader` gives `CS8350` (the receiver might capture the reference). A plain `struct` cannot hold a ref field at all, so ref-safety analysis has nothing to reject. Do not "improve" this to a `ref struct`; it does not compile.

- [ ] **Step 1: Write the failing test file**

Create `tests/MassiveDotNet.WebSocket.Tests/StreamEventWalkTests.cs`:

```csharp
using System.Text;
using System.Text.Json;
using MassiveDotNet.WebSocket.Internal;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// The shared property walk, tested once rather than once per converter. Issue #21's whole
/// sequencing argument is that this shape gets extracted and proven before twenty-four converters
/// copy it, so these are the tests that stand in for all of them.
/// </summary>
public class StreamEventWalkTests
{
    // Reads the two known long properties out of one object, ignoring everything else. Whatever
    // sits between "a" and "b" is what each test varies: the walk must leave the reader positioned
    // so that "b" is still found, whatever shape the unrecognised property took.
    private static (long A, long B) ReadPair(string json)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        reader.Read();

        StreamEventWalk walk = new(ref reader, "Probe");
        long a = 0;
        long b = 0;

        while (walk.NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("a"u8))
            {
                a = walk.Int64(ref reader, "a");
            }
            else if (reader.ValueTextEquals("b"u8))
            {
                b = walk.Int64(ref reader, "b");
            }
        }

        return (a, b);
    }

    // A missed Skip() does not throw. It leaves the reader mid-value, so the NEXT property is read
    // from the wrong token -- which deserializes wrong rather than failing. That is why every case
    // below asserts the value of a property that comes AFTER the unrecognised one, and why "does
    // not throw" would be a worthless assertion here.
    [Theory]
    [InlineData("""{"a":1,"unknown":42,"b":2}""")]
    [InlineData("""{"a":1,"unknown":"text","b":2}""")]
    [InlineData("""{"a":1,"unknown":null,"b":2}""")]
    [InlineData("""{"a":1,"unknown":true,"b":2}""")]
    [InlineData("""{"a":1,"unknown":{"x":1},"b":2}""")]
    [InlineData("""{"a":1,"unknown":[1,2,3],"b":2}""")]
    [InlineData("""{"a":1,"unknown":{"x":{"y":[1,{"z":2}]}},"b":2}""")]
    [InlineData("""{"a":1,"unknown":[[1,[2]],{"x":[3]}],"b":2}""")]
    public void APropertyTheConverterDoesNotConsumeIsSkippedWholesale(string json)
    {
        (long a, long b) = ReadPair(json);

        Assert.Equal(1, a);
        Assert.Equal(2, b);
    }

    [Fact]
    public void SeveralUnrecognisedPropertiesInARowAreEachSkipped()
    {
        (long a, long b) = ReadPair("""{"a":1,"p":{"q":1},"r":[1],"s":null,"t":"x","b":2}""");

        Assert.Equal(1, a);
        Assert.Equal(2, b);
    }

    [Fact]
    public void AnUnrecognisedPropertyBeforeEveryKnownOneIsSkipped()
    {
        (long a, long b) = ReadPair("""{"lead":{"deep":[1,2]},"a":1,"b":2}""");

        Assert.Equal(1, a);
        Assert.Equal(2, b);
    }

    [Fact]
    public void AnUnrecognisedTrailingPropertyEndsTheWalkCleanly()
    {
        (long a, long b) = ReadPair("""{"a":1,"b":2,"trailing":{"x":[1]}}""");

        Assert.Equal(1, a);
        Assert.Equal(2, b);
    }

    [Fact]
    public void AnObjectWithNoRecognisedPropertiesAtAllWalksToTheEnd()
    {
        (long a, long b) = ReadPair("""{"p":1,"q":{"r":[1,2]},"s":"x"}""");

        Assert.Equal(0, a);
        Assert.Equal(0, b);
    }

    [Fact]
    public void LastValueWinsWhenAPropertyRepeats()
    {
        (long a, long _) = ReadPair("""{"a":1,"a":7}""");

        Assert.Equal(7, a);
    }

    [Fact]
    public void MatchingIsOrdinalAndCaseSensitive()
    {
        (long a, long b) = ReadPair("""{"A":9,"a":1,"B":9,"b":2}""");

        Assert.Equal(1, a);
        Assert.Equal(2, b);
    }

    [Fact]
    public void AnEmptyObjectYieldsNoProperties()
    {
        (long a, long b) = ReadPair("{}");

        Assert.Equal(0, a);
        Assert.Equal(0, b);
    }

    // Every accessor delegates to JsonValueReader, so a malformed value is a JsonException naming
    // the model and property -- never Utf8JsonReader's own InvalidOperationException, which the
    // transport does not recognise as a malformed body.
    [Fact]
    public void AMalformedValueThrowsJsonExceptionNamingTheModelAndProperty()
    {
        JsonException error = Assert.Throws<JsonException>(() => ReadPair("""{"a":"not a number"}"""));

        Assert.Contains("Probe", error.Message, StringComparison.Ordinal);
        Assert.Contains("a", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWalkOverSomethingThatIsNotAnObjectThrows()
    {
        JsonException error = Assert.Throws<JsonException>(() =>
        {
            Utf8JsonReader reader = new("[1,2]"u8);
            reader.Read();

            StreamEventWalk walk = new(ref reader, "Probe");
            _ = walk.NextProperty(ref reader);
        });

        Assert.Contains("Probe", error.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run it and watch it fail to build**

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter FullyQualifiedName~StreamEventWalkTests
```

Expected: build failure, `error CS0246: The type or namespace name 'StreamEventWalk' could not be found`. That is the feature being missing, which is the right reason.

- [ ] **Step 3: Write `StreamEventWalk`**

Create `src/MassiveDotNet.WebSocket/Internal/StreamEventWalk.cs`:

```csharp
using System.Text.Json;
using MassiveDotNet.Serialization;
using MassiveDotNet.WebSocket.Events;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>
/// Walks the properties of one streaming event object: skips any value the converter does not
/// consume, and reads every value it does consume through <see cref="JsonValueReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every streaming converter goes through this, so the two halves of a defect shape the #20
/// whole-branch review named have nowhere to live. A converter written against this has no
/// <c>else</c> arm to forget, because <see cref="NextProperty"/> performs the skip itself; and no
/// way to read a value off the raw reader, because it asks this type for the value instead. A
/// missed <c>Skip()</c> is worth this much care precisely because it does not throw: it leaves the
/// reader mid-value, so the next property is read from the wrong token and deserializes wrong.
/// </para>
/// <para>
/// A plain <see langword="struct" /> rather than a <c>ref struct</c>, and the reader travels as a
/// parameter rather than living in a field. Both alternatives are rejected by the compiler: a
/// <c>ref Utf8JsonReader</c> field is <c>CS9050</c>, a ref field cannot refer to a ref struct, and
/// a <c>ref struct</c> receiver taking one is <c>CS8350</c>, since such a receiver might capture
/// the reference. A plain struct cannot hold a ref field at all, so there is nothing left to
/// reject. The cost is <c>ref reader</c> at every call site.
/// </para>
/// </remarks>
internal struct StreamEventWalk
{
    private readonly string _model;

    // Whether the CURRENT property's value has been read. False means the walk is parked on a
    // property name whose value nobody wanted, and NextProperty owes it a skip. True at
    // construction because there is no previous property to skip.
    private bool _valueConsumed;

    /// <summary>Begins a walk over the object the reader is positioned on.</summary>
    /// <param name="reader">A reader positioned on the object's opening token.</param>
    /// <param name="model">The model being read, named in every failure message.</param>
    /// <exception cref="JsonException">The reader is not positioned on an object.</exception>
    public StreamEventWalk(ref Utf8JsonReader reader, string model)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected an object for {model}, but found a {reader.TokenType} token.");
        }

        _model = model;
        _valueConsumed = true;
    }

    /// <summary>
    /// Advances to the next property name, skipping the current property's value if no accessor
    /// consumed it.
    /// </summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <returns><see langword="false"/> at the end of the object.</returns>
    public bool NextProperty(ref Utf8JsonReader reader)
    {
        if (!_valueConsumed)
        {
            // Read moves onto the value; Skip walks past its whole subtree, which is what makes an
            // unrecognised object or array cost the same as an unrecognised scalar.
            reader.Read();
            reader.Skip();
        }

        _valueConsumed = false;

        return reader.Read() && reader.TokenType == JsonTokenType.PropertyName;
    }

    /// <summary>Reads a ticker symbol through the pool, so a repeat symbol allocates nothing.</summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <param name="tickers">The pool to intern through.</param>
    /// <param name="property">The wire property being read, named in a failure message.</param>
    /// <returns>The interned symbol.</returns>
    /// <exception cref="JsonException">The value is not a string.</exception>
    /// <remarks>
    /// <see cref="TickerPool.Intern(ref Utf8JsonReader)"/> reads the raw UTF-8 bytes directly for
    /// the pooling fast path, bypassing <see cref="JsonValueReader"/>, so the token check happens
    /// here instead: without it a null or numeric symbol would surface as
    /// <see cref="InvalidOperationException"/> rather than the <see cref="JsonException"/> every
    /// other field throws.
    /// </remarks>
    public string Ticker(ref Utf8JsonReader reader, TickerPool tickers, string property)
    {
        Advance(ref reader);

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"Expected a string for {_model}.{property}, but found a {reader.TokenType} token.");
        }

        return tickers.Intern(ref reader);
    }

    /// <summary>Reads a string, or <see langword="null"/> from a JSON null.</summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <param name="property">The wire property being read, named in a failure message.</param>
    /// <returns>The string, or <see langword="null"/>.</returns>
    public string? String(ref Utf8JsonReader reader, string property)
    {
        Advance(ref reader);

        return JsonValueReader.ReadString(ref reader, _model, property);
    }

    /// <summary>Reads a boolean.</summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <param name="property">The wire property being read, named in a failure message.</param>
    /// <returns>The boolean.</returns>
    public bool Boolean(ref Utf8JsonReader reader, string property)
    {
        Advance(ref reader);

        return JsonValueReader.ReadBoolean(ref reader, _model, property);
    }

    /// <summary>Reads a 32-bit integer.</summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <param name="property">The wire property being read, named in a failure message.</param>
    /// <returns>The integer.</returns>
    public int Int32(ref Utf8JsonReader reader, string property)
    {
        Advance(ref reader);

        return JsonValueReader.ReadInt32(ref reader, _model, property);
    }

    /// <summary>Reads a 32-bit integer, or <see langword="null"/> from a JSON null.</summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <param name="property">The wire property being read, named in a failure message.</param>
    /// <returns>The integer, or <see langword="null"/>.</returns>
    public int? NullableInt32(ref Utf8JsonReader reader, string property)
    {
        Advance(ref reader);

        return JsonValueReader.ReadNullableInt32(ref reader, _model, property);
    }

    /// <summary>Reads a 64-bit integer.</summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <param name="property">The wire property being read, named in a failure message.</param>
    /// <returns>The integer.</returns>
    public long Int64(ref Utf8JsonReader reader, string property)
    {
        Advance(ref reader);

        return JsonValueReader.ReadInt64(ref reader, _model, property);
    }

    /// <summary>Reads a 64-bit integer, or <see langword="null"/> from a JSON null.</summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <param name="property">The wire property being read, named in a failure message.</param>
    /// <returns>The integer, or <see langword="null"/>.</returns>
    public long? NullableInt64(ref Utf8JsonReader reader, string property)
    {
        Advance(ref reader);

        return JsonValueReader.ReadNullableInt64(ref reader, _model, property);
    }

    /// <summary>Reads a double.</summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <param name="property">The wire property being read, named in a failure message.</param>
    /// <returns>The double.</returns>
    public double Double(ref Utf8JsonReader reader, string property)
    {
        Advance(ref reader);

        return JsonValueReader.ReadDouble(ref reader, _model, property);
    }

    /// <summary>Reads an integer code array into a <see cref="ConditionSet"/>.</summary>
    /// <param name="reader">The reader this walk is driving.</param>
    /// <param name="property">The wire property being read, named in a failure message.</param>
    /// <returns>The parsed set, empty from a JSON null.</returns>
    public ConditionSet Conditions(ref Utf8JsonReader reader, string property)
    {
        Advance(ref reader);

        return ConditionSetSerialization.Read(ref reader, _model, property);
    }

    private void Advance(ref Utf8JsonReader reader)
    {
        reader.Read();
        _valueConsumed = true;
    }
}
```

- [ ] **Step 4: Run the tests and watch them pass**

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter FullyQualifiedName~StreamEventWalkTests
```

Expected: PASS, all cases.

- [ ] **Step 5: Prove the auto-skip assertion is load-bearing (D31)**

Delete the skip from `NextProperty`, leaving:

```csharp
    public bool NextProperty(ref Utf8JsonReader reader)
    {
        _valueConsumed = false;

        return reader.Read() && reader.TokenType == JsonTokenType.PropertyName;
    }
```

Re-run the same filter. Expected: `APropertyTheConverterDoesNotConsumeIsSkippedWholesale` fails on the object, array, and nested cases with `b` reading `0` rather than `2` — **not** with an exception. Confirm the failure message shows a wrong value, not a throw; that is the whole point of asserting a property that comes after the skipped one.

Then restore the method exactly as written in Step 3 and re-run to confirm green.

- [ ] **Step 6: Build warning-free and commit**

```bash
dotnet build MassiveDotNet.slnx
git add src/MassiveDotNet.WebSocket/Internal/StreamEventWalk.cs tests/MassiveDotNet.WebSocket.Tests/StreamEventWalkTests.cs
git commit -m "$(cat <<'MSG'
feat: extract the streaming property walk into one cursor

The #20 whole-branch review named this defect shape and could not
extract it, because #20 had no live instance left: a hand-written
Utf8JsonReader walk that reads a value without going through
JsonValueReader, and/or does not Skip() an unrecognised property. #21
creates four instances of it and #53-#58 create twenty more, so it is
extracted before the copies exist rather than after seven of them do,
which is what the other two recurring shapes on that branch cost.

Both halves are closed structurally rather than by review. NextProperty
performs the skip itself, so a converter has no else arm to forget, and
every value is read through JsonValueReader, so a converter has no way
to reach the raw reader. The skip matters because a missed one does not
throw -- it leaves the reader mid-value and the next property is read
from the wrong token, which deserializes wrong.

The cursor is a plain struct taking the reader as a parameter because
neither natural alternative compiles: a ref Utf8JsonReader field is
CS9050 and a ref struct receiver taking one is CS8350.

Watched failing before it landed: with the skip removed, the object,
array and nested cases report b as 0 rather than 2, silently, with no
exception -- which is why every case asserts a property that comes after
the skipped one.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB
MSG
)"
```

---

## Task 2: Move the two existing converters onto the cursor

Leaving `StockTradeConverter` and `StockQuoteConverter` on their own hand-written walks would defeat Task 1: two copies would survive to drift from the shared one. This is a pure refactor — the existing `EventParsingTests` are the safety net and must stay green without modification.

**Files:**
- Modify: `src/MassiveDotNet.WebSocket/Internal/StockTradeConverter.cs`
- Modify: `src/MassiveDotNet.WebSocket/Internal/StockQuoteConverter.cs`
- Test: `tests/MassiveDotNet.WebSocket.Tests/EventParsingTests.cs`

**Interfaces:**
- Consumes: `StreamEventWalk` from Task 1, exactly as its Produces block declares.
- Produces: nothing new. `StockTradeConverter(TickerPool)` and `StockQuoteConverter(TickerPool)` keep their existing constructors and behaviour.

- [ ] **Step 1: Add the test the existing suite is missing**

`EventParsingTests.AnUnknownPropertyIsSkippedRatherThanThrowing` covers only an unknown *scalar*. Add the nested cases beside it, in `tests/MassiveDotNet.WebSocket.Tests/EventParsingTests.cs`:

```csharp
    // The existing unknown-property test uses a scalar, which a missed Skip() survives by accident:
    // the reader lands on the value and the next Read finds the following property name anyway. An
    // unknown OBJECT or ARRAY is what actually distinguishes a correct walk, so the assertion is on
    // a field that comes after it.
    [Theory]
    [InlineData("""{"ev":"T","sym":"MSFT","future":{"nested":[1,2]},"i":"12345","q":7}""")]
    [InlineData("""{"ev":"T","sym":"MSFT","future":[1,[2,{"x":3}]],"i":"12345","q":7}""")]
    public void AnUnknownNestedPropertyIsSkippedWholesale(string json)
    {
        StockTrade trade = ReadTrade($"[{json}]");

        Assert.Equal("MSFT", trade.Ticker);
        Assert.Equal("12345", trade.TradeId);
        Assert.Equal(7, trade.SequenceNumber);
    }
```

- [ ] **Step 2: Run it against the current, un-refactored converter**

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter FullyQualifiedName~AnUnknownNestedPropertyIsSkippedWholesale
```

Expected: PASS. The existing converter already handles this correctly — that is the point. This test is not proving a bug; it is the net that catches the refactor breaking something the old code got right. Record that it passed before the refactor.

- [ ] **Step 3: Rewrite `StockTradeConverter.Read`**

Replace the whole method body in `src/MassiveDotNet.WebSocket/Internal/StockTradeConverter.cs`. Leave `Write` untouched. Also delete the now-unused `using MassiveDotNet.Serialization;` if the file no longer references `JsonValueReader`.

```csharp
    public override StockTrade Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        StreamEventWalk walk = new(ref reader, Model);

        string? ticker = null;
        string? tradeId = null;
        int exchangeId = 0;
        int? tape = null;
        double price = 0;
        long size = 0;
        string? decimalSize = null;
        ConditionSet conditions = default;
        long sipTimestamp = 0;
        long? participantTimestamp = null;
        long sequenceNumber = 0;
        int? trfId = null;
        long? trfTimestamp = null;

        // Every property the wire may carry, and nothing else: "ev" and anything Massive adds later
        // are skipped by the walk itself, so this loop has no default arm to get wrong.
        while (walk.NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("sym"u8)) { ticker = walk.Ticker(ref reader, tickers, "sym"); }
            else if (reader.ValueTextEquals("i"u8)) { tradeId = walk.String(ref reader, "i"); }
            else if (reader.ValueTextEquals("x"u8)) { exchangeId = walk.Int32(ref reader, "x"); }
            else if (reader.ValueTextEquals("z"u8)) { tape = walk.NullableInt32(ref reader, "z"); }
            else if (reader.ValueTextEquals("p"u8)) { price = walk.Double(ref reader, "p"); }
            else if (reader.ValueTextEquals("s"u8)) { size = walk.Int64(ref reader, "s"); }
            else if (reader.ValueTextEquals("ds"u8)) { decimalSize = walk.String(ref reader, "ds"); }
            else if (reader.ValueTextEquals("c"u8)) { conditions = walk.Conditions(ref reader, "c"); }
            else if (reader.ValueTextEquals("t"u8)) { sipTimestamp = walk.Int64(ref reader, "t"); }
            else if (reader.ValueTextEquals("pt"u8)) { participantTimestamp = walk.NullableInt64(ref reader, "pt"); }
            else if (reader.ValueTextEquals("q"u8)) { sequenceNumber = walk.Int64(ref reader, "q"); }
            else if (reader.ValueTextEquals("trfi"u8)) { trfId = walk.NullableInt32(ref reader, "trfi"); }
            else if (reader.ValueTextEquals("trft"u8)) { trfTimestamp = walk.NullableInt64(ref reader, "trft"); }
        }

        return new StockTrade
        {
            Ticker = ticker ?? throw new JsonException($"{Model} carried no 'sym'."),
            TradeId = tradeId ?? throw new JsonException($"{Model} carried no 'i'."),
            ExchangeId = exchangeId,
            Tape = tape,
            Price = price,
            Size = size,
            DecimalSize = decimalSize,
            Conditions = conditions,
            SipTimestampMilliseconds = sipTimestamp,
            ParticipantTimestampMilliseconds = participantTimestamp,
            SequenceNumber = sequenceNumber,
            TrfId = trfId,
            TrfTimestampMilliseconds = trfTimestamp,
        };
    }
```

Update the type's `<remarks>` to say the walk is shared rather than describing a per-converter shape:

```csharp
/// <remarks>
/// Hand-written rather than generated, because there is no OpenAPI description for the streaming
/// wire to generate from (D36). The object walk itself is <see cref="StreamEventWalk"/>, shared
/// with every other streaming converter, so ordinal matching, last value wins, wholesale skipping
/// of unknown properties, and scalars read through <see cref="JsonValueReader"/> are one
/// implementation rather than one per converter.
/// </remarks>
```

- [ ] **Step 4: Rewrite `StockQuoteConverter.Read`**

Same shape, in `src/MassiveDotNet.WebSocket/Internal/StockQuoteConverter.cs`. `Write` untouched, `<remarks>` updated the same way.

```csharp
    public override StockQuote Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        StreamEventWalk walk = new(ref reader, Model);

        string? ticker = null;
        int bidExchangeId = 0;
        double bidPrice = 0;
        long bidSize = 0;
        int askExchangeId = 0;
        double askPrice = 0;
        long askSize = 0;
        int? condition = null;
        ConditionSet indicators = default;
        long sipTimestamp = 0;
        long sequenceNumber = 0;
        int? tape = null;

        while (walk.NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("sym"u8)) { ticker = walk.Ticker(ref reader, tickers, "sym"); }
            else if (reader.ValueTextEquals("bx"u8)) { bidExchangeId = walk.Int32(ref reader, "bx"); }
            else if (reader.ValueTextEquals("bp"u8)) { bidPrice = walk.Double(ref reader, "bp"); }
            else if (reader.ValueTextEquals("bs"u8)) { bidSize = walk.Int64(ref reader, "bs"); }
            else if (reader.ValueTextEquals("ax"u8)) { askExchangeId = walk.Int32(ref reader, "ax"); }
            else if (reader.ValueTextEquals("ap"u8)) { askPrice = walk.Double(ref reader, "ap"); }
            else if (reader.ValueTextEquals("as"u8)) { askSize = walk.Int64(ref reader, "as"); }
            else if (reader.ValueTextEquals("c"u8)) { condition = walk.NullableInt32(ref reader, "c"); }
            // Indicators are the same wire shape as a trade's condition array.
            else if (reader.ValueTextEquals("i"u8)) { indicators = walk.Conditions(ref reader, "i"); }
            else if (reader.ValueTextEquals("t"u8)) { sipTimestamp = walk.Int64(ref reader, "t"); }
            else if (reader.ValueTextEquals("q"u8)) { sequenceNumber = walk.Int64(ref reader, "q"); }
            else if (reader.ValueTextEquals("z"u8)) { tape = walk.NullableInt32(ref reader, "z"); }
        }

        return new StockQuote
        {
            Ticker = ticker ?? throw new JsonException($"{Model} carried no 'sym'."),
            BidExchangeId = bidExchangeId,
            BidPrice = bidPrice,
            BidSize = bidSize,
            AskExchangeId = askExchangeId,
            AskPrice = askPrice,
            AskSize = askSize,
            Condition = condition,
            Indicators = indicators,
            SipTimestampMilliseconds = sipTimestamp,
            SequenceNumber = sequenceNumber,
            Tape = tape,
        };
    }
```

- [ ] **Step 5: Run the whole WebSocket suite**

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests
```

Expected: all 161 existing tests plus the new ones pass, with no edits to any existing test. If an existing test needed changing, the refactor changed behaviour and is wrong.

- [ ] **Step 6: Confirm no hand-written walk survives**

```bash
grep -n "JsonTokenType.PropertyName" src/MassiveDotNet.WebSocket/Internal/*.cs
```

Expected: exactly one hit, in `StreamEventWalk.cs`. Any other hit is a copy of the walk that Task 1 was supposed to eliminate.

- [ ] **Step 7: Build warning-free and commit**

```bash
dotnet build MassiveDotNet.slnx
git add src/MassiveDotNet.WebSocket/Internal/StockTradeConverter.cs src/MassiveDotNet.WebSocket/Internal/StockQuoteConverter.cs tests/MassiveDotNet.WebSocket.Tests/EventParsingTests.cs
git commit -m "$(cat <<'MSG'
refactor: move the trade and quote converters onto the shared walk

Extracting the cursor and leaving these two on their own copies would
have defeated the extraction: two hand-written walks would survive to
drift from the shared one, which is the outcome D15 rejects for filter
rendering and D32 rejects for scalar reads. After this there is exactly
one JsonTokenType.PropertyName in the package.

Pure refactor, so the existing tests are the net and none of them
changed. Added the case the suite was missing first, and confirmed it
passed against the old converter before touching it: the existing
unknown-property test uses a scalar, which a missed Skip() survives by
accident, so it could never have caught the defect it looks like it
covers. An unknown object or array is what distinguishes a correct walk.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB
MSG
)"
```

---

## Task 3: `StockAggregate`, serving both `A` and `AM`

**Files:**
- Create: `src/MassiveDotNet.WebSocket/Events/StockAggregate.cs`
- Create: `src/MassiveDotNet.WebSocket/Internal/StockAggregateConverter.cs`
- Modify: `src/MassiveDotNet.WebSocket/StockTopic.cs`
- Modify: `tests/MassiveDotNet.WebSocket.Tests/Fixtures.cs`
- Test: `tests/MassiveDotNet.WebSocket.Tests/EventParsingTests.cs`

**Interfaces:**
- Consumes: `StreamEventWalk` (Task 1); `MassiveDotNet.Epoch.FromMilliseconds(long)`, which is in the `MassiveDotNet` namespace and therefore already in scope from `MassiveDotNet.WebSocket.Events` without a `using`.
- Produces: `public readonly record struct StockAggregate` with the members listed in Step 2; `internal sealed class StockAggregateConverter(TickerPool tickers, string topicCode) : JsonConverter<StockAggregate>`; `StockTopic.SecondAggregates` → `"A"` and `StockTopic.MinuteAggregates` → `"AM"`.

- [ ] **Step 1: Add the fixtures**

In `tests/MassiveDotNet.WebSocket.Tests/Fixtures.cs`, add three constants. The first two are Massive's published samples; the third is a live capture, which exists because the published samples omit `dv` and `dav` and a fixture drawn only from them would never exercise those fields.

```csharp
    /// <summary>
    /// The published sample for a second aggregate, the <c>A</c> topic, wrapped in the array the
    /// wire actually delivers. It omits <c>dv</c>, <c>dav</c> and <c>otc</c>, which is the
    /// documentation's own evidence that they are optional — see <see cref="StockSecondAggregateLive"/>
    /// for a frame that carries the first two.
    /// </summary>
    public const string StockSecondAggregate = """
        [{"ev":"A","sym":"SPCE","v":200,"av":8642007,"op":25.66,"vw":25.3981,"o":25.39,"c":25.39,"h":25.39,"l":25.39,"a":25.3714,"z":50,"s":1610144868000,"e":1610144869000}]
        """;

    /// <summary>
    /// The published sample for a minute aggregate, the <c>AM</c> topic. Field-for-field identical
    /// to the second aggregate above, which is why one model serves both (D-W14).
    /// </summary>
    public const string StockMinuteAggregate = """
        [{"ev":"AM","sym":"GTE","v":4110,"av":9470157,"op":0.4372,"vw":0.4488,"o":0.4488,"c":0.4486,"h":0.4489,"l":0.4486,"a":0.4352,"z":685,"s":1610144640000,"e":1610144700000}]
        """;

    /// <summary>
    /// A second aggregate captured live from <c>wss://socket.massive.com/stocks</c> on 2026-09-08
    /// at 09:52 ET. Committed because it carries <c>dv</c> and <c>dav</c>, which the published
    /// sample omits and no fixture drawn from that sample could exercise. Reviewed before
    /// committing: it carries no account identifier and no URL.
    /// </summary>
    public const string StockSecondAggregateLive = """
        [{"ev":"A","sym":"FCX","v":4989,"av":4332125,"op":75.98,"vw":78.0894,"o":78.1,"c":78.08,"h":78.125,"l":78.07,"a":76.8404,"z":62,"s":1788877043000,"e":1788877044000,"dv":"4989.0","dav":"4332125.038360"}]
        """;
```

- [ ] **Step 2: Write the failing tests**

In `tests/MassiveDotNet.WebSocket.Tests/EventParsingTests.cs`, add the reader helper beside `ReadTrade`/`ReadQuote`:

```csharp
    private static StockAggregate ReadAggregate(string json, string topicCode)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        reader.Read();  // [
        reader.Read();  // {

        return new StockAggregateConverter(new TickerPool(16), topicCode)
            .Read(ref reader, typeof(StockAggregate), JsonSerializerOptions.Default);
    }
```

And the tests:

```csharp
    [Fact]
    public void ThePublishedSecondAggregateSampleDeserializes()
    {
        StockAggregate bar = ReadAggregate(Fixtures.StockSecondAggregate, "A");

        Assert.Equal("SPCE", bar.Ticker);
        Assert.Equal(200, bar.Volume);
        Assert.Equal(8642007, bar.AccumulatedVolume);
        Assert.Equal(25.66, bar.OfficialOpenPrice);
        Assert.Equal(25.3981, bar.VolumeWeightedAveragePrice);
        Assert.Equal(25.39, bar.Open);
        Assert.Equal(25.39, bar.Close);
        Assert.Equal(25.39, bar.High);
        Assert.Equal(25.39, bar.Low);
        Assert.Equal(25.3714, bar.DailyVolumeWeightedAveragePrice);
        Assert.Equal(50, bar.AverageTradeSize);
    }

    [Fact]
    public void ThePublishedMinuteAggregateSampleDeserializesThroughTheSameModel()
    {
        StockAggregate bar = ReadAggregate(Fixtures.StockMinuteAggregate, "AM");

        Assert.Equal("GTE", bar.Ticker);
        Assert.Equal(4110, bar.Volume);
        Assert.Equal(685, bar.AverageTradeSize);
    }

    // D5, and the unit the A/AM topics document and send: milliseconds, unlike NOI and LULD, which
    // send nanoseconds for their own timestamp field.
    [Fact]
    public void AggregateWindowBoundsAreMillisecondsExposedAsInstants()
    {
        StockAggregate bar = ReadAggregate(Fixtures.StockSecondAggregate, "A");

        Assert.Equal(1610144868000, bar.StartTimestampMilliseconds);
        Assert.Equal(1610144869000, bar.EndTimestampMilliseconds);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1610144868000), bar.Start);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1610144869000), bar.End);
    }

    // A minute bar spans sixty seconds and a second bar one, which is how a caller tells the two
    // apart from one model (D-W14).
    [Fact]
    public void TheWindowLengthDistinguishesASecondBarFromAMinuteBar()
    {
        StockAggregate second = ReadAggregate(Fixtures.StockSecondAggregate, "A");
        StockAggregate minute = ReadAggregate(Fixtures.StockMinuteAggregate, "AM");

        Assert.Equal(Duration.FromSeconds(1), second.End - second.Start);
        Assert.Equal(Duration.FromMinutes(1), minute.End - minute.Start);
    }

    // The published sample omits all three, which is the documentation's own evidence that they
    // are optional. "otc" is documented as left off when false, so absent reads as false rather
    // than as an unknown.
    [Fact]
    public void AggregateFieldsAbsentFromTheSampleReadAsNullOrFalse()
    {
        StockAggregate bar = ReadAggregate(Fixtures.StockSecondAggregate, "A");

        Assert.Null(bar.DecimalVolume);
        Assert.Null(bar.DecimalAccumulatedVolume);
        Assert.False(bar.Otc);
    }

    // The live capture exists precisely because the published sample cannot exercise these.
    [Fact]
    public void TheLiveAggregateCaptureCarriesTheDecimalVolumesTheSampleOmits()
    {
        StockAggregate bar = ReadAggregate(Fixtures.StockSecondAggregateLive, "A");

        Assert.Equal("FCX", bar.Ticker);
        Assert.Equal("4989.0", bar.DecimalVolume);
        Assert.Equal("4332125.038360", bar.DecimalAccumulatedVolume);
    }

    [Fact]
    public void AnOtcAggregateReadsAsOtc()
    {
        StockAggregate bar = ReadAggregate(
            """[{"ev":"A","sym":"XYZ","v":1,"av":2,"op":1.0,"vw":1.0,"o":1.0,"c":1.0,"h":1.0,"l":1.0,"a":1.0,"z":1,"s":1,"e":2,"otc":true}]""",
            "A");

        Assert.True(bar.Otc);
    }

    [Fact]
    public void AnAggregateWithANonStringSymbolThrowsJsonException()
    {
        JsonException error = Assert.Throws<JsonException>(() =>
            ReadAggregate("""[{"ev":"A","sym":123,"v":1}]""", "A"));

        Assert.Contains("StockAggregate.sym", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAggregateCarryingNoSymbolThrowsJsonException()
    {
        JsonException error = Assert.Throws<JsonException>(() =>
            ReadAggregate("""[{"ev":"A","v":1}]""", "A"));

        Assert.Contains("StockAggregate", error.Message, StringComparison.Ordinal);
    }

    // The topic code the converter was built with is what it writes back, which is the whole reason
    // one model can serve two topics.
    [Theory]
    [InlineData("A")]
    [InlineData("AM")]
    public void AnAggregateRoundTripsThroughWriteAndReadUnderEitherTopicCode(string topicCode)
    {
        StockAggregate original = ReadAggregate(Fixtures.StockSecondAggregateLive, topicCode);

        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            new StockAggregateConverter(new TickerPool(16), topicCode)
                .Write(writer, original, JsonSerializerOptions.Default);
        }

        Assert.Contains(
            $"\"ev\":\"{topicCode}\"",
            Encoding.UTF8.GetString(buffer.WrittenSpan),
            StringComparison.Ordinal);

        Utf8JsonReader reader = new(buffer.WrittenSpan);
        reader.Read();
        StockAggregate roundTripped = new StockAggregateConverter(new TickerPool(16), topicCode)
            .Read(ref reader, typeof(StockAggregate), JsonSerializerOptions.Default);

        Assert.Equal(original, roundTripped);
    }
```

- [ ] **Step 3: Run and watch it fail to build**

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter FullyQualifiedName~Aggregate
```

Expected: build failure, `CS0246` on `StockAggregate` and `StockAggregateConverter`.

- [ ] **Step 4: Write the model**

Create `src/MassiveDotNet.WebSocket/Events/StockAggregate.cs`:

```csharp
using NodaTime;

namespace MassiveDotNet.WebSocket.Events;

/// <summary>One streamed stock aggregate bar: the <c>A</c> and <c>AM</c> topics.</summary>
/// <remarks>
/// One model serves both topics because their wire shapes are field-for-field identical, differing
/// only in the event code and the length of the window (D-W14). Which window produced a bar is
/// readable from <see cref="Start"/> and <see cref="End"/>: one second apart, or sixty.
/// <para>
/// A high-volume type, so a struct (D4). Window bounds are stored as raw Unix <b>millisecond</b>
/// values and exposed as <see cref="Instant"/> only when read (D5). The unit is this topic's own —
/// the imbalance and limit up-limit down topics send nanoseconds for their timestamp field, and
/// REST v3 sends nanoseconds for the conceptually similar tick timestamp.
/// </para>
/// </remarks>
public readonly record struct StockAggregate
{
    /// <summary>The ticker symbol, interned across events.</summary>
    public required string Ticker { get; init; }

    /// <summary>The tick volume within this window.</summary>
    public long Volume { get; init; }

    /// <summary>The tick volume including fractional shares, as the wire's decimal string.</summary>
    public string? DecimalVolume { get; init; }

    /// <summary>Today's accumulated volume.</summary>
    public long AccumulatedVolume { get; init; }

    /// <summary>Today's accumulated volume including fractional shares, as the wire's decimal string.</summary>
    public string? DecimalAccumulatedVolume { get; init; }

    /// <summary>Today's official opening price.</summary>
    public double OfficialOpenPrice { get; init; }

    /// <summary>This window's volume weighted average price.</summary>
    public double VolumeWeightedAveragePrice { get; init; }

    /// <summary>The opening tick price for this window.</summary>
    public double Open { get; init; }

    /// <summary>The closing tick price for this window.</summary>
    public double Close { get; init; }

    /// <summary>The highest tick price for this window.</summary>
    public double High { get; init; }

    /// <summary>The lowest tick price for this window.</summary>
    public double Low { get; init; }

    /// <summary>Today's volume weighted average price.</summary>
    public double DailyVolumeWeightedAveragePrice { get; init; }

    /// <summary>The average trade size within this window.</summary>
    public long AverageTradeSize { get; init; }

    /// <summary>The raw start of this window, in Unix milliseconds.</summary>
    public long StartTimestampMilliseconds { get; init; }

    /// <summary>The raw end of this window, in Unix milliseconds.</summary>
    public long EndTimestampMilliseconds { get; init; }

    /// <summary>Whether this bar is for an OTC ticker. The wire omits the field when false.</summary>
    public bool Otc { get; init; }

    /// <summary>When this window opened.</summary>
    public Instant Start => Epoch.FromMilliseconds(StartTimestampMilliseconds);

    /// <summary>When this window closed.</summary>
    public Instant End => Epoch.FromMilliseconds(EndTimestampMilliseconds);
}
```

- [ ] **Step 5: Write the converter**

Create `src/MassiveDotNet.WebSocket/Internal/StockAggregateConverter.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using MassiveDotNet.WebSocket.Events;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>Reads a <see cref="StockAggregate"/> straight off the reader's tokens.</summary>
/// <remarks>
/// Hand-written rather than generated, because there is no OpenAPI description for the streaming
/// wire to generate from (D36). The object walk is <see cref="StreamEventWalk"/>, shared with every
/// other streaming converter.
/// <para>
/// One converter serves both aggregate topics (D-W14), so the wire code it writes back is a
/// constructor argument rather than a constant: the <c>A</c> and <c>AM</c> payloads are identical
/// apart from that one field, and <c>Read</c> never consults it, since the dispatcher has already
/// used <c>ev</c> to choose this sink.
/// </para>
/// </remarks>
internal sealed class StockAggregateConverter(TickerPool tickers, string topicCode)
    : JsonConverter<StockAggregate>
{
    private const string Model = nameof(StockAggregate);

    public override StockAggregate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        StreamEventWalk walk = new(ref reader, Model);

        string? ticker = null;
        long volume = 0;
        string? decimalVolume = null;
        long accumulatedVolume = 0;
        string? decimalAccumulatedVolume = null;
        double officialOpenPrice = 0;
        double volumeWeightedAveragePrice = 0;
        double open = 0;
        double close = 0;
        double high = 0;
        double low = 0;
        double dailyVolumeWeightedAveragePrice = 0;
        long averageTradeSize = 0;
        long start = 0;
        long end = 0;
        bool otc = false;

        while (walk.NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("sym"u8)) { ticker = walk.Ticker(ref reader, tickers, "sym"); }
            else if (reader.ValueTextEquals("v"u8)) { volume = walk.Int64(ref reader, "v"); }
            else if (reader.ValueTextEquals("dv"u8)) { decimalVolume = walk.String(ref reader, "dv"); }
            else if (reader.ValueTextEquals("av"u8)) { accumulatedVolume = walk.Int64(ref reader, "av"); }
            else if (reader.ValueTextEquals("dav"u8)) { decimalAccumulatedVolume = walk.String(ref reader, "dav"); }
            else if (reader.ValueTextEquals("op"u8)) { officialOpenPrice = walk.Double(ref reader, "op"); }
            else if (reader.ValueTextEquals("vw"u8)) { volumeWeightedAveragePrice = walk.Double(ref reader, "vw"); }
            else if (reader.ValueTextEquals("o"u8)) { open = walk.Double(ref reader, "o"); }
            else if (reader.ValueTextEquals("c"u8)) { close = walk.Double(ref reader, "c"); }
            else if (reader.ValueTextEquals("h"u8)) { high = walk.Double(ref reader, "h"); }
            else if (reader.ValueTextEquals("l"u8)) { low = walk.Double(ref reader, "l"); }
            else if (reader.ValueTextEquals("a"u8)) { dailyVolumeWeightedAveragePrice = walk.Double(ref reader, "a"); }
            else if (reader.ValueTextEquals("z"u8)) { averageTradeSize = walk.Int64(ref reader, "z"); }
            else if (reader.ValueTextEquals("s"u8)) { start = walk.Int64(ref reader, "s"); }
            else if (reader.ValueTextEquals("e"u8)) { end = walk.Int64(ref reader, "e"); }
            else if (reader.ValueTextEquals("otc"u8)) { otc = walk.Boolean(ref reader, "otc"); }
        }

        return new StockAggregate
        {
            Ticker = ticker ?? throw new JsonException($"{Model} carried no 'sym'."),
            Volume = volume,
            DecimalVolume = decimalVolume,
            AccumulatedVolume = accumulatedVolume,
            DecimalAccumulatedVolume = decimalAccumulatedVolume,
            OfficialOpenPrice = officialOpenPrice,
            VolumeWeightedAveragePrice = volumeWeightedAveragePrice,
            Open = open,
            Close = close,
            High = high,
            Low = low,
            DailyVolumeWeightedAveragePrice = dailyVolumeWeightedAveragePrice,
            AverageTradeSize = averageTradeSize,
            StartTimestampMilliseconds = start,
            EndTimestampMilliseconds = end,
            Otc = otc,
        };
    }

    public override void Write(Utf8JsonWriter writer, StockAggregate value, JsonSerializerOptions options)
    {
        // Every field the reader understands is written back, optional ones only when present
        // (an absent optional is omitted, never written as null): a round trip that silently
        // dropped a field would be exactly the data loss this SDK refuses everywhere else. "otc" is
        // written only when true, because the wire's own convention is to omit it when false.
        writer.WriteStartObject();
        writer.WriteString("ev", topicCode);
        writer.WriteString("sym", value.Ticker);
        writer.WriteNumber("v", value.Volume);

        if (value.DecimalVolume is { } decimalVolume)
        {
            writer.WriteString("dv", decimalVolume);
        }

        writer.WriteNumber("av", value.AccumulatedVolume);

        if (value.DecimalAccumulatedVolume is { } decimalAccumulatedVolume)
        {
            writer.WriteString("dav", decimalAccumulatedVolume);
        }

        writer.WriteNumber("op", value.OfficialOpenPrice);
        writer.WriteNumber("vw", value.VolumeWeightedAveragePrice);
        writer.WriteNumber("o", value.Open);
        writer.WriteNumber("c", value.Close);
        writer.WriteNumber("h", value.High);
        writer.WriteNumber("l", value.Low);
        writer.WriteNumber("a", value.DailyVolumeWeightedAveragePrice);
        writer.WriteNumber("z", value.AverageTradeSize);
        writer.WriteNumber("s", value.StartTimestampMilliseconds);
        writer.WriteNumber("e", value.EndTimestampMilliseconds);

        if (value.Otc)
        {
            writer.WriteBoolean("otc", true);
        }

        writer.WriteEndObject();
    }
}
```

- [ ] **Step 6: Add the topic members**

In `src/MassiveDotNet.WebSocket/StockTopic.cs`, add to the enum after `Quotes`:

```csharp
    /// <summary>Second-by-second OHLC aggregate bars, wire code <c>A</c>.</summary>
    SecondAggregates,

    /// <summary>Minute-by-minute OHLC aggregate bars, wire code <c>AM</c>.</summary>
    MinuteAggregates,
```

And to `ToCode`, before the `_ =>` arm:

```csharp
        StockTopic.SecondAggregates => "A",
        StockTopic.MinuteAggregates => "AM",
```

- [ ] **Step 7: Run the tests and watch them pass**

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter FullyQualifiedName~Aggregate
```

Expected: PASS.

- [ ] **Step 8: Build warning-free and commit**

```bash
dotnet build MassiveDotNet.slnx
git add src/MassiveDotNet.WebSocket/Events/StockAggregate.cs src/MassiveDotNet.WebSocket/Internal/StockAggregateConverter.cs src/MassiveDotNet.WebSocket/StockTopic.cs tests/MassiveDotNet.WebSocket.Tests/Fixtures.cs tests/MassiveDotNet.WebSocket.Tests/EventParsingTests.cs
git commit -m "$(cat <<'MSG'
feat: add the stock aggregate topics, A and AM

The two are field-for-field identical, differing only in the event code
and the window length, so one model and one converter serve both, with
the code a constructor argument for Write to emit. Two models would name
the window in the type, and would cost a duplicated model and converter
in every market -- twelve of each across six -- kept in sync by hand,
which is the duplication this issue exists to cut. The window is not
lost: a bar carries its own start and end, one second apart or sixty.

The published samples omit dv and dav, so a live frame captured on
2026-09-08 is committed beside them; a fixture drawn only from the
samples could never exercise either field. otc is absent-means-false by
the wire's own convention, so it reads as false rather than as unknown
and is written back only when true.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB
MSG
)"
```

---

## Task 4: `StockImbalance`, the `NOI` topic

**Files:**
- Create: `src/MassiveDotNet.WebSocket/Events/StockImbalance.cs`
- Create: `src/MassiveDotNet.WebSocket/Internal/StockImbalanceConverter.cs`
- Modify: `src/MassiveDotNet.WebSocket/StockTopic.cs`
- Modify: `tests/MassiveDotNet.WebSocket.Tests/Fixtures.cs`
- Test: `tests/MassiveDotNet.WebSocket.Tests/EventParsingTests.cs`

**Interfaces:**
- Consumes: `StreamEventWalk` (Task 1); `MassiveDotNet.Epoch.FromNanoseconds(long)`.
- Produces: `public readonly record struct StockImbalance`; `internal sealed class StockImbalanceConverter(TickerPool tickers) : JsonConverter<StockImbalance>`; `StockTopic.Imbalances` → `"NOI"`.

**Two wire facts that differ from every topic before it.** The ticker arrives as `T`, not `sym` — the same letter that is the *trade topic's* wire code. And `t` is Unix **nanoseconds**, documented and sampled that way, unlike the aggregate topics' milliseconds.

**No live capture is possible.** The probe on 2026-09-08 found this topic is not entitled on the available key: the server answered `{"ev":"status","status":"error","message":"not authorized"}`. The published sample is therefore the only fixture, and the model does not make `a` required, because its absence on a real event has never been observed either way.

- [ ] **Step 1: Add the fixture**

In `tests/MassiveDotNet.WebSocket.Tests/Fixtures.cs`:

```csharp
    /// <summary>
    /// The published sample for a net order imbalance event, the <c>NOI</c> topic, wrapped in the
    /// array the wire actually delivers. No live capture accompanies it: the topic answered
    /// <c>not authorized</c> on the available key on 2026-09-08 (issue #60).
    /// </summary>
    public const string StockImbalance = """
        [{"ev":"NOI","T":"NTEST.Q","t":1601318039223013600,"at":930,"a":"M","i":44,"x":10,"o":480,"p":440,"b":25.03}]
        """;
```

- [ ] **Step 2: Write the failing tests**

In `tests/MassiveDotNet.WebSocket.Tests/EventParsingTests.cs`, add the helper:

```csharp
    private static StockImbalance ReadImbalance(string json)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        reader.Read();  // [
        reader.Read();  // {

        return new StockImbalanceConverter(new TickerPool(16))
            .Read(ref reader, typeof(StockImbalance), JsonSerializerOptions.Default);
    }
```

And the tests:

```csharp
    [Fact]
    public void ThePublishedImbalanceSampleDeserializes()
    {
        StockImbalance imbalance = ReadImbalance(Fixtures.StockImbalance);

        Assert.Equal("NTEST.Q", imbalance.Ticker);
        Assert.Equal("M", imbalance.AuctionType);
        Assert.Equal(44, imbalance.SymbolSequence);
        Assert.Equal(10, imbalance.ExchangeId);
        Assert.Equal(480, imbalance.ImbalanceQuantity);
        Assert.Equal(440, imbalance.PairedQuantity);
        Assert.Equal(25.03, imbalance.BookClearingPrice);
    }

    // This topic sends the ticker as "T", not "sym" -- the same letter that is the trade topic's
    // own wire code. A converter that assumed "sym" would find no ticker and throw on every event.
    [Fact]
    public void TheImbalanceTickerIsReadFromTheCapitalTProperty()
    {
        StockImbalance imbalance = ReadImbalance(Fixtures.StockImbalance);

        Assert.Equal("NTEST.Q", imbalance.Ticker);
    }

    // Nanoseconds, documented and sampled that way -- unlike the aggregate topics, which send
    // milliseconds. Read as milliseconds this instant would land roughly fifty million years out.
    [Fact]
    public void TheImbalanceTimestampIsNanosecondsExposedAsAnInstant()
    {
        StockImbalance imbalance = ReadImbalance(Fixtures.StockImbalance);

        Assert.Equal(1601318039223013600, imbalance.TimestampNanoseconds);
        Assert.Equal(Epoch.FromNanoseconds(1601318039223013600), imbalance.Timestamp);
        Assert.Equal(2020, imbalance.Timestamp.InUtc().Year);
    }

    // D5's shape applied to a wall clock rather than an epoch: the wire's own (hour x 100) + minutes
    // encoding is stored raw and the NodaTime type computed on read (rule 12's vocabulary).
    [Theory]
    [InlineData(930, 9, 30)]
    [InlineData(1600, 16, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(2359, 23, 59)]
    public void TheAuctionTimeCodeIsExposedAsALocalTime(int code, int hour, int minute)
    {
        StockImbalance imbalance = ReadImbalance(
            $$"""[{"ev":"NOI","T":"AAPL","t":1,"at":{{code}},"a":"M","i":1,"x":1,"o":1,"p":1,"b":1.0}]""");

        Assert.Equal(code, imbalance.AuctionTimeCode);
        Assert.Equal(new LocalTime(hour, minute), imbalance.AuctionTime);
    }

    // A computed property must not throw on a value the server chose, so a code that is not a wall
    // clock reads as null and the raw code stays available.
    [Theory]
    [InlineData(2400)]
    [InlineData(999)]
    [InlineData(-1)]
    [InlineData(1275)]
    public void AnAuctionTimeCodeThatIsNotAWallClockReadsAsNull(int code)
    {
        StockImbalance imbalance = ReadImbalance(
            $$"""[{"ev":"NOI","T":"AAPL","t":1,"at":{{code}},"a":"M","i":1,"x":1,"o":1,"p":1,"b":1.0}]""");

        Assert.Null(imbalance.AuctionTime);
        Assert.Equal(code, imbalance.AuctionTimeCode);
    }

    [Fact]
    public void AnImbalanceWithANonStringTickerThrowsJsonException()
    {
        JsonException error = Assert.Throws<JsonException>(() =>
            ReadImbalance("""[{"ev":"NOI","T":123,"t":1}]"""));

        Assert.Contains("StockImbalance.T", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnImbalanceCarryingNoTickerThrowsJsonException()
    {
        JsonException error = Assert.Throws<JsonException>(() =>
            ReadImbalance("""[{"ev":"NOI","t":1}]"""));

        Assert.Contains("StockImbalance", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnImbalanceRoundTripsThroughWriteAndRead()
    {
        StockImbalance original = ReadImbalance(Fixtures.StockImbalance);

        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            new StockImbalanceConverter(new TickerPool(16))
                .Write(writer, original, JsonSerializerOptions.Default);
        }

        Utf8JsonReader reader = new(buffer.WrittenSpan);
        reader.Read();
        StockImbalance roundTripped = new StockImbalanceConverter(new TickerPool(16))
            .Read(ref reader, typeof(StockImbalance), JsonSerializerOptions.Default);

        Assert.Equal(original, roundTripped);
    }
```

- [ ] **Step 3: Run and watch it fail to build**

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter FullyQualifiedName~Imbalance
```

Expected: build failure, `CS0246` on `StockImbalance` and `StockImbalanceConverter`.

- [ ] **Step 4: Write the model**

Create `src/MassiveDotNet.WebSocket/Events/StockImbalance.cs`:

```csharp
using NodaTime;

namespace MassiveDotNet.WebSocket.Events;

/// <summary>One streamed net order imbalance: the <c>NOI</c> topic.</summary>
/// <remarks>
/// Auction imbalance updates, mostly at the opening and closing auctions but also during
/// ticker-specific halts and mini-auctions. A struct for consistency with every other event type
/// (D4), though this topic is far lower volume than trades or aggregates.
/// <para>
/// Two things differ from the trade, quote and aggregate topics. The ticker arrives as <c>T</c>
/// rather than <c>sym</c> — the same letter that is the trade topic's own wire code — and the
/// timestamp is Unix <b>nanoseconds</b> rather than milliseconds. Both are read from this topic's
/// own documentation, never inferred from a sibling.
/// </para>
/// </remarks>
public readonly record struct StockImbalance
{
    /// <summary>The ticker symbol, interned across events.</summary>
    public required string Ticker { get; init; }

    /// <summary>The raw event timestamp, in Unix nanoseconds.</summary>
    public long TimestampNanoseconds { get; init; }

    /// <summary>
    /// The raw auction time as the wire encodes it: <c>(hour × 100) + minutes</c> in Eastern time,
    /// so <c>930</c> is 09:30 and <c>1600</c> is 16:00.
    /// </summary>
    /// <remarks>
    /// Kept alongside <see cref="AuctionTime"/> so a code the wire sends that is not a wall clock
    /// is still readable, rather than being lost behind a <see langword="null"/>.
    /// </remarks>
    public int AuctionTimeCode { get; init; }

    /// <summary>
    /// The auction type: <c>O</c> early opening, <c>M</c> core opening, <c>H</c> reopening after a
    /// halt, <c>C</c> closing, <c>P</c> extreme closing imbalance, <c>R</c> regulatory closing
    /// imbalance.
    /// </summary>
    /// <remarks>
    /// A <see langword="string"/> rather than an enum, unlike <see cref="StockTopic"/>. That enum
    /// exists because the server silently ignores a topic code it does not recognise, so a caller's
    /// typo is unrecoverable and is made unrepresentable instead — an argument about a value the
    /// SDK <b>sends</b>. This is a value the SDK <b>receives</b>, where an enum could only throw on
    /// a code Massive adds later, failing a whole event over one field, or misfile it as an
    /// existing member. A string carries what arrived.
    /// </remarks>
    public string? AuctionType { get; init; }

    /// <summary>The symbol sequence number.</summary>
    public long SymbolSequence { get; init; }

    /// <summary>The exchange ID.</summary>
    public int ExchangeId { get; init; }

    /// <summary>The imbalance quantity.</summary>
    public long ImbalanceQuantity { get; init; }

    /// <summary>The paired quantity.</summary>
    public long PairedQuantity { get; init; }

    /// <summary>The book clearing price.</summary>
    public double BookClearingPrice { get; init; }

    /// <summary>When the imbalance was published.</summary>
    public Instant Timestamp => Epoch.FromNanoseconds(TimestampNanoseconds);

    /// <summary>
    /// The wall-clock time the auction is planned for, in Eastern time, or <see langword="null"/>
    /// when <see cref="AuctionTimeCode"/> is not a valid wall clock.
    /// </summary>
    /// <remarks>
    /// Nullable rather than throwing: a computed property must not fail on a value the server chose,
    /// and the raw code remains available for a caller who wants to see what actually arrived.
    /// </remarks>
    public LocalTime? AuctionTime =>
        AuctionTimeCode is >= 0 and <= 2359 && AuctionTimeCode % 100 < 60
            ? new LocalTime(AuctionTimeCode / 100, AuctionTimeCode % 100)
            : null;
}
```

- [ ] **Step 5: Write the converter**

Create `src/MassiveDotNet.WebSocket/Internal/StockImbalanceConverter.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using MassiveDotNet.WebSocket.Events;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>Reads a <see cref="StockImbalance"/> straight off the reader's tokens.</summary>
/// <remarks>
/// Hand-written rather than generated, because there is no OpenAPI description for the streaming
/// wire to generate from (D36). The object walk is <see cref="StreamEventWalk"/>, shared with every
/// other streaming converter.
/// <para>
/// The ticker is read from <c>T</c>, which is this topic's spelling and is also the trade topic's
/// wire code. Nothing here reads <c>sym</c>.
/// </para>
/// </remarks>
internal sealed class StockImbalanceConverter(TickerPool tickers) : JsonConverter<StockImbalance>
{
    private const string Model = nameof(StockImbalance);

    public override StockImbalance Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        StreamEventWalk walk = new(ref reader, Model);

        string? ticker = null;
        long timestamp = 0;
        int auctionTimeCode = 0;
        string? auctionType = null;
        long symbolSequence = 0;
        int exchangeId = 0;
        long imbalanceQuantity = 0;
        long pairedQuantity = 0;
        double bookClearingPrice = 0;

        while (walk.NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("T"u8)) { ticker = walk.Ticker(ref reader, tickers, "T"); }
            else if (reader.ValueTextEquals("t"u8)) { timestamp = walk.Int64(ref reader, "t"); }
            else if (reader.ValueTextEquals("at"u8)) { auctionTimeCode = walk.Int32(ref reader, "at"); }
            else if (reader.ValueTextEquals("a"u8)) { auctionType = walk.String(ref reader, "a"); }
            else if (reader.ValueTextEquals("i"u8)) { symbolSequence = walk.Int64(ref reader, "i"); }
            else if (reader.ValueTextEquals("x"u8)) { exchangeId = walk.Int32(ref reader, "x"); }
            else if (reader.ValueTextEquals("o"u8)) { imbalanceQuantity = walk.Int64(ref reader, "o"); }
            else if (reader.ValueTextEquals("p"u8)) { pairedQuantity = walk.Int64(ref reader, "p"); }
            else if (reader.ValueTextEquals("b"u8)) { bookClearingPrice = walk.Double(ref reader, "b"); }
        }

        return new StockImbalance
        {
            Ticker = ticker ?? throw new JsonException($"{Model} carried no 'T'."),
            TimestampNanoseconds = timestamp,
            AuctionTimeCode = auctionTimeCode,
            AuctionType = auctionType,
            SymbolSequence = symbolSequence,
            ExchangeId = exchangeId,
            ImbalanceQuantity = imbalanceQuantity,
            PairedQuantity = pairedQuantity,
            BookClearingPrice = bookClearingPrice,
        };
    }

    public override void Write(Utf8JsonWriter writer, StockImbalance value, JsonSerializerOptions options)
    {
        // Every field the reader understands is written back, optional ones only when present
        // (an absent optional is omitted, never written as null).
        writer.WriteStartObject();
        writer.WriteString("ev", "NOI");
        writer.WriteString("T", value.Ticker);
        writer.WriteNumber("t", value.TimestampNanoseconds);
        writer.WriteNumber("at", value.AuctionTimeCode);

        if (value.AuctionType is { } auctionType)
        {
            writer.WriteString("a", auctionType);
        }

        writer.WriteNumber("i", value.SymbolSequence);
        writer.WriteNumber("x", value.ExchangeId);
        writer.WriteNumber("o", value.ImbalanceQuantity);
        writer.WriteNumber("p", value.PairedQuantity);
        writer.WriteNumber("b", value.BookClearingPrice);
        writer.WriteEndObject();
    }
}
```

- [ ] **Step 6: Add the topic member**

In `src/MassiveDotNet.WebSocket/StockTopic.cs`, after `MinuteAggregates`:

```csharp
    /// <summary>Net order imbalance auction events, wire code <c>NOI</c>.</summary>
    Imbalances,
```

And in `ToCode`:

```csharp
        StockTopic.Imbalances => "NOI",
```

- [ ] **Step 7: Run the tests and watch them pass**

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter FullyQualifiedName~Imbalance
```

Expected: PASS.

- [ ] **Step 8: Prove the ticker-property assertion is load-bearing (D31)**

Change the converter's ticker match from `"T"u8` to `"sym"u8` and re-run. Expected: `ThePublishedImbalanceSampleDeserializes`, `TheImbalanceTickerIsReadFromTheCapitalTProperty` and the round-trip test all fail with `StockImbalance carried no 'T'.` Restore and re-run green. This is the one mistake this topic invites, so it gets a watched failure.

- [ ] **Step 9: Build warning-free and commit**

```bash
dotnet build MassiveDotNet.slnx
git add src/MassiveDotNet.WebSocket/Events/StockImbalance.cs src/MassiveDotNet.WebSocket/Internal/StockImbalanceConverter.cs src/MassiveDotNet.WebSocket/StockTopic.cs tests/MassiveDotNet.WebSocket.Tests/Fixtures.cs tests/MassiveDotNet.WebSocket.Tests/EventParsingTests.cs
git commit -m "$(cat <<'MSG'
feat: add the stock net order imbalance topic, NOI

Two wire facts here differ from every topic before it, and both were
read from this topic's own documentation rather than inferred from a
sibling. The ticker arrives as "T", the same letter that is the trade
topic's wire code, and the timestamp is nanoseconds where the aggregate
topics send milliseconds. The "T" spelling is the mistake this topic
invites, so it has a watched failure: matching "sym" instead makes three
tests fail with "carried no 'T'".

The auction time is stored as the raw (hour x 100) + minutes code and
computed as a LocalTime, D5's shape applied to a wall clock rather than
an epoch. Nullable, because a computed property must not throw on a
value the server chose, and the raw code stays readable either way.

The auction type stays a string. D-W1 made StockTopic an enum because
the server silently ignores a code it does not recognise -- an argument
about a value we send. This is one we receive, where an enum could only
throw on a code Massive adds later or misfile it as an existing member.

No live fixture accompanies the published sample: the topic answered
"not authorized" on the available key on 2026-09-08 (#60).

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB
MSG
)"
```

---

## Task 5: `StockLimitUpLimitDown`, the `LULD` topic

**Files:**
- Create: `src/MassiveDotNet.WebSocket/Events/StockLimitUpLimitDown.cs`
- Create: `src/MassiveDotNet.WebSocket/Internal/StockLimitUpLimitDownConverter.cs`
- Modify: `src/MassiveDotNet.WebSocket/StockTopic.cs`
- Modify: `tests/MassiveDotNet.WebSocket.Tests/Fixtures.cs`
- Test: `tests/MassiveDotNet.WebSocket.Tests/EventParsingTests.cs`

**Interfaces:**
- Consumes: `StreamEventWalk` (Task 1); `MassiveDotNet.Epoch.FromNanoseconds(long)`; `ConditionSet` via `StreamEventWalk.Conditions`.
- Produces: `public readonly record struct StockLimitUpLimitDown`; `internal sealed class StockLimitUpLimitDownConverter(TickerPool tickers) : JsonConverter<StockLimitUpLimitDown>`; `StockTopic.LimitUpLimitDown` → `"LULD"`.

**The unit is the whole point of this task.** Massive's documentation says `t` is "The Timestamp in Unix MS". It is not. Its own published sample carries `1764086430905642800` and the live frame captured on 2026-09-08 carried `1788877046310003385` — both nineteen digits, both nanoseconds. Following the prose would compile, deserialize without error, and hand every caller an instant roughly fifty-six million years in the future. See D-W15 in the spec for the full argument.

`i` is an array of integer indicators, the same wire shape as a trade's conditions and a quote's own indicators, so it reuses `ConditionSet`.

- [ ] **Step 1: Add the fixtures**

In `tests/MassiveDotNet.WebSocket.Tests/Fixtures.cs`:

```csharp
    /// <summary>
    /// The published sample for a limit up-limit down event, the <c>LULD</c> topic, wrapped in the
    /// array the wire actually delivers. Note <c>t</c>: nineteen digits, which is nanoseconds,
    /// against the documentation's own prose claiming milliseconds (D-W15).
    /// </summary>
    public const string StockLimitUpLimitDown = """
        [{"ev":"LULD","T":"MSFT","h":492.99,"l":446.04,"i":[16],"z":3,"t":1764086430905642800,"q":5925769}]
        """;

    /// <summary>
    /// A limit up-limit down event captured live from <c>wss://socket.massive.com/stocks</c> on
    /// 2026-09-08 at 09:52 ET. Committed because it is the evidence that settles the timestamp
    /// unit against the documentation's prose: a fixture drawn from the published sample alone
    /// would be one contested document arguing with another part of itself. Reviewed before
    /// committing: it carries no account identifier and no URL.
    /// </summary>
    public const string StockLimitUpLimitDownLive = """
        [{"ev":"LULD","h":3.63,"l":2.97,"i":[15],"z":3,"T":"ATHR","t":1788877046310003385,"q":3209681}]
        """;
```

- [ ] **Step 2: Write the failing tests**

In `tests/MassiveDotNet.WebSocket.Tests/EventParsingTests.cs`, add the helper:

```csharp
    private static StockLimitUpLimitDown ReadLimitUpLimitDown(string json)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        reader.Read();  // [
        reader.Read();  // {

        return new StockLimitUpLimitDownConverter(new TickerPool(16))
            .Read(ref reader, typeof(StockLimitUpLimitDown), JsonSerializerOptions.Default);
    }
```

And the tests:

```csharp
    [Fact]
    public void ThePublishedLimitUpLimitDownSampleDeserializes()
    {
        StockLimitUpLimitDown band = ReadLimitUpLimitDown(Fixtures.StockLimitUpLimitDown);

        Assert.Equal("MSFT", band.Ticker);
        Assert.Equal(492.99, band.HighPrice);
        Assert.Equal(446.04, band.LowPrice);
        Assert.Equal([16], band.Indicators.AsSpan().ToArray());
        Assert.Equal(3, band.Tape);
        Assert.Equal(5925769, band.SequenceNumber);
    }

    // Like NOI and unlike every other stock topic, the ticker arrives as "T".
    [Fact]
    public void TheLimitUpLimitDownTickerIsReadFromTheCapitalTProperty()
    {
        StockLimitUpLimitDown band = ReadLimitUpLimitDown(Fixtures.StockLimitUpLimitDownLive);

        Assert.Equal("ATHR", band.Ticker);
    }

    // D-W15. Massive's documentation says this field is milliseconds; its own sample and the live
    // wire both say nanoseconds. The assertion is on the resulting YEAR rather than on the raw
    // long, because that is what distinguishes the two readings: as milliseconds these values land
    // roughly fifty-six million years out, which no equality check on the stored long would catch.
    [Fact]
    public void TheLimitUpLimitDownTimestampIsNanosecondsNotTheDocumentedMilliseconds()
    {
        StockLimitUpLimitDown published = ReadLimitUpLimitDown(Fixtures.StockLimitUpLimitDown);
        StockLimitUpLimitDown live = ReadLimitUpLimitDown(Fixtures.StockLimitUpLimitDownLive);

        Assert.Equal(1764086430905642800, published.TimestampNanoseconds);
        Assert.Equal(Epoch.FromNanoseconds(1764086430905642800), published.Timestamp);
        Assert.Equal(2025, published.Timestamp.InUtc().Year);
        Assert.Equal(2026, live.Timestamp.InUtc().Year);
    }

    [Fact]
    public void LimitUpLimitDownIndicatorsReadAsAConditionSet()
    {
        StockLimitUpLimitDown band = ReadLimitUpLimitDown(
            """[{"ev":"LULD","T":"AAPL","h":1.0,"l":0.5,"i":[9,10,11],"z":1,"t":1,"q":1}]""");

        Assert.Equal([9, 10, 11], band.Indicators.AsSpan().ToArray());
    }

    [Fact]
    public void ALimitUpLimitDownWithNoIndicatorsReadsAsAnEmptySet()
    {
        StockLimitUpLimitDown band = ReadLimitUpLimitDown(
            """[{"ev":"LULD","T":"AAPL","h":1.0,"l":0.5,"z":1,"t":1,"q":1}]""");

        Assert.Equal(0, band.Indicators.Count);
    }

    [Fact]
    public void ALimitUpLimitDownWithoutATapeReadsAsNull()
    {
        StockLimitUpLimitDown band = ReadLimitUpLimitDown(
            """[{"ev":"LULD","T":"AAPL","h":1.0,"l":0.5,"i":[16],"t":1,"q":1}]""");

        Assert.Null(band.Tape);
    }

    [Fact]
    public void ALimitUpLimitDownWithANonStringTickerThrowsJsonException()
    {
        JsonException error = Assert.Throws<JsonException>(() =>
            ReadLimitUpLimitDown("""[{"ev":"LULD","T":123,"t":1}]"""));

        Assert.Contains("StockLimitUpLimitDown.T", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALimitUpLimitDownCarryingNoTickerThrowsJsonException()
    {
        JsonException error = Assert.Throws<JsonException>(() =>
            ReadLimitUpLimitDown("""[{"ev":"LULD","t":1}]"""));

        Assert.Contains("StockLimitUpLimitDown", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALimitUpLimitDownRoundTripsThroughWriteAndRead()
    {
        StockLimitUpLimitDown original = ReadLimitUpLimitDown(Fixtures.StockLimitUpLimitDownLive);

        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            new StockLimitUpLimitDownConverter(new TickerPool(16))
                .Write(writer, original, JsonSerializerOptions.Default);
        }

        Utf8JsonReader reader = new(buffer.WrittenSpan);
        reader.Read();
        StockLimitUpLimitDown roundTripped = new StockLimitUpLimitDownConverter(new TickerPool(16))
            .Read(ref reader, typeof(StockLimitUpLimitDown), JsonSerializerOptions.Default);

        Assert.Equal(original, roundTripped);
    }
```

- [ ] **Step 3: Run and watch it fail to build**

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter FullyQualifiedName~LimitUpLimitDown
```

Expected: build failure, `CS0246` on `StockLimitUpLimitDown` and `StockLimitUpLimitDownConverter`.

- [ ] **Step 4: Write the model**

Create `src/MassiveDotNet.WebSocket/Events/StockLimitUpLimitDown.cs`:

```csharp
using NodaTime;

namespace MassiveDotNet.WebSocket.Events;

/// <summary>One streamed limit up-limit down band update: the <c>LULD</c> topic.</summary>
/// <remarks>
/// Price band updates, and the pauses, halts and resumptions that follow a breach. High volume
/// during regular hours, so a struct (D4).
/// <para>
/// The ticker arrives as <c>T</c> rather than <c>sym</c>, as it does on the imbalance topic.
/// </para>
/// <para>
/// <b>The timestamp is nanoseconds, and Massive's documentation says milliseconds.</b> That page
/// contradicts itself: its own published sample carries a nineteen-digit value, and a frame
/// captured live from <c>wss://socket.massive.com/stocks</c> on 2026-09-08 carried
/// <c>1788877046310003385</c>. Read as milliseconds either value lands roughly fifty-six million
/// years in the future — a binding that compiles, deserializes without error, and is silently
/// wrong, which is what D20 introduced <c>DateOrNanoseconds</c> to prevent on the request side.
/// The observation is pinned by a live test and flips the day either the wire or the documentation
/// moves (D-W15).
/// </para>
/// </remarks>
public readonly record struct StockLimitUpLimitDown
{
    /// <summary>The ticker symbol, interned across events.</summary>
    public required string Ticker { get; init; }

    /// <summary>The limit up price band.</summary>
    public double HighPrice { get; init; }

    /// <summary>The limit down price band.</summary>
    public double LowPrice { get; init; }

    /// <summary>
    /// The LULD indicators. The same wire shape as a trade's conditions and a quote's indicators —
    /// an array of integer codes — so it uses the same inline-capacity set.
    /// </summary>
    public ConditionSet Indicators { get; init; }

    /// <summary>The tape: 1 = NYSE, 2 = AMEX, 3 = Nasdaq.</summary>
    public int? Tape { get; init; }

    /// <summary>The raw event timestamp, in Unix nanoseconds — not the milliseconds the docs claim.</summary>
    public long TimestampNanoseconds { get; init; }

    /// <summary>The sequence number, increasing and unique per ticker but not contiguous.</summary>
    public long SequenceNumber { get; init; }

    /// <summary>When the band update was published.</summary>
    public Instant Timestamp => Epoch.FromNanoseconds(TimestampNanoseconds);
}
```

- [ ] **Step 5: Write the converter**

Create `src/MassiveDotNet.WebSocket/Internal/StockLimitUpLimitDownConverter.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using MassiveDotNet.WebSocket.Events;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>Reads a <see cref="StockLimitUpLimitDown"/> straight off the reader's tokens.</summary>
/// <remarks>
/// Hand-written rather than generated, because there is no OpenAPI description for the streaming
/// wire to generate from (D36). The object walk is <see cref="StreamEventWalk"/>, shared with every
/// other streaming converter. The ticker is read from <c>T</c>; nothing here reads <c>sym</c>.
/// </remarks>
internal sealed class StockLimitUpLimitDownConverter(TickerPool tickers)
    : JsonConverter<StockLimitUpLimitDown>
{
    private const string Model = nameof(StockLimitUpLimitDown);

    public override StockLimitUpLimitDown Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        StreamEventWalk walk = new(ref reader, Model);

        string? ticker = null;
        double highPrice = 0;
        double lowPrice = 0;
        ConditionSet indicators = default;
        int? tape = null;
        long timestamp = 0;
        long sequenceNumber = 0;

        while (walk.NextProperty(ref reader))
        {
            if (reader.ValueTextEquals("T"u8)) { ticker = walk.Ticker(ref reader, tickers, "T"); }
            else if (reader.ValueTextEquals("h"u8)) { highPrice = walk.Double(ref reader, "h"); }
            else if (reader.ValueTextEquals("l"u8)) { lowPrice = walk.Double(ref reader, "l"); }
            else if (reader.ValueTextEquals("i"u8)) { indicators = walk.Conditions(ref reader, "i"); }
            else if (reader.ValueTextEquals("z"u8)) { tape = walk.NullableInt32(ref reader, "z"); }
            else if (reader.ValueTextEquals("t"u8)) { timestamp = walk.Int64(ref reader, "t"); }
            else if (reader.ValueTextEquals("q"u8)) { sequenceNumber = walk.Int64(ref reader, "q"); }
        }

        return new StockLimitUpLimitDown
        {
            Ticker = ticker ?? throw new JsonException($"{Model} carried no 'T'."),
            HighPrice = highPrice,
            LowPrice = lowPrice,
            Indicators = indicators,
            Tape = tape,
            TimestampNanoseconds = timestamp,
            SequenceNumber = sequenceNumber,
        };
    }

    public override void Write(
        Utf8JsonWriter writer, StockLimitUpLimitDown value, JsonSerializerOptions options)
    {
        // Every field the reader understands is written back, optional ones only when present
        // (an absent optional is omitted, never written as null).
        writer.WriteStartObject();
        writer.WriteString("ev", "LULD");
        writer.WriteString("T", value.Ticker);
        writer.WriteNumber("h", value.HighPrice);
        writer.WriteNumber("l", value.LowPrice);
        ConditionSetSerialization.Write(writer, "i", value.Indicators);

        if (value.Tape is { } tape)
        {
            writer.WriteNumber("z", tape);
        }

        writer.WriteNumber("t", value.TimestampNanoseconds);
        writer.WriteNumber("q", value.SequenceNumber);
        writer.WriteEndObject();
    }
}
```

- [ ] **Step 6: Add the topic member**

In `src/MassiveDotNet.WebSocket/StockTopic.cs`, after `Imbalances`:

```csharp
    /// <summary>Limit up-limit down price band events, wire code <c>LULD</c>.</summary>
    LimitUpLimitDown,
```

And in `ToCode`:

```csharp
        StockTopic.LimitUpLimitDown => "LULD",
```

- [ ] **Step 7: Run the tests and watch them pass**

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter FullyQualifiedName~LimitUpLimitDown
```

Expected: PASS.

- [ ] **Step 8: Prove the unit assertion is load-bearing (D31)**

Change the model's computed property to `Epoch.FromMilliseconds(TimestampNanoseconds)` — the binding the documentation's prose would have produced. Re-run.

Expected: `TheLimitUpLimitDownTimestampIsNanosecondsNotTheDocumentedMilliseconds` fails on the year assertions, reporting a year in the tens of millions. Note that the raw-`long` assertion in that same test still **passes** under the wrong binding, which is exactly why the test asserts the year: a stored value is not evidence about the unit it is read in.

Restore and re-run green.

- [ ] **Step 9: Build warning-free and commit**

```bash
dotnet build MassiveDotNet.slnx
git add src/MassiveDotNet.WebSocket/Events/StockLimitUpLimitDown.cs src/MassiveDotNet.WebSocket/Internal/StockLimitUpLimitDownConverter.cs src/MassiveDotNet.WebSocket/StockTopic.cs tests/MassiveDotNet.WebSocket.Tests/Fixtures.cs tests/MassiveDotNet.WebSocket.Tests/EventParsingTests.cs
git commit -m "$(cat <<'MSG'
feat: add the stock limit up-limit down topic, LULD

This is the one place in the slice where the SDK contradicts Massive's
written description, so the evidence is committed alongside it. The
field table says t is "Unix MS". Its own published sample carries a
nineteen-digit value, and a frame captured live on 2026-09-08 carried
1788877046310003385. There is no reading under which both halves of that
page are right, and choosing the prose would compile, deserialize
without error, and hand every caller an instant fifty-six million years
out -- D20's silently-wrong unit arriving on the response side.

Watched failing: with the computed property switched to
Epoch.FromMilliseconds, the year assertions report a year in the tens of
millions while the raw-long assertion in that same test still passes.
That is why the test asserts the year at all -- a stored value is not
evidence about the unit it is read in.

The ticker arrives as "T" here as it does on NOI. Indicators are the
same integer-array shape as a trade's conditions, so they reuse
ConditionSet rather than growing a parallel type.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB
MSG
)"
```

---

## Task 6: The `MassiveStockStream` façade, and collapsing what would otherwise grow six times

**Files:**
- Modify: `src/MassiveDotNet.WebSocket/MassiveStockStream.cs`
- Test: `tests/MassiveDotNet.WebSocket.Tests/StockStreamTests.cs`

**Interfaces:**
- Consumes: `StockAggregateConverter(TickerPool, string)` (Task 3), `StockImbalanceConverter(TickerPool)` (Task 4), `StockLimitUpLimitDownConverter(TickerPool)` (Task 5), and the four `StockTopic` members those tasks added.
- Produces: `Task<MassiveTopicSubscription<StockAggregate>> SubscribeSecondAggregatesAsync(IReadOnlyCollection<string>, CancellationToken = default)`, `SubscribeMinuteAggregatesAsync` with the same signature, `Task<MassiveTopicSubscription<StockImbalance>> SubscribeImbalancesAsync(...)`, `Task<MassiveTopicSubscription<StockLimitUpLimitDown>> SubscribeLimitUpLimitDownAsync(...)`.

**Why the refactor is part of this task rather than a separate one.** Adding four topics the existing way means six copies of a locking body and six `?.Complete()` calls, and #53-#58 repeat that per market. `EventRaiser` and `StreamEventWalk` are both this same lesson learned late; this one is learned before the copies exist.

- [ ] **Step 1: Write the failing tests**

In `tests/MassiveDotNet.WebSocket.Tests/StockStreamTests.cs`, add:

```csharp
    [Fact]
    public async Task EachTopicSubscribesUnderItsOwnWireCode()
    {
        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync();
        await using MassiveStockStream _ = stream;

        socket.AutoAcknowledgeSubscribes = true;

        await stream.SubscribeSecondAggregatesAsync(["AAPL"], TestContext.Current.CancellationToken);
        await stream.SubscribeMinuteAggregatesAsync(["AAPL"], TestContext.Current.CancellationToken);
        await stream.SubscribeImbalancesAsync(["AAPL"], TestContext.Current.CancellationToken);
        await stream.SubscribeLimitUpLimitDownAsync(["AAPL"], TestContext.Current.CancellationToken);

        Assert.Contains(socket.Sent, frame => frame.Contains("\"params\":\"A.AAPL\"", StringComparison.Ordinal));
        Assert.Contains(socket.Sent, frame => frame.Contains("\"params\":\"AM.AAPL\"", StringComparison.Ordinal));
        Assert.Contains(socket.Sent, frame => frame.Contains("\"params\":\"NOI.AAPL\"", StringComparison.Ordinal));
        Assert.Contains(socket.Sent, frame => frame.Contains("\"params\":\"LULD.AAPL\"", StringComparison.Ordinal));
    }

    // Two topics over ONE model type. Sinks are keyed by wire code, not by CLR type, so this needs
    // nothing special from the dispatcher -- but it is the assumption D-W14 rests on, so it is
    // pinned rather than assumed.
    [Fact]
    public async Task TheTwoAggregateTopicsAreSeparateSequencesDespiteSharingAModel()
    {
        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync();
        await using MassiveStockStream _ = stream;

        socket.AutoAcknowledgeSubscribes = true;

        MassiveTopicSubscription<StockAggregate> second =
            await stream.SubscribeSecondAggregatesAsync(["AAPL"], TestContext.Current.CancellationToken);
        MassiveTopicSubscription<StockAggregate> minute =
            await stream.SubscribeMinuteAggregatesAsync(["AAPL"], TestContext.Current.CancellationToken);

        Assert.NotSame(second, minute);

        socket.EnqueueText(
            """[{"ev":"A","sym":"AAPL","v":1,"av":2,"op":1.0,"vw":1.0,"o":1.0,"c":1.0,"h":1.0,"l":1.0,"a":1.0,"z":1,"s":1000,"e":2000}]""");
        socket.EnqueueText(
            """[{"ev":"AM","sym":"AAPL","v":9,"av":2,"op":1.0,"vw":1.0,"o":1.0,"c":1.0,"h":1.0,"l":1.0,"a":1.0,"z":1,"s":1000,"e":61000}]""");

        await using IAsyncEnumerator<StockAggregate> secondBars =
            second.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await using IAsyncEnumerator<StockAggregate> minuteBars =
            minute.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await secondBars.MoveNextAsync());
        Assert.True(await minuteBars.MoveNextAsync());
        Assert.Equal(1, secondBars.Current.Volume);
        Assert.Equal(9, minuteBars.Current.Volume);
    }

    [Fact]
    public async Task SubscribingToATopicTwiceReturnsTheSameSequence()
    {
        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync();
        await using MassiveStockStream _ = stream;

        socket.AutoAcknowledgeSubscribes = true;

        MassiveTopicSubscription<StockLimitUpLimitDown> first =
            await stream.SubscribeLimitUpLimitDownAsync(["AAPL"], TestContext.Current.CancellationToken);
        MassiveTopicSubscription<StockLimitUpLimitDown> second =
            await stream.SubscribeLimitUpLimitDownAsync(["MSFT"], TestContext.Current.CancellationToken);

        Assert.Same(first, second);
    }

    // The reason disposal iterates the created sinks rather than naming each field: a topic added
    // after the first must still have its sequence ended, or its consumer's await foreach parks
    // forever. Naming fields one by one is exactly how a topic gets left out.
    [Fact]
    public async Task DisposalEndsEverySequenceIncludingTopicsAddedAfterTheFirst()
    {
        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync();

        socket.AutoAcknowledgeSubscribes = true;

        MassiveTopicSubscription<StockTrade> trades =
            await stream.SubscribeTradesAsync(["AAPL"], TestContext.Current.CancellationToken);
        MassiveTopicSubscription<StockAggregate> bars =
            await stream.SubscribeSecondAggregatesAsync(["AAPL"], TestContext.Current.CancellationToken);
        MassiveTopicSubscription<StockImbalance> imbalances =
            await stream.SubscribeImbalancesAsync(["AAPL"], TestContext.Current.CancellationToken);

        await stream.DisposeAsync();

        await using IAsyncEnumerator<StockTrade> tradeEnumerator =
            trades.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await using IAsyncEnumerator<StockAggregate> barEnumerator =
            bars.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await using IAsyncEnumerator<StockImbalance> imbalanceEnumerator =
            imbalances.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.False(await tradeEnumerator.MoveNextAsync());
        Assert.False(await barEnumerator.MoveNextAsync());
        Assert.False(await imbalanceEnumerator.MoveNextAsync());
    }

    [Fact]
    public async Task ADisposedStreamRefusesEveryNewTopicByName()
    {
        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync();

        socket.AutoAcknowledgeSubscribes = true;
        await stream.DisposeAsync();

        ObjectDisposedException error = await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            stream.SubscribeMinuteAggregatesAsync(["AAPL"], TestContext.Current.CancellationToken));

        Assert.Contains(nameof(MassiveStockStream), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsubscribingFromANewTopicSendsItsWireCode()
    {
        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync();
        await using MassiveStockStream _ = stream;

        socket.AutoAcknowledgeSubscribes = true;
        await stream.SubscribeImbalancesAsync(["AAPL"], TestContext.Current.CancellationToken);
        await stream.UnsubscribeAsync(StockTopic.Imbalances, ["AAPL"], TestContext.Current.CancellationToken);

        Assert.Contains(
            socket.Sent,
            frame => frame.Contains("\"action\":\"unsubscribe\"", StringComparison.Ordinal)
                && frame.Contains("\"params\":\"NOI.AAPL\"", StringComparison.Ordinal));
    }
```

- [ ] **Step 2: Run and watch it fail to build**

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter FullyQualifiedName~StockStreamTests
```

Expected: build failure, `CS1061` — `MassiveStockStream` does not contain a definition for `SubscribeSecondAggregatesAsync`.

- [ ] **Step 3: Add the sink fields and the generic creator**

In `src/MassiveDotNet.WebSocket/MassiveStockStream.cs`, replace the `_trades`/`_quotes` field declarations with all six plus the created-sink list. Keep every existing comment on `_sinkLock` — it documents two fixes and must not be lost.

```csharp
    private readonly object _sinkLock = new();
    private TopicSink<StockTrade>? _trades;
    private TopicSink<StockQuote>? _quotes;
    private TopicSink<StockAggregate>? _secondAggregates;
    private TopicSink<StockAggregate>? _minuteAggregates;
    private TopicSink<StockImbalance>? _imbalances;
    private TopicSink<StockLimitUpLimitDown>? _limitUpLimitDown;
    private bool _disposed;

    // Every sink this stream has created, in creation order, so disposal ends every sequence
    // without naming each topic. Naming them one by one is how a topic gets left out of DisposeAsync
    // and its consumer's `await foreach` parks forever -- and with six topics here and five more
    // markets to come, that list would be copied twenty-four more times. Guarded by _sinkLock, the
    // same lock that guards the fields above and _disposed.
    private readonly List<ITopicSink> _createdSinks = [];
```

Replace `GetOrCreateTradeSink` and `GetOrCreateQuoteSink` entirely with one generic method plus thin call sites:

```csharp
    // One creator for every topic. Holds the check-create-register sequence, the disposal refusal,
    // and the drop wiring in one place, so a new topic is a field and a call rather than another
    // copy of this body. The locking argument is unchanged and is documented on _sinkLock above:
    // the sink returned is the LOCAL value this lock resolved, never a re-read of the field after
    // the round trip, which is what let two concurrent first callers observe different winners.
    //
    // The converter is constructed by the caller even when the sink already exists, so a repeat
    // subscribe allocates one converter it discards. Subscribing is a network round trip that
    // happens once or twice per topic per process; the alternative is a factory delegate, which
    // allocates a closure on every call instead.
    private TopicSink<T> GetOrCreateSink<T>(
        ref TopicSink<T>? field, StockTopic topic, JsonConverter<T> converter)
    {
        lock (_sinkLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (field is null)
            {
                string topicCode = topic.ToCode();
                TopicSink<T> sink = new(topicCode, _options.TopicBufferCapacity, converter);

                sink.ItemDropped += () => OnItemDropped(topicCode, sink.Subscription.DroppedCount);
                _connection.AddSink(sink);
                _createdSinks.Add(sink);
                field = sink;
            }

            return field;
        }
    }
```

This needs `using System.Text.Json.Serialization;` at the top of the file for `JsonConverter<T>`.

- [ ] **Step 4: Point the two existing subscribe methods at it**

Replace the bodies of the existing private helpers, leaving `SubscribeTradesAsync` and `SubscribeQuotesAsync` themselves untouched:

```csharp
    private TopicSink<StockTrade> GetOrCreateTradeSink() =>
        GetOrCreateSink(ref _trades, StockTopic.Trades, new StockTradeConverter(_tickers));

    private TopicSink<StockQuote> GetOrCreateQuoteSink() =>
        GetOrCreateSink(ref _quotes, StockTopic.Quotes, new StockQuoteConverter(_tickers));
```

- [ ] **Step 5: Add the four subscribe methods**

Add after `SubscribeQuotesAsync` and its helper:

```csharp
    /// <summary>Subscribes to second-by-second aggregate bars.</summary>
    /// <param name="tickers">Symbols, or <c>*</c> for every symbol.</param>
    /// <param name="cancellationToken">Cancels the subscribe.</param>
    /// <returns>
    /// This stream's second-bar sequence. Calling again widens the ticker set and returns the same
    /// sequence, so a topic has one buffer and one consumer however many times it is called.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    /// <exception cref="MassiveStreamSubscriptionException">
    /// The server acknowledged fewer subscriptions than were requested.
    /// </exception>
    public async Task<MassiveTopicSubscription<StockAggregate>> SubscribeSecondAggregatesAsync(
        IReadOnlyCollection<string> tickers,
        CancellationToken cancellationToken = default)
    {
        TopicSink<StockAggregate> sink = GetOrCreateSink(
            ref _secondAggregates,
            StockTopic.SecondAggregates,
            new StockAggregateConverter(_tickers, StockTopic.SecondAggregates.ToCode()));

        await _connection.SubscribeAsync(StockTopic.SecondAggregates.ToCode(), tickers, cancellationToken);

        return sink.Subscription;
    }

    /// <summary>Subscribes to minute-by-minute aggregate bars.</summary>
    /// <param name="tickers">Symbols, or <c>*</c> for every symbol.</param>
    /// <param name="cancellationToken">Cancels the subscribe.</param>
    /// <returns>
    /// This stream's minute-bar sequence, separate from the second-bar sequence even though both
    /// carry <see cref="StockAggregate"/>: sinks are keyed by wire code, not by type.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    /// <exception cref="MassiveStreamSubscriptionException">
    /// The server acknowledged fewer subscriptions than were requested.
    /// </exception>
    public async Task<MassiveTopicSubscription<StockAggregate>> SubscribeMinuteAggregatesAsync(
        IReadOnlyCollection<string> tickers,
        CancellationToken cancellationToken = default)
    {
        TopicSink<StockAggregate> sink = GetOrCreateSink(
            ref _minuteAggregates,
            StockTopic.MinuteAggregates,
            new StockAggregateConverter(_tickers, StockTopic.MinuteAggregates.ToCode()));

        await _connection.SubscribeAsync(StockTopic.MinuteAggregates.ToCode(), tickers, cancellationToken);

        return sink.Subscription;
    }

    /// <summary>Subscribes to net order imbalance auction events.</summary>
    /// <param name="tickers">Symbols, or <c>*</c> for every symbol.</param>
    /// <param name="cancellationToken">Cancels the subscribe.</param>
    /// <returns>This stream's imbalance sequence, on the same terms as the other topics.</returns>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    /// <exception cref="MassiveStreamSubscriptionException">
    /// The server acknowledged fewer subscriptions than were requested — including every pair when
    /// the account's plan does not cover this topic, which the server refuses rather than ignores
    /// (issue #60).
    /// </exception>
    public async Task<MassiveTopicSubscription<StockImbalance>> SubscribeImbalancesAsync(
        IReadOnlyCollection<string> tickers,
        CancellationToken cancellationToken = default)
    {
        TopicSink<StockImbalance> sink = GetOrCreateSink(
            ref _imbalances, StockTopic.Imbalances, new StockImbalanceConverter(_tickers));

        await _connection.SubscribeAsync(StockTopic.Imbalances.ToCode(), tickers, cancellationToken);

        return sink.Subscription;
    }

    /// <summary>Subscribes to limit up-limit down price band events.</summary>
    /// <param name="tickers">Symbols, or <c>*</c> for every symbol.</param>
    /// <param name="cancellationToken">Cancels the subscribe.</param>
    /// <returns>This stream's band sequence, on the same terms as the other topics.</returns>
    /// <exception cref="ObjectDisposedException">The stream has been disposed.</exception>
    /// <exception cref="MassiveStreamSubscriptionException">
    /// The server acknowledged fewer subscriptions than were requested.
    /// </exception>
    public async Task<MassiveTopicSubscription<StockLimitUpLimitDown>> SubscribeLimitUpLimitDownAsync(
        IReadOnlyCollection<string> tickers,
        CancellationToken cancellationToken = default)
    {
        TopicSink<StockLimitUpLimitDown> sink = GetOrCreateSink(
            ref _limitUpLimitDown, StockTopic.LimitUpLimitDown, new StockLimitUpLimitDownConverter(_tickers));

        await _connection.SubscribeAsync(StockTopic.LimitUpLimitDown.ToCode(), tickers, cancellationToken);

        return sink.Subscription;
    }
```

- [ ] **Step 6: Make disposal iterate rather than enumerate by hand**

Replace the top of `DisposeAsync`:

```csharp
    /// <summary>Closes the stream and ends every topic sequence.</summary>
    public async ValueTask DisposeAsync()
    {
        ITopicSink[] created;

        lock (_sinkLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            created = [.. _createdSinks];
        }

        // Completed here rather than left to the connection's own CompleteAllSinks: that runs only
        // after the read loop has been awaited, so relying on it would delay every consumer's
        // `await foreach` ending by the length of that drain. Complete() is TryComplete()
        // underneath, so the connection completing them again below is a no-op.
        foreach (ITopicSink sink in created)
        {
            sink.Complete();
        }

        await _connection.DisposeAsync();

        // F4: last, so the client only ever unregisters a stream that has genuinely finished
        // tearing itself down.
        _onDisposed?.Invoke();
    }
```

- [ ] **Step 7: Run the whole WebSocket suite**

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests
```

Expected: PASS, including every pre-existing test unchanged.

- [ ] **Step 8: Prove the disposal iteration is load-bearing (D31)**

Replace the `foreach` in `DisposeAsync` with the shape this task removed — `_trades?.Complete(); _quotes?.Complete();` — and re-run.

Expected: `DisposalEndsEverySequenceIncludingTopicsAddedAfterTheFirst` hangs or fails on the aggregate and imbalance enumerators, because nothing completed those sequences. Restore and re-run green.

- [ ] **Step 9: Build warning-free and commit**

```bash
dotnet build MassiveDotNet.slnx
git add src/MassiveDotNet.WebSocket/MassiveStockStream.cs tests/MassiveDotNet.WebSocket.Tests/StockStreamTests.cs
git commit -m "$(cat <<'MSG'
feat: expose the four new stock topics, and stop the façade growing per topic

Adding these the existing way would have meant six copies of a locking
body and six ?.Complete() calls, with #53-#58 repeating both per market.
So the check-create-register sequence, the disposal refusal and the drop
wiring collapse into one generic creator, and disposal iterates the
sinks this stream created rather than naming each field. EventRaiser and
StreamEventWalk are both this lesson learned after seven redundant
fixes; this one is learned before the copies exist.

The disposal change is watched failing: with the per-field ?.Complete()
calls restored, a consumer of any topic subscribed after the first never
sees its sequence end -- which is the hang, not a wrong value, so it is
worth a test that names it.

Both aggregate topics are separate sequences over one model type, since
sinks are keyed by wire code rather than by CLR type. That is what D-W14
rests on, so it is pinned rather than assumed.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB
MSG
)"
```

---

## Task 7: Allocation ceilings for the three new parse paths

**Files:**
- Modify: `tests/MassiveDotNet.WebSocket.Tests/AllocationTests.cs`
- Modify: `docs/performance/2026-09-07-streaming-allocation-figures.md`

**Interfaces:**
- Consumes: the three converters from Tasks 3-5; `MassiveDotNet.Rest.Tests.Allocation.Measure(Action)` and `Allocation.Describe(long)`, already linked into this test project and already used by the four existing ceilings.
- Produces: nothing other tasks depend on.

**D31 is not optional here.** A ceiling that has never been seen to fail is indistinguishable from a clean tree. Each of the three below is set by measuring, then breaking the path, watching the assertion go red for the right reason, restoring, and recording the regression beside the number. The numbers are not invented — Step 1 measures them.

Expected costs, so a wildly different measurement is a signal to stop and investigate rather than to write down whatever came out:

| Path | What should still allocate | What should not |
|---|---|---|
| `StockAggregate` with `dv`/`dav` | two decimal-volume strings | the ticker (pooled) |
| `StockAggregate` without them | nothing | everything |
| `StockImbalance` | one auction-type string | the ticker (pooled) |
| `StockLimitUpLimitDown` | nothing | the ticker (pooled), the indicators (inline) |

- [ ] **Step 1: Measure, before writing any ceiling**

Add a temporary test to `AllocationTests.cs` that prints rather than asserts, run it, and write the three numbers down. Delete it in Step 3.

```csharp
    [Fact]
    public void MeasureTheNewParsePaths()
    {
        TickerPool pool = new(16);

        StockAggregateConverter aggregates = new(pool, "A");
        byte[] withDecimals = Encoding.UTF8.GetBytes(
            """[{"ev":"A","sym":"MSFT","v":1,"av":2,"op":1.0,"vw":1.0,"o":1.0,"c":1.0,"h":1.0,"l":1.0,"a":1.0,"z":1,"s":1,"e":2,"dv":"1.0","dav":"2.0"}]""");
        byte[] withoutDecimals = Encoding.UTF8.GetBytes(
            """[{"ev":"A","sym":"MSFT","v":1,"av":2,"op":1.0,"vw":1.0,"o":1.0,"c":1.0,"h":1.0,"l":1.0,"a":1.0,"z":1,"s":1,"e":2}]""");

        StockImbalanceConverter imbalances = new(pool);
        byte[] imbalance = Encoding.UTF8.GetBytes(
            """[{"ev":"NOI","T":"MSFT","t":1,"at":930,"a":"M","i":1,"x":10,"o":480,"p":440,"b":25.03}]""");

        StockLimitUpLimitDownConverter bands = new(pool);
        byte[] band = Encoding.UTF8.GetBytes(
            """[{"ev":"LULD","T":"MSFT","h":1.0,"l":0.5,"i":[16],"z":3,"t":1,"q":1}]""");

        // Warm every pooled ticker and JIT every path first, so the figure measured is the steady
        // state rather than the first call's one-off costs.
        Parse(aggregates, withDecimals);
        Parse(aggregates, withoutDecimals);
        Parse(imbalances, imbalance);
        Parse(bands, band);

        Assert.Fail(
            $"aggregate with dv/dav: {Allocation.Measure(() => Parse(aggregates, withDecimals))} B; "
            + $"aggregate without: {Allocation.Measure(() => Parse(aggregates, withoutDecimals))} B; "
            + $"imbalance: {Allocation.Measure(() => Parse(imbalances, imbalance))} B; "
            + $"band: {Allocation.Measure(() => Parse(bands, band))} B");

        static void Parse<T>(System.Text.Json.Serialization.JsonConverter<T> converter, byte[] frame)
        {
            Utf8JsonReader reader = new(frame);
            reader.Read();
            reader.Read();
            _ = converter.Read(ref reader, typeof(T), JsonSerializerOptions.Default);
        }
    }
```

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter FullyQualifiedName~MeasureTheNewParsePaths
```

Run it three times and confirm the four numbers are identical each time. The project sets `TieredCompilation=false` precisely so they are (D31); if they vary, stop — something is wrong with the measurement, not with the code.

- [ ] **Step 2: Regress each path and record what the regression measures**

Run the temporary test again under each of these, one at a time, and write down the number it reports. Restore before moving to the next.

1. In `StockAggregateConverter.Read`, replace `walk.Ticker(ref reader, tickers, "sym")` with `walk.String(ref reader, "sym")!`. The aggregate figures should rise by the cost of an unpooled `"MSFT"`.
2. In `StockLimitUpLimitDownConverter.Read`, replace `walk.Conditions(ref reader, "i")` with a call that forces a spill — temporarily change `ConditionSetSerialization.Read` to start with `spilled ??= [.. inline];` before the loop. The band figure should rise by a `List<int>` and its backing array.
3. In `StockImbalanceConverter.Read`, replace `walk.Ticker(ref reader, tickers, "T")` with `walk.String(ref reader, "T")!`. The imbalance figure should rise by an unpooled `"MSFT"`.

- [ ] **Step 3: Delete the temporary test and write the real ceilings**

Remove `MeasureTheNewParsePaths`. Add the three tests below, substituting the figures measured in Step 1 for `<measured>` and the regression figures from Step 2 for `<regressed>`, and setting each `Ceiling` to roughly 20% above its measured figure, rounded up to the next multiple of 8 — except a zero, which is asserted with strict equality, because headroom there would defeat the point.

```csharp
    /// <summary>
    /// An aggregate bar carrying the wire's two decimal-volume strings. Those two are the only
    /// fields on this model that can allocate: the ticker is pooled, and every other field is a
    /// number or a bool read straight off the tokens.
    /// </summary>
    /// <remarks>
    /// Regressed on 2026-09-08 by replacing the pooled <c>walk.Ticker(…)</c> call with
    /// <c>walk.String(…)</c>: measured allocation rose from <measured> B to <regressed> B, the
    /// difference being a fresh, unpooled "MSFT". Confirmed to fail under the regression before
    /// this ceiling was committed.
    /// </remarks>
    [Fact]
    public void ParsingAnAggregateAllocatesNothingBeyondItsDecimalVolumes()
    {
        // Measured <measured> B on 2026-09-08.
        const long Ceiling = <measured plus ~20%, rounded up to a multiple of 8>;

        TickerPool pool = new(16);
        StockAggregateConverter converter = new(pool, "A");
        byte[] frame = Encoding.UTF8.GetBytes(
            """[{"ev":"A","sym":"MSFT","v":1,"av":2,"op":1.0,"vw":1.0,"o":1.0,"c":1.0,"h":1.0,"l":1.0,"a":1.0,"z":1,"s":1,"e":2,"dv":"1.0","dav":"2.0"}]""");

        long allocated = Allocation.Measure(() =>
        {
            Utf8JsonReader reader = new(frame);
            reader.Read();
            reader.Read();
            _ = converter.Read(ref reader, typeof(StockAggregate), JsonSerializerOptions.Default);
        });

        Assert.True(
            allocated <= Ceiling,
            $"Parsing one aggregate allocated {Allocation.Describe(allocated)}, over its "
                + $"{Allocation.Describe(Ceiling)} ceiling. The ticker is pooled, so only the two "
                + "decimal-volume strings should remain.");
    }

    /// <summary>
    /// The same bar without <c>dv</c> and <c>dav</c>, which is the shape the published sample and
    /// most live frames carry. Nothing on it can allocate at all.
    /// </summary>
    /// <remarks>
    /// Regressed on 2026-09-08 by the same unpooled-ticker change as above, which takes this path
    /// from 0 B to <regressed> B. Confirmed to fail under the regression before this was committed.
    /// </remarks>
    [Fact]
    public void ParsingAnAggregateWithoutDecimalVolumesAllocatesNothing()
    {
        TickerPool pool = new(16);
        StockAggregateConverter converter = new(pool, "A");
        byte[] frame = Encoding.UTF8.GetBytes(
            """[{"ev":"A","sym":"MSFT","v":1,"av":2,"op":1.0,"vw":1.0,"o":1.0,"c":1.0,"h":1.0,"l":1.0,"a":1.0,"z":1,"s":1,"e":2}]""");

        Utf8JsonReader warm = new(frame);
        warm.Read();
        warm.Read();
        _ = converter.Read(ref warm, typeof(StockAggregate), JsonSerializerOptions.Default);

        long allocated = Allocation.Measure(() =>
        {
            Utf8JsonReader reader = new(frame);
            reader.Read();
            reader.Read();
            _ = converter.Read(ref reader, typeof(StockAggregate), JsonSerializerOptions.Default);
        });

        // Strict equality, not a ceiling: "allocates nothing" is the claim, and headroom on a zero
        // would defeat the point (D31).
        Assert.True(
            allocated == 0,
            $"Parsing one aggregate without decimal volumes allocated {Allocation.Describe(allocated)}, "
                + "and every field on that shape is a pooled ticker or a number.");
    }

    /// <summary>
    /// A limit up-limit down band. Its ticker is pooled and its indicators fit inline, so nothing
    /// on the hot path touches the heap — the strongest claim of the three new topics.
    /// </summary>
    /// <remarks>
    /// Regressed on 2026-09-08 by forcing <c>ConditionSetSerialization.Read</c> to spill instead of
    /// using the inline buffer: this path went from 0 B to <regressed> B, a <c>List&lt;int&gt;</c>
    /// and its backing array. Confirmed to fail under the regression before this was committed.
    /// </remarks>
    [Fact]
    public void ParsingALimitUpLimitDownBandAllocatesNothing()
    {
        TickerPool pool = new(16);
        StockLimitUpLimitDownConverter converter = new(pool);
        byte[] frame = Encoding.UTF8.GetBytes(
            """[{"ev":"LULD","T":"MSFT","h":1.0,"l":0.5,"i":[16],"z":3,"t":1,"q":1}]""");

        Utf8JsonReader warm = new(frame);
        warm.Read();
        warm.Read();
        _ = converter.Read(ref warm, typeof(StockLimitUpLimitDown), JsonSerializerOptions.Default);

        long allocated = Allocation.Measure(() =>
        {
            Utf8JsonReader reader = new(frame);
            reader.Read();
            reader.Read();
            _ = converter.Read(ref reader, typeof(StockLimitUpLimitDown), JsonSerializerOptions.Default);
        });

        Assert.True(
            allocated == 0,
            $"Parsing one limit up-limit down band allocated {Allocation.Describe(allocated)}, and its "
                + "ticker is pooled while its indicators fit inline.");
    }

    /// <summary>
    /// An imbalance. Its one-character auction type is the only field that can allocate — the
    /// ticker is pooled and everything else is a number.
    /// </summary>
    /// <remarks>
    /// Regressed on 2026-09-08 by replacing the pooled <c>walk.Ticker(…)</c> call with
    /// <c>walk.String(…)</c>: measured allocation rose from <measured> B to <regressed> B.
    /// Confirmed to fail under the regression before this ceiling was committed.
    /// </remarks>
    [Fact]
    public void ParsingAnImbalanceAllocatesNothingBeyondItsAuctionType()
    {
        // Measured <measured> B on 2026-09-08.
        const long Ceiling = <measured plus ~20%, rounded up to a multiple of 8>;

        TickerPool pool = new(16);
        StockImbalanceConverter converter = new(pool);
        byte[] frame = Encoding.UTF8.GetBytes(
            """[{"ev":"NOI","T":"MSFT","t":1,"at":930,"a":"M","i":1,"x":10,"o":480,"p":440,"b":25.03}]""");

        Utf8JsonReader warm = new(frame);
        warm.Read();
        warm.Read();
        _ = converter.Read(ref warm, typeof(StockImbalance), JsonSerializerOptions.Default);

        long allocated = Allocation.Measure(() =>
        {
            Utf8JsonReader reader = new(frame);
            reader.Read();
            reader.Read();
            _ = converter.Read(ref reader, typeof(StockImbalance), JsonSerializerOptions.Default);
        });

        Assert.True(
            allocated <= Ceiling,
            $"Parsing one imbalance allocated {Allocation.Describe(allocated)}, over its "
                + $"{Allocation.Describe(Ceiling)} ceiling. The ticker is pooled, so only the "
                + "auction-type string should remain.");
    }
```

- [ ] **Step 4: Watch each ceiling fail**

Re-apply each regression from Step 2, one at a time, and confirm the matching assertion goes red — and that it is the *matching* one. A regression that reddens a different test than expected means the ceiling is measuring something other than what it claims.

Restore after each. Then run the whole file green:

```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter FullyQualifiedName~AllocationTests
```

- [ ] **Step 5: Record the figures**

In `docs/performance/2026-09-07-streaming-allocation-figures.md`, add four rows to "The gate's own figures" table, in the same format as the existing four — assertion, path, measured, ceiling, and the regression that proved it. Add a short paragraph under the table noting that these four were measured on 2026-09-08 alongside issue #21, and that the two exactly-zero claims are asserted with strict equality for the same reason the existing two are.

- [ ] **Step 6: Commit**

```bash
git add tests/MassiveDotNet.WebSocket.Tests/AllocationTests.cs docs/performance/2026-09-07-streaming-allocation-figures.md
git commit -m "$(cat <<'MSG'
test: gate the three new parse paths on measured allocation ceilings

Four assertions, each set the way D31 requires: measure, break the path,
watch the assertion go red for the right reason, restore, then write the
number down beside the regression that proved it. A guard nobody has
seen fail is indistinguishable from a clean tree.

Two of them claim a path allocates nothing at all -- an aggregate
without the wire's optional decimal volumes, and a limit up-limit down
band whose ticker is pooled and whose indicators fit inline -- and are
asserted with strict equality, since headroom on a zero defeats the
point. The other two carry roughly 20% over their measured figure and
name what is still allowed to allocate: two decimal-volume strings, and
one auction-type string.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB
MSG
)"
```

---

## Task 8: Live pins, decision D36, and the release gates

**Files:**
- Create: `tests/MassiveDotNet.IntegrationTests/StreamTopicsLiveTests.cs`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: everything from Tasks 1-7; `LiveApiTest` (which exposes `Ct`), `LiveCredentials.IsAvailable`, `LiveCredentials.MissingKeyReason`, `LiveCredentials.ApiKey`.
- Produces: nothing.

**Rule 13.** These tests are committed and compiled in CI, and never executed there. They are excluded by `Category=Integration` and they skip locally when no key is present, with a reason naming the variable to set.

**Rule 11.** Never put the key on a command line, in a test name, or in an assertion message. `LiveCredentials` is the only way it reaches a test.

- [ ] **Step 1: Write the live tests**

Create `tests/MassiveDotNet.IntegrationTests/StreamTopicsLiveTests.cs`:

```csharp
using MassiveDotNet.WebSocket;
using MassiveDotNet.WebSocket.Events;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// What a fixture structurally cannot verify about the four topics issue #21 added: that the wire
/// codes are the ones the service actually serves, and that the entitlements and units observed on
/// 2026-09-08 still hold.
/// </summary>
/// <remarks>
/// An acknowledgement is the strongest evidence available that a topic code is right, because the
/// server answers a code it does not recognise with silence — no acknowledgement and no error
/// (D33). A subscribe that returns rather than throwing is therefore a positive result, not the
/// absence of a negative one.
/// </remarks>
public sealed class StreamTopicsLiveTests : LiveApiTest
{
    private static MassiveStreamOptions Options()
    {
        Assert.SkipUnless(LiveCredentials.IsAvailable, LiveCredentials.MissingKeyReason);

        return new MassiveStreamOptions { ApiKey = LiveCredentials.ApiKey! };
    }

    /// <summary>
    /// Pinned observation, 2026-09-08: the service acknowledges <c>A</c>, <c>AM</c> and
    /// <c>LULD</c>. No assertion is made that data arrives — the market is closed outside trading
    /// hours, and a test that passes only during them fails for a reason unrelated to the SDK.
    /// </summary>
    [Fact]
    public async Task TheAggregateAndBandTopicCodesAreAcknowledged()
    {
        await using MassiveStreamClient client = new(Options());
        await using MassiveStockStream stream = await client.ConnectStocksAsync(Ct);

        await stream.SubscribeSecondAggregatesAsync(["AAPL"], Ct);
        await stream.SubscribeMinuteAggregatesAsync(["AAPL"], Ct);
        await stream.SubscribeLimitUpLimitDownAsync(["AAPL"], Ct);

        Assert.Equal(0, stream.ReconnectCount);
    }

    /// <summary>
    /// Pinned observation, 2026-09-08: this key is not entitled to the imbalance topic. The server
    /// answers <c>{"ev":"status","status":"error","message":"not authorized"}</c> rather than the
    /// <c>success</c> a subscribe expects, so the acknowledgement count falls short and the
    /// subscribe throws.
    /// </summary>
    /// <remarks>
    /// D21's posture: the observation is pinned and dated so it flips the day the entitlement
    /// changes, where a skip would read as green.
    /// <para>
    /// The exception's <b>message</b> is deliberately not asserted here. It currently says the
    /// server "ignores a topic code it does not recognise", which is not what happened — the code
    /// was recognised and the plan was not entitled. That is issue #60, and it will change this
    /// message. Pinning the type and the parameter rather than the prose means #60 has to move
    /// this test deliberately without it failing for the wrong reason first.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheImbalanceTopicIsStillNotAuthorizedOnThisKey()
    {
        await using MassiveStreamClient client = new(Options());
        await using MassiveStockStream stream = await client.ConnectStocksAsync(Ct);

        MassiveStreamSubscriptionException error =
            await Assert.ThrowsAsync<MassiveStreamSubscriptionException>(() =>
                stream.SubscribeImbalancesAsync(["AAPL"], Ct));

        Assert.Equal("NOI.AAPL", error.Parameters);
        Assert.Equal(1, error.Unacknowledged);
    }

    /// <summary>
    /// Pinned observation, 2026-09-08: <c>LULD</c>'s <c>t</c> is Unix nanoseconds, against the
    /// documentation's own prose claiming milliseconds (D-W15). This is the test that flips the day
    /// either the wire or the documentation moves.
    /// </summary>
    /// <remarks>
    /// Band updates flow continuously during regular hours and not at all outside them, so this
    /// skips rather than fails when no event arrives within the window — an honest report to a
    /// person reading the output, not a false signal, since a run outside market hours proves
    /// nothing either way.
    /// </remarks>
    [Fact]
    public async Task ALiveBandUpdateCarriesANanosecondTimestamp()
    {
        await using MassiveStreamClient client = new(Options());
        await using MassiveStockStream stream = await client.ConnectStocksAsync(Ct);

        MassiveTopicSubscription<StockLimitUpLimitDown> bands =
            await stream.SubscribeLimitUpLimitDownAsync(["*"], Ct);

        using CancellationTokenSource window = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        window.CancelAfter(NodaTime.Duration.FromSeconds(30).ToTimeSpan());

        StockLimitUpLimitDown? observed = null;

        try
        {
            await foreach (StockLimitUpLimitDown band in bands.WithCancellation(window.Token))
            {
                observed = band;
                break;
            }
        }
        catch (OperationCanceledException)
        {
            // Falls through to the skip below.
        }

        Assert.SkipWhen(
            observed is null,
            "No limit up-limit down event arrived within 30 seconds. Band updates flow only during "
                + "regular trading hours, so this proves nothing outside them.");

        // Read as milliseconds, a nanosecond value of this magnitude lands roughly fifty-six
        // million years out, so the year is what distinguishes the two readings.
        int year = observed!.Value.Timestamp.InUtc().Year;

        Assert.InRange(year, 2020, 2100);
    }
}
```

**Rule 12 check on this file.** `NodaTime.Duration.FromSeconds(30).ToTimeSpan()` is the sanctioned boundary pattern — the BCL type is never named. Add the row to `CLAUDE.md`'s "Known boundary points" table in Step 3.

- [ ] **Step 2: Confirm the live tier compiles and is excluded, then run it locally**

```bash
dotnet build MassiveDotNet.slnx
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
```

Expected: the integration project compiles; the offline run reports `No test matches the given testcase filter` for it, exactly as it does today.

Then, with the key sourced from the gitignored `.env` — never on the command line:

```bash
set -a; . ./.env >/dev/null 2>&1; set +a
dotnet test tests/MassiveDotNet.IntegrationTests --filter "FullyQualifiedName~StreamTopicsLiveTests"
```

Expected during market hours: all three pass. Outside them: the first two pass and the third skips with its stated reason.

- [ ] **Step 3: Record decision D36 in `CLAUDE.md`**

Add this row to the "Architecture decisions" table, after D35:

```
| D36 | Streaming event converters are **hand-written**, not generated, and every one walks its object through a single shared cursor, `StreamEventWalk`, which skips any property the converter does not consume and reads every value through `JsonValueReader`. | D1's premise is that the generator reads a machine-readable description; there is none for the streaming wire. Generating would mean curating a second source of truth by hand — the same field names, types, and units in JSON rather than C# — losing compile-time checking and the per-field prose that argues why a name disagrees with Massive's own documentation (D-W15), and buying rules 5 and 6's regeneration and determinism obligations for artefacts with no upstream that can drift. Twenty-four converters across six markets is real repetition, and the cursor is what answers it: with the walk extracted, a converter is a property list. The extraction is not optional polish. #20's whole-branch review named this exact defect shape — a walk that reads a value off the raw reader, or fails to `Skip()` an unrecognised property — and could not extract it because no instance survived on that branch; a missed `Skip()` does not throw, it leaves the reader mid-value so the next property is read from the wrong token, which is D32's silently-wrong binding arriving where nothing refuses it. The same review watched two other shapes fixed per-member seven times before anyone generalised them, which is why this one is settled with four converters in the tree rather than twenty-four. The cursor holds the reader as a parameter rather than a field because `CS9050` forbids a ref field to a ref struct and `CS8350` forbids a `ref struct` receiver from taking one, both confirmed against the compiler; a plain `struct` receiver is what compiles, and the cost is `ref reader` at every call site. |
```

Update the `Layout` section's WebSocket line to name the cursor:

```
src/MassiveDotNet.WebSocket WebSocket streaming client: reconnect with backoff, per-topic
                            backpressure, and the hand-written event converters (D33-D36), which
                            all walk their objects through StreamEventWalk.
```

Add the boundary row to "Known boundary points":

```
| `CancellationTokenSource.CancelAfter` | produce | `window.CancelAfter(Duration.FromSeconds(30).ToTimeSpan())` | `StreamTopicsLiveTests` |
```

- [ ] **Step 4: Run every release gate**

```bash
dotnet build MassiveDotNet.slnx
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/
dotnet publish samples/MassiveDotNet.AotSmokeTest -r osx-arm64 -c Release
```

Expected, in order: 0 warnings and 0 errors; every offline test passing; **no diff under `src/`** — nothing in this issue touches generated code, so any diff here means something went wrong; zero IL warnings.

Run the WebSocket project three more times to confirm nothing added here is timing-sensitive:

```bash
for i in 1 2 3; do dotnet test tests/MassiveDotNet.WebSocket.Tests || break; done
```

- [ ] **Step 5: Confirm nothing forbidden was left behind**

```bash
git status --short
grep -rn "JsonTokenType.PropertyName" src/MassiveDotNet.WebSocket/Internal/*.cs
```

Expected: `.claude/` still untracked and unstaged; exactly one hit for the walk, in `StreamEventWalk.cs`.

- [ ] **Step 6: Commit**

```bash
git add tests/MassiveDotNet.IntegrationTests/StreamTopicsLiveTests.cs CLAUDE.md
git commit -m "$(cat <<'MSG'
test: pin the live topic observations and record D36

Three pins, each dated so it flips the day the service changes rather
than the day someone notices. The service acknowledges A, AM and LULD,
which is the strongest evidence available that a wire code is right,
since it answers a code it does not recognise with silence. The
imbalance topic is not authorized on this key, so its subscribe throws;
the exception TYPE and parameter are pinned but not its message, because
that message is wrong and #60 will change it -- pinning the prose would
make #60 fail here for the wrong reason first. And a live band update
carries a nanosecond timestamp, which is the observation D-W15 rests on.

The band test skips rather than fails when no event arrives, because
band updates flow only during regular hours and a run outside them
proves nothing either way. That is honest feedback to a person reading
the output; CI never runs this tier at all (rule 13).

D36 records both decisions issue #21 owns, so #53-#58 inherit them
rather than re-litigating them once per market.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB
MSG
)"
```

---

## Done when

- Four topics modelled, parsed, and subscribable, with `A` and `AM` sharing one model.
- Exactly one `Utf8JsonReader` property walk exists in the package, tested once, and watched failing with the auto-skip removed.
- Both pre-existing converters run on that walk, with no existing test modified to accommodate the change.
- Four new allocation ceilings, each watched failing under its own regression, recorded in `docs/performance/`.
- `CLAUDE.md` carries D36, so #53-#58 inherit both decisions.
- The four release gates pass: warning-free build, green offline suite, no generated-file drift, zero IL warnings.
