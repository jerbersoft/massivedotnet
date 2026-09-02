# Stocks Group Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Map the fifteen remaining Stocks operations from issue #8 into `client.Stocks`, with a nanosecond-precision timestamp filter type and a snapshot direction enum in core, the map corrections the deprecated tick endpoints need, fixtures from the published examples, and a live tier that proves D19 and one tick page boundary.

**Architecture:** Every generator path these operations need already exists, so the work is two small core types (`DateOrNanoseconds`, `SnapshotDirection`) registered in `TypeBinding` and `RequestUriBuilder`, then seventeen model rows and fifteen endpoint rows in `specs/endpoints.map.json`, regenerated output, ten hand-written partials that expose computed `Instant`s through one internal `Epoch` helper, and one offline test class per endpoint family driven through the public API against the stub handler. The deprecated pair is mapped after the v3 pair it points at, so the `[Obsolete]` messages resolve.

**Tech Stack:** .NET 10, C# latest, xUnit v3, System.Text.Json source generation, NodaTime 3.3.3. No new package dependencies.

**Spec:** `docs/superpowers/specs/2026-09-02-stocks-group-design.md`

## Global Constraints

Copied from `CLAUDE.md` and the spec. Every task inherits these.

- **Rule 2** — Deprecated operations ship, marked `[Obsolete("Massive has deprecated this operation. Use Stocks.ListTradesAsync instead.", DiagnosticId = "MASSIVE0002")]`. The generator emits the attribute from `x-polygon-deprecation`; the map never declares stability (D18). A test project that calls a deprecated method adds `<NoWarn>$(NoWarn);MASSIVE0002</NoWarn>` to its own `.csproj`. Nothing is ever suppressed inside generated code.
- **Rule 3** — No reflection-based serialization in shipped code. `System.Text.Json` source generation only; every new envelope is registered on `MassiveRestJsonContext` by the generator.
- **Rule 5** — `*.g.cs` files are never hand-edited. Change `specs/endpoints.map.json` or `tools/MassiveDotNet.CodeGen` and regenerate with `dotnet run --project tools/MassiveDotNet.CodeGen`. Commit the regenerated files with the map change that produced them.
- **Rule 6** — The generator is deterministic. Running it twice on the same inputs produces byte-identical output; CI checks `git diff --exit-code src/` after a regeneration.
- **Rule 7** — `MassiveDotNet` (core) references no external package other than NodaTime.
- **Rule 9** — `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are on. An **unused `using` fails the build** (IDE0005). `AnalysisLevel` is `latest-recommended`: **CA1305** (pass a format provider), **CA1307/CA1310** (pass a `StringComparison` to `Contains`, `StartsWith`, `IndexOf`, `Replace` on strings), **CA1861** (hoist constant arrays to `static readonly`), and the naming rules apply to test code too.
- **Rule 10** — Every public member carries XML documentation, or CS1591 fails the build. Every map row therefore supplies a `summary`, and every property either has a description in the spec or a `summary` on its row.
- **Rule 11** — API keys are never logged, echoed in exception messages, or written to disk. The live tests read the key from the gitignored `.env` through `LiveCredentials`; never open, print, or echo that file.
- **Rule 12** — NodaTime only. No BCL `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, or `TimeSpan` may be *named* anywhere in `src`, `tests`, `samples`, or `tools`. `TemporalTypeTests` scans every one of those directories. The temporal types this plan touches are `Instant`, `LocalDate`, and `Duration`.
- **Rule 13** — CI runs offline only. Live tests derive from `LiveApiTest`, which carries `[Trait("Category", "Integration")]`, and live in `tests/MassiveDotNet.IntegrationTests`. No offline test class may carry `LiveTests` in its name.
- **Spec D-G1** — Fifteen operations ship. `CoverageBaseline` in `EndpointCoverageTests` ends at 22; each endpoint task raises it to the running count.
- **Spec D-G2** — `DateOrNanoseconds` is a second core value type, `DateOrTimestamp` with the unit changed: `FromDate`, `FromInstant` (Unix nanoseconds), `FromUnixNanoseconds`, `FromLiteral`; implicit from `LocalDate`, `Instant`, `long`, `string`; `ToString` renders the wire form. It joins `TypeBinding.ElementTypes` and `RequestUriBuilder.AppendElement`. `ToWireValueNanoseconds` becomes `(value - NodaConstants.UnixEpoch).ToInt64Nanoseconds()`. Recorded in `CLAUDE.md` as D20. `DateOrTimestamp` itself does not change.
- **Spec D-G3** — `SnapshotDirection { Gainers, Losers }` is a core enum rendered by `ToWireValue` as `gainers` / `losers`, added to `TypeBinding`'s enum arm. The direction route is one method, `ListMoversAsync(SnapshotDirection direction, ...)`.
- **Spec D-G4** — `GroupedDailyBar`, `PreviousCloseBar`, `SnapshotDay`, and `SnapshotPreviousDay` are separate models. No map-level reuse override is added.
- **Spec D-G5** — On `HistoricTrade` the map types `T` as `string?`, `f` as `long?`, `e` and `r` as `int?`, and `t`, `y` as `long`; on `HistoricQuote` it types `T` as `string?`, `f` as `long?`, `i` as `int[]?`, and `t`, `y` as `long`. Neither model gets a hand-written partial. `timestamp` and `timestampLimit` bind to `long?`. The `date` path parameter binds to `LocalDate` from its `format: date` with no override.
- **Spec D-G6** — `MassiveDotNet.Rest.Models.Epoch` is an internal static class with `FromMilliseconds(long)` and `FromNanoseconds(long)`; every model partial that computes an `Instant` calls it.
- **Spec D-G7** — A fixture is the published example verbatim except where the example fails its own schema. The three `request_id` defects become strings, and the fixture's doc comment names the field and the reason. The deprecated pair's examples are used unchanged.
- **Spec D-G8** — The live tier: the all-tickers snapshot with two tickers returns exactly two; trades on a fixed 2024 session with `limit: 2` cross a page boundary; every other operation gets one shape-asserting call, the deprecated pair included.
- **Style** — Explicit types, never `var`; collection expressions (`[]`, `[.. x]`); `is not { } x` null patterns; file-scoped namespaces; raw string literals for JSON. Match the surrounding code. Comments explain *why*.
- **Convention** — Do not commit or push unless asked. Steps below include commits; the user chose the brainstorm-to-plan workflow, which authorizes them on the feature branch `feat/stocks-group`. Commit messages end with the trailer `Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB`.
- **Working tree** — Work happens in place on `feat/stocks-group`, not in a worktree, because the live tier in Task 11 needs the repository's gitignored `.env`.

**Verification commands** (from CLAUDE.md, "Before opening a PR"):

```bash
dotnet build MassiveDotNet.slnx                                              # must be warning-free
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/
dotnet publish samples/MassiveDotNet.AotSmokeTest -r osx-arm64 -c Release   # zero IL warnings
```

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `src/MassiveDotNet/DateOrNanoseconds.cs` | **Create.** A date or a Unix nanosecond timestamp, for the v3 tick filters (D20). | 1 |
| `src/MassiveDotNet/SnapshotDirection.cs` | **Create.** `Gainers` or `Losers`, the snapshot direction path segment. | 1 |
| `src/MassiveDotNet/MassiveEnumValues.cs` | **Modify.** `SnapshotDirection.ToWireValue`; `ToWireValueNanoseconds` corrected. | 1 |
| `src/MassiveDotNet/Http/RequestUriBuilder.cs` | **Modify.** `AppendElement` arm and doc lists for `DateOrNanoseconds`. | 1 |
| `tests/MassiveDotNet.Rest.Tests/DateOrNanosecondsTests.cs` | **Create.** Wire forms, conversions, equality, the nanosecond extension, builder rendering. | 1 |
| `tests/MassiveDotNet.Rest.Tests/SnapshotDirectionTests.cs` | **Create.** Wire literals. | 1 |
| `tests/MassiveDotNet.Rest.Tests/FilterRenderingTests.cs` | **Modify.** The closed element set gains a member. | 1 |
| `tools/MassiveDotNet.CodeGen/TypeBinding.cs` | **Modify.** `ElementTypes` + `DateOrNanoseconds`; enum arm + `SnapshotDirection`; `DateOrNanoseconds` path arm. | 2 |
| `tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs` | **Modify.** A `SnapshotDirection` path parameter; a `DateOrNanoseconds` range group. | 2 |
| `src/MassiveDotNet.Rest/Models/Epoch.cs` | **Create.** Internal millisecond and nanosecond conversions (D-G6). | 3 |
| `src/MassiveDotNet.Rest/Models/{Agg,IndicatorValue,LastTrade}.cs` | **Modify.** Call `Epoch`. | 3 |
| `specs/endpoints.map.json` | **Modify.** Seventeen model rows and fifteen endpoint rows, added family by family. | 4–9 |
| `src/MassiveDotNet.Rest/Generated/` | **Regenerate.** Never hand-edit. | 4–9 |
| `src/MassiveDotNet.Rest/Models/{GroupedDailyBar,PreviousCloseBar,LastQuote}.cs` | **Create.** Computed instants. | 4 |
| `tests/MassiveDotNet.Rest.Tests/Fixtures.cs` | **Modify.** One published example per operation, plus two derived last pages. | 4–9 |
| `tests/MassiveDotNet.Rest.Tests/Stocks{GroupedDaily,PreviousClose,LastQuote}Tests.cs` | **Create.** Request rendering and deserialization. | 4 |
| `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` | **Modify.** `CoverageBaseline` 7 → 10 → 12 → 15 → 18 → 20 → 22. | 4–9 |
| `src/MassiveDotNet.Rest/Models/{Trade,Quote}.cs` | **Create.** Three computed instants each. | 5 |
| `tests/MassiveDotNet.Rest.Tests/Stocks{Trades,Quotes}Tests.cs` | **Create.** Nanosecond filter rendering, round-trip, page boundary. | 5 |
| `src/MassiveDotNet.Rest/Models/{SnapshotMinute,SnapshotLastQuote,SnapshotLastTrade,TickerSnapshot}.cs` | **Create.** Computed instants. | 6 |
| `tests/MassiveDotNet.Rest.Tests/StocksSnapshotsTests.cs` | **Create.** Array parameter, direction path, nested structs. | 6 |
| `src/MassiveDotNet.Rest/Models/MacdValue.cs` | **Create.** Computed instant. | 7 |
| `tests/MassiveDotNet.Rest.Tests/Stocks{EmaRsi,Macd}Tests.cs` | **Create.** Indicator reuse and the MACD shape. | 7 |
| `tests/MassiveDotNet.Rest.Tests/MassiveDotNet.Rest.Tests.csproj` | **Modify.** `NoWarn` MASSIVE0002. | 8 |
| `tests/MassiveDotNet.IntegrationTests/MassiveDotNet.IntegrationTests.csproj` | **Modify.** `NoWarn` MASSIVE0002. | 8 |
| `tests/MassiveDotNet.Rest.Tests/StocksHistoricTicksTests.cs` | **Create.** The deprecated pair. | 8 |
| `tests/MassiveDotNet.Rest.Tests/Stocks{Splits,Exchanges}Tests.cs` | **Create.** Filters, round-trip, traversal. | 9 |
| `samples/MassiveDotNet.AotSmokeTest/Program.cs` | **Modify.** Trades with a nanosecond range; snapshots with a ticker array. | 10 |
| `CLAUDE.md` | **Modify.** D20; the Filters and Stability conventions. | 10 |
| `tests/MassiveDotNet.IntegrationTests/Stocks{Bars,Ticks,Snapshots,Splits,Exchanges}LiveTests.cs` | **Create.** D-G8. | 11 |
| `tests/MassiveDotNet.IntegrationTests/StocksIndicatorsLiveTests.cs` | **Modify.** EMA, RSI, MACD one call each. | 11 |

---

### Task 1: Core types: `DateOrNanoseconds`, `SnapshotDirection`, and the nanosecond fix

**Files:**
- Create: `src/MassiveDotNet/DateOrNanoseconds.cs`
- Create: `src/MassiveDotNet/SnapshotDirection.cs`
- Modify: `src/MassiveDotNet/MassiveEnumValues.cs`
- Modify: `src/MassiveDotNet/Http/RequestUriBuilder.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/DateOrNanosecondsTests.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/SnapshotDirectionTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/FilterRenderingTests.cs`

**Interfaces:**
- Consumes: `DateOrTimestamp` (`src/MassiveDotNet/DateOrTimestamp.cs`) as the pattern to mirror; `RequestUriBuilder.AppendElement<T>` as the dispatch to extend.
- Produces: `public readonly struct DateOrNanoseconds : IEquatable<DateOrNanoseconds>` in namespace `MassiveDotNet` with `FromDate(LocalDate)`, `FromInstant(Instant)`, `FromUnixNanoseconds(long)`, `FromLiteral(string)`, implicit operators from `LocalDate`, `Instant`, `long`, `string`, and `ToString()`; `public enum SnapshotDirection { Gainers = 0, Losers = 1 }` with `ToWireValue()` returning `"gainers"` / `"losers"`; `MassiveEnumValues.ToWireValueNanoseconds(this Instant)` returning Unix nanoseconds. Task 2 registers both types in the generator; Tasks 5 and 6 map them.

- [ ] **Step 1: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/DateOrNanosecondsTests.cs`:

```csharp
using MassiveDotNet.Http;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The wire forms of <see cref="DateOrNanoseconds"/>, the tick-level counterpart of
/// <see cref="DateOrTimestamp"/> (D20): the same four factories, with an instant rendered as Unix
/// nanoseconds rather than milliseconds.
/// </summary>
public sealed class DateOrNanosecondsTests
{
    // One microsecond after the epoch is 1000 ns, 10 ticks, and 0 ms. Each unit gives a different
    // answer, so a render of "1000" proves the nanosecond path and nothing else.
    private static readonly Instant OneMicrosecond = NodaConstants.UnixEpoch + Duration.FromNanoseconds(1000);

    [Fact]
    public void ADateRendersAsIso()
    {
        Assert.Equal("2024-01-16", DateOrNanoseconds.FromDate(new LocalDate(2024, 1, 16)).ToString());
    }

    [Fact]
    public void AnInstantRendersAsUnixNanoseconds()
    {
        Assert.Equal("1000", DateOrNanoseconds.FromInstant(OneMicrosecond).ToString());
    }

    [Fact]
    public void ANanosecondCountRendersUnchanged()
    {
        Assert.Equal("1517562000016036600", DateOrNanoseconds.FromUnixNanoseconds(1517562000016036600L).ToString());
    }

    [Fact]
    public void ALiteralRendersVerbatim()
    {
        Assert.Equal("2024-01-16", DateOrNanoseconds.FromLiteral("2024-01-16").ToString());
    }

    [Fact]
    public void ABlankLiteralIsRefused()
    {
        Assert.Throws<ArgumentException>(() => DateOrNanoseconds.FromLiteral("  "));
    }

    [Fact]
    public void ImplicitConversionsCoverEveryForm()
    {
        DateOrNanoseconds fromDate = new LocalDate(2024, 1, 16);
        DateOrNanoseconds fromInstant = OneMicrosecond;
        DateOrNanoseconds fromCount = 1000L;
        DateOrNanoseconds fromLiteral = "2024-01-16";

        Assert.Equal("2024-01-16", fromDate.ToString());
        Assert.Equal("1000", fromInstant.ToString());
        Assert.Equal("1000", fromCount.ToString());
        Assert.Equal("2024-01-16", fromLiteral.ToString());
    }

    [Fact]
    public void EqualityFollowsTheValue()
    {
        Assert.Equal(DateOrNanoseconds.FromUnixNanoseconds(1000), DateOrNanoseconds.FromInstant(OneMicrosecond));
        Assert.Equal(
            DateOrNanoseconds.FromUnixNanoseconds(1000).GetHashCode(),
            DateOrNanoseconds.FromInstant(OneMicrosecond).GetHashCode());
        Assert.True(DateOrNanoseconds.FromDate(new LocalDate(2024, 1, 16)) == DateOrNanoseconds.FromLiteral("2024-01-16"));
        Assert.True(DateOrNanoseconds.FromUnixNanoseconds(1000) != DateOrNanoseconds.FromUnixNanoseconds(1001));
    }

    [Fact]
    public void ToWireValueNanosecondsRendersNanosecondsNotTicks()
    {
        // The extension existed before any caller did, and rendered ToUnixTimeTicks: 100 ns
        // units, which would have asked a tick endpoint for a moment a hundred times too early.
        Assert.Equal("1000", OneMicrosecond.ToWireValueNanoseconds());
    }

    [Fact]
    public void RendersThroughTheBuilderLikeAnyOtherElement()
    {
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery<DateOrNanoseconds>("f", RangeFilter.Between(
            DateOrNanoseconds.FromInstant(OneMicrosecond),
            DateOrNanoseconds.FromDate(new LocalDate(2024, 1, 16))));

        Assert.Equal("/x?f.gte=1000&f.lte=2024-01-16", builder.ToUriString());
    }

    [Fact]
    public void LiteralsArePercentEscapedLikeAnyOtherCallerSuppliedValue()
    {
        // FromLiteral accepts any non-whitespace string and ToString returns it unchanged, so an
        // unescaped render would let a crafted literal inject a second query parameter.
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery<DateOrNanoseconds>("f", RangeFilter.Gte(DateOrNanoseconds.FromLiteral("2024-01-16&limit=50000")));

        Assert.Equal("/x?f.gte=2024-01-16%26limit%3D50000", builder.ToUriString());
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/SnapshotDirectionTests.cs`:

```csharp
using Xunit;

namespace MassiveDotNet.Rest.Tests;

public sealed class SnapshotDirectionTests
{
    [Theory]
    [InlineData(SnapshotDirection.Gainers, "gainers")]
    [InlineData(SnapshotDirection.Losers, "losers")]
    public void RendersTheWireLiteral(SnapshotDirection value, string expected)
    {
        Assert.Equal(expected, value.ToWireValue());
    }

    [Fact]
    public void RefusesAnUndefinedMember()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((SnapshotDirection)42).ToWireValue());
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/FilterRenderingTests.cs`, add one line to the end of `EveryElementTypeRendersItsWireForm`, after the `DateOrTimestamp` `lte` assertion:

```csharp
        Assert.Equal("/x?f.gte=1517562000016036600", RenderRange<DateOrNanoseconds>(RangeFilter.Gte<DateOrNanoseconds>(1517562000016036600L)));
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~DateOrNanoseconds|FullyQualifiedName~SnapshotDirection|FullyQualifiedName~FilterRenderingTests"`
Expected: the build fails with CS0246 for `DateOrNanoseconds` and `SnapshotDirection`. A compile failure is the correct red here: the types do not exist.

- [ ] **Step 3: Create `SnapshotDirection` and its wire value**

Create `src/MassiveDotNet/SnapshotDirection.cs`:

```csharp
namespace MassiveDotNet;

/// <summary>
/// Which end of the market a snapshot of the day's movers describes.
/// </summary>
public enum SnapshotDirection
{
    /// <summary>The tickers with the largest percentage gain today.</summary>
    Gainers = 0,

    /// <summary>The tickers with the largest percentage loss today.</summary>
    Losers = 1,
}
```

In `src/MassiveDotNet/MassiveEnumValues.cs`, insert after the `SeriesType` overload and before the `LocalDate` overload:

```csharp
    /// <summary>Returns the wire representation of a <see cref="SnapshotDirection"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>Either <c>"gainers"</c> or <c>"losers"</c>, the path segment the snapshot route takes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined enum member.</exception>
    public static string ToWireValue(this SnapshotDirection value) => value switch
    {
        SnapshotDirection.Gainers => "gainers",
        SnapshotDirection.Losers => "losers",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
```

- [ ] **Step 4: Correct `ToWireValueNanoseconds`**

In `src/MassiveDotNet/MassiveEnumValues.cs`, replace the last method with:

```csharp
    /// <summary>Returns the wire representation of an instant, as Unix nanoseconds.</summary>
    /// <param name="value">The instant to convert.</param>
    /// <returns>Nanoseconds since the Unix epoch, used by the tick-level endpoints.</returns>
    public static string ToWireValueNanoseconds(this Instant value) =>
        // Not ToUnixTimeTicks: a tick is 100 ns, and the tick endpoints count nanoseconds, so
        // that form would name a moment a hundred times too early. A Duration from the epoch
        // keeps the full precision an Instant carries.
        (value - NodaConstants.UnixEpoch).ToInt64Nanoseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
```

- [ ] **Step 5: Create `DateOrNanoseconds`**

Create `src/MassiveDotNet/DateOrNanoseconds.cs`:

```csharp
using System.Globalization;
using NodaTime;
using NodaTime.Text;

namespace MassiveDotNet;

/// <summary>
/// A point in time accepted by the tick-level Massive endpoints that take "either a date with the
/// format YYYY-MM-DD or a nanosecond timestamp".
/// </summary>
/// <remarks>
/// <para>
/// This is <see cref="DateOrTimestamp"/> with the unit changed. The two are separate types with
/// the unit in the name because an <see cref="Instant"/> converts implicitly to either, and a
/// millisecond render on a nanosecond endpoint would compile and ask for a moment in 1970
/// (decision D20). The map chooses the type each endpoint documents.
/// </para>
/// <para>
/// Implicit conversions exist from <see cref="LocalDate"/>, <see cref="Instant"/>,
/// <see cref="long"/>, and <see cref="string"/>, so callers can pass whichever form they
/// already have without converting by hand.
/// </para>
/// </remarks>
public readonly struct DateOrNanoseconds : IEquatable<DateOrNanoseconds>
{
    private readonly string? _literal;
    private readonly long _epochNanoseconds;

    private DateOrNanoseconds(string literal)
    {
        _literal = literal;
        _epochNanoseconds = 0;
    }

    private DateOrNanoseconds(long epochNanoseconds)
    {
        _literal = null;
        _epochNanoseconds = epochNanoseconds;
    }

    /// <summary>Creates a value from a calendar date, rendered as <c>YYYY-MM-DD</c>.</summary>
    /// <param name="value">The calendar date.</param>
    /// <returns>The wrapped value.</returns>
    public static DateOrNanoseconds FromDate(LocalDate value) =>
        new(LocalDatePattern.Iso.Format(value));

    /// <summary>Creates a value from an instant, rendered as Unix nanoseconds.</summary>
    /// <param name="value">The instant.</param>
    /// <returns>The wrapped value.</returns>
    public static DateOrNanoseconds FromInstant(Instant value) =>
        new((value - NodaConstants.UnixEpoch).ToInt64Nanoseconds());

    /// <summary>Creates a value from a Unix nanosecond timestamp.</summary>
    /// <param name="epochNanoseconds">Nanoseconds since the Unix epoch.</param>
    /// <returns>The wrapped value.</returns>
    public static DateOrNanoseconds FromUnixNanoseconds(long epochNanoseconds) =>
        new(epochNanoseconds);

    /// <summary>Creates a value from a literal already in a form the API accepts.</summary>
    /// <param name="value">The literal value, such as <c>"2026-01-15"</c>.</param>
    /// <returns>The wrapped value.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null or whitespace.</exception>
    public static DateOrNanoseconds FromLiteral(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new DateOrNanoseconds(value);
    }

    /// <summary>Converts a calendar date.</summary>
    /// <param name="value">The calendar date.</param>
    public static implicit operator DateOrNanoseconds(LocalDate value) => FromDate(value);

    /// <summary>Converts an instant.</summary>
    /// <param name="value">The instant.</param>
    public static implicit operator DateOrNanoseconds(Instant value) => FromInstant(value);

    /// <summary>Converts a Unix nanosecond timestamp.</summary>
    /// <param name="value">Nanoseconds since the Unix epoch.</param>
    public static implicit operator DateOrNanoseconds(long value) => FromUnixNanoseconds(value);

    /// <summary>Converts a literal value.</summary>
    /// <param name="value">The literal value.</param>
    public static implicit operator DateOrNanoseconds(string value) => FromLiteral(value);

    /// <summary>Renders the value in the form the API expects.</summary>
    /// <returns>Either a <c>YYYY-MM-DD</c> date or a Unix nanosecond timestamp.</returns>
    public override string ToString() =>
        _literal ?? _epochNanoseconds.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public bool Equals(DateOrNanoseconds other) =>
        _literal == other._literal && _epochNanoseconds == other._epochNanoseconds;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DateOrNanoseconds other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_literal, _epochNanoseconds);

    /// <summary>Compares two values for equality.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    public static bool operator ==(DateOrNanoseconds left, DateOrNanoseconds right) => left.Equals(right);

    /// <summary>Compares two values for inequality.</summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true"/> when the values differ.</returns>
    public static bool operator !=(DateOrNanoseconds left, DateOrNanoseconds right) => !left.Equals(right);
}
```

- [ ] **Step 6: Teach the builder to render it**

In `src/MassiveDotNet/Http/RequestUriBuilder.cs`, make three edits.

First, in every `<typeparam name="T">` block (there are five: the array overload and the four filter overloads), replace the text

```
    /// <see cref="double"/>, <see cref="LocalDate"/>, or <see cref="DateOrTimestamp"/>.
```

with

```
    /// <see cref="double"/>, <see cref="LocalDate"/>, <see cref="DateOrTimestamp"/>, or
    /// <see cref="DateOrNanoseconds"/>.
```

Second, in `AppendElement<T>`, insert a branch after the `DateOrTimestamp` branch and before the `else`:

```csharp
        else if (typeof(T) == typeof(DateOrNanoseconds))
        {
            // Caller-supplied like DateOrTimestamp, and escaped for the same reason.
            _builder.Append(Uri.EscapeDataString(Unsafe.As<T, DateOrNanoseconds>(ref value).ToString()));
        }
```

Third, update the `NotSupportedException` message in the same method to:

```csharp
                $"{typeof(T)} is not a supported filter element type. Supported: string, int, long, double, LocalDate, DateOrTimestamp, DateOrNanoseconds.");
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~DateOrNanoseconds|FullyQualifiedName~SnapshotDirection|FullyQualifiedName~FilterRenderingTests|FullyQualifiedName~TemporalTypeTests"`
Expected: PASS. `TemporalTypeTests` is included because it reflects over every public member of core, and the new struct's operators are exactly what it exists to inspect.

- [ ] **Step 8: Build the whole solution warning-free**

Run: `dotnet build MassiveDotNet.slnx`
Expected: 0 warnings, 0 errors.

- [ ] **Step 9: Commit**

```bash
git add src/MassiveDotNet/DateOrNanoseconds.cs src/MassiveDotNet/SnapshotDirection.cs src/MassiveDotNet/MassiveEnumValues.cs src/MassiveDotNet/Http/RequestUriBuilder.cs tests/MassiveDotNet.Rest.Tests/DateOrNanosecondsTests.cs tests/MassiveDotNet.Rest.Tests/SnapshotDirectionTests.cs tests/MassiveDotNet.Rest.Tests/FilterRenderingTests.cs
git commit -m "feat: add DateOrNanoseconds and SnapshotDirection to core

DateOrNanoseconds is DateOrTimestamp with the unit in the name, for the
v3 tick endpoints whose timestamp filter takes a date or a nanosecond
count; an Instant converts to either, so two types keep the wrong unit
from compiling (D20). SnapshotDirection is the gainers/losers path
segment. ToWireValueNanoseconds rendered 100 ns ticks; it had no caller
yet, and this type would have been the first.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 2: Generator: register both types in `TypeBinding`

**Files:**
- Modify: `tools/MassiveDotNet.CodeGen/TypeBinding.cs`
- Modify: `tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs`

**Interfaces:**
- Consumes: `DateOrNanoseconds` and `SnapshotDirection` from Task 1 (by name only; the generator emits strings).
- Produces: a map row `{ "type": "SnapshotDirection" }` on a path parameter emits `builder.AppendPathLiteral(direction.ToWireValue());`; a map row `{ "type": "DateOrNanoseconds" }` on a comparator group emits `RangeFilter<DateOrNanoseconds>? timestamp = null` and `builder.AppendQuery("timestamp", timestamp);`. Tasks 5 and 6 rely on both.

- [ ] **Step 1: Write the failing tests**

Add to `tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs`, after `ASeriesTypeParameterRendersItsWireValue`:

```csharp
    [Fact]
    public void ASnapshotDirectionPathParameterRendersItsWireLiteral()
    {
        string spec = Harness.Document(new Operation(
            "ListThings",
            "/v1/things/{direction}",
            Harness.Envelope(Item),
            """[ { "name": "direction", "in": "path", "required": true, "schema": { "type": "string", "enum": ["gainers", "losers"] } } ]"""));

        Dictionary<string, string> files = Harness.Generate(spec, MapDocument("""{ "direction": { "name": "direction", "type": "SnapshotDirection" } }"""));

        string group = files["ReferenceGroup.g.cs"];
        Assert.Contains("SnapshotDirection direction,", group, StringComparison.Ordinal);
        // An enum wire value is a fixed literal, so it takes the unescaped path method, as
        // AggregateTimespan does on the aggregates route.
        Assert.Contains("builder.AppendPathLiteral(direction.ToWireValue());", group, StringComparison.Ordinal);
    }

    [Fact]
    public void ADateOrNanosecondsComparatorGroupBindsARangeFilter()
    {
        string spec = Document("""
            [
              { "name": "timestamp",     "in": "query", "schema": { "type": "string" } },
              { "name": "timestamp.gt",  "in": "query", "schema": { "type": "string" } },
              { "name": "timestamp.gte", "in": "query", "schema": { "type": "string" } },
              { "name": "timestamp.lt",  "in": "query", "schema": { "type": "string" } },
              { "name": "timestamp.lte", "in": "query", "schema": { "type": "string" } }
            ]
            """);

        Dictionary<string, string> files = Harness.Generate(spec, MapDocument("""{ "timestamp": { "type": "DateOrNanoseconds" } }"""));

        string group = files["ReferenceGroup.g.cs"];
        Assert.Contains("RangeFilter<DateOrNanoseconds>? timestamp = null", group, StringComparison.Ordinal);
        Assert.Contains("builder.AppendQuery(\"timestamp\", timestamp);", group, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests --filter "FullyQualifiedName~ParameterBindingTests"`
Expected: `ASnapshotDirectionPathParameterRendersItsWireLiteral` fails because the emitted line is `builder.AppendPathSegment(direction);` (the fallback arm); `ADateOrNanosecondsComparatorGroupBindsARangeFilter` fails with the refusal `... element type 'DateOrNanoseconds', which RequestUriBuilder cannot render`.

- [ ] **Step 3: Extend `TypeBinding`**

In `tools/MassiveDotNet.CodeGen/TypeBinding.cs`, make three edits inside `Resolve` and one to `ElementTypes`.

Replace the enum arm:

```csharp
            // Enum wire values are fixed literals, so they need no percent-escaping.
            "AggregateTimespan" or "SortOrder" or "MarketType" or "SeriesType" or "SnapshotDirection" =>
                new TypeBinding(type, "AppendPathLiteral", "ToWireValue()"),
```

Replace the `DateOrTimestamp` arm:

```csharp
            "DateOrTimestamp" or "DateOrNanoseconds" =>
                new TypeBinding(type, "AppendPathSegment", "ToString()"),
```

Replace the `ElementTypes` declaration:

```csharp
    /// <summary>
    /// The element types a filter or a bare array parameter can render. This mirrors the dispatch
    /// in <c>RequestUriBuilder.AppendElement</c>; extend the two together.
    /// </summary>
    private static readonly HashSet<string> ElementTypes =
        new(StringComparer.Ordinal) { "string", "int", "long", "double", "LocalDate", "DateOrTimestamp", "DateOrNanoseconds" };
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests`
Expected: PASS, every test in the project.

- [ ] **Step 5: Confirm the committed output is unchanged**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff. Nothing in the map uses the new types yet, so regeneration is a no-op; this proves the change is additive.

- [ ] **Step 6: Commit**

```bash
git add tools/MassiveDotNet.CodeGen/TypeBinding.cs tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs
git commit -m "feat: bind SnapshotDirection and DateOrNanoseconds in the generator

SnapshotDirection joins the enum arm, so a path parameter typed with it
renders through ToWireValue as a fixed literal. DateOrNanoseconds joins
the closed element set and the caller-supplied path arm, mirroring the
builder's dispatch as the comment on ElementTypes requires.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 3: `Epoch`: one internal helper for every computed instant

**Files:**
- Create: `src/MassiveDotNet.Rest/Models/Epoch.cs`
- Modify: `src/MassiveDotNet.Rest/Models/Agg.cs`
- Modify: `src/MassiveDotNet.Rest/Models/IndicatorValue.cs`
- Modify: `src/MassiveDotNet.Rest/Models/LastTrade.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `internal static class Epoch` in namespace `MassiveDotNet.Rest.Models` with `Instant FromMilliseconds(long milliseconds)` and `Instant FromNanoseconds(long nanoseconds)`. Every partial in Tasks 4–7 calls one of these.

This task is a refactor under existing tests: `StocksAggregatesTests`, `StocksIndicatorsTests`, and `StocksLastTradeTests` already pin the millisecond and nanosecond conversions (the last-trade test asserts the nanosecond digits `FromUnixTimeTicks` would lose). No new test is written; the existing ones must stay green through the change. The spec names `LastTrade` and `IndicatorValue`; `Agg` is switched too so every model spells the conversion one way.

- [ ] **Step 1: Run the tests that pin the conversions**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~StocksAggregatesTests|FullyQualifiedName~StocksIndicatorsTests|FullyQualifiedName~StocksLastTradeTests"`
Expected: PASS. This is the green baseline the refactor must preserve.

- [ ] **Step 2: Create the helper**

Create `src/MassiveDotNet.Rest/Models/Epoch.cs`:

```csharp
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Converts the raw epoch values wire DTOs store into instants, at the precision each wire field
/// carries.
/// </summary>
/// <remarks>
/// Models store the epoch value as a <see cref="long"/> and compute the <see cref="Instant"/> only
/// when read (decision D5), so this is called from computed properties, never at deserialization.
/// Nanosecond precision is kept: <see cref="Instant"/> resolves to the nanosecond and a
/// <see cref="Duration"/> built from nanoseconds loses nothing, whereas
/// <see cref="Instant.FromUnixTimeTicks"/> would truncate to 100 ns. Internal to the REST package
/// because core has no reason to know about wire epochs.
/// </remarks>
internal static class Epoch
{
    /// <summary>Converts Unix milliseconds, the unit of aggregate and indicator timestamps.</summary>
    /// <param name="milliseconds">Milliseconds since the Unix epoch.</param>
    /// <returns>The instant.</returns>
    public static Instant FromMilliseconds(long milliseconds) => Instant.FromUnixTimeMilliseconds(milliseconds);

    /// <summary>Converts Unix nanoseconds, the unit of every tick-level timestamp.</summary>
    /// <param name="nanoseconds">Nanoseconds since the Unix epoch.</param>
    /// <returns>The instant, to the nanosecond.</returns>
    public static Instant FromNanoseconds(long nanoseconds) => NodaConstants.UnixEpoch + Duration.FromNanoseconds(nanoseconds);
}
```

- [ ] **Step 3: Switch the three existing partials to it**

In `src/MassiveDotNet.Rest/Models/Agg.cs`, replace the property body:

```csharp
    [JsonIgnore]
    public Instant Timestamp => Epoch.FromMilliseconds(TimestampMilliseconds);
```

In `src/MassiveDotNet.Rest/Models/IndicatorValue.cs`, replace the property body the same way:

```csharp
    [JsonIgnore]
    public Instant Timestamp => Epoch.FromMilliseconds(TimestampMilliseconds);
```

Replace the whole of `src/MassiveDotNet.Rest/Models/LastTrade.cs` with:

```csharp
using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="LastTrade"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct LastTrade
{
    /// <summary>The moment the SIP received this trade, converted from <see cref="SipTimestampNanoseconds"/>.</summary>
    [JsonIgnore]
    public Instant SipTimestamp => Epoch.FromNanoseconds(SipTimestampNanoseconds);

    /// <summary>The moment the exchange generated this trade, converted from <see cref="ParticipantTimestampNanoseconds"/>.</summary>
    [JsonIgnore]
    public Instant ParticipantTimestamp => Epoch.FromNanoseconds(ParticipantTimestampNanoseconds);

    /// <summary>
    /// The moment the trade reporting facility received this trade, converted from
    /// <see cref="TrfTimestampNanoseconds"/>, or <see langword="null"/> when the trade did not pass
    /// through one.
    /// </summary>
    [JsonIgnore]
    public Instant? TrfTimestamp => TrfTimestampNanoseconds is { } nanoseconds ? Epoch.FromNanoseconds(nanoseconds) : null;
}
```

- [ ] **Step 4: Run the same tests to verify they still pass**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~StocksAggregatesTests|FullyQualifiedName~StocksIndicatorsTests|FullyQualifiedName~StocksLastTradeTests|FullyQualifiedName~TemporalTypeTests"`
Expected: PASS. `TemporalTypeTests` confirms the new file names no BCL temporal type.

- [ ] **Step 5: Build warning-free**

Run: `dotnet build MassiveDotNet.slnx`
Expected: 0 warnings, 0 errors. `Agg.cs` and `IndicatorValue.cs` still need their `using NodaTime;` for the `Instant` return type, so IDE0005 does not fire.

- [ ] **Step 6: Commit**

```bash
git add src/MassiveDotNet.Rest/Models/Epoch.cs src/MassiveDotNet.Rest/Models/Agg.cs src/MassiveDotNet.Rest/Models/IndicatorValue.cs src/MassiveDotNet.Rest/Models/LastTrade.cs
git commit -m "refactor: share the epoch conversions through one internal helper

Ten more partials are about to compute instants from millisecond and
nanosecond longs. A private two-line method per model is where a
helper becomes worth having; it stays internal to the REST package
because core has no reason to know about wire epochs.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 4: Bars and the last quote: grouped daily, previous close, last NBBO

**Files:**
- Modify: `specs/endpoints.map.json` (three model rows, three endpoint rows)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Create: `src/MassiveDotNet.Rest/Models/GroupedDailyBar.cs`
- Create: `src/MassiveDotNet.Rest/Models/PreviousCloseBar.cs`
- Create: `src/MassiveDotNet.Rest/Models/LastQuote.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/StocksGroupedDailyTests.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/StocksPreviousCloseTests.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/StocksLastQuoteTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 7 → 10)

**Interfaces:**
- Consumes: `Epoch.FromMilliseconds`, `Epoch.FromNanoseconds` (Task 3); the `Agg` model row as the pattern for a bar.
- Produces: `client.Stocks.ListGroupedDailyAsync(LocalDate date, bool? adjusted = null, bool? includeOtc = null, CancellationToken cancellationToken = default)` returning `Task<GroupedDailyBar[]>`; `client.Stocks.ListPreviousCloseAsync(string ticker, bool? adjusted = null, CancellationToken cancellationToken = default)` returning `Task<PreviousCloseBar[]>`; `client.Stocks.GetLastQuoteAsync(string ticker, CancellationToken cancellationToken = default)` returning `Task<LastQuote>`. Models: `readonly partial record struct GroupedDailyBar` (`Ticker`, `Open`, `High`, `Low`, `Close`, `Volume`, `VolumeWeightedAveragePrice`, `TimestampMilliseconds`, `TransactionCount`, `IsOtc`, computed `Timestamp`); `PreviousCloseBar` (the same without `Ticker` and `IsOtc`); `LastQuote` (`Ticker`, `SipTimestampNanoseconds`, `ParticipantTimestampNanoseconds`, `TrfTimestampNanoseconds`, `SequenceNumber`, `BidPrice`, `BidSize`, `BidExchangeId`, `AskPrice`, `AskSize`, `AskExchangeId`, `Conditions`, `Indicators`, `Tape`, computed `SipTimestamp`, `ParticipantTimestamp`, `TrfTimestamp`). Task 10 calls none of these; Task 11 calls all three.

- [ ] **Step 1: Add the fixtures**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, before the `SingularWithoutResults` member:

```csharp
    /// <summary>
    /// The documented sample for GET /v2/aggs/grouped/locale/us/market/stocks/{date}, with one
    /// departure from the published text: the sample's <c>request_id</c> is a schema fragment (an
    /// object carrying a <c>description</c> and a <c>type</c>) pasted where a value belongs, while
    /// the envelope schema declares a string and every other endpoint returns one. The fixture
    /// uses a string; the object would fail deserialization, which is the sample's error rather
    /// than the service's.
    /// </summary>
    public const string StocksGroupedDaily = """
        {
          "adjusted": true,
          "queryCount": 3,
          "request_id": "6a7e466379af0a71039d60cc78e72282",
          "results": [
            {
              "T": "KIMpL",
              "c": 25.9102,
              "h": 26.25,
              "l": 25.91,
              "n": 74,
              "o": 26.07,
              "t": 1602705600000,
              "v": 4369,
              "vw": 26.0407
            },
            {
              "T": "TANH",
              "c": 23.4,
              "h": 24.763,
              "l": 22.65,
              "n": 1096,
              "o": 24.5,
              "t": 1602705600000,
              "v": 25933.6,
              "vw": 23.493
            },
            {
              "T": "VSAT",
              "c": 34.24,
              "h": 35.47,
              "l": 34.21,
              "n": 4966,
              "o": 34.9,
              "t": 1602705600000,
              "v": 312583,
              "vw": 34.4736
            }
          ],
          "resultsCount": 3,
          "status": "OK"
        }
        """;

    /// <summary>
    /// The documented sample for GET /v2/aggs/ticker/{stocksTicker}/prev, verbatim. The result
    /// carries a <c>T</c> the schema does not declare; the model follows the schema, so the field
    /// is ignored on the way in.
    /// </summary>
    public const string StocksPreviousClose = """
        {
          "adjusted": true,
          "queryCount": 1,
          "request_id": "6a7e466379af0a71039d60cc78e72282",
          "results": [
            {
              "T": "AAPL",
              "c": 115.97,
              "h": 117.59,
              "l": 114.13,
              "o": 115.55,
              "t": 1605042000000,
              "v": 131704427,
              "vw": 116.3058
            }
          ],
          "resultsCount": 1,
          "status": "OK",
          "ticker": "AAPL"
        }
        """;

    /// <summary>The documented sample for GET /v2/last/nbbo/{stocksTicker}, verbatim.</summary>
    public const string StocksLastQuote = """
        {
          "request_id": "b84e24636301f19f88e0dfbf9a45ed5c",
          "results": {
            "P": 127.98,
            "S": 7,
            "T": "AAPL",
            "X": 19,
            "p": 127.96,
            "q": 83480742,
            "s": 1,
            "t": 1617827221349730300,
            "x": 11,
            "y": 1617827221349366000,
            "z": 3
          },
          "status": "OK"
        }
        """;
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/StocksGroupedDailyTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The grouped daily endpoint: a calendar date in the path, and a bar per ticker whose wide
/// integers the description declares without a format.
/// </summary>
public sealed class StocksGroupedDailyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task BuildsTheDocumentedRequestWithEveryParameter()
    {
        StubHandler handler = new(Fixtures.StocksGroupedDaily);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListGroupedDailyAsync(new LocalDate(2020, 10, 14), adjusted: true, includeOtc: true, cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v2/aggs/grouped/locale/us/market/stocks/2020-10-14?adjusted=true&include_otc=true",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task OmitsTheOptionalParametersWhenNull()
    {
        StubHandler handler = new(Fixtures.StocksGroupedDaily);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListGroupedDailyAsync(new LocalDate(2020, 10, 14), cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v2/aggs/grouped/locale/us/market/stocks/2020-10-14", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesTheCorrectedSample()
    {
        StubHandler handler = new(Fixtures.StocksGroupedDaily);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        GroupedDailyBar[] bars;

        using (client)
        using (transport)
        {
            bars = await client.Stocks.ListGroupedDailyAsync(new LocalDate(2020, 10, 14), cancellationToken: Ct);
        }

        Assert.Equal(3, bars.Length);

        GroupedDailyBar first = bars[0];
        Assert.Equal("KIMpL", first.Ticker);
        Assert.Equal(26.07, first.Open);
        Assert.Equal(26.25, first.High);
        Assert.Equal(25.91, first.Low);
        Assert.Equal(25.9102, first.Close);
        Assert.Equal(4369d, first.Volume);
        Assert.Equal(26.0407, first.VolumeWeightedAveragePrice);
        Assert.Equal(74L, first.TransactionCount);
        Assert.False(first.IsOtc);

        // 1602705600000 ms is 2020-10-14T20:00:00Z, which overflows int32 by five orders of
        // magnitude: the description declares a bare integer, and the map corrects it to long.
        Assert.Equal(1602705600000, first.TimestampMilliseconds);
        Assert.Equal(Instant.FromUtc(2020, 10, 14, 20, 0), first.Timestamp);

        Assert.Equal("TANH", bars[1].Ticker);
        Assert.Equal(25933.6, bars[1].Volume);
        Assert.Equal("VSAT", bars[2].Ticker);
    }

    [Fact]
    public async Task AnEmptyResultsArrayIsAnEmptyArray()
    {
        StubHandler handler = new("""{ "adjusted": true, "queryCount": 0, "request_id": "r", "results": [], "resultsCount": 0, "status": "OK" }""");
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            Assert.Empty(await client.Stocks.ListGroupedDailyAsync(new LocalDate(2020, 10, 11), cancellationToken: Ct));
        }
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/StocksPreviousCloseTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The previous close endpoint: an array of one bar, returned as the array the description
/// declares rather than unwrapped.
/// </summary>
public sealed class StocksPreviousCloseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task BuildsTheDocumentedRequestPath()
    {
        StubHandler handler = new(Fixtures.StocksPreviousClose);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListPreviousCloseAsync("AAPL", adjusted: false, cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v2/aggs/ticker/AAPL/prev?adjusted=false", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.StocksPreviousClose);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        PreviousCloseBar[] bars;

        using (client)
        using (transport)
        {
            bars = await client.Stocks.ListPreviousCloseAsync("AAPL", cancellationToken: Ct);
        }

        PreviousCloseBar bar = Assert.Single(bars);
        Assert.Equal(115.55, bar.Open);
        Assert.Equal(117.59, bar.High);
        Assert.Equal(114.13, bar.Low);
        Assert.Equal(115.97, bar.Close);
        Assert.Equal(131704427d, bar.Volume);
        Assert.Equal(116.3058, bar.VolumeWeightedAveragePrice);
        Assert.Null(bar.TransactionCount);
        Assert.Equal(1605042000000, bar.TimestampMilliseconds);
        Assert.Equal(Instant.FromUtc(2020, 11, 10, 21, 0), bar.Timestamp);
    }

    [Fact]
    public void RejectsABlankTickerBeforeIssuingARequest()
    {
        StubHandler handler = new(Fixtures.StocksPreviousClose);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            Assert.Throws<ArgumentException>(() => { _ = client.Stocks.ListPreviousCloseAsync("  ", cancellationToken: Ct); });
        }

        Assert.Null(handler.LastRequestUri);
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/StocksLastQuoteTests.cs`:

```csharp
using System.Net;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The last NBBO quote: one struct under <c>results</c> whose v2 single-letter keys the map names,
/// and whose timestamps the description declares as bare integers.
/// </summary>
public sealed class StocksLastQuoteTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task BuildsTheDocumentedRequestPath()
    {
        StubHandler handler = new(Fixtures.StocksLastQuote);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.GetLastQuoteAsync("AAPL", Ct);
        }

        Assert.Equal("https://api.massive.com/v2/last/nbbo/AAPL", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.StocksLastQuote);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        LastQuote quote;

        using (client)
        using (transport)
        {
            quote = await client.Stocks.GetLastQuoteAsync("AAPL", Ct);
        }

        Assert.Equal("AAPL", quote.Ticker);
        Assert.Equal(127.98, quote.AskPrice);
        Assert.Equal(7, quote.AskSize);
        Assert.Equal(19, quote.AskExchangeId);
        Assert.Equal(127.96, quote.BidPrice);
        Assert.Equal(1, quote.BidSize);
        Assert.Equal(11, quote.BidExchangeId);
        Assert.Equal(83480742L, quote.SequenceNumber);
        Assert.Equal(3, quote.Tape);
        Assert.Null(quote.Conditions);
        Assert.Null(quote.Indicators);

        // Nanosecond precision survives: 1617827221349730300 ns is 2021-04-07 20:27:01.349730300
        // UTC, whose last two digits FromUnixTimeTicks would have dropped.
        Assert.Equal(1617827221349730300, quote.SipTimestampNanoseconds);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1617827221349730300), quote.SipTimestamp);
        Assert.Equal(new LocalDate(2021, 4, 7), quote.SipTimestamp.InUtc().Date);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1617827221349366000), quote.ParticipantTimestamp);
        Assert.Null(quote.TrfTimestampNanoseconds);
        Assert.Null(quote.TrfTimestamp);
    }

    [Fact]
    public async Task ASuccessWithoutItsPayloadThrowsWithStatusOkAndTheRequestId()
    {
        StubHandler handler = new(Fixtures.SingularWithoutResults);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Stocks.GetLastQuoteAsync("AAPL", Ct));

            Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
            Assert.Equal("r", exception.RequestId);
            Assert.Contains("/v2/last/nbbo/AAPL", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RejectsABlankTickerBeforeIssuingARequest()
    {
        StubHandler handler = new(Fixtures.StocksLastQuote);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            Assert.Throws<ArgumentException>(() => { _ = client.Stocks.GetLastQuoteAsync("  ", Ct); });
        }

        Assert.Null(handler.LastRequestUri);
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, change the baseline:

```csharp
    private const int CoverageBaseline = 10;
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~StocksGroupedDaily|FullyQualifiedName~StocksPreviousClose|FullyQualifiedName~StocksLastQuote|FullyQualifiedName~EndpointCoverageTests"`
Expected: the build fails with CS1061 (`StocksGroup` has no `ListGroupedDailyAsync`) and CS0246 for the three model types. `CoverageDoesNotRegress` would report 7 against a baseline of 10 once the build succeeds.

- [ ] **Step 4: Add the model rows**

In `specs/endpoints.map.json`, append inside the `models` object, after the `MarketHoliday` row (add a comma after its closing brace):

```json
    "GroupedDailyBar": {
      "kind": "struct",
      "summary": "One ticker's bar from the grouped daily endpoint: open, high, low, close, and volume for a whole trading day, with the ticker it belongs to.",
      "remarks": "<see cref=\"Agg\"/>'s shape plus <see cref=\"Ticker\"/>, which this endpoint carries on every row because one response spans the whole market. A separate model because the generator refuses a model at a site that carries a property the model lacks (decision D16). <see cref=\"Timestamp\"/> is computed from <see cref=\"TimestampMilliseconds\"/> only when read (decision D5).",
      "schema": { "operationId": "GetGroupedStocksAggregates", "pointer": "results/items" },
      "properties": {
        "T":   { "name": "Ticker" },
        "o":   { "name": "Open" },
        "h":   { "name": "High" },
        "l":   { "name": "Low" },
        "c":   { "name": "Close" },
        "v":   { "name": "Volume" },
        "vw":  { "name": "VolumeWeightedAveragePrice" },
        "t":   { "name": "TimestampMilliseconds", "type": "long", "summary": "The Unix millisecond timestamp for the start of the aggregate window. The description declares a bare integer; every value overflows int32." },
        "n":   { "name": "TransactionCount", "type": "long?" },
        "otc": { "name": "IsOtc", "type": "bool", "summary": "Whether this aggregate is for an OTC ticker. The API omits the field entirely when false, which deserializes to false here." }
      }
    },

    "PreviousCloseBar": {
      "kind": "struct",
      "summary": "The previous trading day's bar for one ticker: open, high, low, close, and volume.",
      "remarks": "<see cref=\"Agg\"/>'s shape without the OTC flag, which this endpoint's schema does not declare. A separate model because the generator refuses a model whose properties the site does not declare (decision D16). The schema also omits the ticker its example carries, so none is bound; the caller named it in the request. <see cref=\"Timestamp\"/> is computed from <see cref=\"TimestampMilliseconds\"/> only when read (decision D5).",
      "schema": { "operationId": "GetPreviousStocksAggregates", "pointer": "results/items" },
      "properties": {
        "o":  { "name": "Open" },
        "h":  { "name": "High" },
        "l":  { "name": "Low" },
        "c":  { "name": "Close" },
        "v":  { "name": "Volume" },
        "vw": { "name": "VolumeWeightedAveragePrice" },
        "t":  { "name": "TimestampMilliseconds", "type": "long", "summary": "The Unix millisecond timestamp for the start of the aggregate window. The description declares a bare integer; every value overflows int32." },
        "n":  { "name": "TransactionCount", "type": "long?" }
      }
    },

    "LastQuote": {
      "kind": "struct",
      "summary": "The most recent NBBO quote for a ticker: bid and ask price, size, and exchange, with three nanosecond timestamps.",
      "remarks": "A tick-level type, so a struct (decision D4). The v2 wire keys are single letters, which the map names. The description declares the timestamps as bare integers, which every published value overflows, so the map types them <see cref=\"long\"/>; they are exposed as <see cref=\"NodaTime.Instant\"/> only when read (decision D5).",
      "schema": { "operationId": "LastQuote", "pointer": "results" },
      "properties": {
        "T": { "name": "Ticker" },
        "t": { "name": "SipTimestampNanoseconds", "type": "long", "summary": "The nanosecond Unix timestamp at which the SIP received this quote from the exchange that produced it." },
        "y": { "name": "ParticipantTimestampNanoseconds", "type": "long", "summary": "The nanosecond Unix timestamp at which the quote was generated at the exchange." },
        "f": { "name": "TrfTimestampNanoseconds", "type": "long?", "summary": "The nanosecond Unix timestamp at which the trade reporting facility received this quote, when it passed through one." },
        "q": { "name": "SequenceNumber" },
        "p": { "name": "BidPrice" },
        "s": { "name": "BidSize" },
        "x": { "name": "BidExchangeId" },
        "P": { "name": "AskPrice" },
        "S": { "name": "AskSize" },
        "X": { "name": "AskExchangeId" },
        "c": { "name": "Conditions" },
        "i": { "name": "Indicators" },
        "z": { "name": "Tape" }
      }
    }
```

- [ ] **Step 5: Add the endpoint rows**

In `specs/endpoints.map.json`, append inside the `endpoints` array, after the `GetMarketHolidays` row (add a comma after its closing brace):

```json
    {
      "operationId": "GetGroupedStocksAggregates",
      "group": "Stocks",
      "method": "ListGroupedDaily",
      "summary": "Retrieves the daily bar for every US stock on one trading day.",
      "remarks": "One request returns the whole market, so each bar carries its own <see cref=\"GroupedDailyBar.Ticker\"/>. OTC securities are excluded unless <paramref name=\"includeOtc\"/> is set.",
      "result": { "kind": "array", "model": "GroupedDailyBar", "property": "results" },
      "parameters": {
        "date":        { "name": "date", "type": "LocalDate" },
        "adjusted":    { "name": "adjusted" },
        "include_otc": { "name": "includeOtc" }
      }
    },
    {
      "operationId": "GetPreviousStocksAggregates",
      "group": "Stocks",
      "method": "ListPreviousClose",
      "summary": "Retrieves the previous trading day's bar for a stock.",
      "remarks": "The description declares an array, and the service answers with an array of one bar; it is returned as it arrives rather than unwrapped, so the shape cannot drift silently if the service ever sends more.",
      "result": { "kind": "array", "model": "PreviousCloseBar", "property": "results" },
      "parameters": {
        "stocksTicker": { "name": "ticker" },
        "adjusted":     { "name": "adjusted" }
      }
    },
    {
      "operationId": "LastQuote",
      "group": "Stocks",
      "method": "GetLastQuote",
      "summary": "Retrieves the most recent NBBO quote for a stock.",
      "remarks": "A 200 without its payload is reported as <see cref=\"MassiveApiException\"/> rather than as <see langword=\"null\"/> (decision D17).",
      "result": { "kind": "object", "model": "LastQuote", "property": "results" },
      "parameters": {
        "stocksTicker": { "name": "ticker" }
      }
    }
```

- [ ] **Step 6: Regenerate and add the partials**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: three new files under `src/MassiveDotNet.Rest/Generated/Models/`, three new envelopes, three new registrations on the context, and six new methods on `StocksGroup.g.cs`. Never edit these by hand.

Create `src/MassiveDotNet.Rest/Models/GroupedDailyBar.cs`:

```csharp
using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="GroupedDailyBar"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct GroupedDailyBar
{
    /// <summary>The start of the trading day, converted from <see cref="TimestampMilliseconds"/>.</summary>
    /// <remarks>
    /// The raw <see cref="long"/> is what gets deserialized and stored; this conversion happens
    /// only when read, so a whole-market response costs nothing until a value is actually wanted.
    /// </remarks>
    [JsonIgnore]
    public Instant Timestamp => Epoch.FromMilliseconds(TimestampMilliseconds);
}
```

Create `src/MassiveDotNet.Rest/Models/PreviousCloseBar.cs`:

```csharp
using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="PreviousCloseBar"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct PreviousCloseBar
{
    /// <summary>The start of the previous trading day, converted from <see cref="TimestampMilliseconds"/>.</summary>
    /// <remarks>
    /// The raw <see cref="long"/> is what gets deserialized and stored; the conversion happens only
    /// when read (decision D5).
    /// </remarks>
    [JsonIgnore]
    public Instant Timestamp => Epoch.FromMilliseconds(TimestampMilliseconds);
}
```

Create `src/MassiveDotNet.Rest/Models/LastQuote.cs`:

```csharp
using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="LastQuote"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct LastQuote
{
    /// <summary>The moment the SIP received this quote, converted from <see cref="SipTimestampNanoseconds"/>.</summary>
    [JsonIgnore]
    public Instant SipTimestamp => Epoch.FromNanoseconds(SipTimestampNanoseconds);

    /// <summary>The moment the exchange generated this quote, converted from <see cref="ParticipantTimestampNanoseconds"/>.</summary>
    [JsonIgnore]
    public Instant ParticipantTimestamp => Epoch.FromNanoseconds(ParticipantTimestampNanoseconds);

    /// <summary>
    /// The moment the trade reporting facility received this quote, converted from
    /// <see cref="TrfTimestampNanoseconds"/>, or <see langword="null"/> when the quote did not pass
    /// through one.
    /// </summary>
    [JsonIgnore]
    public Instant? TrfTimestamp => TrfTimestampNanoseconds is { } nanoseconds ? Epoch.FromNanoseconds(nanoseconds) : null;
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS across every project, including `EndpointCoverageTests.StabilityAttributesMatchTheSpecification` (none of these three is deprecated, so no attribute is expected) and `TemporalTypeTests`.

- [ ] **Step 8: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff on the second run.

- [ ] **Step 9: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated src/MassiveDotNet.Rest/Models/GroupedDailyBar.cs src/MassiveDotNet.Rest/Models/PreviousCloseBar.cs src/MassiveDotNet.Rest/Models/LastQuote.cs tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/StocksGroupedDailyTests.cs tests/MassiveDotNet.Rest.Tests/StocksPreviousCloseTests.cs tests/MassiveDotNet.Rest.Tests/StocksLastQuoteTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs
git commit -m "feat: map grouped daily, previous close, and the last NBBO quote

Two bar models beside Agg rather than one shared shape, because D16
refuses a model at a site that carries a property it lacks or lacks
one it carries (D-G4). The grouped daily fixture replaces a schema
fragment the published example pastes where its request id belongs.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 5: Trades and quotes: the v3 tick feeds with a nanosecond filter

**Files:**
- Modify: `specs/endpoints.map.json` (two model rows, two endpoint rows)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Create: `src/MassiveDotNet.Rest/Models/Trade.cs`
- Create: `src/MassiveDotNet.Rest/Models/Quote.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/StocksTradesTests.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/StocksQuotesTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 10 → 12)

**Interfaces:**
- Consumes: `DateOrNanoseconds` (Task 1); the generator's `DateOrNanoseconds` element binding (Task 2); `Epoch.FromNanoseconds` (Task 3).
- Produces: `client.Stocks.ListTradesAsync(string ticker, RangeFilter<DateOrNanoseconds>? timestamp = null, SortOrder? order = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<Trade>>`, and `EnumerateTradesAsync` with the same parameters returning `IAsyncEnumerable<Trade>`; `ListQuotesAsync` / `EnumerateQuotesAsync` likewise over `Quote`. Models: `readonly partial record struct Trade` (`TradeId`, `SipTimestampNanoseconds`, `ParticipantTimestampNanoseconds`, `TrfTimestampNanoseconds`, `SequenceNumber`, `Price`, `Size`, `DecimalSize`, `ExchangeId`, `TrfId`, `Conditions`, `CorrectionIndicator`, `Tape`, computed `SipTimestamp`, `ParticipantTimestamp`, `TrfTimestamp`); `Quote` (`SipTimestampNanoseconds`, `ParticipantTimestampNanoseconds`, `TrfTimestampNanoseconds`, `SequenceNumber`, `BidPrice`, `BidSize`, `BidExchangeId`, `AskPrice`, `AskSize`, `AskExchangeId`, `Conditions`, `Indicators`, `Tape`, the same three computed instants). Task 8's `[Obsolete]` messages name `Stocks.ListTradesAsync` and `Stocks.ListQuotesAsync`, so this task must land first. Tasks 10 and 11 call `ListTradesAsync`.

- [ ] **Step 1: Add the fixtures**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, before the `SingularWithoutResults` member:

```csharp
    /// <summary>The documented sample for GET /v3/trades/{stockTicker}, verbatim.</summary>
    public const string StocksTrades = """
        {
          "next_url": "https://api.massive.com/v3/trades/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "a47d1beb8c11b6ae897ab76cdbbf35a3",
          "results": [
            {
              "conditions": [
                12,
                41
              ],
              "decimal_size": "100.0",
              "exchange": 11,
              "id": "1",
              "participant_timestamp": 1517562000015577000,
              "price": 171.55,
              "sequence_number": 1063,
              "sip_timestamp": 1517562000016036600,
              "size": 100,
              "tape": 3
            },
            {
              "conditions": [
                12,
                41
              ],
              "decimal_size": "100.0",
              "exchange": 11,
              "id": "2",
              "participant_timestamp": 1517562000015577600,
              "price": 171.55,
              "sequence_number": 1064,
              "sip_timestamp": 1517562000016038100,
              "size": 100,
              "tape": 3
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// A hand-written final page in the trades envelope's shape, with no <c>next_url</c> and one
    /// trade later than the sample's, so a traversal that starts from <see cref="StocksTrades"/>
    /// ends after two requests. The service cannot be asked for "the page after the published
    /// sample".
    /// </summary>
    public const string StocksTradesLastPage = """
        {
          "request_id": "0d5b6f1e9f3c4a7b8e2d1c0f9a8b7c6d",
          "results": [
            {
              "conditions": [
                12
              ],
              "decimal_size": "50.0",
              "exchange": 11,
              "id": "3",
              "participant_timestamp": 1517562000015580000,
              "price": 171.56,
              "sequence_number": 1065,
              "sip_timestamp": 1517562000016040000,
              "size": 50,
              "tape": 3
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /v3/quotes/{stockTicker}, verbatim.</summary>
    public const string StocksQuotes = """
        {
          "next_url": "https://api.massive.com/v3/quotes/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "a47d1beb8c11b6ae897ab76cdbbf35a3",
          "results": [
            {
              "ask_exchange": 0,
              "ask_price": 0,
              "ask_size": 0,
              "bid_exchange": 11,
              "bid_price": 102.7,
              "bid_size": 60,
              "conditions": [
                1
              ],
              "participant_timestamp": 1517562000065321200,
              "sequence_number": 2060,
              "sip_timestamp": 1517562000065700400,
              "tape": 3
            },
            {
              "ask_exchange": 0,
              "ask_price": 0,
              "ask_size": 0,
              "bid_exchange": 11,
              "bid_price": 170,
              "bid_size": 2,
              "conditions": [
                1
              ],
              "participant_timestamp": 1517562000065408300,
              "sequence_number": 2061,
              "sip_timestamp": 1517562000065791500,
              "tape": 3
            }
          ],
          "status": "OK"
        }
        """;
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/StocksTradesTests.cs`:

```csharp
using System.Text.Json;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The v3 trades feed: the first operation whose timestamp filter takes a nanosecond count (D20),
/// and a paginated array of tick-level structs.
/// </summary>
public sealed class StocksTradesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // 2018-02-02T09:00:00.016036600Z, the sample's first SIP timestamp, exactly.
    private static readonly Instant FirstSipTimestamp = NodaConstants.UnixEpoch + Duration.FromNanoseconds(1517562000016036600);

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersAnInstantBoundAsNineteenDigitsAndADateBoundAsIso()
    {
        StubHandler handler = new(Fixtures.StocksTrades);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListTradesAsync(
                "AAPL",
                timestamp: RangeFilter.Between(
                    DateOrNanoseconds.FromInstant(FirstSipTimestamp),
                    DateOrNanoseconds.FromDate(new LocalDate(2018, 2, 3))),
                order: SortOrder.Ascending,
                limit: 2,
                sort: "timestamp",
                cancellationToken: Ct);
        }

        // Nineteen digits: a millisecond render would have been thirteen, and would have asked
        // for a moment in 1970 that the service answers with an empty page rather than an error.
        Assert.Equal(
            "https://api.massive.com/v3/trades/AAPL?timestamp.gte=1517562000016036600&timestamp.lte=2018-02-03&order=asc&limit=2&sort=timestamp",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task ADateEqualityRendersThePlainField()
    {
        StubHandler handler = new(Fixtures.StocksTrades);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListTradesAsync("AAPL", timestamp: DateOrNanoseconds.FromDate(new LocalDate(2018, 2, 2)), cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v3/trades/AAPL?timestamp=2018-02-02", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSampleToTheNanosecond()
    {
        StubHandler handler = new(Fixtures.StocksTrades);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Trade> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListTradesAsync("AAPL", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);
        Assert.Equal("a47d1beb8c11b6ae897ab76cdbbf35a3", page.RequestId);

        Trade first = page.Results[0];
        Assert.Equal("1", first.TradeId);
        Assert.Equal(171.55, first.Price);
        Assert.Equal(100d, first.Size);
        Assert.Equal("100.0", first.DecimalSize);
        Assert.Equal(11, first.ExchangeId);
        Assert.Equal(1063L, first.SequenceNumber);
        Assert.Equal(3, first.Tape);
        Assert.Equal([12, 41], first.Conditions!);
        Assert.Null(first.CorrectionIndicator);
        Assert.Null(first.TrfId);
        Assert.Null(first.TrfTimestampNanoseconds);
        Assert.Null(first.TrfTimestamp);

        Assert.Equal(1517562000016036600, first.SipTimestampNanoseconds);
        Assert.Equal(FirstSipTimestamp, first.SipTimestamp);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1517562000015577000), first.ParticipantTimestamp);

        Assert.Equal("2", page.Results[1].TradeId);
        Assert.Equal(1517562000016038100, page.Results[1].SipTimestampNanoseconds);
    }

    [Fact]
    public async Task ReportsNoFurtherPagesFromAPageWithoutACursor()
    {
        StubHandler handler = new(Fixtures.StocksTradesLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Trade> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListTradesAsync("AAPL", cancellationToken: Ct);
        }

        Assert.False(page.HasMore);
        Assert.Equal("3", Assert.Single(page.Results).TradeId);
    }

    [Fact]
    public async Task EnumerateCrossesThePageBoundaryFollowingTheCursorVerbatim()
    {
        PagingStubHandler handler = new(Fixtures.StocksTrades, Fixtures.StocksTradesLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string> ids = [];

        using (client)
        using (transport)
        {
            await foreach (Trade trade in client.Stocks.EnumerateTradesAsync("AAPL", limit: 2, cancellationToken: Ct))
            {
                ids.Add(trade.TradeId);
            }
        }

        Assert.Equal(["1", "2", "3"], ids);
        Assert.Equal(2, handler.Requests.Count);

        // The cursor is followed verbatim (D14): the second request must match the fixture's own
        // next_url exactly.
        using JsonDocument firstPage = JsonDocument.Parse(Fixtures.StocksTrades);
        Uri nextUrl = new(firstPage.RootElement.GetProperty("next_url").GetString()!);

        Assert.Equal(nextUrl.PathAndQuery, handler.Requests[1].PathAndQuery);
    }

    [Fact]
    public void RejectsABlankTickerBeforeIssuingARequest()
    {
        StubHandler handler = new(Fixtures.StocksTrades);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            Assert.Throws<ArgumentException>(() => { _ = client.Stocks.ListTradesAsync("  ", cancellationToken: Ct); });
            Assert.Throws<ArgumentException>(() => client.Stocks.EnumerateTradesAsync("  ", cancellationToken: Ct));
        }

        Assert.Null(handler.LastRequestUri);
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/StocksQuotesTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The v3 quotes feed: the same nanosecond filter as trades (D20), over NBBO structs with a bid
/// and an ask side.
/// </summary>
public sealed class StocksQuotesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersTheNanosecondRangeAndTheOrder()
    {
        StubHandler handler = new(Fixtures.StocksQuotes);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListQuotesAsync(
                "AAPL",
                timestamp: RangeFilter.Gte(DateOrNanoseconds.FromUnixNanoseconds(1517562000065700400)).Lt(DateOrNanoseconds.FromDate(new LocalDate(2018, 2, 3))),
                order: SortOrder.Descending,
                limit: 2,
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v3/quotes/AAPL?timestamp.gte=1517562000065700400&timestamp.lt=2018-02-03&order=desc&limit=2",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSampleToTheNanosecond()
    {
        StubHandler handler = new(Fixtures.StocksQuotes);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Quote> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListQuotesAsync("AAPL", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);

        Quote first = page.Results[0];
        Assert.Equal(102.7, first.BidPrice);
        Assert.Equal(60d, first.BidSize);
        Assert.Equal(11, first.BidExchangeId);
        Assert.Equal(0d, first.AskPrice);
        Assert.Equal(0d, first.AskSize);
        Assert.Equal(0, first.AskExchangeId);
        Assert.Equal(2060L, first.SequenceNumber);
        Assert.Equal(3, first.Tape);
        Assert.Equal([1], first.Conditions!);
        Assert.Null(first.Indicators);
        Assert.Null(first.TrfTimestampNanoseconds);
        Assert.Null(first.TrfTimestamp);

        Assert.Equal(1517562000065700400, first.SipTimestampNanoseconds);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1517562000065700400), first.SipTimestamp);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1517562000065321200), first.ParticipantTimestamp);

        Assert.Equal(170d, page.Results[1].BidPrice);
    }

    [Fact]
    public async Task EnumerateWalksTheSinglePage()
    {
        // The published sample advertises a cursor; the stub serves the same body again, so the
        // traversal is bounded here to prove Enumerate starts from the same URI List would.
        PagingStubHandler handler = new(Fixtures.StocksQuotes);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<long> sequenceNumbers = [];

        using (client)
        using (transport)
        {
            await foreach (Quote quote in client.Stocks.EnumerateQuotesAsync("AAPL", limit: 2, cancellationToken: Ct))
            {
                sequenceNumbers.Add(quote.SequenceNumber);

                if (sequenceNumbers.Count == 2)
                {
                    break;
                }
            }
        }

        Assert.Equal([2060L, 2061L], sequenceNumbers);
        Assert.Equal("https://api.massive.com/v3/quotes/AAPL?limit=2", handler.Requests[0].ToString());
        Assert.Single(handler.Requests);
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, change the baseline:

```csharp
    private const int CoverageBaseline = 12;
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~StocksTrades|FullyQualifiedName~StocksQuotes"`
Expected: the build fails with CS1061 (`StocksGroup` has no `ListTradesAsync`) and CS0246 for `Trade` and `Quote`.

- [ ] **Step 4: Add the model rows**

In `specs/endpoints.map.json`, append inside the `models` object after the `LastQuote` row (add a comma after its closing brace):

```json
    "Trade": {
      "kind": "struct",
      "summary": "One trade from the v3 trades feed: price, size, exchange, conditions, and three nanosecond timestamps.",
      "remarks": "A tick-level type, so a struct (decision D4). Timestamps are stored as nanosecond <see cref=\"long\"/> values and exposed as <see cref=\"NodaTime.Instant\"/> only when read (decision D5).",
      "schema": { "operationId": "Trades", "pointer": "results/items" },
      "properties": {
        "id":                    { "name": "TradeId" },
        "sip_timestamp":         { "name": "SipTimestampNanoseconds" },
        "participant_timestamp": { "name": "ParticipantTimestampNanoseconds" },
        "trf_timestamp":         { "name": "TrfTimestampNanoseconds" },
        "sequence_number":       { "name": "SequenceNumber" },
        "price":                 { "name": "Price" },
        "size":                  { "name": "Size" },
        "decimal_size":          { "name": "DecimalSize" },
        "exchange":              { "name": "ExchangeId" },
        "trf_id":                { "name": "TrfId" },
        "conditions":            { "name": "Conditions" },
        "correction":            { "name": "CorrectionIndicator" },
        "tape":                  { "name": "Tape" }
      }
    },

    "Quote": {
      "kind": "struct",
      "summary": "One NBBO quote from the v3 quotes feed: bid and ask price, size, and exchange, with conditions, indicators, and three nanosecond timestamps.",
      "remarks": "A tick-level type, so a struct (decision D4). Timestamps are stored as nanosecond <see cref=\"long\"/> values and exposed as <see cref=\"NodaTime.Instant\"/> only when read (decision D5).",
      "schema": { "operationId": "Quotes", "pointer": "results/items" },
      "properties": {
        "sip_timestamp":         { "name": "SipTimestampNanoseconds" },
        "participant_timestamp": { "name": "ParticipantTimestampNanoseconds" },
        "trf_timestamp":         { "name": "TrfTimestampNanoseconds" },
        "sequence_number":       { "name": "SequenceNumber" },
        "bid_price":             { "name": "BidPrice" },
        "bid_size":              { "name": "BidSize" },
        "bid_exchange":          { "name": "BidExchangeId" },
        "ask_price":             { "name": "AskPrice" },
        "ask_size":              { "name": "AskSize" },
        "ask_exchange":          { "name": "AskExchangeId" },
        "conditions":            { "name": "Conditions" },
        "indicators":            { "name": "Indicators" },
        "tape":                  { "name": "Tape" }
      }
    }
```

- [ ] **Step 5: Add the endpoint rows**

In `specs/endpoints.map.json`, append inside the `endpoints` array after the `LastQuote` row (add a comma after its closing brace):

```json
    {
      "operationId": "Trades",
      "group": "Stocks",
      "method": "ListTrades",
      "summary": "Retrieves tick-level trades for a stock, filtered by timestamp.",
      "remarks": "<paramref name=\"timestamp\"/> takes a calendar date for a whole session or a <see cref=\"DateOrNanoseconds\"/> for a moment within one; an <see cref=\"NodaTime.Instant\"/> converts implicitly and renders as Unix nanoseconds (decision D20). A busy session is millions of trades, so set <paramref name=\"limit\"/> and let <see cref=\"EnumerateTradesAsync\"/> follow the cursor.",
      "result": { "kind": "array", "model": "Trade", "property": "results" },
      "parameters": {
        "stockTicker": { "name": "ticker" },
        "timestamp":   { "name": "timestamp", "type": "DateOrNanoseconds" },
        "order":       { "name": "order", "type": "SortOrder" },
        "limit":       { "name": "limit" },
        "sort":        { "name": "sort" }
      }
    },
    {
      "operationId": "Quotes",
      "group": "Stocks",
      "method": "ListQuotes",
      "summary": "Retrieves tick-level NBBO quotes for a stock, filtered by timestamp.",
      "remarks": "<paramref name=\"timestamp\"/> takes a calendar date for a whole session or a <see cref=\"DateOrNanoseconds\"/> for a moment within one; an <see cref=\"NodaTime.Instant\"/> converts implicitly and renders as Unix nanoseconds (decision D20). Quotes outnumber trades many times over, so set <paramref name=\"limit\"/> and let <see cref=\"EnumerateQuotesAsync\"/> follow the cursor.",
      "result": { "kind": "array", "model": "Quote", "property": "results" },
      "parameters": {
        "stockTicker": { "name": "ticker" },
        "timestamp":   { "name": "timestamp", "type": "DateOrNanoseconds" },
        "order":       { "name": "order", "type": "SortOrder" },
        "limit":       { "name": "limit" },
        "sort":        { "name": "sort" }
      }
    }
```

- [ ] **Step 6: Regenerate and add the partials**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: `Trade.g.cs` and `Quote.g.cs` under `Generated/Models/`, two paged envelopes implementing `IPagedEnvelope<Trade>` and `IPagedEnvelope<Quote>`, and four new methods on `StocksGroup.g.cs`.

Create `src/MassiveDotNet.Rest/Models/Trade.cs`:

```csharp
using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="Trade"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct Trade
{
    /// <summary>The moment the SIP received this trade, converted from <see cref="SipTimestampNanoseconds"/>.</summary>
    [JsonIgnore]
    public Instant SipTimestamp => Epoch.FromNanoseconds(SipTimestampNanoseconds);

    /// <summary>The moment the exchange generated this trade, converted from <see cref="ParticipantTimestampNanoseconds"/>.</summary>
    [JsonIgnore]
    public Instant ParticipantTimestamp => Epoch.FromNanoseconds(ParticipantTimestampNanoseconds);

    /// <summary>
    /// The moment the trade reporting facility received this trade, converted from
    /// <see cref="TrfTimestampNanoseconds"/>, or <see langword="null"/> when the trade did not pass
    /// through one.
    /// </summary>
    [JsonIgnore]
    public Instant? TrfTimestamp => TrfTimestampNanoseconds is { } nanoseconds ? Epoch.FromNanoseconds(nanoseconds) : null;
}
```

Create `src/MassiveDotNet.Rest/Models/Quote.cs`:

```csharp
using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="Quote"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct Quote
{
    /// <summary>The moment the SIP received this quote, converted from <see cref="SipTimestampNanoseconds"/>.</summary>
    [JsonIgnore]
    public Instant SipTimestamp => Epoch.FromNanoseconds(SipTimestampNanoseconds);

    /// <summary>The moment the exchange generated this quote, converted from <see cref="ParticipantTimestampNanoseconds"/>.</summary>
    [JsonIgnore]
    public Instant ParticipantTimestamp => Epoch.FromNanoseconds(ParticipantTimestampNanoseconds);

    /// <summary>
    /// The moment the trade reporting facility received this quote, converted from
    /// <see cref="TrfTimestampNanoseconds"/>, or <see langword="null"/> when the quote did not pass
    /// through one.
    /// </summary>
    [JsonIgnore]
    public Instant? TrfTimestamp => TrfTimestampNanoseconds is { } nanoseconds ? Epoch.FromNanoseconds(nanoseconds) : null;
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS across every project.

- [ ] **Step 8: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff.

- [ ] **Step 9: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated src/MassiveDotNet.Rest/Models/Trade.cs src/MassiveDotNet.Rest/Models/Quote.cs tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/StocksTradesTests.cs tests/MassiveDotNet.Rest.Tests/StocksQuotesTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs
git commit -m "feat: map the v3 trades and quotes feeds

The first use of DateOrNanoseconds: the timestamp filter renders an
Instant as nineteen digits and a LocalDate as an ISO date, which a
fixture can pin exactly. Mapped before the deprecated v2 pair so their
Obsolete messages have a method to name (D18).

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 6: Snapshots: six nested models, three operations, and the direction enum

**Files:**
- Modify: `specs/endpoints.map.json` (six model rows, three endpoint rows)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Create: `src/MassiveDotNet.Rest/Models/SnapshotMinute.cs`
- Create: `src/MassiveDotNet.Rest/Models/SnapshotLastQuote.cs`
- Create: `src/MassiveDotNet.Rest/Models/SnapshotLastTrade.cs`
- Create: `src/MassiveDotNet.Rest/Models/TickerSnapshot.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/StocksSnapshotsTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 12 → 15)

**Interfaces:**
- Consumes: `SnapshotDirection` (Task 1) and its generator binding (Task 2); `Epoch` (Task 3); the D19 array-parameter binding, which already exists.
- Produces: `client.Stocks.ListSnapshotsAsync(string[]? tickers = null, bool? includeOtc = null, CancellationToken cancellationToken = default)` returning `Task<TickerSnapshot[]>`; `client.Stocks.GetSnapshotAsync(string ticker, CancellationToken cancellationToken = default)` returning `Task<TickerSnapshot>`; `client.Stocks.ListMoversAsync(SnapshotDirection direction, bool? includeOtc = null, CancellationToken cancellationToken = default)` returning `Task<TickerSnapshot[]>`. Models: `sealed partial record TickerSnapshot` (`Ticker`, `Day`, `PreviousDay`, `Minute`, `LastQuote`, `LastTrade`, `TodaysChange`, `TodaysChangePercent`, `UpdatedNanoseconds`, `FairMarketValue`, computed `Updated`); structs `SnapshotDay` (`Open`, `High`, `Low`, `Close`, `Volume`, `VolumeWeightedAveragePrice`, `DecimalVolume`, `IsOtc`), `SnapshotPreviousDay` (the same without `DecimalVolume`), `SnapshotMinute` (`TimestampMilliseconds`, the OHLCV five, `VolumeWeightedAveragePrice`, `TransactionCount`, `AccumulatedVolume`, `DecimalAccumulatedVolume`, `DecimalVolume`, `IsOtc`, computed `Timestamp`), `SnapshotLastQuote` (`SipTimestampNanoseconds`, `BidPrice`, `BidSize`, `AskPrice`, `AskSize`, computed `SipTimestamp`), `SnapshotLastTrade` (`SipTimestampNanoseconds`, `TradeId`, `Price`, `Size`, `DecimalSize`, `ExchangeId`, `Conditions`, computed `SipTimestamp`). Tasks 10 and 11 call `ListSnapshotsAsync`; Task 11 calls all three.

- [ ] **Step 1: Add the fixtures**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, before the `SingularWithoutResults` member:

```csharp
    /// <summary>The documented sample for GET /v2/snapshot/locale/us/markets/stocks/tickers/{stocksTicker}, verbatim.</summary>
    public const string StocksSnapshot = """
        {
          "request_id": "657e430f1ae768891f018e08e03598d8",
          "status": "OK",
          "ticker": {
            "day": {
              "c": 120.4229,
              "dv": "28727868.0",
              "h": 120.53,
              "l": 118.81,
              "o": 119.62,
              "v": 28727868,
              "vw": 119.725
            },
            "lastQuote": {
              "P": 120.47,
              "S": 4,
              "p": 120.46,
              "s": 8,
              "t": 1605195918507251700
            },
            "lastTrade": {
              "c": [
                14,
                41
              ],
              "ds": "236.0",
              "i": "4046",
              "p": 120.47,
              "s": 236,
              "t": 1605195918306274000,
              "x": 10
            },
            "min": {
              "av": 28724441,
              "c": 120.4201,
              "dav": "28724441.0",
              "dv": "270796.0",
              "h": 120.468,
              "l": 120.37,
              "n": 762,
              "o": 120.435,
              "t": 1684428720000,
              "v": 270796,
              "vw": 120.4129
            },
            "prevDay": {
              "c": 119.49,
              "h": 119.63,
              "l": 116.44,
              "o": 117.19,
              "v": 110597265,
              "vw": 118.4998
            },
            "ticker": "AAPL",
            "todaysChange": 0.98,
            "todaysChangePerc": 0.82,
            "updated": 1605195918306274000
          }
        }
        """;

    /// <summary>
    /// The documented sample for GET /v2/snapshot/locale/us/markets/stocks/tickers, verbatim. The
    /// envelope carries a <c>count</c> and no <c>request_id</c>.
    /// </summary>
    public const string StocksSnapshots = """
        {
          "count": 1,
          "status": "OK",
          "tickers": [
            {
              "day": {
                "c": 20.506,
                "dv": "37216.0",
                "h": 20.64,
                "l": 20.506,
                "o": 20.64,
                "v": 37216,
                "vw": 20.616
              },
              "lastQuote": {
                "P": 20.6,
                "S": 22,
                "p": 20.5,
                "s": 13,
                "t": 1605192959994246100
              },
              "lastTrade": {
                "c": [
                  14,
                  41
                ],
                "ds": "2416.0",
                "i": "71675577320245",
                "p": 20.506,
                "s": 2416,
                "t": 1605192894630916600,
                "x": 4
              },
              "min": {
                "av": 37216,
                "c": 20.506,
                "dav": "37216.0",
                "dv": "5000.0",
                "h": 20.506,
                "l": 20.506,
                "n": 1,
                "o": 20.506,
                "t": 1684428600000,
                "v": 5000,
                "vw": 20.5105
              },
              "prevDay": {
                "c": 20.63,
                "h": 21,
                "l": 20.5,
                "o": 20.79,
                "v": 292738,
                "vw": 20.6939
              },
              "ticker": "BCAT",
              "todaysChange": -0.124,
              "todaysChangePerc": -0.601,
              "updated": 1605192894630916600
            }
          ]
        }
        """;

    /// <summary>The documented sample for GET /v2/snapshot/locale/us/markets/stocks/{direction}, verbatim.</summary>
    public const string StocksMovers = """
        {
          "status": "OK",
          "tickers": [
            {
              "day": {
                "c": 14.2284,
                "dv": "133963.0",
                "h": 15.09,
                "l": 14.2,
                "o": 14.33,
                "v": 133963,
                "vw": 14.5311
              },
              "lastQuote": {
                "P": 14.44,
                "S": 11,
                "p": 14.2,
                "s": 25,
                "t": 1605195929997325600
              },
              "lastTrade": {
                "c": [
                  63
                ],
                "ds": "536.0",
                "i": "79372124707124",
                "p": 14.2284,
                "s": 536,
                "t": 1605195848258266000,
                "x": 4
              },
              "min": {
                "av": 133963,
                "c": 14.2284,
                "dav": "133963.0",
                "dv": "6108.0",
                "h": 14.325,
                "l": 14.2,
                "n": 5,
                "o": 14.28,
                "t": 1684428600000,
                "v": 6108,
                "vw": 14.2426
              },
              "prevDay": {
                "c": 0.73,
                "h": 0.799,
                "l": 0.73,
                "o": 0.75,
                "v": 1568097,
                "vw": 0.7721
              },
              "ticker": "PDS",
              "todaysChange": 13.498,
              "todaysChangePerc": 1849.096,
              "updated": 1605195848258266000
            }
          ]
        }
        """;
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/StocksSnapshotsTests.cs`:

```csharp
using System.Net;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The three snapshot operations: a bare array parameter rendered comma-joined (D19), an enum in
/// the path, and one item shape of five optional nested structs shared across all three (D16).
/// </summary>
public sealed class StocksSnapshotsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] TwoTickers = ["BCAT", "BRK/B"];

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersTheTickerListCommaJoinedWithEachElementEscaped()
    {
        StubHandler handler = new(Fixtures.StocksSnapshots);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListSnapshotsAsync(tickers: TwoTickers, includeOtc: true, cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v2/snapshot/locale/us/markets/stocks/tickers?tickers=BCAT,BRK%2FB&include_otc=true",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task AnEmptyTickerListIsOmittedLikeNull()
    {
        StubHandler handler = new(Fixtures.StocksSnapshots);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListSnapshotsAsync(tickers: [], cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v2/snapshot/locale/us/markets/stocks/tickers", handler.LastRequestUri?.ToString());
    }

    [Theory]
    [InlineData(SnapshotDirection.Gainers, "gainers")]
    [InlineData(SnapshotDirection.Losers, "losers")]
    public async Task TheMoversPathCarriesTheDirection(SnapshotDirection direction, string segment)
    {
        StubHandler handler = new(Fixtures.StocksMovers);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListMoversAsync(direction, includeOtc: false, cancellationToken: Ct);
        }

        Assert.Equal(
            $"https://api.massive.com/v2/snapshot/locale/us/markets/stocks/{segment}?include_otc=false",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task BuildsTheSingleTickerPath()
    {
        StubHandler handler = new(Fixtures.StocksSnapshot);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.GetSnapshotAsync("AAPL", Ct);
        }

        Assert.Equal("https://api.massive.com/v2/snapshot/locale/us/markets/stocks/tickers/AAPL", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesTheSingleTickerSampleThroughEveryNestedStruct()
    {
        StubHandler handler = new(Fixtures.StocksSnapshot);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        TickerSnapshot snapshot;

        using (client)
        using (transport)
        {
            snapshot = await client.Stocks.GetSnapshotAsync("AAPL", Ct);
        }

        Assert.Equal("AAPL", snapshot.Ticker);
        Assert.Equal(0.98, snapshot.TodaysChange);
        Assert.Equal(0.82, snapshot.TodaysChangePercent);
        Assert.Null(snapshot.FairMarketValue);
        Assert.Equal(1605195918306274000, snapshot.UpdatedNanoseconds);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1605195918306274000), snapshot.Updated);

        Assert.NotNull(snapshot.Day);
        SnapshotDay day = snapshot.Day.Value;
        Assert.Equal(119.62, day.Open);
        Assert.Equal(120.53, day.High);
        Assert.Equal(118.81, day.Low);
        Assert.Equal(120.4229, day.Close);
        Assert.Equal(28727868d, day.Volume);
        Assert.Equal(119.725, day.VolumeWeightedAveragePrice);
        Assert.Equal("28727868.0", day.DecimalVolume);
        Assert.False(day.IsOtc);

        Assert.NotNull(snapshot.PreviousDay);
        SnapshotPreviousDay previousDay = snapshot.PreviousDay.Value;
        Assert.Equal(119.49, previousDay.Close);
        Assert.Equal(110597265d, previousDay.Volume);

        Assert.NotNull(snapshot.Minute);
        SnapshotMinute minute = snapshot.Minute.Value;
        Assert.Equal(28724441L, minute.AccumulatedVolume);
        Assert.Equal("28724441.0", minute.DecimalAccumulatedVolume);
        Assert.Equal("270796.0", minute.DecimalVolume);
        Assert.Equal(762L, minute.TransactionCount);
        Assert.Equal(120.4201, minute.Close);
        Assert.Equal(1684428720000, minute.TimestampMilliseconds);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1684428720000), minute.Timestamp);

        Assert.NotNull(snapshot.LastQuote);
        SnapshotLastQuote lastQuote = snapshot.LastQuote.Value;
        Assert.Equal(120.47, lastQuote.AskPrice);
        Assert.Equal(4, lastQuote.AskSize);
        Assert.Equal(120.46, lastQuote.BidPrice);
        Assert.Equal(8, lastQuote.BidSize);
        Assert.Equal(1605195918507251700, lastQuote.SipTimestampNanoseconds);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1605195918507251700), lastQuote.SipTimestamp);

        Assert.NotNull(snapshot.LastTrade);
        SnapshotLastTrade lastTrade = snapshot.LastTrade.Value;
        Assert.Equal("4046", lastTrade.TradeId);
        Assert.Equal(120.47, lastTrade.Price);
        Assert.Equal(236, lastTrade.Size);
        Assert.Equal("236.0", lastTrade.DecimalSize);
        Assert.Equal(10, lastTrade.ExchangeId);
        Assert.Equal([14, 41], lastTrade.Conditions);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1605195918306274000), lastTrade.SipTimestamp);
    }

    [Fact]
    public async Task DeserializesTheAllTickersSample()
    {
        StubHandler handler = new(Fixtures.StocksSnapshots);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        TickerSnapshot[] snapshots;

        using (client)
        using (transport)
        {
            snapshots = await client.Stocks.ListSnapshotsAsync(cancellationToken: Ct);
        }

        TickerSnapshot snapshot = Assert.Single(snapshots);
        Assert.Equal("BCAT", snapshot.Ticker);
        Assert.Equal(-0.601, snapshot.TodaysChangePercent);
        Assert.NotNull(snapshot.Minute);
        Assert.Equal(37216L, snapshot.Minute.Value.AccumulatedVolume);
    }

    [Fact]
    public async Task DeserializesTheMoversSample()
    {
        StubHandler handler = new(Fixtures.StocksMovers);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        TickerSnapshot[] snapshots;

        using (client)
        using (transport)
        {
            snapshots = await client.Stocks.ListMoversAsync(SnapshotDirection.Gainers, cancellationToken: Ct);
        }

        TickerSnapshot snapshot = Assert.Single(snapshots);
        Assert.Equal("PDS", snapshot.Ticker);
        Assert.Equal(1849.096, snapshot.TodaysChangePercent);
        Assert.NotNull(snapshot.LastTrade);
        Assert.Equal([63], snapshot.LastTrade.Value.Conditions);
    }

    [Fact]
    public async Task ATickerThatHasNotTradedLeavesEveryNestedStructNull()
    {
        // Each nested object is optional in the schema: a ticker with no activity in a window
        // has no bar for it. A struct member would deserialize to zeros, which reads as a
        // real bar; a nullable struct reads as absent.
        StubHandler handler = new("""
            {
              "request_id": "r",
              "status": "OK",
              "ticker": { "ticker": "AAPL", "todaysChange": 0, "todaysChangePerc": 0, "updated": 1605195918306274000 }
            }
            """);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        TickerSnapshot snapshot;

        using (client)
        using (transport)
        {
            snapshot = await client.Stocks.GetSnapshotAsync("AAPL", Ct);
        }

        Assert.Null(snapshot.Day);
        Assert.Null(snapshot.PreviousDay);
        Assert.Null(snapshot.Minute);
        Assert.Null(snapshot.LastQuote);
        Assert.Null(snapshot.LastTrade);
        Assert.Null(snapshot.FairMarketValue);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1605195918306274000), snapshot.Updated);
    }

    [Fact]
    public async Task AnAbsentUpdatedTimestampIsNull()
    {
        StubHandler handler = new("""{ "request_id": "r", "status": "OK", "ticker": { "ticker": "AAPL" } }""");
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        TickerSnapshot snapshot;

        using (client)
        using (transport)
        {
            snapshot = await client.Stocks.GetSnapshotAsync("AAPL", Ct);
        }

        Assert.Null(snapshot.UpdatedNanoseconds);
        Assert.Null(snapshot.Updated);
    }

    [Fact]
    public async Task ASuccessWithoutItsPayloadThrowsNamingTheTickerProperty()
    {
        StubHandler handler = new(Fixtures.SingularWithoutResults);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Stocks.GetSnapshotAsync("AAPL", Ct));

            Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
            Assert.Equal("r", exception.RequestId);
            Assert.Contains("carried no 'ticker' payload", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnAbsentTickersArrayIsAnEmptyArray()
    {
        StubHandler handler = new("""{ "count": 0, "status": "OK" }""");
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            Assert.Empty(await client.Stocks.ListMoversAsync(SnapshotDirection.Losers, cancellationToken: Ct));
        }
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, change the baseline:

```csharp
    private const int CoverageBaseline = 15;
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~StocksSnapshots"`
Expected: the build fails with CS1061 (`StocksGroup` has no `ListSnapshotsAsync`) and CS0246 for the six model types.

- [ ] **Step 4: Add the model rows**

In `specs/endpoints.map.json`, append inside the `models` object after the `Quote` row (add a comma after its closing brace). The five nested models are declared from the single-ticker operation; the other two operations name them through `TickerSnapshot`, and the generator's structural check verifies each site (D16).

```json
    "SnapshotDay": {
      "kind": "struct",
      "summary": "The current trading day's bar inside a snapshot: open, high, low, close, volume, and the day's volume as a decimal string.",
      "remarks": "A struct like <see cref=\"Agg\"/> (decision D4). It carries no timestamp: the day is the snapshot's own. <see cref=\"SnapshotPreviousDay\"/> is the same shape without <see cref=\"DecimalVolume\"/>, which is why the two are separate models (decision D16).",
      "schema": { "operationId": "GetStocksSnapshotTicker", "pointer": "ticker/day" },
      "properties": {
        "o":   { "name": "Open" },
        "h":   { "name": "High" },
        "l":   { "name": "Low" },
        "c":   { "name": "Close" },
        "v":   { "name": "Volume" },
        "vw":  { "name": "VolumeWeightedAveragePrice" },
        "dv":  { "name": "DecimalVolume" },
        "otc": { "name": "IsOtc", "type": "bool", "summary": "Whether this aggregate is for an OTC ticker. The API omits the field entirely when false, which deserializes to false here." }
      }
    },

    "SnapshotPreviousDay": {
      "kind": "struct",
      "summary": "The previous trading day's bar inside a snapshot: open, high, low, close, and volume.",
      "remarks": "A struct like <see cref=\"Agg\"/> (decision D4). <see cref=\"SnapshotDay\"/> carries a decimal volume this shape lacks, which is why the two are separate models (decision D16).",
      "schema": { "operationId": "GetStocksSnapshotTicker", "pointer": "ticker/prevDay" },
      "properties": {
        "o":   { "name": "Open" },
        "h":   { "name": "High" },
        "l":   { "name": "Low" },
        "c":   { "name": "Close" },
        "v":   { "name": "Volume" },
        "vw":  { "name": "VolumeWeightedAveragePrice" },
        "otc": { "name": "IsOtc", "type": "bool", "summary": "Whether this aggregate is for an OTC ticker. The API omits the field entirely when false, which deserializes to false here." }
      }
    },

    "SnapshotMinute": {
      "kind": "struct",
      "summary": "The most recent minute bar inside a snapshot, with the day's accumulated volume so far.",
      "remarks": "A struct like <see cref=\"Agg\"/> (decision D4). <see cref=\"Timestamp\"/> is computed from <see cref=\"TimestampMilliseconds\"/> only when read (decision D5). The description declares the timestamp and the accumulated volume as bare integers; both overflow int32, so the map types them <see cref=\"long\"/>.",
      "schema": { "operationId": "GetStocksSnapshotTicker", "pointer": "ticker/min" },
      "properties": {
        "t":   { "name": "TimestampMilliseconds", "type": "long", "summary": "The Unix millisecond timestamp for the start of the minute window. The description declares a bare integer; every value overflows int32." },
        "o":   { "name": "Open" },
        "h":   { "name": "High" },
        "l":   { "name": "Low" },
        "c":   { "name": "Close" },
        "v":   { "name": "Volume" },
        "vw":  { "name": "VolumeWeightedAveragePrice" },
        "n":   { "name": "TransactionCount", "type": "long" },
        "av":  { "name": "AccumulatedVolume", "type": "long", "summary": "The accumulated volume for the day so far. The description declares a bare integer; a busy session overflows int32." },
        "dav": { "name": "DecimalAccumulatedVolume" },
        "dv":  { "name": "DecimalVolume" },
        "otc": { "name": "IsOtc", "type": "bool", "summary": "Whether this aggregate is for an OTC ticker. The API omits the field entirely when false, which deserializes to false here." }
      }
    },

    "SnapshotLastQuote": {
      "kind": "struct",
      "summary": "The most recent NBBO quote inside a snapshot: bid and ask price and size, with the SIP timestamp.",
      "remarks": "A tick-level type, so a struct (decision D4). <see cref=\"SipTimestamp\"/> is computed from <see cref=\"SipTimestampNanoseconds\"/> only when read (decision D5).",
      "schema": { "operationId": "GetStocksSnapshotTicker", "pointer": "ticker/lastQuote" },
      "properties": {
        "t": { "name": "SipTimestampNanoseconds", "type": "long", "summary": "The nanosecond Unix timestamp at which the SIP received this quote. The description declares a bare integer; every value overflows int32." },
        "p": { "name": "BidPrice" },
        "s": { "name": "BidSize" },
        "P": { "name": "AskPrice" },
        "S": { "name": "AskSize" }
      }
    },

    "SnapshotLastTrade": {
      "kind": "struct",
      "summary": "The most recent trade inside a snapshot: price, size, exchange, conditions, and the SIP timestamp.",
      "remarks": "A tick-level type, so a struct (decision D4). <see cref=\"SipTimestamp\"/> is computed from <see cref=\"SipTimestampNanoseconds\"/> only when read (decision D5).",
      "schema": { "operationId": "GetStocksSnapshotTicker", "pointer": "ticker/lastTrade" },
      "properties": {
        "t":  { "name": "SipTimestampNanoseconds", "type": "long", "summary": "The nanosecond Unix timestamp at which the SIP received this trade. The description declares a bare integer; every value overflows int32." },
        "i":  { "name": "TradeId" },
        "p":  { "name": "Price" },
        "s":  { "name": "Size" },
        "ds": { "name": "DecimalSize" },
        "x":  { "name": "ExchangeId" },
        "c":  { "name": "Conditions" }
      }
    },

    "TickerSnapshot": {
      "summary": "The current state of one ticker: today's and the previous day's bars, the latest minute bar, the last quote and trade, and today's change.",
      "remarks": "A container of five optional structs rather than a tick, so a class (decision D4). Each nested object is a model of its own (decision D16), <see langword=\"null\"/> when the ticker has not traded in the window it describes. <see cref=\"Updated\"/> is computed from <see cref=\"UpdatedNanoseconds\"/> only when read (decision D5). The three snapshot operations share this shape, and the generator verifies each site against it.",
      "schema": { "operationId": "GetStocksSnapshotTicker", "pointer": "ticker" },
      "properties": {
        "ticker":           { "name": "Ticker" },
        "day":              { "name": "Day", "model": "SnapshotDay" },
        "prevDay":          { "name": "PreviousDay", "model": "SnapshotPreviousDay" },
        "min":              { "name": "Minute", "model": "SnapshotMinute" },
        "lastQuote":        { "name": "LastQuote", "model": "SnapshotLastQuote" },
        "lastTrade":        { "name": "LastTrade", "model": "SnapshotLastTrade" },
        "todaysChange":     { "name": "TodaysChange" },
        "todaysChangePerc": { "name": "TodaysChangePercent" },
        "updated":          { "name": "UpdatedNanoseconds", "type": "long?", "summary": "The nanosecond Unix timestamp of the last update to this snapshot. The description declares a bare integer; every value overflows int32." },
        "fmv":              { "name": "FairMarketValue" }
      }
    }
```

- [ ] **Step 5: Add the endpoint rows**

In `specs/endpoints.map.json`, append inside the `endpoints` array after the `Quotes` row (add a comma after its closing brace):

```json
    {
      "operationId": "GetStocksSnapshotTicker",
      "group": "Stocks",
      "method": "GetSnapshot",
      "summary": "Retrieves the current snapshot of one stock: today's and the previous day's bars, the latest minute bar, the last quote and trade, and today's change.",
      "remarks": "A 200 without its payload is reported as <see cref=\"MassiveApiException\"/> rather than as <see langword=\"null\"/> (decision D17).",
      "result": { "kind": "object", "model": "TickerSnapshot", "property": "ticker" },
      "parameters": {
        "stocksTicker": { "name": "ticker" }
      }
    },
    {
      "operationId": "GetStocksSnapshotTickers",
      "group": "Stocks",
      "method": "ListSnapshots",
      "summary": "Retrieves the current snapshot of every US stock, or of the tickers named.",
      "remarks": "<paramref name=\"tickers\"/> renders comma-joined, the only form the service reads every element of (decision D19); <see langword=\"null\"/> or empty asks for the whole market, which is thousands of snapshots in one response. OTC securities are excluded unless <paramref name=\"includeOtc\"/> is set.",
      "result": { "kind": "array", "model": "TickerSnapshot", "property": "tickers" },
      "parameters": {
        "tickers":     { "name": "tickers" },
        "include_otc": { "name": "includeOtc" }
      }
    },
    {
      "operationId": "GetStocksSnapshotDirection",
      "group": "Stocks",
      "method": "ListMovers",
      "summary": "Retrieves the current snapshots of the day's top twenty gainers or losers.",
      "remarks": "One operation with a path enum, so one method: <paramref name=\"direction\"/> chooses the end of the market. OTC securities are excluded unless <paramref name=\"includeOtc\"/> is set.",
      "result": { "kind": "array", "model": "TickerSnapshot", "property": "tickers" },
      "parameters": {
        "direction":   { "name": "direction", "type": "SnapshotDirection" },
        "include_otc": { "name": "includeOtc" }
      }
    }
```

- [ ] **Step 6: Regenerate and add the partials**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: six new model files, three envelopes (two of them without a `RequestId`), and six new methods on `StocksGroup.g.cs`. Generation must succeed without a D16 refusal: the three snapshot schemas are property-for-property identical.

Create `src/MassiveDotNet.Rest/Models/SnapshotMinute.cs`:

```csharp
using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="SnapshotMinute"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct SnapshotMinute
{
    /// <summary>The start of the minute window, converted from <see cref="TimestampMilliseconds"/>.</summary>
    /// <remarks>The conversion happens only when read (decision D5).</remarks>
    [JsonIgnore]
    public Instant Timestamp => Epoch.FromMilliseconds(TimestampMilliseconds);
}
```

Create `src/MassiveDotNet.Rest/Models/SnapshotLastQuote.cs`:

```csharp
using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="SnapshotLastQuote"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct SnapshotLastQuote
{
    /// <summary>The moment the SIP received this quote, converted from <see cref="SipTimestampNanoseconds"/>.</summary>
    /// <remarks>The conversion happens only when read (decision D5).</remarks>
    [JsonIgnore]
    public Instant SipTimestamp => Epoch.FromNanoseconds(SipTimestampNanoseconds);
}
```

Create `src/MassiveDotNet.Rest/Models/SnapshotLastTrade.cs`:

```csharp
using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="SnapshotLastTrade"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct SnapshotLastTrade
{
    /// <summary>The moment the SIP received this trade, converted from <see cref="SipTimestampNanoseconds"/>.</summary>
    /// <remarks>The conversion happens only when read (decision D5).</remarks>
    [JsonIgnore]
    public Instant SipTimestamp => Epoch.FromNanoseconds(SipTimestampNanoseconds);
}
```

Create `src/MassiveDotNet.Rest/Models/TickerSnapshot.cs`:

```csharp
using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="TickerSnapshot"/>, alongside the generated wire properties.
/// </summary>
public sealed partial record TickerSnapshot
{
    /// <summary>
    /// The moment this snapshot was last updated, converted from <see cref="UpdatedNanoseconds"/>,
    /// or <see langword="null"/> when the service sent none.
    /// </summary>
    /// <remarks>The conversion happens only when read (decision D5).</remarks>
    [JsonIgnore]
    public Instant? Updated => UpdatedNanoseconds is { } nanoseconds ? Epoch.FromNanoseconds(nanoseconds) : null;
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS across every project.

- [ ] **Step 8: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff.

- [ ] **Step 9: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated src/MassiveDotNet.Rest/Models/SnapshotMinute.cs src/MassiveDotNet.Rest/Models/SnapshotLastQuote.cs src/MassiveDotNet.Rest/Models/SnapshotLastTrade.cs src/MassiveDotNet.Rest/Models/TickerSnapshot.cs tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/StocksSnapshotsTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs
git commit -m "feat: map the three stocks snapshot operations

One item shape, declared once from the single-ticker operation and
verified by D16 at the two array sites. The day and previous-day bars
differ by one property, so they are two models (D-G4). The direction
route is one method over a core enum, since the coverage test counts
operations, not path values (D-G3). This is also the first mapped use
of a bare array parameter, whose comma-joined form D19 chose.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 7: Indicators: EMA and RSI reuse the SMA shape; MACD gets its own

**Files:**
- Modify: `specs/endpoints.map.json` (two model rows, three endpoint rows)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Create: `src/MassiveDotNet.Rest/Models/MacdValue.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/StocksEmaRsiTests.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/StocksMacdTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 15 → 18)

**Interfaces:**
- Consumes: the existing `IndicatorSeries`, `IndicatorValue`, and `IndicatorUnderlying` models and the `SMA` endpoint row as the pattern; `Epoch.FromMilliseconds` (Task 3).
- Produces: `client.Stocks.ListEmaAsync(...)` and `ListRsiAsync(...)` with SMA's exact parameter list (`string ticker, RangeFilter<DateOrTimestamp>? timestamp = null, AggregateTimespan? timespan = null, bool? adjusted = null, int? window = null, SeriesType? seriesType = null, bool? expandUnderlying = null, SortOrder? order = null, int? limit = null, CancellationToken cancellationToken = default`) returning `Task<MassivePagedResult<IndicatorSeries>>`, with `EnumerateEmaAsync` / `EnumerateRsiAsync` yielding `IndicatorValue`; `client.Stocks.ListMacdAsync(string ticker, RangeFilter<DateOrTimestamp>? timestamp = null, AggregateTimespan? timespan = null, bool? adjusted = null, int? shortWindow = null, int? longWindow = null, int? signalWindow = null, SeriesType? seriesType = null, bool? expandUnderlying = null, SortOrder? order = null, int? limit = null, CancellationToken cancellationToken = default)` returning `Task<MassivePagedResult<MacdSeries>>`, with `EnumerateMacdAsync` yielding `MacdValue`. Models: `sealed partial record MacdSeries` (`Values`, `Underlying`); `readonly partial record struct MacdValue` (`TimestampMilliseconds`, `Value`, `Signal`, `Histogram`, computed `Timestamp`). Task 11 calls all three.

- [ ] **Step 1: Add the fixtures**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, before the `SingularWithoutResults` member:

```csharp
    /// <summary>The documented sample for GET /v1/indicators/ema/{stockTicker}, verbatim.</summary>
    public const string StocksEma = """
        {
          "next_url": "https://api.massive.com/v1/indicators/ema/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "a47d1beb8c11b6ae897ab76cdbbf35a3",
          "results": {
            "underlying": {
              "url": "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2003-01-01/2022-07-25"
            },
            "values": [
              {
                "timestamp": 1517562000016,
                "value": 140.139
              }
            ]
          },
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /v1/indicators/rsi/{stockTicker}, verbatim.</summary>
    public const string StocksRsi = """
        {
          "next_url": "https://api.massive.com/v1/indicators/rsi/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "a47d1beb8c11b6ae897ab76cdbbf35a3",
          "results": {
            "underlying": {
              "url": "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2003-01-01/2022-07-25"
            },
            "values": [
              {
                "timestamp": 1517562000016,
                "value": 82.19
              }
            ]
          },
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /v1/indicators/macd/{stockTicker}, verbatim.</summary>
    public const string StocksMacd = """
        {
          "next_url": "https://api.massive.com/v1/indicators/macd/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "a47d1beb8c11b6ae897ab76cdbbf35a3",
          "results": {
            "underlying": {
              "url": "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2003-01-01/2022-07-25"
            },
            "values": [
              {
                "histogram": 38.3801666667,
                "signal": 106.9811666667,
                "timestamp": 1517562000016,
                "value": 145.3613333333
              },
              {
                "histogram": 41.098859136,
                "signal": 102.7386283473,
                "timestamp": 1517562001016,
                "value": 143.8374874833
              }
            ]
          },
          "status": "OK"
        }
        """;

    /// <summary>
    /// A hand-written final page in the MACD envelope's shape, with no <c>next_url</c>, so a
    /// traversal that starts from <see cref="StocksMacd"/> ends after two requests.
    /// </summary>
    public const string StocksMacdLastPage = """
        {
          "request_id": "0d5b6f1e9f3c4a7b8e2d1c0f9a8b7c6d",
          "results": {
            "underlying": {
              "url": "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2003-01-01/2022-07-24"
            },
            "values": [
              {
                "histogram": 40.1,
                "signal": 101.2,
                "timestamp": 1517562002016,
                "value": 141.3
              }
            ]
          },
          "status": "OK"
        }
        """;
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/StocksEmaRsiTests.cs`:

```csharp
using System.Text.Json;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// EMA and RSI are SMA property for property, so they reuse <see cref="IndicatorSeries"/> and
/// the generator verifies the reuse at each site (D16). These tests pin the two routes and one
/// traversal; the page shape itself is covered by <see cref="StocksIndicatorsTests"/>.
/// </summary>
public sealed class StocksEmaRsiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task EmaBuildsTheDocumentedRequestWithEveryParameter()
    {
        StubHandler handler = new(Fixtures.StocksEma);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListEmaAsync(
                "AAPL",
                timestamp: RangeFilter.Between(
                    DateOrTimestamp.FromDate(new LocalDate(2024, 1, 1)),
                    DateOrTimestamp.FromDate(new LocalDate(2024, 6, 30))),
                timespan: AggregateTimespan.Day,
                adjusted: true,
                window: 50,
                seriesType: SeriesType.Close,
                expandUnderlying: false,
                order: SortOrder.Ascending,
                limit: 1,
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v1/indicators/ema/AAPL"
                + "?timestamp.gte=2024-01-01&timestamp.lte=2024-06-30"
                + "&timespan=day&adjusted=true&window=50&series_type=close&expand_underlying=false&order=asc&limit=1",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task EmaDeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.StocksEma);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePagedResult<IndicatorSeries> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListEmaAsync("AAPL", cancellationToken: Ct);
        }

        Assert.NotNull(page.Result.Values);
        IndicatorValue value = Assert.Single(page.Result.Values);
        Assert.Equal(140.139, value.Value);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1517562000016), value.Timestamp);
        Assert.NotNull(page.Result.Underlying);
        Assert.Null(page.Result.Underlying.Aggregates);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task EmaEnumerateCrossesThePageBoundary()
    {
        // The SMA last page is the same envelope shape, which is the point of the reuse.
        PagingStubHandler handler = new(Fixtures.StocksEma, Fixtures.StocksSmaLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<long> timestamps = [];

        using (client)
        using (transport)
        {
            await foreach (IndicatorValue value in client.Stocks.EnumerateEmaAsync("AAPL", limit: 1, cancellationToken: Ct))
            {
                timestamps.Add(value.TimestampMilliseconds);
            }
        }

        Assert.Equal([1517562000016, 1517475600016], timestamps);
        Assert.Equal(2, handler.Requests.Count);

        using JsonDocument firstPage = JsonDocument.Parse(Fixtures.StocksEma);
        Uri nextUrl = new(firstPage.RootElement.GetProperty("next_url").GetString()!);
        Assert.Equal(nextUrl.PathAndQuery, handler.Requests[1].PathAndQuery);
    }

    [Fact]
    public async Task RsiBuildsTheDocumentedRequestPath()
    {
        StubHandler handler = new(Fixtures.StocksRsi);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListRsiAsync("AAPL", window: 14, timespan: AggregateTimespan.Day, cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v1/indicators/rsi/AAPL?timespan=day&window=14", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task RsiDeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.StocksRsi);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePagedResult<IndicatorSeries> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListRsiAsync("AAPL", cancellationToken: Ct);
        }

        Assert.NotNull(page.Result.Values);
        Assert.Equal(82.19, Assert.Single(page.Result.Values).Value);
        Assert.True(page.HasMore);
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/StocksMacdTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// MACD: the indicator whose values carry a signal and a histogram beside the line, and whose
/// window is three parameters rather than one.
/// </summary>
public sealed class StocksMacdTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersTheThreeWindows()
    {
        StubHandler handler = new(Fixtures.StocksMacd);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListMacdAsync(
                "AAPL",
                timespan: AggregateTimespan.Day,
                adjusted: true,
                shortWindow: 12,
                longWindow: 26,
                signalWindow: 9,
                seriesType: SeriesType.Close,
                order: SortOrder.Descending,
                limit: 2,
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v1/indicators/macd/AAPL"
                + "?timespan=day&adjusted=true&short_window=12&long_window=26&signal_window=9&series_type=close&order=desc&limit=2",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSampleWithSignalAndHistogram()
    {
        StubHandler handler = new(Fixtures.StocksMacd);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePagedResult<MacdSeries> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListMacdAsync("AAPL", cancellationToken: Ct);
        }

        Assert.NotNull(page.Result.Values);
        Assert.Equal(2, page.Result.Values.Length);

        MacdValue first = page.Result.Values[0];
        Assert.Equal(145.3613333333, first.Value);
        Assert.Equal(106.9811666667, first.Signal);
        Assert.Equal(38.3801666667, first.Histogram);
        Assert.Equal(1517562000016, first.TimestampMilliseconds);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1517562000016), first.Timestamp);

        Assert.Equal(1517562001016, page.Result.Values[1].TimestampMilliseconds);

        Assert.NotNull(page.Result.Underlying);
        Assert.Equal("https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2003-01-01/2022-07-25", page.Result.Underlying.Url);
        Assert.True(page.HasMore);
        Assert.Equal("a47d1beb8c11b6ae897ab76cdbbf35a3", page.RequestId);
    }

    [Fact]
    public async Task EnumerateYieldsEveryValueAcrossPages()
    {
        PagingStubHandler handler = new(Fixtures.StocksMacd, Fixtures.StocksMacdLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<double> histograms = [];

        using (client)
        using (transport)
        {
            await foreach (MacdValue value in client.Stocks.EnumerateMacdAsync("AAPL", limit: 2, cancellationToken: Ct))
            {
                histograms.Add(value.Histogram);
            }
        }

        Assert.Equal([38.3801666667, 41.098859136, 40.1], histograms);
        Assert.Equal(2, handler.Requests.Count);
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, change the baseline:

```csharp
    private const int CoverageBaseline = 18;
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~StocksEmaRsi|FullyQualifiedName~StocksMacd"`
Expected: the build fails with CS1061 (`StocksGroup` has no `ListEmaAsync`) and CS0246 for `MacdSeries` and `MacdValue`.

- [ ] **Step 4: Add the model rows**

In `specs/endpoints.map.json`, append inside the `models` object after the `TickerSnapshot` row (add a comma after its closing brace):

```json
    "MacdSeries": {
      "summary": "One page of the MACD indicator: the values computed for the page, and the aggregates they were computed from.",
      "remarks": "<see cref=\"IndicatorSeries\"/> with a richer value: each point carries the signal line and the histogram beside the MACD line, so the values bind to <see cref=\"MacdValue\"/>. The underlying is the shape every indicator shares (decision D16). <c>Enumerate</c> yields the values and <c>List</c> returns the whole page (decision D17).",
      "schema": { "operationId": "MACD", "pointer": "results" },
      "items": "values",
      "properties": {
        "values":     { "name": "Values",     "model": "MacdValue", "summary": "The MACD values for this page, oldest or newest first as requested." },
        "underlying": { "name": "Underlying", "model": "IndicatorUnderlying" }
      }
    },

    "MacdValue": {
      "kind": "struct",
      "summary": "One point of a MACD series: the MACD line, its signal line, the histogram between them, and the timestamp of the last aggregate that produced them.",
      "remarks": "A struct, like <see cref=\"IndicatorValue\"/> (decision D4). <see cref=\"Timestamp\"/> is computed from <see cref=\"TimestampMilliseconds\"/> only when read (decision D5).",
      "schema": { "operationId": "MACD", "pointer": "results/values/items" },
      "properties": {
        "timestamp": { "name": "TimestampMilliseconds", "type": "long", "summary": "The Unix millisecond timestamp of the last aggregate used in this calculation." },
        "value":     { "name": "Value", "type": "double", "summary": "The MACD line: the short exponential moving average minus the long one." },
        "signal":    { "name": "Signal", "type": "double", "summary": "The signal line: an exponential moving average of the MACD line over the signal window." },
        "histogram": { "name": "Histogram", "type": "double", "summary": "The MACD line minus the signal line." }
      }
    }
```

- [ ] **Step 5: Add the endpoint rows**

In `specs/endpoints.map.json`, append inside the `endpoints` array after the `GetStocksSnapshotDirection` row (add a comma after its closing brace):

```json
    {
      "operationId": "EMA",
      "group": "Stocks",
      "method": "ListEma",
      "summary": "Retrieves the exponential moving average (EMA) of a stock's price over a window of aggregates.",
      "remarks": "The same page shape as <see cref=\"ListSmaAsync\"/>: each page carries the values computed for it and, with <paramref name=\"expandUnderlying\"/>, the aggregates they were computed from. <paramref name=\"timespan\"/> accepts every <see cref=\"AggregateTimespan\"/> except <see cref=\"AggregateTimespan.Second\"/>, which this endpoint does not offer and rejects with a 400.",
      "result": { "kind": "object", "model": "IndicatorSeries", "property": "results" },
      "parameters": {
        "stockTicker":       { "name": "ticker" },
        "timestamp":         { "name": "timestamp", "type": "DateOrTimestamp" },
        "timespan":          { "name": "timespan", "type": "AggregateTimespan" },
        "adjusted":          { "name": "adjusted" },
        "window":            { "name": "window" },
        "series_type":       { "name": "seriesType", "type": "SeriesType" },
        "expand_underlying": { "name": "expandUnderlying" },
        "order":             { "name": "order", "type": "SortOrder" },
        "limit":             { "name": "limit" }
      }
    },
    {
      "operationId": "RSI",
      "group": "Stocks",
      "method": "ListRsi",
      "summary": "Retrieves the relative strength index (RSI) of a stock's price over a window of aggregates.",
      "remarks": "The same page shape as <see cref=\"ListSmaAsync\"/>: each page carries the values computed for it and, with <paramref name=\"expandUnderlying\"/>, the aggregates they were computed from. <paramref name=\"timespan\"/> accepts every <see cref=\"AggregateTimespan\"/> except <see cref=\"AggregateTimespan.Second\"/>, which this endpoint does not offer and rejects with a 400.",
      "result": { "kind": "object", "model": "IndicatorSeries", "property": "results" },
      "parameters": {
        "stockTicker":       { "name": "ticker" },
        "timestamp":         { "name": "timestamp", "type": "DateOrTimestamp" },
        "timespan":          { "name": "timespan", "type": "AggregateTimespan" },
        "adjusted":          { "name": "adjusted" },
        "window":            { "name": "window" },
        "series_type":       { "name": "seriesType", "type": "SeriesType" },
        "expand_underlying": { "name": "expandUnderlying" },
        "order":             { "name": "order", "type": "SortOrder" },
        "limit":             { "name": "limit" }
      }
    },
    {
      "operationId": "MACD",
      "group": "Stocks",
      "method": "ListMacd",
      "summary": "Retrieves the moving average convergence/divergence (MACD) of a stock's price: the MACD line, its signal line, and the histogram between them.",
      "remarks": "Three windows replace the single <c>window</c> of the other indicators: <paramref name=\"shortWindow\"/> and <paramref name=\"longWindow\"/> size the two averages whose difference is the MACD line, and <paramref name=\"signalWindow\"/> sizes the average of that line. Each page carries the values computed for it and, with <paramref name=\"expandUnderlying\"/>, the aggregates they were computed from. <paramref name=\"timespan\"/> accepts every <see cref=\"AggregateTimespan\"/> except <see cref=\"AggregateTimespan.Second\"/>, which this endpoint does not offer and rejects with a 400.",
      "result": { "kind": "object", "model": "MacdSeries", "property": "results" },
      "parameters": {
        "stockTicker":       { "name": "ticker" },
        "timestamp":         { "name": "timestamp", "type": "DateOrTimestamp" },
        "timespan":          { "name": "timespan", "type": "AggregateTimespan" },
        "adjusted":          { "name": "adjusted" },
        "short_window":      { "name": "shortWindow" },
        "long_window":       { "name": "longWindow" },
        "signal_window":     { "name": "signalWindow" },
        "series_type":       { "name": "seriesType", "type": "SeriesType" },
        "expand_underlying": { "name": "expandUnderlying" },
        "order":             { "name": "order", "type": "SortOrder" },
        "limit":             { "name": "limit" }
      }
    }
```

- [ ] **Step 6: Regenerate and add the partial**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: `MacdSeries.g.cs` and `MacdValue.g.cs`, three paged envelopes, and six new methods on `StocksGroup.g.cs`. The EMA and RSI rows must pass the D16 reuse check without a refusal; the MACD `underlying` site must pass it for `IndicatorUnderlying`.

Create `src/MassiveDotNet.Rest/Models/MacdValue.cs`:

```csharp
using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="MacdValue"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct MacdValue
{
    /// <summary>
    /// The moment of the last aggregate used to compute this point, converted from
    /// <see cref="TimestampMilliseconds"/>.
    /// </summary>
    /// <remarks>The conversion happens only when read (decision D5).</remarks>
    [JsonIgnore]
    public Instant Timestamp => Epoch.FromMilliseconds(TimestampMilliseconds);
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS across every project.

- [ ] **Step 8: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff.

- [ ] **Step 9: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated src/MassiveDotNet.Rest/Models/MacdValue.cs tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/StocksEmaRsiTests.cs tests/MassiveDotNet.Rest.Tests/StocksMacdTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs
git commit -m "feat: map EMA, RSI, and MACD

EMA and RSI are SMA property for property and reuse IndicatorSeries,
which D16 verifies at each site. MACD's values carry a signal and a
histogram, so it gets a page model of its own over the shared
underlying.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 8: The deprecated v2 tick pair, with the map correcting the description

**Files:**
- Modify: `tests/MassiveDotNet.Rest.Tests/MassiveDotNet.Rest.Tests.csproj`
- Modify: `tests/MassiveDotNet.IntegrationTests/MassiveDotNet.IntegrationTests.csproj`
- Modify: `specs/endpoints.map.json` (two model rows, two endpoint rows)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/StocksHistoricTicksTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 18 → 20)

**Interfaces:**
- Consumes: `ListTradesAsync` and `ListQuotesAsync` (Task 5), which the generator resolves as the replacements from the description's `x-polygon-deprecation.replaces.path` slugs `get_v3_trades__stockticker` and `get_v3_quotes__stockticker`.
- Produces: `client.Stocks.ListHistoricTradesAsync(string ticker, LocalDate date, long? timestamp = null, long? timestampLimit = null, bool? reverse = null, int? limit = null, CancellationToken cancellationToken = default)` returning `Task<HistoricTrade[]>`, marked `[Obsolete("Massive has deprecated this operation. Use Stocks.ListTradesAsync instead.", DiagnosticId = "MASSIVE0002")]`; `ListHistoricQuotesAsync` with the same parameters returning `Task<HistoricQuote[]>`, naming `Stocks.ListQuotesAsync`. Models: `readonly partial record struct HistoricTrade` (`Ticker`, `SipTimestampNanoseconds`, `ParticipantTimestampNanoseconds`, `TrfTimestampNanoseconds`, `SequenceNumber`, `TradeId`, `Price`, `Size`, `ExchangeId`, `TrfId`, `Conditions`, `CorrectionIndicator`, `Tape`) and `HistoricQuote` (`Ticker`, `SipTimestampNanoseconds`, `ParticipantTimestampNanoseconds`, `TrfTimestampNanoseconds`, `SequenceNumber`, `BidPrice`, `BidSize`, `BidExchangeId`, `AskPrice`, `AskSize`, `AskExchangeId`, `Conditions`, `Indicators`, `Tape`). No partials, no computed instants (D-G5). Task 11 calls both.

- [ ] **Step 1: Suppress the deprecation diagnostic in the two test projects**

In `tests/MassiveDotNet.Rest.Tests/MassiveDotNet.Rest.Tests.csproj`, add a property group after the first one:

```xml
  <PropertyGroup>
    <!--
      The deprecated tick endpoints are exercised here on purpose (constitution rule 2): the SDK
      ships them marked Obsolete, and a test has to call them to prove that. The suppression is
      scoped to this project; nothing is ever suppressed inside generated code (D18).
    -->
    <NoWarn>$(NoWarn);MASSIVE0002</NoWarn>
  </PropertyGroup>
```

In `tests/MassiveDotNet.IntegrationTests/MassiveDotNet.IntegrationTests.csproj`, add the same property group with the same comment after the first one.

- [ ] **Step 2: Add the fixtures**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, before the `SingularWithoutResults` member. Both are used unchanged: the requiredness defect lives in the schema, and the map absorbs it (D-G7).

```csharp
    /// <summary>
    /// The documented sample for GET /v2/ticks/stocks/trades/{ticker}/{date}, verbatim. The
    /// schema marks <c>T</c>, <c>f</c>, <c>e</c>, and <c>r</c> required; the sample omits all
    /// four, which is why the map types them nullable (D-G5). The <c>map</c> member is a key
    /// legend the schema does not declare, and is ignored on the way in.
    /// </summary>
    public const string StocksHistoricTrades = """
        {
          "db_latency": 11,
          "map": {
            "I": {
              "name": "orig_id",
              "type": "string"
            },
            "c": {
              "name": "conditions",
              "type": "int"
            },
            "e": {
              "name": "correction",
              "type": "int"
            },
            "f": {
              "name": "trf_timestamp",
              "type": "int64"
            },
            "i": {
              "name": "id",
              "type": "string"
            },
            "p": {
              "name": "price",
              "type": "float64"
            },
            "q": {
              "name": "sequence_number",
              "type": "int64"
            },
            "r": {
              "name": "trf_id",
              "type": "int"
            },
            "s": {
              "name": "size",
              "type": "int"
            },
            "t": {
              "name": "sip_timestamp",
              "type": "int64"
            },
            "x": {
              "name": "exchange",
              "type": "int"
            },
            "y": {
              "name": "participant_timestamp",
              "type": "int64"
            },
            "z": {
              "name": "tape",
              "type": "int"
            }
          },
          "results": [
            {
              "c": [
                12,
                41
              ],
              "i": "1",
              "p": 171.55,
              "q": 1063,
              "s": 100,
              "t": 1517562000016036600,
              "x": 11,
              "y": 1517562000015577000,
              "z": 3
            },
            {
              "c": [
                12,
                41
              ],
              "i": "2",
              "p": 171.55,
              "q": 1064,
              "s": 100,
              "t": 1517562000016038100,
              "x": 11,
              "y": 1517562000015577600,
              "z": 3
            }
          ],
          "results_count": 2,
          "success": true,
          "ticker": "AAPL"
        }
        """;

    /// <summary>
    /// The documented sample for GET /v2/ticks/stocks/nbbo/{ticker}/{date}, verbatim. The schema
    /// marks <c>T</c>, <c>f</c>, and <c>i</c> required; the sample omits all three, which is why
    /// the map types them nullable (D-G5).
    /// </summary>
    public const string StocksHistoricQuotes = """
        {
          "db_latency": 43,
          "map": {
            "P": {
              "name": "ask_price",
              "type": "float64"
            },
            "S": {
              "name": "ask_size",
              "type": "int"
            },
            "X": {
              "name": "ask_exchange",
              "type": "int"
            },
            "c": {
              "name": "conditions",
              "type": "int"
            },
            "f": {
              "name": "trf_timestamp",
              "type": "int64"
            },
            "i": {
              "name": "indicators",
              "type": "int"
            },
            "p": {
              "name": "bid_price",
              "type": "float64"
            },
            "q": {
              "name": "sequence_number",
              "type": "int"
            },
            "s": {
              "name": "bid_size",
              "type": "int"
            },
            "t": {
              "name": "sip_timestamp",
              "type": "int64"
            },
            "x": {
              "name": "bid_exchange",
              "type": "int"
            },
            "y": {
              "name": "participant_timestamp",
              "type": "int64"
            },
            "z": {
              "name": "tape",
              "type": "int"
            }
          },
          "results": [
            {
              "P": 0,
              "S": 0,
              "X": 0,
              "c": [
                1
              ],
              "p": 102.7,
              "q": 2060,
              "s": 60,
              "t": 1517562000065700400,
              "x": 11,
              "y": 1517562000065321200,
              "z": 3
            },
            {
              "P": 0,
              "S": 0,
              "X": 0,
              "c": [
                1
              ],
              "p": 170,
              "q": 2061,
              "s": 2,
              "t": 1517562000065791500,
              "x": 11,
              "y": 1517562000065408300,
              "z": 3
            }
          ],
          "results_count": 2,
          "success": true,
          "ticker": "AAPL"
        }
        """;
```

- [ ] **Step 3: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/StocksHistoricTicksTests.cs`:

```csharp
using System.Reflection;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The deprecated v2 tick endpoints, shipped marked <c>[Obsolete]</c> (rule 2). The description
/// marks fields required that its own examples omit and declares nanosecond timestamps as bare
/// integers; the map corrects both (D-G5), and these tests prove the unchanged published examples
/// deserialize as a result.
/// </summary>
public sealed class StocksHistoricTicksTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly LocalDate Session = new(2018, 2, 2);

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task TradesPathCarriesTheDateAndTheOffsetsRenderAsLongs()
    {
        StubHandler handler = new(Fixtures.StocksHistoricTrades);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListHistoricTradesAsync(
                "AAPL",
                Session,
                timestamp: 1517562000016036600,
                timestampLimit: 1517562000016038100,
                reverse: true,
                limit: 2,
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v2/ticks/stocks/trades/AAPL/2018-02-02?timestamp=1517562000016036600&timestampLimit=1517562000016038100&reverse=true&limit=2",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task TradesDeserializeThePublishedSampleWithTheFalselyRequiredFieldsNull()
    {
        StubHandler handler = new(Fixtures.StocksHistoricTrades);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        HistoricTrade[] trades;

        using (client)
        using (transport)
        {
            trades = await client.Stocks.ListHistoricTradesAsync("AAPL", Session, cancellationToken: Ct);
        }

        Assert.Equal(2, trades.Length);

        HistoricTrade first = trades[0];
        Assert.Equal("1", first.TradeId);
        Assert.Equal(171.55, first.Price);
        Assert.Equal(100d, first.Size);
        Assert.Equal(11, first.ExchangeId);
        Assert.Equal(1063L, first.SequenceNumber);
        Assert.Equal(3, first.Tape);
        Assert.Equal([12, 41], first.Conditions);
        Assert.Equal(1517562000016036600, first.SipTimestampNanoseconds);
        Assert.Equal(1517562000015577000, first.ParticipantTimestampNanoseconds);

        // The four the schema requires and the sample omits: a required modifier on any of them
        // would have made this a JsonException.
        Assert.Null(first.Ticker);
        Assert.Null(first.TrfTimestampNanoseconds);
        Assert.Null(first.CorrectionIndicator);
        Assert.Null(first.TrfId);

        Assert.Equal("2", trades[1].TradeId);
    }

    [Fact]
    public async Task QuotesPathCarriesTheDate()
    {
        StubHandler handler = new(Fixtures.StocksHistoricQuotes);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListHistoricQuotesAsync("AAPL", Session, limit: 2, cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v2/ticks/stocks/nbbo/AAPL/2018-02-02?limit=2", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task QuotesDeserializeThePublishedSampleWithTheFalselyRequiredFieldsNull()
    {
        StubHandler handler = new(Fixtures.StocksHistoricQuotes);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        HistoricQuote[] quotes;

        using (client)
        using (transport)
        {
            quotes = await client.Stocks.ListHistoricQuotesAsync("AAPL", Session, cancellationToken: Ct);
        }

        Assert.Equal(2, quotes.Length);

        HistoricQuote first = quotes[0];
        Assert.Equal(102.7, first.BidPrice);
        Assert.Equal(60, first.BidSize);
        Assert.Equal(11, first.BidExchangeId);
        Assert.Equal(0d, first.AskPrice);
        Assert.Equal(0, first.AskSize);
        Assert.Equal(0, first.AskExchangeId);
        Assert.Equal(2060L, first.SequenceNumber);
        Assert.Equal(3, first.Tape);
        Assert.Equal([1], first.Conditions);
        Assert.Equal(1517562000065700400, first.SipTimestampNanoseconds);
        Assert.Equal(1517562000065321200, first.ParticipantTimestampNanoseconds);

        Assert.Null(first.Ticker);
        Assert.Null(first.TrfTimestampNanoseconds);
        Assert.Null(first.Indicators);

        Assert.Equal(170d, quotes[1].BidPrice);
    }

    [Theory]
    [InlineData("ListHistoricTradesAsync", "Stocks.ListTradesAsync")]
    [InlineData("ListHistoricQuotesAsync", "Stocks.ListQuotesAsync")]
    public void TheObsoleteMessageNamesTheReplacement(string method, string replacement)
    {
        // EndpointCoverageTests checks that the attribute is present with the right id; this pins
        // the message, which is the part a consumer actually reads.
        ObsoleteAttribute? attribute = typeof(StocksGroup).GetMethod(method)?.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal("MASSIVE0002", attribute.DiagnosticId);
        Assert.Equal($"Massive has deprecated this operation. Use {replacement} instead.", attribute.Message);
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, change the baseline:

```csharp
    private const int CoverageBaseline = 20;
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~StocksHistoricTicks"`
Expected: the build fails with CS1061 (`StocksGroup` has no `ListHistoricTradesAsync`) and CS0246 for the two model types.

- [ ] **Step 5: Add the model rows**

In `specs/endpoints.map.json`, append inside the `models` object after the `MacdValue` row (add a comma after its closing brace):

```json
    "HistoricTrade": {
      "kind": "struct",
      "summary": "One trade from the deprecated v2 historic ticks endpoint. Prefer <see cref=\"Trade\"/>, which the v3 endpoint returns.",
      "remarks": "Generated members only: no computed instants are added, because a consumer who wants them is meant to move to <see cref=\"Trade\"/>. The description marks <c>T</c>, <c>f</c>, <c>e</c>, and <c>r</c> required although its own example omits all four, and declares the nanosecond timestamps as bare integers; the map corrects both, and each corrected row says so.",
      "schema": { "operationId": "DeprecatedGetHistoricStocksTrades", "pointer": "results/items" },
      "properties": {
        "T": { "name": "Ticker", "type": "string?", "summary": "The exchange symbol that this item is traded under. The description marks it required; the published example omits it." },
        "t": { "name": "SipTimestampNanoseconds", "type": "long", "summary": "The nanosecond Unix timestamp at which the SIP received this trade. The description declares a bare integer; every value overflows int32." },
        "y": { "name": "ParticipantTimestampNanoseconds", "type": "long", "summary": "The nanosecond Unix timestamp at which the trade was generated at the exchange. The description declares a bare integer; every value overflows int32." },
        "f": { "name": "TrfTimestampNanoseconds", "type": "long?", "summary": "The nanosecond Unix timestamp at which the trade reporting facility received this trade, when it passed through one. The description marks it required; the published example omits it." },
        "q": { "name": "SequenceNumber" },
        "i": { "name": "TradeId" },
        "p": { "name": "Price" },
        "s": { "name": "Size" },
        "x": { "name": "ExchangeId" },
        "r": { "name": "TrfId", "type": "int?", "summary": "The ID for the Trade Reporting Facility where the trade took place. The description marks it required; the published example omits it." },
        "c": { "name": "Conditions" },
        "e": { "name": "CorrectionIndicator", "type": "int?", "summary": "The trade correction indicator. The description marks it required; the published example omits it." },
        "z": { "name": "Tape" }
      }
    },

    "HistoricQuote": {
      "kind": "struct",
      "summary": "One NBBO quote from the deprecated v2 historic ticks endpoint. Prefer <see cref=\"Quote\"/>, which the v3 endpoint returns.",
      "remarks": "Generated members only: no computed instants are added, because a consumer who wants them is meant to move to <see cref=\"Quote\"/>. The description marks <c>T</c>, <c>f</c>, and <c>i</c> required although its own example omits all three, and declares the nanosecond timestamps as bare integers; the map corrects both, and each corrected row says so.",
      "schema": { "operationId": "DeprecatedGetHistoricStocksQuotes", "pointer": "results/items" },
      "properties": {
        "T": { "name": "Ticker", "type": "string?", "summary": "The exchange symbol that this item is traded under. The description marks it required; the published example omits it." },
        "t": { "name": "SipTimestampNanoseconds", "type": "long", "summary": "The nanosecond Unix timestamp at which the SIP received this quote. The description declares a bare integer; every value overflows int32." },
        "y": { "name": "ParticipantTimestampNanoseconds", "type": "long", "summary": "The nanosecond Unix timestamp at which the quote was generated at the exchange. The description declares a bare integer; every value overflows int32." },
        "f": { "name": "TrfTimestampNanoseconds", "type": "long?", "summary": "The nanosecond Unix timestamp at which the trade reporting facility received this quote, when it passed through one. The description marks it required; the published example omits it." },
        "q": { "name": "SequenceNumber" },
        "p": { "name": "BidPrice" },
        "s": { "name": "BidSize" },
        "x": { "name": "BidExchangeId" },
        "P": { "name": "AskPrice" },
        "S": { "name": "AskSize" },
        "X": { "name": "AskExchangeId" },
        "c": { "name": "Conditions" },
        "i": { "name": "Indicators", "type": "int[]?", "summary": "The indicator codes. The description marks it required; the published example omits it." },
        "z": { "name": "Tape" }
      }
    }
```

- [ ] **Step 6: Add the endpoint rows**

In `specs/endpoints.map.json`, append inside the `endpoints` array after the `MACD` row (add a comma after its closing brace). Stability is not declared here: the generator reads `x-polygon-deprecation` from the description and resolves the replacement from its slug (D18).

```json
    {
      "operationId": "DeprecatedGetHistoricStocksTrades",
      "group": "Stocks",
      "method": "ListHistoricTrades",
      "summary": "Retrieves tick-level trades for a stock on one trading day from the deprecated v2 endpoint.",
      "remarks": "Pagination here is manual: pass the last result's <see cref=\"HistoricTrade.SipTimestampNanoseconds\"/> as <paramref name=\"timestamp\"/> to fetch the next page. <see cref=\"ListTradesAsync\"/> replaces this with a cursor the SDK follows for you.",
      "result": { "kind": "array", "model": "HistoricTrade", "property": "results" },
      "parameters": {
        "ticker":         { "name": "ticker" },
        "date":           { "name": "date" },
        "timestamp":      { "name": "timestamp", "type": "long" },
        "timestampLimit": { "name": "timestampLimit", "type": "long" },
        "reverse":        { "name": "reverse" },
        "limit":          { "name": "limit" }
      }
    },
    {
      "operationId": "DeprecatedGetHistoricStocksQuotes",
      "group": "Stocks",
      "method": "ListHistoricQuotes",
      "summary": "Retrieves tick-level NBBO quotes for a stock on one trading day from the deprecated v2 endpoint.",
      "remarks": "Pagination here is manual: pass the last result's <see cref=\"HistoricQuote.SipTimestampNanoseconds\"/> as <paramref name=\"timestamp\"/> to fetch the next page. <see cref=\"ListQuotesAsync\"/> replaces this with a cursor the SDK follows for you.",
      "result": { "kind": "array", "model": "HistoricQuote", "property": "results" },
      "parameters": {
        "ticker":         { "name": "ticker" },
        "date":           { "name": "date" },
        "timestamp":      { "name": "timestamp", "type": "long" },
        "timestampLimit": { "name": "timestampLimit", "type": "long" },
        "reverse":        { "name": "reverse" },
        "limit":          { "name": "limit" }
      }
    }
```

- [ ] **Step 7: Regenerate**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: `HistoricTrade.g.cs` and `HistoricQuote.g.cs`, two envelopes without a `RequestId` (the schema has `db_latency`, `results_count`, `success`, and `ticker` instead), and two methods on `StocksGroup.g.cs`, each preceded by `[Obsolete("Massive has deprecated this operation. Use Stocks.ListTradesAsync instead.", DiagnosticId = "MASSIVE0002")]` or its quotes counterpart. No partial is created (D-G5).

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS across every project, including `EndpointCoverageTests.StabilityAttributesMatchTheSpecification`, which now finds two deprecated operations and requires the attribute on both.

- [ ] **Step 9: Prove the generator is deterministic and the build is warning-free**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/ && dotnet build MassiveDotNet.slnx`
Expected: no diff; 0 warnings. The integration project compiles the two calls Task 11 will add only then, but its `NoWarn` lands now so both projects change in one place.

- [ ] **Step 10: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/StocksHistoricTicksTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs tests/MassiveDotNet.Rest.Tests/MassiveDotNet.Rest.Tests.csproj tests/MassiveDotNet.IntegrationTests/MassiveDotNet.IntegrationTests.csproj
git commit -m "feat: map the deprecated v2 historic trades and quotes

Shipped marked Obsolete naming the v3 methods, as rule 2 requires. The
description marks fields required that its own examples omit and types
nanosecond timestamps as bare integers; the map types the former
nullable and the latter long, and the unchanged published examples are
the proof (D-G5). No partials: a consumer who wants computed instants
moves to Trade and Quote.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 9: Splits and exchanges: two reference lists under Stocks

**Files:**
- Modify: `specs/endpoints.map.json` (two model rows, two endpoint rows)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/StocksSplitsTests.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/StocksExchangesTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 20 → 22)

**Interfaces:**
- Consumes: the `Dividend` model row and `get_stocks_v1_dividends` endpoint row as the pattern; the existing filter bindings (`Filter<T>` for a range-and-set group, `SetFilter<T>` for plain-plus-`any_of`, `RangeFilter<T>` for plain-plus-four-bounds).
- Produces: `client.Stocks.ListSplitsAsync(Filter<string>? ticker = null, RangeFilter<LocalDate>? executionDate = null, SetFilter<string>? adjustmentType = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<Split>>` with `EnumerateSplitsAsync`; `client.Stocks.ListExchangesAsync(int? limit = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<StockExchange>>` with `EnumerateExchangesAsync`. Models: `sealed partial record Split` (`Ticker`, `ExecutionDate` as `LocalDate?`, `AdjustmentType`, `SplitFrom`, `SplitTo`, `HistoricalAdjustmentFactor`, `Id`); `sealed partial record StockExchange` (`Id`, `Name`, `Acronym`, `Type`, `Locale`, `Mic`, `OperatingMic`, `ParticipantId`, `Url`). No partials. Task 11 calls both.

- [ ] **Step 1: Add the fixtures**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, before the `SingularWithoutResults` member:

```csharp
    /// <summary>
    /// The documented sample for GET /stocks/v1/splits, with one departure from the published
    /// text: the sample shows <c>"request_id": 1</c>, a number, while the envelope schema declares
    /// a string and every other endpoint returns one. The fixture uses the string, as the
    /// dividends fixture does for the same defect.
    /// </summary>
    public const string StocksSplits = """
        {
          "request_id": "1",
          "results": [
            {
              "adjustment_type": "forward_split",
              "execution_date": "2005-02-28",
              "historical_adjustment_factor": 0.017857,
              "id": "E90a77bdf742661741ed7c8fc086415f0457c2816c45899d73aaa88bdc8ff6025",
              "split_from": 1,
              "split_to": 2,
              "ticker": "AAPL"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// The documented sample for GET /stocks/v1/exchanges, with one departure from the published
    /// text: <c>"request_id": 1</c> becomes a string, for the reason given on
    /// <see cref="StocksSplits"/>. The <c>count</c> member is not in the schema and is ignored.
    /// </summary>
    public const string StocksExchanges = """
        {
          "count": 2,
          "request_id": "1",
          "results": [
            {
              "id": "10",
              "locale": "US",
              "mic": "XNYS",
              "name": "New York Stock Exchange",
              "operating_mic": "XNYS",
              "participant_id": "N",
              "type": "exchange",
              "url": "https://www.nyse.com"
            },
            {
              "id": "12",
              "locale": "US",
              "mic": "XNAS",
              "name": "Nasdaq",
              "operating_mic": "XNAS",
              "participant_id": "T",
              "type": "exchange",
              "url": "https://www.nasdaq.com"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// A hand-written final page in the exchanges envelope's shape, with no <c>next_url</c>, so a
    /// traversal from a cursored copy of <see cref="StocksExchanges"/> ends after two requests.
    /// The published sample has no cursor of its own; the test adds one.
    /// </summary>
    public const string StocksExchangesLastPage = """
        {
          "count": 1,
          "request_id": "2",
          "results": [
            {
              "id": "15",
              "locale": "US",
              "mic": "IEXG",
              "name": "Investors Exchange",
              "operating_mic": "IEXG",
              "participant_id": "V",
              "type": "exchange",
              "url": "https://www.iextrading.com"
            }
          ],
          "status": "OK"
        }
        """;
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/StocksSplitsTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Splits: a range-and-set filter on the ticker, a calendar-date range, and a plain-plus-any_of
/// set, each derived from the spec's suffix set (D15).
/// </summary>
public sealed class StocksSplitsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersEqualityRangeAndSetFiltersInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.StocksSplits);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListSplitsAsync(
                ticker: "AAPL",
                executionDate: RangeFilter.Gte(new LocalDate(2020, 1, 1)),
                adjustmentType: SetFilter.AnyOf("forward_split", "reverse_split"),
                limit: 10,
                sort: "execution_date.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/v1/splits"
                + "?ticker=AAPL"
                + "&execution_date.gte=2020-01-01"
                + "&adjustment_type.any_of=forward_split,reverse_split"
                + "&limit=10&sort=execution_date.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task AcceptsATickerSetAndAnAdjustmentTypeEquality()
    {
        StubHandler handler = new(Fixtures.StocksSplits);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListSplitsAsync(
                ticker: SetFilter.AnyOf("AAPL", "MSFT"),
                adjustmentType: "stock_dividend",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/v1/splits?ticker.any_of=AAPL,MSFT&adjustment_type=stock_dividend",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesTheCorrectedSample()
    {
        StubHandler handler = new(Fixtures.StocksSplits);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Split> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListSplitsAsync(ticker: "AAPL", cancellationToken: Ct);
        }

        Split split = Assert.Single(page.Results);
        Assert.Equal("AAPL", split.Ticker);
        Assert.Equal(new LocalDate(2005, 2, 28), split.ExecutionDate);
        Assert.Equal("forward_split", split.AdjustmentType);
        Assert.Equal(1d, split.SplitFrom);
        Assert.Equal(2d, split.SplitTo);
        Assert.Equal(0.017857, split.HistoricalAdjustmentFactor);
        Assert.Equal("E90a77bdf742661741ed7c8fc086415f0457c2816c45899d73aaa88bdc8ff6025", split.Id);
        Assert.False(page.HasMore);
        Assert.Equal("1", page.RequestId);
    }

    [Fact]
    public async Task EnumerateWalksTheSinglePage()
    {
        StubHandler handler = new(Fixtures.StocksSplits);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string?> ids = [];

        using (client)
        using (transport)
        {
            await foreach (Split split in client.Stocks.EnumerateSplitsAsync(ticker: "AAPL", cancellationToken: Ct))
            {
                ids.Add(split.Id);
            }
        }

        Assert.Equal("E90a77bdf742661741ed7c8fc086415f0457c2816c45899d73aaa88bdc8ff6025", Assert.Single(ids));
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/StocksExchangesTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Exchanges: the one paginated operation in this batch with a single optional parameter, and
/// the one whose traversal is proven against two stub pages.
/// </summary>
public sealed class StocksExchangesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Cursor = "https://api.massive.com/stocks/v1/exchanges?cursor=YWZ0ZXI9MTI";

    /// <summary>The published sample with a cursor added, since the sample itself has none.</summary>
    private static readonly string FirstPage = Fixtures.StocksExchanges.Replace(
        "\"count\": 2,",
        $"\"count\": 2,\n  \"next_url\": \"{Cursor}\",",
        StringComparison.Ordinal);

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task BuildsTheDocumentedRequestPath()
    {
        StubHandler handler = new(Fixtures.StocksExchanges);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListExchangesAsync(limit: 2, cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/stocks/v1/exchanges?limit=2", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesTheCorrectedSample()
    {
        StubHandler handler = new(Fixtures.StocksExchanges);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<StockExchange> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListExchangesAsync(cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);

        StockExchange nyse = page.Results[0];
        Assert.Equal("10", nyse.Id);
        Assert.Equal("New York Stock Exchange", nyse.Name);
        Assert.Equal("XNYS", nyse.Mic);
        Assert.Equal("XNYS", nyse.OperatingMic);
        Assert.Equal("N", nyse.ParticipantId);
        Assert.Equal("exchange", nyse.Type);
        Assert.Equal("US", nyse.Locale);
        Assert.Equal("https://www.nyse.com", nyse.Url);
        Assert.Null(nyse.Acronym);

        Assert.Equal("Nasdaq", page.Results[1].Name);
        Assert.False(page.HasMore);
        Assert.Equal("1", page.RequestId);
    }

    [Fact]
    public async Task EnumerateTraversesTwoPagesFollowingTheCursorVerbatim()
    {
        PagingStubHandler handler = new(FirstPage, Fixtures.StocksExchangesLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string> names = [];

        using (client)
        using (transport)
        {
            await foreach (StockExchange exchange in client.Stocks.EnumerateExchangesAsync(cancellationToken: Ct))
            {
                names.Add(exchange.Name);
            }
        }

        Assert.Equal(["New York Stock Exchange", "Nasdaq", "Investors Exchange"], names);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(Cursor, handler.Requests[1].ToString());
    }

    [Fact]
    public async Task ListReportsMorePagesFromTheCursoredCopy()
    {
        StubHandler handler = new(FirstPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<StockExchange> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListExchangesAsync(cancellationToken: Ct);
        }

        Assert.True(page.HasMore);
        Assert.Equal(2, page.Results.Length);
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, change the baseline to its final value:

```csharp
    private const int CoverageBaseline = 22;
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~StocksSplits|FullyQualifiedName~StocksExchanges"`
Expected: the build fails with CS1061 (`StocksGroup` has no `ListSplitsAsync`) and CS0246 for `Split` and `StockExchange`.

- [ ] **Step 4: Add the model rows**

In `specs/endpoints.map.json`, append inside the `models` object after the `HistoricQuote` row (add a comma after its closing brace):

```json
    "Split": {
      "summary": "A stock split or similar share-count change: the ticker, the execution date, the ratio, and the factor that normalises historical prices for it.",
      "remarks": "Reference data, so a class rather than a struct (decision D4). <see cref=\"ExecutionDate\"/> is a calendar date with no time or zone, hence <see cref=\"NodaTime.LocalDate\"/>.",
      "schema": { "operationId": "get_stocks_v1_splits", "pointer": "results/items" },
      "properties": {
        "ticker":                       { "name": "Ticker" },
        "execution_date":               { "name": "ExecutionDate" },
        "adjustment_type":              { "name": "AdjustmentType" },
        "split_from":                   { "name": "SplitFrom" },
        "split_to":                     { "name": "SplitTo" },
        "historical_adjustment_factor": { "name": "HistoricalAdjustmentFactor" },
        "id":                           { "name": "Id" }
      }
    },

    "StockExchange": {
      "summary": "An exchange or trade reporting facility that US stocks trade on, with its MIC codes and identifiers.",
      "remarks": "Reference data, so a class rather than a struct (decision D4). Prefixed by family because other asset classes list exchanges of their own, whose shapes the generator's structural check will judge when those groups arrive (decision D16).",
      "schema": { "operationId": "get_stocks_v1_exchanges", "pointer": "results/items" },
      "properties": {
        "id":             { "name": "Id" },
        "name":           { "name": "Name" },
        "acronym":        { "name": "Acronym" },
        "type":           { "name": "Type" },
        "locale":         { "name": "Locale" },
        "mic":            { "name": "Mic" },
        "operating_mic":  { "name": "OperatingMic" },
        "participant_id": { "name": "ParticipantId" },
        "url":            { "name": "Url" }
      }
    }
```

- [ ] **Step 5: Add the endpoint rows**

In `specs/endpoints.map.json`, append inside the `endpoints` array after the `DeprecatedGetHistoricStocksQuotes` row (add a comma after its closing brace):

```json
    {
      "operationId": "get_stocks_v1_splits",
      "group": "Stocks",
      "method": "ListSplits",
      "summary": "Retrieves stock splits and similar share-count changes for US stocks, with the execution date and ratio of each.",
      "remarks": "Every filter is optional and defaults to no constraint. Pass a plain value for equality, a <see cref=\"RangeFilter\"/> factory for a range, or <see cref=\"SetFilter\"/> for a set of values. Lives beside <see cref=\"ListDividendsAsync\"/> because the description marks it a stocks operation.",
      "result": { "kind": "array", "model": "Split", "property": "results" },
      "parameters": {
        "ticker":          { "name": "ticker" },
        "execution_date":  { "name": "executionDate", "type": "LocalDate" },
        "adjustment_type": { "name": "adjustmentType" },
        "limit":           { "name": "limit" },
        "sort":            { "name": "sort" }
      }
    },
    {
      "operationId": "get_stocks_v1_exchanges",
      "group": "Stocks",
      "method": "ListExchanges",
      "summary": "Retrieves the exchanges and trade reporting facilities that US stocks trade on.",
      "remarks": "The list is short and rarely changes, so a single page usually holds all of it; <see cref=\"EnumerateExchangesAsync\"/> follows the cursor if the service ever pages it.",
      "result": { "kind": "array", "model": "StockExchange", "property": "results" },
      "parameters": {
        "limit": { "name": "limit" }
      }
    }
```

- [ ] **Step 6: Regenerate**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: `Split.g.cs` (with `using NodaTime;` for the `LocalDate?`) and `StockExchange.g.cs`, two paged envelopes, and four methods on `StocksGroup.g.cs`: `ListSplitsAsync` with `Filter<string>? ticker`, `RangeFilter<LocalDate>? executionDate`, and `SetFilter<string>? adjustmentType`; `ListExchangesAsync` with only `int? limit`. No partials.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS across every project; `EndpointCoverageTests.ReportsOperationsThatRemainUnmapped` now reports 22 mapped.

- [ ] **Step 8: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff.

- [ ] **Step 9: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/StocksSplitsTests.cs tests/MassiveDotNet.Rest.Tests/StocksExchangesTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs
git commit -m "feat: map stock splits and exchanges under Stocks

Splits carries the stocks entitlement and belongs beside dividends;
exchanges is the last of the issue's unnamed three that ships here.
Both fixtures correct the numeric request_id the published examples
share with dividends. Coverage reaches 22 (D-G1).

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 10: Root the new instantiations in the AOT smoke test and record D20

**Files:**
- Modify: `samples/MassiveDotNet.AotSmokeTest/Program.cs`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: `ListTradesAsync` (Task 5), `ListSnapshotsAsync` (Task 6), `DateOrNanoseconds` (Task 1).
- Produces: nothing new. The publish is the proof for rules 3 and 4.

The AOT smoke test is the enforcement mechanism for rules 3 and 4, and an unreferenced generic instantiation is simply trimmed away. `AppendElement<DateOrNanoseconds>` and `AppendQuery<string>(string[])` are new instantiations of the builder's `typeof(T)` dispatch, and the snapshot envelope is the first with five optional nested structs; a clean publish says nothing about any of them unless the sample reaches them.

- [ ] **Step 1: Add the two calls to the sample**

In `samples/MassiveDotNet.AotSmokeTest/Program.cs`, insert the following after the market holidays block (after the `if (holidays is not [.., { Status: "early-close", Open: not null }]) { ... }` statement) and before `Console.WriteLine($"\nrequests: {handler.Requests}");`:

```csharp
// The nanosecond filter type and the bare array parameter are new generic instantiations of the
// builder's element dispatch (D19, D20), and the snapshot item is the first model with five
// optional nested structs; each is reachable only through these two calls.
Console.WriteLine("\ntrades, with a nanosecond range:");

MassivePage<Trade> trades = await client.Stocks.ListTradesAsync(
    "AAPL",
    timestamp: RangeFilter.Between(
        DateOrNanoseconds.FromInstant(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1517562000000000000)),
        DateOrNanoseconds.FromDate(new LocalDate(2018, 2, 3))),
    order: SortOrder.Ascending,
    limit: 2);

Console.WriteLine($"request : {handler.LastRequestUri}");

foreach (Trade trade in trades.Results)
{
    Console.WriteLine($"  {trade.TradeId,-3} {trade.Price,9:F2} x {trade.Size,6:N0}  at {InstantPattern.ExtendedIso.Format(trade.SipTimestamp)}");
}

const string ExpectedTradesQuery = "?timestamp.gte=1517562000000000000&timestamp.lte=2018-02-03&order=asc&limit=2";

if (handler.LastRequestUri?.Query != ExpectedTradesQuery)
{
    Console.Error.WriteLine($"FAIL: expected the trades query {ExpectedTradesQuery}; got {handler.LastRequestUri?.Query}.");
    return 1;
}

if (!trades.HasMore || trades.Results is not [{ SipTimestampNanoseconds: 1517562000016036600 }, _])
{
    Console.Error.WriteLine("FAIL: expected two trades, the first at 1517562000016036600, with more pages.");
    return 1;
}

Console.WriteLine("\nsnapshots, for two tickers:");

TickerSnapshot[] snapshots = await client.Stocks.ListSnapshotsAsync(tickers: ["BCAT", "BRK/B"], includeOtc: false);

Console.WriteLine($"request : {handler.LastRequestUri}");

foreach (TickerSnapshot snapshot in snapshots)
{
    Console.WriteLine($"  {snapshot.Ticker,-6} day close {snapshot.Day?.Close,9:F3}  accumulated volume {snapshot.Minute?.AccumulatedVolume,10:N0}");
}

const string ExpectedSnapshotsQuery = "?tickers=BCAT,BRK%2FB&include_otc=false";

if (handler.LastRequestUri?.Query != ExpectedSnapshotsQuery)
{
    Console.Error.WriteLine($"FAIL: expected the snapshots query {ExpectedSnapshotsQuery}; got {handler.LastRequestUri?.Query}.");
    return 1;
}

if (snapshots is not [{ Ticker: "BCAT", Minute: { AccumulatedVolume: 37216 }, LastTrade: { SipTimestampNanoseconds: 1605192894630916600 }, Updated: not null }])
{
    Console.Error.WriteLine("FAIL: expected one BCAT snapshot with accumulated volume 37216 and a last trade at 1605192894630916600.");
    return 1;
}
```

Then update the request-count check that follows. Replace the comment and condition:

```csharp
// Two pages of the aggregates enumeration, the single-page aggregates call, the dividends call,
// the news call, one SMA page, two SMA pages enumerated, the last trade, the open/close day, the
// holidays, the trades page, and the snapshots.
if (enumerated != 3 || handler.Requests != 13)
{
    Console.Error.WriteLine(
        $"FAIL: expected 3 enumerated bars over 13 requests; got {enumerated} over {handler.Requests}.");
    return 1;
}
```

In the sample's `StubHandler` class, add two bodies after the `Holidays` constant:

```csharp
    private const string Trades = """
        {
          "next_url": "https://api.massive.com/v3/trades/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "a47d1beb8c11b6ae897ab76cdbbf35a3",
          "results": [
            { "conditions": [ 12, 41 ], "decimal_size": "100.0", "exchange": 11, "id": "1", "participant_timestamp": 1517562000015577000, "price": 171.55, "sequence_number": 1063, "sip_timestamp": 1517562000016036600, "size": 100, "tape": 3 },
            { "conditions": [ 12, 41 ], "decimal_size": "100.0", "exchange": 11, "id": "2", "participant_timestamp": 1517562000015577600, "price": 171.55, "sequence_number": 1064, "sip_timestamp": 1517562000016038100, "size": 100, "tape": 3 }
          ],
          "status": "OK"
        }
        """;

    private const string Snapshots = """
        {
          "count": 1,
          "status": "OK",
          "tickers": [
            {
              "day": { "c": 20.506, "dv": "37216.0", "h": 20.64, "l": 20.506, "o": 20.64, "v": 37216, "vw": 20.616 },
              "lastQuote": { "P": 20.6, "S": 22, "p": 20.5, "s": 13, "t": 1605192959994246100 },
              "lastTrade": { "c": [ 14, 41 ], "ds": "2416.0", "i": "71675577320245", "p": 20.506, "s": 2416, "t": 1605192894630916600, "x": 4 },
              "min": { "av": 37216, "c": 20.506, "dav": "37216.0", "dv": "5000.0", "h": 20.506, "l": 20.506, "n": 1, "o": 20.506, "t": 1684428600000, "v": 5000, "vw": 20.5105 },
              "prevDay": { "c": 20.63, "h": 21, "l": 20.5, "o": 20.79, "v": 292738, "vw": 20.6939 },
              "ticker": "BCAT",
              "todaysChange": -0.124,
              "todaysChangePerc": -0.601,
              "updated": 1605192894630916600
            }
          ]
        }
        """;
```

And add two arms to the route switch in `SendAsync`, before the `_ =>` fallback:

```csharp
            "/v3/trades/AAPL" => Trades,
            "/v2/snapshot/locale/us/markets/stocks/tickers" => Snapshots,
```

- [ ] **Step 2: Run the sample under the JIT first**

Run: `dotnet run --project samples/MassiveDotNet.AotSmokeTest`
Expected: the trades and snapshots sections print, then `AOT smoke test passed.` with exit code 0. A `FAIL:` line means the query or the deserialized shape is off; fix the sample's expectation only if the generated code is right and the expectation was mistyped.

- [ ] **Step 3: Publish Native AOT and run the binary**

Run: `dotnet publish samples/MassiveDotNet.AotSmokeTest -r osx-arm64 -c Release`
Expected: 0 warnings. Any `IL2xxx` or `IL3xxx` line is a failure of rule 3 or 4 and must be fixed in the SDK, never suppressed.

Run: `samples/MassiveDotNet.AotSmokeTest/bin/Release/net10.0/osx-arm64/publish/MassiveDotNet.AotSmokeTest`
Expected: `AOT smoke test passed.` and exit code 0. If the path differs on this machine, locate it with `find samples/MassiveDotNet.AotSmokeTest/bin -type f -name MassiveDotNet.AotSmokeTest -path '*publish*'`.

- [ ] **Step 4: Record D20 and update the two conventions in `CLAUDE.md`**

In `CLAUDE.md`, add a row to the Architecture decisions table immediately after the `D19` row:

```markdown
| D20 | Tick-level timestamp filters bind to `DateOrNanoseconds`, a second core value type with the unit in its name. It joins the closed element set beside `DateOrTimestamp`, and the map names whichever the endpoint documents. | The v3 trades and quotes `timestamp` takes "a date or a nanosecond timestamp". `DateOrTimestamp` renders an `Instant` as Unix milliseconds, so binding it there would compile and ask for a moment in 1970; adding nanosecond factories to it would keep one type but leave its implicit `Instant` conversion rendering the wrong unit on half the endpoints. Two types make the wrong unit unrepresentable. Binding the filter to `LocalDate` alone was rejected because it drops the nanosecond form the API documents, which is rule 2's silent omission arriving on the request side. |
```

Replace the **Filters** convention bullet with:

```markdown
- **Filters**: a field that carries comparator variants becomes one optional parameter typed
  `RangeFilter<T>`, `SetFilter<T>`, `Filter<T>`, or `ArrayFilter<T>` by its suffix set; a plain
  `T` converts implicitly to equality. The map names the **element** type on the base field's row
  (`"ex_dividend_date": { "type": "LocalDate" }`), never the filter type, and never a row keyed by
  a variant. Grouping is detected from the spec, never declared in the map (D15). Element types
  are a closed set: `string`, `int`, `long`, `double`, `LocalDate`, `DateOrTimestamp`, and
  `DateOrNanoseconds`; the last two differ only in the unit an `Instant` renders as, and the map
  names whichever the endpoint documents (D20). Calendar dates (`format: date`) are `LocalDate` on
  parameters and models alike, read by `LocalDateJsonConverter`, and date-times are `Instant`,
  read by `InstantJsonConverter`. A field whose own schema is `type: array` and carries no
  comparators is not a filter: it binds to `T[]?` and renders comma-joined as the plain field,
  with an empty array omitted (D19). A map override names the C# type, `string[]`, the same way it
  does for any plain parameter.
```

Replace the **Stability** convention bullet with:

```markdown
- **Stability**: a deprecated operation's entry points carry
  `[Obsolete("…", DiagnosticId = "MASSIVE0002")]`, a warning whose message names the replacement;
  a `vX` operation's carry `[Experimental("MASSIVE0001")]`, an error until a consumer opts in
  with `<NoWarn>$(NoWarn);MASSIVE0001</NoWarn>` or a `#pragma`. Both are read from the spec, never
  declared in the map (D18). A test or sample project that exercises such an endpoint suppresses
  the id in its own `.csproj`, as the REST and integration test projects do for the deprecated
  tick endpoints; nothing is ever suppressed inside generated code.
```

- [ ] **Step 5: Confirm nothing else moved**

Run: `dotnet build MassiveDotNet.slnx && dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: 0 warnings; PASS.

- [ ] **Step 6: Commit**

```bash
git add samples/MassiveDotNet.AotSmokeTest/Program.cs CLAUDE.md
git commit -m "feat: root the nanosecond filter and the snapshot array in the AOT smoke test and record decision D20

The publish is the proof for rules 3 and 4, and it proves nothing about
a generic instantiation nothing reaches: the DateOrNanoseconds element
dispatch, the bare string array, and an envelope of five optional
nested structs are each rooted here. D20 records why a second
date-or-epoch type exists rather than a parameter on the first.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 11: The live tier: D19's proof, a tick page boundary, and one call per operation

**Files:**
- Create: `tests/MassiveDotNet.IntegrationTests/StocksBarsLiveTests.cs`
- Create: `tests/MassiveDotNet.IntegrationTests/StocksTicksLiveTests.cs`
- Create: `tests/MassiveDotNet.IntegrationTests/StocksSnapshotsLiveTests.cs`
- Create: `tests/MassiveDotNet.IntegrationTests/StocksSplitsLiveTests.cs`
- Create: `tests/MassiveDotNet.IntegrationTests/StocksExchangesLiveTests.cs`
- Modify: `tests/MassiveDotNet.IntegrationTests/StocksIndicatorsLiveTests.cs`

**Interfaces:**
- Consumes: every method from Tasks 4–9; `LiveApiTest` (`Client`, `Ct`), which skips when no key is present and carries the `Integration` trait.
- Produces: nothing new. These never run in CI (rule 13); they compile there.

These tests call the real service. `LiveCredentials` finds the key in the gitignored `.env` at the repository root on its own; do not open, print, or echo that file, and do not put the key on a command line. A failure with a `403` from the tick or snapshot endpoints means the account's plan lacks that entitlement: report it as a finding, do not retry with diagnostics that could print a URL or a header.

Live tests are not test-first in the red-green sense: there is no implementation to write, and the service is the oracle. Write each class, run it, and fix the assertion only when the service's answer shows the assumption was wrong, saying so in a comment.

- [ ] **Step 1: Write the bars, ticks, and snapshots classes**

Create `tests/MassiveDotNet.IntegrationTests/StocksBarsLiveTests.cs`:

```csharp
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// One shape-asserting call each for grouped daily and previous close (D-G8), so a moved response
/// shape shows up on the next local run. Values are not asserted; the fixtures do that.
/// </summary>
public sealed class StocksBarsLiveTests : LiveApiTest
{
    // A fixed historical session, so the assertions do not depend on when the suite is run.
    private static readonly LocalDate Session = new(2024, 1, 16);

    [Fact]
    public async Task GroupedDailyReturnsABarPerTicker()
    {
        GroupedDailyBar[] bars = await Client.Stocks.ListGroupedDailyAsync(Session, adjusted: true, cancellationToken: Ct);

        Assert.NotEmpty(bars);
        Assert.Contains(bars, bar => bar.Ticker == "AAPL");

        foreach (GroupedDailyBar bar in bars)
        {
            Assert.False(string.IsNullOrEmpty(bar.Ticker));
            Assert.True(bar.High >= bar.Low, $"{bar.Ticker}: high {bar.High} was below low {bar.Low}.");
        }
    }

    [Fact]
    public async Task PreviousCloseReturnsOneRecentBar()
    {
        PreviousCloseBar[] bars = await Client.Stocks.ListPreviousCloseAsync("AAPL", cancellationToken: Ct);

        PreviousCloseBar bar = Assert.Single(bars);
        Assert.True(bar.Volume > 0, "A trading day should report volume.");
        Assert.True(bar.High >= bar.Low, $"High {bar.High} was below low {bar.Low}.");

        // Previous close is relative to today, so only recency is checkable; a week covers any
        // long weekend.
        Instant now = SystemClock.Instance.GetCurrentInstant();
        Assert.InRange(bar.Timestamp, now - Duration.FromDays(7), now);
    }
}
```

Create `tests/MassiveDotNet.IntegrationTests/StocksTicksLiveTests.cs`:

```csharp
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// The tick endpoints against the real service: a traversal across a real page boundary on the
/// v3 trades envelope, the nanosecond bound that <see cref="DateOrNanoseconds"/> exists for (D20),
/// and one call each for quotes, the last quote, and the deprecated v2 pair (D-G8).
/// </summary>
public sealed class StocksTicksLiveTests : LiveApiTest
{
    // A fixed historical session, so the assertions do not depend on when the suite is run.
    private static readonly LocalDate Session = new(2024, 1, 16);

    // 10:00 Eastern on that session, which is 15:00 UTC in January.
    private static readonly Instant MidMorning = Instant.FromUtc(2024, 1, 16, 15, 0);

    [Fact]
    public async Task TradesCrossARealPageBoundary()
    {
        // limit is per page, so five trades at two per page is at least three round trips, and
        // the seams are where a rebuilt cursor would repeat or skip.
        List<Trade> trades = [];

        await foreach (Trade trade in Client.Stocks.EnumerateTradesAsync(
            "AAPL",
            timestamp: DateOrNanoseconds.FromDate(Session),
            order: SortOrder.Ascending,
            limit: 2,
            cancellationToken: Ct))
        {
            trades.Add(trade);

            if (trades.Count >= 5)
            {
                break;
            }
        }

        Assert.Equal(5, trades.Count);
        Assert.Equal(
            trades.Select(t => t.SipTimestampNanoseconds).Order(),
            trades.Select(t => t.SipTimestampNanoseconds));
        Assert.Equal(5, trades.Select(t => t.SequenceNumber).Distinct().Count());

        foreach (Trade trade in trades)
        {
            // An Eastern session runs from 09:00 to 01:00 UTC the next day.
            Assert.InRange(trade.SipTimestamp.InUtc().Date, Session, Session.PlusDays(1));
            Assert.True(trade.Price > 0);
        }
    }

    [Fact]
    public async Task ANanosecondBoundIsHonoured()
    {
        // A millisecond render of the same instant would name a moment in 1970, and the service
        // would answer with the oldest trades it holds rather than an error. Every trade on or
        // after the bound proves the nineteen-digit form was read as intended.
        MassivePage<Trade> page = await Client.Stocks.ListTradesAsync(
            "AAPL",
            timestamp: RangeFilter.Gte(DateOrNanoseconds.FromInstant(MidMorning)),
            order: SortOrder.Ascending,
            limit: 3,
            cancellationToken: Ct);

        Assert.Equal(3, page.Results.Length);
        Assert.True(page.HasMore);

        foreach (Trade trade in page.Results)
        {
            Assert.True(trade.SipTimestamp >= MidMorning, $"Trade at {trade.SipTimestamp} precedes the bound {MidMorning}.");
            Assert.Equal(Session, trade.SipTimestamp.InUtc().Date);
        }
    }

    [Fact]
    public async Task QuotesReturnAPage()
    {
        MassivePage<Quote> page = await Client.Stocks.ListQuotesAsync(
            "AAPL",
            timestamp: DateOrNanoseconds.FromDate(Session),
            limit: 2,
            cancellationToken: Ct);

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);

        foreach (Quote quote in page.Results)
        {
            Assert.InRange(quote.SipTimestamp.InUtc().Date, Session, Session.PlusDays(1));
            Assert.True(quote.BidPrice > 0 || quote.AskPrice > 0, "A quote should carry at least one side.");
        }
    }

    [Fact]
    public async Task LastQuoteReturnsTheTicker()
    {
        LastQuote quote = await Client.Stocks.GetLastQuoteAsync("AAPL", Ct);

        Assert.Equal("AAPL", quote.Ticker);
        Assert.True(quote.SipTimestamp > Instant.FromUtc(2024, 1, 1, 0, 0), "The last quote should be recent.");
    }

    [Fact]
    public async Task TheDeprecatedTradesEndpointStillAnswers()
    {
        // Included so the suite reports the day Massive retires the v2 tick endpoints (D-G8).
        HistoricTrade[] trades = await Client.Stocks.ListHistoricTradesAsync("AAPL", Session, limit: 2, cancellationToken: Ct);

        Assert.Equal(2, trades.Length);
        Assert.All(trades, trade => Assert.True(trade.Price > 0));
        Assert.All(trades, trade => Assert.True(trade.SipTimestampNanoseconds > 0));
    }

    [Fact]
    public async Task TheDeprecatedQuotesEndpointStillAnswers()
    {
        HistoricQuote[] quotes = await Client.Stocks.ListHistoricQuotesAsync("AAPL", Session, limit: 2, cancellationToken: Ct);

        Assert.Equal(2, quotes.Length);
        Assert.All(quotes, quote => Assert.True(quote.SipTimestampNanoseconds > 0));
    }
}
```

Create `tests/MassiveDotNet.IntegrationTests/StocksSnapshotsLiveTests.cs`:

```csharp
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// The snapshot operations against the real service, and the proof D19 deferred to them: only a
/// live call can tell the comma-joined ticker list from the repeated-key form the OpenAPI default
/// implies, because the service reads one ticker from the latter and every ticker from the former.
/// </summary>
public sealed class StocksSnapshotsLiveTests : LiveApiTest
{
    private static readonly string[] Tickers = ["AAPL", "MSFT"];

    [Fact]
    public async Task TwoTickersReturnExactlyTwoSnapshots()
    {
        TickerSnapshot[] snapshots = await Client.Stocks.ListSnapshotsAsync(tickers: Tickers, cancellationToken: Ct);

        Assert.Equal(2, snapshots.Length);
        Assert.Equal(Tickers, snapshots.Select(snapshot => snapshot.Ticker!).Order());
    }

    [Fact]
    public async Task OneTickerReturnsItsSnapshot()
    {
        TickerSnapshot snapshot = await Client.Stocks.GetSnapshotAsync("AAPL", Ct);

        Assert.Equal("AAPL", snapshot.Ticker);
        Assert.NotNull(snapshot.PreviousDay);
        Assert.True(snapshot.PreviousDay.Value.Close > 0);
        Assert.NotNull(snapshot.Updated);
    }

    [Fact]
    public async Task MoversReturnSnapshots()
    {
        TickerSnapshot[] gainers = await Client.Stocks.ListMoversAsync(SnapshotDirection.Gainers, cancellationToken: Ct);

        Assert.NotEmpty(gainers);
        Assert.All(gainers, snapshot => Assert.False(string.IsNullOrEmpty(snapshot.Ticker)));
    }
}
```

- [ ] **Step 2: Write the splits, exchanges, and indicator additions**

Create `tests/MassiveDotNet.IntegrationTests/StocksSplitsLiveTests.cs`:

```csharp
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// One call for splits (D-G8): a ticker and a calendar-date range around a split whose ratio and
/// date are a matter of record.
/// </summary>
public sealed class StocksSplitsLiveTests : LiveApiTest
{
    // AAPL's 4-for-1 split on 2020-08-31 is the only AAPL split in this window.
    private static readonly LocalDate WindowStart = new(2020, 1, 1);
    private static readonly LocalDate WindowEnd = new(2020, 12, 31);

    [Fact]
    public async Task HonoursATickerAndADateRange()
    {
        MassivePage<Split> page = await Client.Stocks.ListSplitsAsync(
            ticker: "AAPL",
            executionDate: RangeFilter.Between(WindowStart, WindowEnd),
            cancellationToken: Ct);

        Split split = Assert.Single(page.Results);
        Assert.Equal("AAPL", split.Ticker);
        Assert.Equal(new LocalDate(2020, 8, 31), split.ExecutionDate);
        Assert.Equal(1d, split.SplitFrom);
        Assert.Equal(4d, split.SplitTo);
    }
}
```

Create `tests/MassiveDotNet.IntegrationTests/StocksExchangesLiveTests.cs`:

```csharp
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>One call for exchanges (D-G8): the list is short, and NYSE is in it.</summary>
public sealed class StocksExchangesLiveTests : LiveApiTest
{
    [Fact]
    public async Task ListsTheExchanges()
    {
        MassivePage<StockExchange> page = await Client.Stocks.ListExchangesAsync(cancellationToken: Ct);

        Assert.NotEmpty(page.Results);
        Assert.All(page.Results, exchange => Assert.False(string.IsNullOrEmpty(exchange.Id)));
        Assert.All(page.Results, exchange => Assert.False(string.IsNullOrEmpty(exchange.Name)));
        Assert.Contains(page.Results, exchange => exchange.Mic == "XNYS");
    }
}
```

In `tests/MassiveDotNet.IntegrationTests/StocksIndicatorsLiveTests.cs`, add three tests after `ListReportsMorePagesAndCarriesTheUnderlying`, inside the class:

```csharp
    [Fact]
    public async Task EmaReturnsValuesInTheWindow()
    {
        MassivePagedResult<IndicatorSeries> page = await Client.Stocks.ListEmaAsync(
            "AAPL",
            timestamp: RangeFilter.Between(DateOrTimestamp.FromDate(WindowStart), DateOrTimestamp.FromDate(WindowEnd)),
            timespan: AggregateTimespan.Day,
            window: 10,
            limit: 2,
            cancellationToken: Ct);

        Assert.NotNull(page.Result.Values);
        Assert.Equal(2, page.Result.Values.Length);
        Assert.All(page.Result.Values, value => Assert.True(value.Value > 0));
    }

    [Fact]
    public async Task RsiReturnsValuesBetweenZeroAndOneHundred()
    {
        MassivePagedResult<IndicatorSeries> page = await Client.Stocks.ListRsiAsync(
            "AAPL",
            timestamp: RangeFilter.Between(DateOrTimestamp.FromDate(WindowStart), DateOrTimestamp.FromDate(WindowEnd)),
            timespan: AggregateTimespan.Day,
            window: 14,
            limit: 2,
            cancellationToken: Ct);

        Assert.NotNull(page.Result.Values);
        Assert.Equal(2, page.Result.Values.Length);
        Assert.All(page.Result.Values, value => Assert.InRange(value.Value, 0, 100));
    }

    [Fact]
    public async Task MacdReturnsAHistogramThatIsTheLineMinusTheSignal()
    {
        MassivePagedResult<MacdSeries> page = await Client.Stocks.ListMacdAsync(
            "AAPL",
            timestamp: RangeFilter.Between(DateOrTimestamp.FromDate(WindowStart), DateOrTimestamp.FromDate(WindowEnd)),
            timespan: AggregateTimespan.Day,
            shortWindow: 12,
            longWindow: 26,
            signalWindow: 9,
            limit: 2,
            cancellationToken: Ct);

        Assert.NotNull(page.Result.Values);
        Assert.Equal(2, page.Result.Values.Length);

        // The histogram is defined as the MACD line minus its signal, so the three members of
        // every point must agree with each other whatever their values are.
        foreach (MacdValue value in page.Result.Values)
        {
            Assert.Equal(value.Value - value.Signal, value.Histogram, precision: 6);
        }
    }
```

- [ ] **Step 3: Compile the live project as CI does**

Run: `dotnet build tests/MassiveDotNet.IntegrationTests`
Expected: 0 warnings, 0 errors. The two deprecated calls compile because Task 8 added `MASSIVE0002` to this project's `NoWarn`.

- [ ] **Step 4: Run the live tier locally**

Run: `dotnet test tests/MassiveDotNet.IntegrationTests --filter "Category=Integration"`
Expected: every test passes, including the pre-existing ones. If a test is skipped with `No Massive API key found`, the `.env` was not located: report that and stop; do not print the file or set the variable on the command line. If a snapshot count is wrong, that is D19's proof failing and is a finding, not a flaky test. If a tick call returns `403`, report the entitlement gap as a finding.

- [ ] **Step 5: Confirm CI's exclusion still selects no live test**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS, and the integration project reports zero tests run.

- [ ] **Step 6: Commit**

```bash
git add tests/MassiveDotNet.IntegrationTests/StocksBarsLiveTests.cs tests/MassiveDotNet.IntegrationTests/StocksTicksLiveTests.cs tests/MassiveDotNet.IntegrationTests/StocksSnapshotsLiveTests.cs tests/MassiveDotNet.IntegrationTests/StocksSplitsLiveTests.cs tests/MassiveDotNet.IntegrationTests/StocksExchangesLiveTests.cs tests/MassiveDotNet.IntegrationTests/StocksIndicatorsLiveTests.cs
git commit -m "test: add the live tier for the fifteen stocks operations

Two tickers on the all-tickers snapshot return exactly two, the proof
D19 deferred to the operation that has an array parameter. Trades on a
fixed 2024 session cross a real page boundary on the tick envelope, and
a nanosecond bound is honoured, which is what DateOrNanoseconds exists
for. Every other operation gets one shape-asserting call, the
deprecated pair included so the suite reports the day they retire.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 12: Issue bookkeeping (controller only, after the branch lands, with the user's go-ahead)

**Files:** none in the repository.

This task posts to GitHub, which is a side effect outside the working tree. Do not dispatch a subagent for it, and do not run it until the user has chosen how the branch lands (merge or pull request) and has said to post. Present the comment texts below and ask once.

- [ ] **Step 1: Close #8 with the merge**

If the branch lands as a pull request, the description carries `Closes #8` and this list; if it is merged locally, post the same text as a comment on #8 and close it:

```
Fifteen operations ship under `client.Stocks` with this branch:

- `GetGroupedStocksAggregates` → `ListGroupedDailyAsync`
- `GetPreviousStocksAggregates` → `ListPreviousCloseAsync`
- `Trades` → `ListTradesAsync` / `EnumerateTradesAsync`
- `Quotes` → `ListQuotesAsync` / `EnumerateQuotesAsync`
- `LastQuote` → `GetLastQuoteAsync`
- `GetStocksSnapshotTickers` → `ListSnapshotsAsync`
- `GetStocksSnapshotTicker` → `GetSnapshotAsync`
- `GetStocksSnapshotDirection` → `ListMoversAsync`
- `EMA` → `ListEmaAsync` / `EnumerateEmaAsync`
- `RSI` → `ListRsiAsync` / `EnumerateRsiAsync`
- `MACD` → `ListMacdAsync` / `EnumerateMacdAsync`
- `DeprecatedGetHistoricStocksTrades` → `ListHistoricTradesAsync` (`[Obsolete]`)
- `DeprecatedGetHistoricStocksQuotes` → `ListHistoricQuotesAsync` (`[Obsolete]`)
- `get_stocks_v1_splits` → `ListSplitsAsync` / `EnumerateSplitsAsync`
- `get_stocks_v1_exchanges` → `ListExchangesAsync` / `EnumerateExchangesAsync`

Of the twenty this issue counted: four (custom bars, daily open/close, last trade, SMA) were mapped by earlier issues as reference patterns; `/stocks/v1/exchanges` ships here; `/v1/open-close/{indicesTicker}/{date}` is an indices route and moves to #14; `/stocks/dev/trades/{ticker}` carries a `dev` segment D18 has no reading of and gets its own issue. `/stocks/v1/splits`, which the issue did not count, ships here because it carries the stocks entitlement. Design: `docs/superpowers/specs/2026-09-02-stocks-group-design.md`.
```

- [ ] **Step 2: Comment on #9 and #14**

```bash
gh issue comment 9 --body "\`/stocks/v1/splits\` and \`/stocks/v1/exchanges\` landed under \`Stocks\` with #8 (they carry \`x-polygon-entitlement-market-type: stocks\` and belong beside dividends), so this issue's count drops from 37 to 35."
gh issue comment 14 --body "\`/v1/open-close/{indicesTicker}/{date}\` is an indices route that #8 counted but no issue owned; it belongs here. Its shape is the stocks daily open/close body object (\`GetDailyOpenCloseAsync\`), so the map row is the same pattern with the indices ticker parameter."
```

- [ ] **Step 3: Open the `dev` stability issue**

```bash
gh issue create --title "Decide what the dev path segment means for stability: /stocks/dev/trades/{ticker}" --body "\`/stocks/dev/trades/{ticker}\` is the one stocks operation #8 left unmapped. Its route carries a \`dev\` segment that is neither \`vX\` nor \`x-polygon-experimental\`, so D18 has no reading of it: the generator would ship it unmarked, which is wrong if \`dev\` means what \`vX\` means, and marking it would be a map-declared stability signal, which D18 forbids.

Options: treat \`dev\` as a second experimental marker in \`Spec\` (read from the path, like \`vX\`); ask Massive what the segment means; or leave it unmapped until the description settles it. Whichever is chosen, it is a generator change or a documented exclusion, not a map row.

Milestone: v0.1 REST."
```

- [ ] **Step 4: Report the URLs**

Relay the new issue's URL and confirm the two comments posted.
