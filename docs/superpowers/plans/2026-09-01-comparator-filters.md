# Comparator Filters Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collapse the spec's 1,182 dotted comparator parameters into one typed, optional parameter per field, so `ticker: "AAPL"`, `exDividendDate: RangeFilter.Between(a, b)`, and `ticker: SetFilter.AnyOf("AAPL", "MSFT")` all compile against the same signature and render the documented query string.

**Architecture:** Four `readonly struct` filter types in core (`RangeFilter<T>`, `SetFilter<T>`, `Filter<T>`, `ArrayFilter<T>`) with implicit conversions from `T` and from each other. `RequestUriBuilder` gains one generic overload per filter type and does all rendering, dispatching on `typeof(T)` over a closed set of element types. The generator groups an operation's parameters by base name using a suffix allowlist, picks the filter type from the exact suffix set, and emits one `AppendQuery` line per field. One endpoint, `/stocks/v1/dividends`, maps in this plan to prove the path end to end; its result model is the first with calendar dates, which a span-parsing `LocalDateJsonConverter` handles.

**Tech Stack:** .NET 10, C# latest, xUnit v3, System.Text.Json source generation, NodaTime 3.3.3. No new package dependencies.

**Spec:** `docs/superpowers/specs/2026-09-01-comparator-filters-design.md`

## Global Constraints

Copied from `CLAUDE.md` and the spec. Every task inherits these.

- **Rule 3** — No reflection-based serialization in shipped code. `System.Text.Json` source generation only. The converter in Task 4 is a plain `JsonConverter<LocalDate>` registered on the generated context; nothing reflects.
- **Rule 5** — `*.g.cs` files are never hand-edited. Change `tools/MassiveDotNet.CodeGen` and regenerate with `dotnet run --project tools/MassiveDotNet.CodeGen`.
- **Rule 6** — The generator is deterministic. Every emission decision below uses order-independent tests (`Exists`, `Any`) so byte-identical output does not depend on iteration order.
- **Rule 7** — `MassiveDotNet` (core) references no external package other than NodaTime.
- **Rule 9** — `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are on. An **unused `using` fails the build** (IDE0005), so the generator emits `using NodaTime;` only when a file needs it. `AnalysisLevel` is `latest-recommended`, which enables these as warnings and therefore errors: **CA1000** (no static members on generic types — factories go on the non-generic `RangeFilter`, `SetFilter`, `ArrayFilter` classes; conversion operators are exempt), **CA1305** (pass a format provider — `double` formatting names `CultureInfo.InvariantCulture`), **CA1861** (no constant array literals as call arguments in loops — hoist to `static readonly`), and the naming rules CA1710/1711/1716/1720/1725.
- **Rule 10** — Every public member carries XML documentation, or CS1591 fails the build. Internal members are documented too, by convention.
- **Rule 12** — NodaTime only. No BCL `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, or `TimeSpan` may be *named* anywhere in `src`, `tests`, `samples`, or `tools`. The only temporal type this plan touches is `LocalDate`.
- **Rule 13** — CI runs offline only. The live test in Task 8 derives from `LiveApiTest`, which carries `[Trait("Category", "Integration")]`, and lives in `tests/MassiveDotNet.IntegrationTests`.
- **Spec D-F4** — Filter element types are exactly `string`, `int`, `long`, `double`, `LocalDate`, `DateOrTimestamp`. Nothing else renders, and the generator rejects anything else.
- **Spec D-F5** — Within one field the wire order is always `field`, `.gt`, `.gte`, `.lt`, `.lte`, `.any_of`, `.all_of`. Set elements are percent-escaped individually and joined with a literal comma.
- **Style** — The codebase uses explicit types, never `var`; collection expressions (`[]`, `[.. x]`); `is not { } x` null patterns; and file-scoped namespaces. Match it.
- **Convention** — Do not commit or push unless asked. Steps below include commits; confirm with the user before the first one. Commit messages end with the trailer `Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB`.

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
| `src/MassiveDotNet/FilterMode.cs` | **Create.** Internal flags enum naming which comparator forms a filter carries. Shared by all four filter types and the builder. | 1 |
| `src/MassiveDotNet/FilterGuard.cs` | **Create.** Internal null and empty-set checks shared by the filter types, written so value-type `T` never boxes. | 1 |
| `src/MassiveDotNet/RangeFilter.cs` | **Create.** `RangeFilter<T>` struct plus the non-generic `RangeFilter` factory class. | 1 |
| `src/MassiveDotNet/SetFilter.cs` | **Create.** `SetFilter<T>` struct plus factory class. | 2 |
| `src/MassiveDotNet/ArrayFilter.cs` | **Create.** `ArrayFilter<T>` struct plus factory class. | 2 |
| `src/MassiveDotNet/Filter.cs` | **Create.** `Filter<T>` struct, the union, constructed only through conversion. | 2 |
| `src/MassiveDotNet/Internal/ValueStringBuilder.cs` | **Modify.** Add `Append(double)`, invariant culture, formatted in place. | 3 |
| `src/MassiveDotNet/Http/RequestUriBuilder.cs` | **Modify.** Add `AppendQuery(name, double?)`, the four filter overloads, and the private element dispatch. | 3 |
| `src/MassiveDotNet/Serialization/LocalDateJsonConverter.cs` | **Create.** Span-parsing `JsonConverter<LocalDate>`. | 4 |
| `tools/MassiveDotNet.CodeGen/Spec.cs` | **Modify.** Add `ComparatorGroup`, `ParameterSlot`, and `Spec.Slots(...)`, which groups parameters by base name. | 5 |
| `tools/MassiveDotNet.CodeGen/TypeBinding.cs` | **Modify.** `format: date` becomes `LocalDate`; add `ResolveFilter` with the element allowlist. | 5 |
| `tools/MassiveDotNet.CodeGen/Argument.cs` | **Modify.** Create arguments from slots; filter arguments carry a generated doc sentence and an optional call suffix. | 5 |
| `tools/MassiveDotNet.CodeGen/Emitter.cs` | **Modify.** Build arguments from slots, validate map keys, emit `using NodaTime;` conditionally, register the converter on the context. | 5 |
| `specs/endpoints.map.json` | **Modify.** Add the `Dividend` model and the `get_stocks_v1_dividends` endpoint. | 6 |
| `src/MassiveDotNet.Rest/Generated/` | **Regenerate.** Never hand-edit. | 5, 6 |
| `tests/MassiveDotNet.Rest.Tests/FilterConstructionTests.cs` | **Create.** Guards on the four filter types. | 1, 2 |
| `tests/MassiveDotNet.Rest.Tests/FilterRenderingTests.cs` | **Create.** Builder-level wire-form tests. | 3 |
| `tests/MassiveDotNet.Rest.Tests/LocalDateJsonConverterTests.cs` | **Create.** Converter read and write. | 4 |
| `tests/MassiveDotNet.Rest.Tests/Fixtures.cs` | **Modify.** Add the published dividends sample. | 6 |
| `tests/MassiveDotNet.Rest.Tests/StocksDividendsTests.cs` | **Create.** The generated surface, end to end through the stub. | 6 |
| `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` | **Modify.** `CoverageBaseline` 1 → 2. | 6 |
| `samples/MassiveDotNet.AotSmokeTest/Program.cs` | **Modify.** Root the filter overloads and the converter with a filtered dividends call. | 7 |
| `tests/MassiveDotNet.IntegrationTests/StocksDividendsLiveTests.cs` | **Create.** One live test: date range plus ticker set. | 8 |
| `CLAUDE.md` | **Modify.** Decision D15 and the Filters convention. | 8 |

---

### Task 1: `RangeFilter<T>` and the shared internals

**Files:**
- Create: `src/MassiveDotNet/FilterMode.cs`
- Create: `src/MassiveDotNet/FilterGuard.cs`
- Create: `src/MassiveDotNet/RangeFilter.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/FilterConstructionTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `internal enum FilterMode : byte { None, Equal, Gt, Gte, Lt, Lte, AnyOf, AllOf }` (flags); `internal static class FilterGuard { ThrowIfNull<T>(T, string); T[] ValidateSet<T>(T[], string) }`; `public readonly struct RangeFilter<T>` with `internal T Lower`, `internal T Upper`, `internal FilterMode Mode`, an internal constructor `RangeFilter(T lower, T upper, FilterMode mode)`, public instance `Gt/Gte/Lt/Lte(T)`, and `implicit operator RangeFilter<T>(T)`; `public static class RangeFilter` with `Gt/Gte/Lt/Lte<T>(T)` and `Between<T>(T, T)`.

- [ ] **Step 1: Write the failing construction tests**

Create `tests/MassiveDotNet.Rest.Tests/FilterConstructionTests.cs`:

```csharp
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Filters validate at construction, where the stack trace names the caller, rather than at
/// render time inside a generated method.
/// </summary>
public sealed class FilterConstructionTests
{
    [Fact]
    public void RangeFactoriesRejectNull()
    {
        Assert.Throws<ArgumentNullException>(() => RangeFilter.Gt<string>(null!));
        Assert.Throws<ArgumentNullException>(() => RangeFilter.Between<string>("a", null!));
    }

    [Fact]
    public void EqualityConversionRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => (RangeFilter<string>)(string)null!);
    }

    [Fact]
    public void ALowerBoundCannotBeSetTwice()
    {
        Assert.Throws<InvalidOperationException>(() => RangeFilter.Gt(1L).Gte(2L));
        Assert.Throws<InvalidOperationException>(() => RangeFilter.Gte(1L).Gt(2L));
    }

    [Fact]
    public void AnUpperBoundCannotBeSetTwice()
    {
        Assert.Throws<InvalidOperationException>(() => RangeFilter.Lt(1L).Lte(2L));
        Assert.Throws<InvalidOperationException>(() => RangeFilter.Lte(1L).Lt(2L));
    }

    [Fact]
    public void AnEqualityCannotTakeABound()
    {
        RangeFilter<long> equality = 5L;

        Assert.Throws<InvalidOperationException>(() => equality.Gt(1L));
        Assert.Throws<InvalidOperationException>(() => equality.Lte(9L));
    }

    [Fact]
    public void ChainingTheOtherSideIsAllowedInEitherOrder()
    {
        // Neither call throws: a lower bound may gain an upper bound and vice versa.
        _ = RangeFilter.Gte(1L).Lt(10L);
        _ = RangeFilter.Lt(10L).Gte(1L);
    }

    [Fact]
    public void BetweenDoesNotCheckOrdering()
    {
        // Deliberate: ordering needs a comparison constraint that string cannot honour sensibly,
        // and the server rejects an empty range on its own.
        _ = RangeFilter.Between(2L, 1L);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~FilterConstructionTests"`
Expected: build FAILS with `CS0103: The name 'RangeFilter' does not exist`.

- [ ] **Step 3: Create `FilterMode`**

Create `src/MassiveDotNet/FilterMode.cs`:

```csharp
namespace MassiveDotNet;

/// <summary>
/// Which comparator forms a filter carries. Read by <see cref="Http.RequestUriBuilder"/> when it
/// renders the filter; never exposed to callers, who express intent through the factories.
/// </summary>
[Flags]
internal enum FilterMode : byte
{
    /// <summary>An unset filter, which renders nothing.</summary>
    None = 0,

    /// <summary>The plain field: <c>field=value</c>. For array fields, "contains".</summary>
    Equal = 1,

    /// <summary><c>field.gt=value</c>.</summary>
    Gt = 2,

    /// <summary><c>field.gte=value</c>.</summary>
    Gte = 4,

    /// <summary><c>field.lt=value</c>.</summary>
    Lt = 8,

    /// <summary><c>field.lte=value</c>.</summary>
    Lte = 16,

    /// <summary><c>field.any_of=a,b</c>.</summary>
    AnyOf = 32,

    /// <summary><c>field.all_of=a,b</c>.</summary>
    AllOf = 64,
}
```

- [ ] **Step 4: Create `FilterGuard`**

Create `src/MassiveDotNet/FilterGuard.cs`:

```csharp
namespace MassiveDotNet;

/// <summary>Argument checks shared by the filter types.</summary>
/// <remarks>
/// <see cref="ArgumentNullException.ThrowIfNull(object?, string?)"/> takes <see cref="object"/>,
/// which boxes a value-type <c>T</c> on every call. The <c>is null</c> pattern compiles to a plain
/// reference check for reference types and to nothing at all for value types.
/// </remarks>
internal static class FilterGuard
{
    /// <summary>Throws when a filter value is <see langword="null"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="parameterName">The name reported in the exception.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static void ThrowIfNull<T>(T value, string parameterName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }
    }

    /// <summary>Validates the values of a set filter and returns them unchanged.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="values">The caller-supplied array. It is not copied.</param>
    /// <param name="parameterName">The name reported in the exception.</param>
    /// <returns><paramref name="values"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty or contains a <see langword="null"/>.</exception>
    public static T[] ValidateSet<T>(T[] values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);

        if (values.Length == 0)
        {
            throw new ArgumentException("A set filter needs at least one value.", parameterName);
        }

        foreach (T value in values)
        {
            if (value is null)
            {
                throw new ArgumentException("A set filter cannot contain a null value.", parameterName);
            }
        }

        return values;
    }
}
```

- [ ] **Step 5: Create `RangeFilter<T>` and its factories**

Create `src/MassiveDotNet/RangeFilter.cs`:

```csharp
namespace MassiveDotNet;

/// <summary>
/// A filter over an ordered field: an exact value, a lower bound, an upper bound, or both.
/// </summary>
/// <typeparam name="T">The field's element type.</typeparam>
/// <remarks>
/// <para>
/// Build one with the factories on <see cref="RangeFilter"/>, or pass a plain
/// <typeparamref name="T"/> where a filter is expected: it converts implicitly to an equality
/// filter, so <c>ticker: "AAPL"</c> keeps compiling on an endpoint that also accepts a range.
/// </para>
/// <para>
/// A lower bound accepts an upper bound afterwards and vice versa, so a half-open window is
/// <c>RangeFilter.Gte(start).Lt(end)</c>. An unset filter (<see langword="default"/>) renders
/// nothing, exactly like a <see langword="null"/> parameter.
/// </para>
/// </remarks>
public readonly struct RangeFilter<T>
{
    private readonly T _lower;
    private readonly T _upper;
    private readonly FilterMode _mode;

    internal RangeFilter(T lower, T upper, FilterMode mode)
    {
        _lower = lower;
        _upper = upper;
        _mode = mode;
    }

    /// <summary>The lower bound, or the exact value for an equality filter.</summary>
    internal T Lower => _lower;

    /// <summary>The upper bound.</summary>
    internal T Upper => _upper;

    /// <summary>Which forms are set.</summary>
    internal FilterMode Mode => _mode;

    /// <summary>Adds an exclusive lower bound: <c>field.gt=value</c>.</summary>
    /// <param name="value">The value results must exceed.</param>
    /// <returns>The filter with the bound added.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The filter is an equality, or already has a lower bound.</exception>
    public RangeFilter<T> Gt(T value) => WithLower(value, FilterMode.Gt);

    /// <summary>Adds an inclusive lower bound: <c>field.gte=value</c>.</summary>
    /// <param name="value">The value results must reach.</param>
    /// <returns>The filter with the bound added.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The filter is an equality, or already has a lower bound.</exception>
    public RangeFilter<T> Gte(T value) => WithLower(value, FilterMode.Gte);

    /// <summary>Adds an exclusive upper bound: <c>field.lt=value</c>.</summary>
    /// <param name="value">The value results must stay below.</param>
    /// <returns>The filter with the bound added.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The filter is an equality, or already has an upper bound.</exception>
    public RangeFilter<T> Lt(T value) => WithUpper(value, FilterMode.Lt);

    /// <summary>Adds an inclusive upper bound: <c>field.lte=value</c>.</summary>
    /// <param name="value">The value results must not exceed.</param>
    /// <returns>The filter with the bound added.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The filter is an equality, or already has an upper bound.</exception>
    public RangeFilter<T> Lte(T value) => WithUpper(value, FilterMode.Lte);

    /// <summary>Converts a value to an equality filter: <c>field=value</c>.</summary>
    /// <param name="value">The exact value to match.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static implicit operator RangeFilter<T>(T value)
    {
        FilterGuard.ThrowIfNull(value, nameof(value));
        return new RangeFilter<T>(value, default!, FilterMode.Equal);
    }

    private RangeFilter<T> WithLower(T value, FilterMode bound)
    {
        FilterGuard.ThrowIfNull(value, nameof(value));
        ThrowIfEquality();

        if ((_mode & (FilterMode.Gt | FilterMode.Gte)) != 0)
        {
            throw new InvalidOperationException("This filter already has a lower bound.");
        }

        return new RangeFilter<T>(value, _upper, _mode | bound);
    }

    private RangeFilter<T> WithUpper(T value, FilterMode bound)
    {
        FilterGuard.ThrowIfNull(value, nameof(value));
        ThrowIfEquality();

        if ((_mode & (FilterMode.Lt | FilterMode.Lte)) != 0)
        {
            throw new InvalidOperationException("This filter already has an upper bound.");
        }

        return new RangeFilter<T>(_lower, value, _mode | bound);
    }

    private void ThrowIfEquality()
    {
        if ((_mode & FilterMode.Equal) != 0)
        {
            throw new InvalidOperationException("An equality filter cannot take a bound.");
        }
    }
}

/// <summary>
/// Factories for <see cref="RangeFilter{T}"/>. They live on a non-generic class so the element
/// type is inferred from the argument: <c>RangeFilter.Gt(0.5)</c> rather than
/// <c>RangeFilter&lt;double&gt;.Gt(0.5)</c>.
/// </summary>
public static class RangeFilter
{
    /// <summary>Results strictly greater than <paramref name="value"/>: <c>field.gt=value</c>.</summary>
    /// <typeparam name="T">The field's element type.</typeparam>
    /// <param name="value">The exclusive lower bound.</param>
    /// <returns>The filter.</returns>
    public static RangeFilter<T> Gt<T>(T value) => default(RangeFilter<T>).Gt(value);

    /// <summary>Results at or above <paramref name="value"/>: <c>field.gte=value</c>.</summary>
    /// <typeparam name="T">The field's element type.</typeparam>
    /// <param name="value">The inclusive lower bound.</param>
    /// <returns>The filter.</returns>
    public static RangeFilter<T> Gte<T>(T value) => default(RangeFilter<T>).Gte(value);

    /// <summary>Results strictly below <paramref name="value"/>: <c>field.lt=value</c>.</summary>
    /// <typeparam name="T">The field's element type.</typeparam>
    /// <param name="value">The exclusive upper bound.</param>
    /// <returns>The filter.</returns>
    public static RangeFilter<T> Lt<T>(T value) => default(RangeFilter<T>).Lt(value);

    /// <summary>Results at or below <paramref name="value"/>: <c>field.lte=value</c>.</summary>
    /// <typeparam name="T">The field's element type.</typeparam>
    /// <param name="value">The inclusive upper bound.</param>
    /// <returns>The filter.</returns>
    public static RangeFilter<T> Lte<T>(T value) => default(RangeFilter<T>).Lte(value);

    /// <summary>
    /// Results at or above <paramref name="lower"/> and at or below <paramref name="upper"/>:
    /// <c>field.gte=lower&amp;field.lte=upper</c>. Inclusive at both ends; ordering is not checked.
    /// </summary>
    /// <typeparam name="T">The field's element type.</typeparam>
    /// <param name="lower">The inclusive lower bound.</param>
    /// <param name="upper">The inclusive upper bound.</param>
    /// <returns>The filter.</returns>
    public static RangeFilter<T> Between<T>(T lower, T upper) => Gte(lower).Lte(upper);
}
```

`default!` on the unused bound is deliberate: for an unconstrained `T` the compiler treats `default(T)` as maybe-null, and the field is never read unless its mode bit is set.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~FilterConstructionTests"`
Expected: 7 passed. If the build reports **CA1000** on the implicit operator, that is the one place a targeted `#pragma warning disable CA1000` with a justification comment is acceptable: a conversion operator has to be a static member of the type it converts to.

- [ ] **Step 7: Run the whole offline suite, including the temporal scan**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: all pass. `TemporalTypeTests` reflects over the new public surface and scans the new files; nothing here names a BCL temporal type.

- [ ] **Step 8: Commit**

```bash
git add src/MassiveDotNet/FilterMode.cs src/MassiveDotNet/FilterGuard.cs src/MassiveDotNet/RangeFilter.cs tests/MassiveDotNet.Rest.Tests/FilterConstructionTests.cs
git commit -m "feat: add RangeFilter<T> with chained bounds

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 2: `SetFilter<T>`, `ArrayFilter<T>`, and `Filter<T>`

**Files:**
- Create: `src/MassiveDotNet/SetFilter.cs`
- Create: `src/MassiveDotNet/ArrayFilter.cs`
- Create: `src/MassiveDotNet/Filter.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/FilterConstructionTests.cs`

**Interfaces:**
- Consumes: `FilterMode`, `FilterGuard`, `RangeFilter<T>` (Task 1).
- Produces: `public readonly struct SetFilter<T>` with `internal T Value`, `internal T[]? Values`, `internal FilterMode Mode`, and `implicit operator SetFilter<T>(T)`; `public static class SetFilter { AnyOf<T>(params T[]) }`. `public readonly struct ArrayFilter<T>` with the same three internals, `implicit operator ArrayFilter<T>(T)` (contains), `implicit operator ArrayFilter<T>(SetFilter<T>)`; `public static class ArrayFilter { Contains<T>(T), AnyOf<T>(params T[]), AllOf<T>(params T[]) }`. `public readonly struct Filter<T>` with `internal T Lower`, `internal T Upper`, `internal T[]? Values`, `internal FilterMode Mode`, and implicit operators from `T`, `RangeFilter<T>`, `SetFilter<T>`.

- [ ] **Step 1: Add the failing construction tests**

Append to the class in `tests/MassiveDotNet.Rest.Tests/FilterConstructionTests.cs`:

```csharp
    [Fact]
    public void SetFactoriesRejectAnEmptySet()
    {
        Assert.Throws<ArgumentException>(() => SetFilter.AnyOf<string>());
        Assert.Throws<ArgumentException>(() => ArrayFilter.AnyOf<string>());
        Assert.Throws<ArgumentException>(() => ArrayFilter.AllOf<string>());
    }

    [Fact]
    public void SetFactoriesRejectANullArray()
    {
        Assert.Throws<ArgumentNullException>(() => SetFilter.AnyOf<string>(null!));
        Assert.Throws<ArgumentNullException>(() => ArrayFilter.AllOf<string>(null!));
    }

    [Fact]
    public void SetFactoriesRejectANullElement()
    {
        Assert.Throws<ArgumentException>(() => SetFilter.AnyOf("AAPL", null!));
        Assert.Throws<ArgumentException>(() => ArrayFilter.AnyOf("AAPL", null!));
    }

    [Fact]
    public void SetAndArrayEqualityConversionsRejectNull()
    {
        Assert.Throws<ArgumentNullException>(() => (SetFilter<string>)(string)null!);
        Assert.Throws<ArgumentNullException>(() => (ArrayFilter<string>)(string)null!);
        Assert.Throws<ArgumentNullException>(() => (Filter<string>)(string)null!);
        Assert.Throws<ArgumentNullException>(() => ArrayFilter.Contains<string>(null!));
    }

    [Fact]
    public void FilterAcceptsARangeASetOrAValueByConversion()
    {
        // Compiles, and none of these throw: Filter<T> is only ever built by conversion.
        Filter<long> fromValue = 5L;
        Filter<long> fromRange = RangeFilter.Between(1L, 9L);
        Filter<long> fromSet = SetFilter.AnyOf(1L, 2L);

        _ = fromValue;
        _ = fromRange;
        _ = fromSet;
    }

    [Fact]
    public void ArrayFilterAcceptsASetByConversion()
    {
        ArrayFilter<string> fromSet = SetFilter.AnyOf("AAPL", "MSFT");
        ArrayFilter<string> fromValue = "AAPL";

        _ = fromSet;
        _ = fromValue;
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~FilterConstructionTests"`
Expected: build FAILS with `CS0103: The name 'SetFilter' does not exist`.

- [ ] **Step 3: Create `SetFilter<T>`**

Create `src/MassiveDotNet/SetFilter.cs`:

```csharp
namespace MassiveDotNet;

/// <summary>
/// A filter over a field that accepts membership in a set: an exact value, or any of several.
/// </summary>
/// <typeparam name="T">The field's element type.</typeparam>
/// <remarks>
/// Build one with <see cref="SetFilter.AnyOf{T}(T[])"/>, or pass a plain
/// <typeparamref name="T"/> where a filter is expected: it converts implicitly to an equality
/// filter. An unset filter (<see langword="default"/>) renders nothing, exactly like a
/// <see langword="null"/> parameter.
/// </remarks>
public readonly struct SetFilter<T>
{
    private readonly T _value;
    private readonly T[]? _values;
    private readonly FilterMode _mode;

    internal SetFilter(T value, T[]? values, FilterMode mode)
    {
        _value = value;
        _values = values;
        _mode = mode;
    }

    /// <summary>The exact value, for an equality filter.</summary>
    internal T Value => _value;

    /// <summary>The set's values, for an any-of filter. Not copied from the caller.</summary>
    internal T[]? Values => _values;

    /// <summary>Which form is set.</summary>
    internal FilterMode Mode => _mode;

    /// <summary>Converts a value to an equality filter: <c>field=value</c>.</summary>
    /// <param name="value">The exact value to match.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static implicit operator SetFilter<T>(T value)
    {
        FilterGuard.ThrowIfNull(value, nameof(value));
        return new SetFilter<T>(value, null, FilterMode.Equal);
    }
}

/// <summary>
/// Factories for <see cref="SetFilter{T}"/>. They live on a non-generic class so the element
/// type is inferred from the arguments.
/// </summary>
public static class SetFilter
{
    /// <summary>Results whose field equals any of <paramref name="values"/>: <c>field.any_of=a,b</c>.</summary>
    /// <typeparam name="T">The field's element type.</typeparam>
    /// <param name="values">One or more values. The array is held, not copied.</param>
    /// <returns>The filter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty or contains a <see langword="null"/>.</exception>
    public static SetFilter<T> AnyOf<T>(params T[] values) =>
        new(default!, FilterGuard.ValidateSet(values, nameof(values)), FilterMode.AnyOf);
}
```

- [ ] **Step 4: Create `ArrayFilter<T>`**

Create `src/MassiveDotNet/ArrayFilter.cs`:

```csharp
namespace MassiveDotNet;

/// <summary>
/// A filter over an array-valued field such as <c>tickers</c>: arrays that contain a value, any
/// of several values, or all of them.
/// </summary>
/// <typeparam name="T">The array's element type.</typeparam>
/// <remarks>
/// Build one with the factories on <see cref="ArrayFilter"/>. A plain <typeparamref name="T"/>
/// converts implicitly to <see cref="ArrayFilter.Contains{T}(T)"/>, and a
/// <see cref="SetFilter{T}"/> converts to the matching form, so <c>SetFilter.AnyOf</c> can be
/// passed wherever an array filter is expected. An unset filter (<see langword="default"/>)
/// renders nothing, exactly like a <see langword="null"/> parameter.
/// </remarks>
public readonly struct ArrayFilter<T>
{
    private readonly T _value;
    private readonly T[]? _values;
    private readonly FilterMode _mode;

    internal ArrayFilter(T value, T[]? values, FilterMode mode)
    {
        _value = value;
        _values = values;
        _mode = mode;
    }

    /// <summary>The single value, for a contains filter.</summary>
    internal T Value => _value;

    /// <summary>The set's values, for an any-of or all-of filter. Not copied from the caller.</summary>
    internal T[]? Values => _values;

    /// <summary>Which form is set.</summary>
    internal FilterMode Mode => _mode;

    /// <summary>Converts a value to a contains filter: <c>field=value</c>.</summary>
    /// <param name="value">The value the array must contain.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static implicit operator ArrayFilter<T>(T value) => ArrayFilter.Contains(value);

    /// <summary>
    /// Converts a set filter. Any-of stays any-of; an equality becomes a contains filter, which is
    /// what the plain form of an array field means.
    /// </summary>
    /// <param name="filter">The set filter.</param>
    public static implicit operator ArrayFilter<T>(SetFilter<T> filter) =>
        new(filter.Value, filter.Values, filter.Mode);
}

/// <summary>
/// Factories for <see cref="ArrayFilter{T}"/>. They live on a non-generic class so the element
/// type is inferred from the arguments.
/// </summary>
public static class ArrayFilter
{
    /// <summary>Results whose array contains <paramref name="value"/>: <c>field=value</c>.</summary>
    /// <typeparam name="T">The array's element type.</typeparam>
    /// <param name="value">The value the array must contain.</param>
    /// <returns>The filter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static ArrayFilter<T> Contains<T>(T value)
    {
        FilterGuard.ThrowIfNull(value, nameof(value));
        return new ArrayFilter<T>(value, null, FilterMode.Equal);
    }

    /// <summary>Results whose array contains any of <paramref name="values"/>: <c>field.any_of=a,b</c>.</summary>
    /// <typeparam name="T">The array's element type.</typeparam>
    /// <param name="values">One or more values. The array is held, not copied.</param>
    /// <returns>The filter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty or contains a <see langword="null"/>.</exception>
    public static ArrayFilter<T> AnyOf<T>(params T[] values) =>
        new(default!, FilterGuard.ValidateSet(values, nameof(values)), FilterMode.AnyOf);

    /// <summary>Results whose array contains every one of <paramref name="values"/>: <c>field.all_of=a,b</c>.</summary>
    /// <typeparam name="T">The array's element type.</typeparam>
    /// <param name="values">One or more values. The array is held, not copied.</param>
    /// <returns>The filter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty or contains a <see langword="null"/>.</exception>
    public static ArrayFilter<T> AllOf<T>(params T[] values) =>
        new(default!, FilterGuard.ValidateSet(values, nameof(values)), FilterMode.AllOf);
}
```

- [ ] **Step 5: Create `Filter<T>`**

Create `src/MassiveDotNet/Filter.cs`:

```csharp
namespace MassiveDotNet;

/// <summary>
/// A filter over a field that accepts both a range and set membership: an exact value, bounds,
/// or any of several values.
/// </summary>
/// <typeparam name="T">The field's element type.</typeparam>
/// <remarks>
/// There are no factories on this type. It is built by conversion from a plain
/// <typeparamref name="T"/> (equality), a <see cref="RangeFilter{T}"/>, or a
/// <see cref="SetFilter{T}"/>, so callers only ever write <c>RangeFilter.Between(a, b)</c> or
/// <c>SetFilter.AnyOf(a, b)</c> and meet this type in signatures alone. An unset filter
/// (<see langword="default"/>) renders nothing, exactly like a <see langword="null"/> parameter.
/// </remarks>
public readonly struct Filter<T>
{
    private readonly T _lower;
    private readonly T _upper;
    private readonly T[]? _values;
    private readonly FilterMode _mode;

    private Filter(T lower, T upper, T[]? values, FilterMode mode)
    {
        _lower = lower;
        _upper = upper;
        _values = values;
        _mode = mode;
    }

    /// <summary>The lower bound, or the exact value for an equality filter.</summary>
    internal T Lower => _lower;

    /// <summary>The upper bound.</summary>
    internal T Upper => _upper;

    /// <summary>The set's values, for an any-of filter. Not copied from the caller.</summary>
    internal T[]? Values => _values;

    /// <summary>Which forms are set.</summary>
    internal FilterMode Mode => _mode;

    /// <summary>Converts a value to an equality filter: <c>field=value</c>.</summary>
    /// <param name="value">The exact value to match.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static implicit operator Filter<T>(T value)
    {
        FilterGuard.ThrowIfNull(value, nameof(value));
        return new Filter<T>(value, default!, null, FilterMode.Equal);
    }

    /// <summary>Converts a range filter, unchanged.</summary>
    /// <param name="filter">The range filter.</param>
    public static implicit operator Filter<T>(RangeFilter<T> filter) =>
        new(filter.Lower, filter.Upper, null, filter.Mode);

    /// <summary>Converts a set filter, unchanged.</summary>
    /// <param name="filter">The set filter.</param>
    public static implicit operator Filter<T>(SetFilter<T> filter) =>
        new(filter.Value, default!, filter.Values, filter.Mode);
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~FilterConstructionTests"`
Expected: 13 passed. The same CA1000 note from Task 1 applies to the conversion operators.

- [ ] **Step 7: Run the whole offline suite**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add src/MassiveDotNet/SetFilter.cs src/MassiveDotNet/ArrayFilter.cs src/MassiveDotNet/Filter.cs tests/MassiveDotNet.Rest.Tests/FilterConstructionTests.cs
git commit -m "feat: add SetFilter<T>, ArrayFilter<T>, and the Filter<T> union

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 3: Rendering in `RequestUriBuilder`

**Files:**
- Modify: `src/MassiveDotNet/Internal/ValueStringBuilder.cs` (after `Append(long)`, line 81)
- Modify: `src/MassiveDotNet/Http/RequestUriBuilder.cs` (after `AppendQuery(name, long?)`, line 105)
- Create: `tests/MassiveDotNet.Rest.Tests/FilterRenderingTests.cs`

**Interfaces:**
- Consumes: the four filter types and their `internal` accessors (Tasks 1 and 2); `LocalDate.ToWireValue()` and `DateOrTimestamp.ToString()` (existing).
- Produces: on `RequestUriBuilder`, `AppendQuery(ReadOnlySpan<char> name, double? value)`, `AppendQuery<T>(name, RangeFilter<T>? filter)`, `AppendQuery<T>(name, SetFilter<T>? filter, bool hasExactForm = true)`, `AppendQuery<T>(name, Filter<T>? filter)`, `AppendQuery<T>(name, ArrayFilter<T>? filter)`. On `ValueStringBuilder`, `Append(double)`.

- [ ] **Step 1: Write the failing rendering tests**

Create `tests/MassiveDotNet.Rest.Tests/FilterRenderingTests.cs`:

```csharp
using MassiveDotNet.Http;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Wire forms of the four filter types, asserted on the builder directly. The builder is public
/// API, so these are not tests of internals; they pin the exact strings every generated endpoint
/// will produce, for every element type in the closed set.
/// </summary>
public sealed class FilterRenderingTests
{
    private static string RenderRange<T>(RangeFilter<T>? filter)
    {
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery("f", filter);
        return builder.ToUriString();
    }

    private static string RenderSet<T>(SetFilter<T>? filter, bool hasExactForm = true)
    {
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery("f", filter, hasExactForm);
        return builder.ToUriString();
    }

    private static string RenderFilter<T>(Filter<T>? filter)
    {
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery("f", filter);
        return builder.ToUriString();
    }

    private static string RenderArray<T>(ArrayFilter<T>? filter)
    {
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery("f", filter);
        return builder.ToUriString();
    }

    [Fact]
    public void EqualityRendersThePlainField()
    {
        Assert.Equal("/x?f=AAPL", RenderRange<string>("AAPL"));
        Assert.Equal("/x?f=AAPL", RenderSet<string>("AAPL"));
        Assert.Equal("/x?f=AAPL", RenderFilter<string>("AAPL"));
        Assert.Equal("/x?f=AAPL", RenderArray<string>("AAPL"));
    }

    [Fact]
    public void EachBoundRendersItsOwnSuffix()
    {
        Assert.Equal("/x?f.gt=5", RenderRange(RangeFilter.Gt(5L)));
        Assert.Equal("/x?f.gte=5", RenderRange(RangeFilter.Gte(5L)));
        Assert.Equal("/x?f.lt=5", RenderRange(RangeFilter.Lt(5L)));
        Assert.Equal("/x?f.lte=5", RenderRange(RangeFilter.Lte(5L)));
    }

    [Fact]
    public void BetweenRendersInclusiveBounds()
    {
        Assert.Equal(
            "/x?f.gte=2026-01-01&f.lte=2026-01-31",
            RenderRange(RangeFilter.Between(new LocalDate(2026, 1, 1), new LocalDate(2026, 1, 31))));
    }

    [Fact]
    public void BoundsRenderInFixedOrderRegardlessOfChainingOrder()
    {
        Assert.Equal("/x?f.gte=1&f.lt=10", RenderRange(RangeFilter.Gte(1L).Lt(10L)));
        Assert.Equal("/x?f.gte=1&f.lt=10", RenderRange(RangeFilter.Lt(10L).Gte(1L)));
    }

    [Fact]
    public void EveryElementTypeRendersItsWireForm()
    {
        Assert.Equal("/x?f.gt=3", RenderRange(RangeFilter.Gt(3)));
        Assert.Equal("/x?f.gt=3", RenderRange(RangeFilter.Gt(3L)));
        Assert.Equal("/x?f.gt=0.5", RenderRange(RangeFilter.Gt(0.5)));
        Assert.Equal("/x?f.gt=1E-07", RenderRange(RangeFilter.Gt(0.0000001)));
        Assert.Equal("/x?f.gt=2026-01-01", RenderRange(RangeFilter.Gt(new LocalDate(2026, 1, 1))));
        Assert.Equal("/x?f.gte=1578114000000", RenderRange(RangeFilter.Gte<DateOrTimestamp>(1578114000000L)));
        Assert.Equal("/x?f.lte=2020-01-10", RenderRange(RangeFilter.Lte<DateOrTimestamp>(new LocalDate(2020, 1, 10))));
    }

    [Fact]
    public void StringElementsArePercentEscaped()
    {
        Assert.Equal("/x?f=BRK%2FB", RenderRange<string>("BRK/B"));
        Assert.Equal("/x?f.gte=a%20b", RenderRange(RangeFilter.Gte("a b")));
    }

    [Fact]
    public void NullAndUnsetFiltersRenderNothing()
    {
        Assert.Equal("/x", RenderRange<string>(null));
        Assert.Equal("/x", RenderRange(default(RangeFilter<string>)));
        Assert.Equal("/x", RenderSet<string>(null));
        Assert.Equal("/x", RenderSet(default(SetFilter<string>)));
        Assert.Equal("/x", RenderFilter<string>(null));
        Assert.Equal("/x", RenderFilter(default(Filter<string>)));
        Assert.Equal("/x", RenderArray<string>(null));
        Assert.Equal("/x", RenderArray(default(ArrayFilter<string>)));
    }

    [Fact]
    public void AnyOfJoinsWithALiteralCommaAndEscapesEachElement()
    {
        Assert.Equal("/x?f.any_of=BRK%2FB,AAPL", RenderSet(SetFilter.AnyOf("BRK/B", "AAPL")));
        Assert.Equal("/x?f.any_of=1,2,3", RenderSet(SetFilter.AnyOf(1L, 2L, 3L)));
    }

    [Fact]
    public void SetEqualityWithoutAnExactFormRendersAOneElementSet()
    {
        // The one base-less group in the spec (/v1/summaries ticker.any_of): no plain `ticker`
        // exists, so equality has to travel as a one-element any_of.
        Assert.Equal("/x?f.any_of=AAPL", RenderSet<string>("AAPL", hasExactForm: false));
        Assert.Equal("/x?f.any_of=AAPL,MSFT", RenderSet(SetFilter.AnyOf("AAPL", "MSFT"), hasExactForm: false));
    }

    [Fact]
    public void FilterRendersWhateverItWasBuiltFrom()
    {
        Assert.Equal("/x?f=5", RenderFilter<long>(5L));
        Assert.Equal("/x?f.gte=1&f.lte=9", RenderFilter<long>(RangeFilter.Between(1L, 9L)));
        Assert.Equal("/x?f.any_of=1,2", RenderFilter<long>(SetFilter.AnyOf(1L, 2L)));
    }

    [Fact]
    public void ArrayFilterRendersContainsAnyOfAndAllOf()
    {
        Assert.Equal("/x?f=AAPL", RenderArray(ArrayFilter.Contains("AAPL")));
        Assert.Equal("/x?f.any_of=A,B", RenderArray(ArrayFilter.AnyOf("A", "B")));
        Assert.Equal("/x?f.all_of=A,B", RenderArray(ArrayFilter.AllOf("A", "B")));
        Assert.Equal("/x?f.any_of=A", RenderArray<string>(SetFilter.AnyOf("A")));
    }

    [Fact]
    public void FiltersShareTheSeparatorWithOtherParameters()
    {
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery("a", "1");
        builder.AppendQuery("f", RangeFilter.Between(1L, 2L));
        builder.AppendQuery("z", 3);

        Assert.Equal("/x?a=1&f.gte=1&f.lte=2&z=3", builder.ToUriString());
    }

    [Fact]
    public void PlainDoubleParameterRendersInvariantAndSkipsNull()
    {
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery("d", (double?)0.25);
        builder.AppendQuery("e", (double?)null);

        Assert.Equal("/x?d=0.25", builder.ToUriString());
    }
}
```

There is deliberately no comma-decimal-culture test: the repository builds with `InvariantGlobalization`, so a request for `de-DE` cannot produce `0,5` here and the test would be vacuous. The guarantee comes from CA1305, which fails the build unless every format call names a provider, and from the code below naming `CultureInfo.InvariantCulture`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~FilterRenderingTests"`
Expected: build FAILS with `CS1503` on `builder.AppendQuery("f", filter)`: no overload takes a filter.

- [ ] **Step 3: Add `Append(double)` to `ValueStringBuilder`**

In `src/MassiveDotNet/Internal/ValueStringBuilder.cs`, add `using System.Globalization;` to the usings and insert after `Append(long)`:

```csharp
    /// <summary>Appends the shortest round-trippable invariant representation of a double.</summary>
    /// <param name="value">The value to append.</param>
    /// <remarks>
    /// The provider is named explicitly: a query string is not user-facing text, and
    /// <c>0,5</c> under a comma-decimal culture would be a silent wrong request.
    /// </remarks>
    public void Append(double value)
    {
        if (!value.TryFormat(_chars[_position..], out int written, format: default, provider: CultureInfo.InvariantCulture))
        {
            Grow(32);
            bool ok = value.TryFormat(_chars[_position..], out written, format: default, provider: CultureInfo.InvariantCulture);
            Debug.Assert(ok, "Formatting a double into a freshly grown buffer should always succeed.");
        }

        _position += written;
    }
```

- [ ] **Step 4: Add the overloads and the dispatch to `RequestUriBuilder`**

In `src/MassiveDotNet/Http/RequestUriBuilder.cs`, extend the usings to:

```csharp
using System.Runtime.CompilerServices;
using MassiveDotNet.Internal;
using NodaTime;
```

Insert after `AppendQuery(scoped ReadOnlySpan<char> name, long? value)` and before `ToUriString`:

```csharp
    /// <summary>Appends a floating-point query parameter in invariant culture, skipping it when <see langword="null"/>.</summary>
    /// <param name="name">The parameter name, which must already be URI-safe.</param>
    /// <param name="value">The parameter value, or <see langword="null"/> to omit the parameter.</param>
    public void AppendQuery(scoped ReadOnlySpan<char> name, double? value)
    {
        if (value is null)
        {
            return;
        }

        AppendSeparator();
        _builder.Append(name);
        _builder.Append('=');
        _builder.Append(value.Value);
    }

    /// <summary>
    /// Appends a range filter as its comparator parameters, skipping it when <see langword="null"/>
    /// or unset.
    /// </summary>
    /// <typeparam name="T">
    /// The element type: <see cref="string"/>, <see cref="int"/>, <see cref="long"/>,
    /// <see cref="double"/>, <see cref="LocalDate"/>, or <see cref="DateOrTimestamp"/>.
    /// </typeparam>
    /// <param name="name">The field's base name, which must already be URI-safe.</param>
    /// <param name="filter">The filter, or <see langword="null"/> to omit the field entirely.</param>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not one of the supported element types.</exception>
    public void AppendQuery<T>(scoped ReadOnlySpan<char> name, RangeFilter<T>? filter)
    {
        if (filter is not { } value)
        {
            return;
        }

        AppendFilter(name, value.Mode, value.Lower, value.Upper, values: null, hasExactForm: true);
    }

    /// <summary>
    /// Appends a set filter as its comparator parameters, skipping it when <see langword="null"/>
    /// or unset.
    /// </summary>
    /// <typeparam name="T">
    /// The element type: <see cref="string"/>, <see cref="int"/>, <see cref="long"/>,
    /// <see cref="double"/>, <see cref="LocalDate"/>, or <see cref="DateOrTimestamp"/>.
    /// </typeparam>
    /// <param name="name">The field's base name, which must already be URI-safe.</param>
    /// <param name="filter">The filter, or <see langword="null"/> to omit the field entirely.</param>
    /// <param name="hasExactForm">
    /// Whether the endpoint declares the plain <c>field</c> parameter. When it declares only
    /// <c>field.any_of</c>, an equality is rendered as a one-element set, which means the same thing.
    /// </param>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not one of the supported element types.</exception>
    public void AppendQuery<T>(scoped ReadOnlySpan<char> name, SetFilter<T>? filter, bool hasExactForm = true)
    {
        if (filter is not { } value)
        {
            return;
        }

        AppendFilter(name, value.Mode, value.Value, default!, value.Values, hasExactForm);
    }

    /// <summary>
    /// Appends a range-or-set filter as its comparator parameters, skipping it when
    /// <see langword="null"/> or unset.
    /// </summary>
    /// <typeparam name="T">
    /// The element type: <see cref="string"/>, <see cref="int"/>, <see cref="long"/>,
    /// <see cref="double"/>, <see cref="LocalDate"/>, or <see cref="DateOrTimestamp"/>.
    /// </typeparam>
    /// <param name="name">The field's base name, which must already be URI-safe.</param>
    /// <param name="filter">The filter, or <see langword="null"/> to omit the field entirely.</param>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not one of the supported element types.</exception>
    public void AppendQuery<T>(scoped ReadOnlySpan<char> name, Filter<T>? filter)
    {
        if (filter is not { } value)
        {
            return;
        }

        AppendFilter(name, value.Mode, value.Lower, value.Upper, value.Values, hasExactForm: true);
    }

    /// <summary>
    /// Appends an array filter as its comparator parameters, skipping it when <see langword="null"/>
    /// or unset.
    /// </summary>
    /// <typeparam name="T">
    /// The element type: <see cref="string"/>, <see cref="int"/>, <see cref="long"/>,
    /// <see cref="double"/>, <see cref="LocalDate"/>, or <see cref="DateOrTimestamp"/>.
    /// </typeparam>
    /// <param name="name">The field's base name, which must already be URI-safe.</param>
    /// <param name="filter">The filter, or <see langword="null"/> to omit the field entirely.</param>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not one of the supported element types.</exception>
    public void AppendQuery<T>(scoped ReadOnlySpan<char> name, ArrayFilter<T>? filter)
    {
        if (filter is not { } value)
        {
            return;
        }

        AppendFilter(name, value.Mode, value.Value, default!, value.Values, hasExactForm: true);
    }
```

Then add these private members after `AppendSeparator()` at the end of the struct:

```csharp
    /// <summary>
    /// Renders every form a filter carries. The order is fixed here -- plain, gt, gte, lt, lte,
    /// any_of, all_of -- whatever order the OpenAPI description happened to declare the variants
    /// in, so two endpoints with the same filter always produce the same query string.
    /// </summary>
    private void AppendFilter<T>(
        scoped ReadOnlySpan<char> name,
        FilterMode mode,
        T lower,
        T upper,
        T[]? values,
        bool hasExactForm)
    {
        if ((mode & FilterMode.Equal) != 0)
        {
            // A one-element set is equality, and it is the only form an endpoint without a plain
            // parameter can accept.
            AppendComparator(name, hasExactForm ? "" : ".any_of", lower);
        }

        if ((mode & FilterMode.Gt) != 0)
        {
            AppendComparator(name, ".gt", lower);
        }

        if ((mode & FilterMode.Gte) != 0)
        {
            AppendComparator(name, ".gte", lower);
        }

        if ((mode & FilterMode.Lt) != 0)
        {
            AppendComparator(name, ".lt", upper);
        }

        if ((mode & FilterMode.Lte) != 0)
        {
            AppendComparator(name, ".lte", upper);
        }

        if ((mode & FilterMode.AnyOf) != 0)
        {
            AppendSet(name, ".any_of", values!);
        }

        if ((mode & FilterMode.AllOf) != 0)
        {
            AppendSet(name, ".all_of", values!);
        }
    }

    private void AppendComparator<T>(scoped ReadOnlySpan<char> name, scoped ReadOnlySpan<char> suffix, T value)
    {
        AppendSeparator();
        _builder.Append(name);
        _builder.Append(suffix);
        _builder.Append('=');
        AppendElement(value);
    }

    /// <summary>
    /// Elements are escaped one at a time and joined with a literal comma, so a value that itself
    /// contains a comma arrives as <c>%2C</c> and stays distinguishable from the separator.
    /// </summary>
    private void AppendSet<T>(scoped ReadOnlySpan<char> name, scoped ReadOnlySpan<char> suffix, T[] values)
    {
        AppendSeparator();
        _builder.Append(name);
        _builder.Append(suffix);
        _builder.Append('=');

        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                _builder.Append(',');
            }

            AppendElement(values[i]);
        }
    }

    /// <summary>
    /// Formats one element. <c>typeof(T)</c> is a constant for each generic instantiation, so the
    /// JIT and the Native AOT compiler keep exactly one branch and <see cref="Unsafe.As{TFrom, TTo}(ref TFrom)"/>
    /// reinterprets without boxing. The set is closed on purpose: the generator refuses any
    /// other element type before it reaches here, so the fallback is unreachable from generated
    /// code and exists only to fail loudly for hand-written callers.
    /// </summary>
    private void AppendElement<T>(T value)
    {
        if (typeof(T) == typeof(string))
        {
            _builder.Append(Uri.EscapeDataString(Unsafe.As<T, string>(ref value)));
        }
        else if (typeof(T) == typeof(int))
        {
            _builder.Append(Unsafe.As<T, int>(ref value));
        }
        else if (typeof(T) == typeof(long))
        {
            _builder.Append(Unsafe.As<T, long>(ref value));
        }
        else if (typeof(T) == typeof(double))
        {
            _builder.Append(Unsafe.As<T, double>(ref value));
        }
        else if (typeof(T) == typeof(LocalDate))
        {
            // A fixed-form literal: ISO dates need no escaping.
            _builder.Append(Unsafe.As<T, LocalDate>(ref value).ToWireValue());
        }
        else if (typeof(T) == typeof(DateOrTimestamp))
        {
            _builder.Append(Unsafe.As<T, DateOrTimestamp>(ref value).ToString());
        }
        else
        {
            throw new NotSupportedException(
                $"{typeof(T)} is not a supported filter element type. Supported: string, int, long, double, LocalDate, DateOrTimestamp.");
        }
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~FilterRenderingTests"`
Expected: 13 passed.

If `EveryElementTypeRendersItsWireForm` fails on `1E-07`, the runtime's shortest round-trip format differs from what this plan assumed; keep the assertion on whatever `0.0000001.ToString(CultureInfo.InvariantCulture)` returns on this runtime, since the point is invariance, not the exact exponent notation.

- [ ] **Step 6: Run the whole offline suite and the AOT publish**

Run:

```bash
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
dotnet publish samples/MassiveDotNet.AotSmokeTest -r osx-arm64 -c Release 2>&1 | grep -E ': (warning|error) (IL|AOT|Trim)?[0-9]{4}' || echo "no IL warnings"
```

Expected: all pass; `no IL warnings`. The sample does not yet call a filter overload, so this only proves the builder still publishes; Task 7 roots the generic instantiations.

- [ ] **Step 7: Commit**

```bash
git add src/MassiveDotNet/Internal/ValueStringBuilder.cs src/MassiveDotNet/Http/RequestUriBuilder.cs tests/MassiveDotNet.Rest.Tests/FilterRenderingTests.cs
git commit -m "feat: render filters from RequestUriBuilder, one overload per filter type

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 4: `LocalDateJsonConverter`

**Files:**
- Create: `src/MassiveDotNet/Serialization/LocalDateJsonConverter.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/LocalDateJsonConverterTests.cs`

**Interfaces:**
- Consumes: NodaTime `LocalDate`, `LocalDatePattern.Iso`.
- Produces: `public sealed class LocalDateJsonConverter : JsonConverter<LocalDate>` in namespace `MassiveDotNet.Serialization`. Task 5 registers it on the generated context; nothing else references it by name.

- [ ] **Step 1: Write the failing converter tests**

Create `tests/MassiveDotNet.Rest.Tests/LocalDateJsonConverterTests.cs`:

```csharp
using System.Buffers;
using System.Text;
using System.Text.Json;
using MassiveDotNet.Serialization;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The converter is driven directly through <see cref="Utf8JsonReader"/> because the test
/// project, like the shipped code, has reflection-based serialization disabled and so cannot call
/// <c>JsonSerializer.Deserialize&lt;LocalDate&gt;</c> without a context of its own. The end-to-end
/// path through a generated model is covered in <c>StocksDividendsTests</c>.
/// </summary>
public sealed class LocalDateJsonConverterTests
{
    private static readonly LocalDateJsonConverter Converter = new();
    private static readonly JsonSerializerOptions Options = new();

    private static LocalDate Read(string json)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        Assert.True(reader.Read(), "The test JSON should contain one token.");
        return Converter.Read(ref reader, typeof(LocalDate), Options);
    }

    [Fact]
    public void ReadsAnIsoDate()
    {
        Assert.Equal(new LocalDate(2025, 8, 11), Read("\"2025-08-11\""));
    }

    [Fact]
    public void ReadsAnEscapedIsoDateThroughTheSlowPath()
    {
        // - is a hyphen. An escaped value forces the copy-and-unescape path, which the
        // unescaped fast path never exercises.
        Assert.Equal(new LocalDate(2025, 8, 11), Read("\"2025\\u002D08-11\""));
    }

    [Theory]
    [InlineData("\"2025-8-11\"")]
    [InlineData("\"20250811\"")]
    [InlineData("\"2025-13-01\"")]
    [InlineData("\"2025-02-30\"")]
    [InlineData("\"abcd-ef-gh\"")]
    [InlineData("\"2025-08-11T00:00:00Z\"")]
    [InlineData("20250811")]
    [InlineData("null")]
    public void RejectsAnythingThatIsNotAnIsoDate(string json)
    {
        Assert.Throws<JsonException>(() => Read(json));
    }

    [Fact]
    public void WritesTheIsoForm()
    {
        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            Converter.Write(writer, new LocalDate(2025, 8, 11), Options);
        }

        Assert.Equal("\"2025-08-11\"", Encoding.UTF8.GetString(buffer.WrittenSpan));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~LocalDateJsonConverterTests"`
Expected: build FAILS with `CS0246: The type or namespace name 'LocalDateJsonConverter' could not be found`.

- [ ] **Step 3: Create the converter**

Create `src/MassiveDotNet/Serialization/LocalDateJsonConverter.cs`:

```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;
using NodaTime.Text;

namespace MassiveDotNet.Serialization;

/// <summary>
/// Reads and writes a calendar date in the <c>YYYY-MM-DD</c> form the Massive API uses.
/// </summary>
/// <remarks>
/// <para>
/// Parses straight from the reader's UTF-8 bytes: no intermediate string, and none of the
/// <c>ParseResult</c> allocation NodaTime's pattern API incurs per value. A reference model with
/// four date fields over a thousand-row page would otherwise allocate eight thousand objects
/// that are discarded immediately.
/// </para>
/// <para>
/// Registered once on the generated REST serialization context, so every <see cref="LocalDate"/>
/// property on every model uses it without a per-property attribute. A nullable
/// <c>LocalDate?</c> property resolves to this converter through the serializer's own nullable
/// wrapper.
/// </para>
/// </remarks>
public sealed class LocalDateJsonConverter : JsonConverter<LocalDate>
{
    private const int IsoLength = 10;

    // Longer than any escaped spelling of a ten-character date. A raw value past this cannot be a
    // date, so it is rejected before any buffer is sized from it.
    private const int MaxRawLength = 64;

    /// <inheritdoc />
    public override LocalDate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a YYYY-MM-DD string for a date, found a {reader.TokenType} token.");
        }

        // The fast path reads the bytes in place. A date never needs an escape, and a value split
        // across buffer segments is rare, so the copying path below is for correctness only.
        if (!reader.HasValueSequence && !reader.ValueIsEscaped)
        {
            return Parse(reader.ValueSpan);
        }

        long rawLength = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;

        if (rawLength > MaxRawLength)
        {
            throw new JsonException("Expected a YYYY-MM-DD string for a date, found a longer value.");
        }

        Span<byte> buffer = stackalloc byte[MaxRawLength];
        int written = reader.CopyString(buffer);

        return Parse(buffer[..written]);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, LocalDate value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(LocalDatePattern.Iso.Format(value));
    }

    private static LocalDate Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length != IsoLength
            || utf8[4] != (byte)'-'
            || utf8[7] != (byte)'-'
            || !TryDigits(utf8[..4], out int year)
            || !TryDigits(utf8.Slice(5, 2), out int month)
            || !TryDigits(utf8.Slice(8, 2), out int day))
        {
            throw new JsonException($"Expected a YYYY-MM-DD date, found '{Encoding.UTF8.GetString(utf8)}'.");
        }

        try
        {
            return new LocalDate(year, month, day);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new JsonException($"'{Encoding.UTF8.GetString(utf8)}' is not a valid calendar date.", ex);
        }
    }

    private static bool TryDigits(ReadOnlySpan<byte> utf8, out int value)
    {
        value = 0;

        foreach (byte b in utf8)
        {
            if (b is < (byte)'0' or > (byte)'9')
            {
                return false;
            }

            value = (value * 10) + (b - '0');
        }

        return true;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~LocalDateJsonConverterTests"`
Expected: 11 passed (three facts and eight theory cases).

- [ ] **Step 5: Run the whole offline suite**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: all pass. The source scan in `TemporalTypeTests` reads the new file; `LocalDate` is NodaTime and permitted.

- [ ] **Step 6: Commit**

```bash
git add src/MassiveDotNet/Serialization/LocalDateJsonConverter.cs tests/MassiveDotNet.Rest.Tests/LocalDateJsonConverterTests.cs
git commit -m "feat: add a span-parsing LocalDate JSON converter

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 5: Generate one filter parameter per comparator field

**Files:**
- Modify: `tools/MassiveDotNet.CodeGen/Spec.cs` (records at the top; `Slots` after `Parameters`, line 89)
- Modify: `tools/MassiveDotNet.CodeGen/TypeBinding.cs`
- Modify: `tools/MassiveDotNet.CodeGen/Argument.cs`
- Modify: `tools/MassiveDotNet.CodeGen/Emitter.cs` (`EmitModel`, `EmitGroup`, `EmitEndpoint`, `EmitJsonContext`)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`

**Interfaces:**
- Consumes: `LocalDateJsonConverter` (Task 4), the filter type names (Tasks 1 and 2), the `hasExactForm` parameter on the builder's `SetFilter` overload (Task 3).
- Produces: `internal sealed record ComparatorGroup(string BaseName, IReadOnlySet<string> Suffixes, bool HasExactForm)` with `FilterType(string operationId)` and `DocSentence(string operationId)`; `internal sealed record ParameterSlot(string WireName, SpecParameter Parameter, ComparatorGroup? Group)`; `static List<ParameterSlot> Spec.Slots(List<SpecParameter>)`; `static TypeBinding TypeBinding.ResolveFilter(ComparatorGroup, string? mapType, JsonElement schema, string operationId)`; `static Argument Argument.Create(ParameterSlot, MapParameter?, string operationId)`.

The generator has no test project. Its contract is checked three ways: the build of the regenerated code, the offline suite, and the diff after regeneration, which for this task must touch **only** `MassiveRestJsonContext.g.cs`, because no mapped operation yet declares a comparator.

- [ ] **Step 1: Add the grouping records and `Spec.Slots`**

In `tools/MassiveDotNet.CodeGen/Spec.cs`, add after the `SpecProperty` record:

```csharp
/// <summary>The comparator variants a field declares, such as <c>gt gte lt lte any_of</c>.</summary>
/// <param name="BaseName">The wire name of the field, for example <c>ticker</c>.</param>
/// <param name="Suffixes">The suffixes declared, drawn from <c>gt gte lt lte any_of all_of</c>.</param>
/// <param name="HasExactForm">
/// Whether the operation also declares the plain <c>field</c> parameter. One operation in the
/// description does not (<c>/v1/summaries</c> declares only <c>ticker.any_of</c>).
/// </param>
internal sealed record ComparatorGroup(string BaseName, IReadOnlySet<string> Suffixes, bool HasExactForm)
{
    /// <summary>
    /// The filter type the exact suffix set maps to. Any other set fails generation: a new
    /// combination needs a filter type designed for it, and this is what makes a spec sync that
    /// introduces an unknown suffix fail loudly rather than emit something plausible.
    /// </summary>
    public string FilterType(string operationId) => Key switch
    {
        "gt gte lt lte" => "RangeFilter",
        "any_of" => "SetFilter",
        "any_of gt gte lt lte" => "Filter",
        "all_of any_of" => "ArrayFilter",
        _ => throw new InvalidOperationException(
            $"Operation '{operationId}' declares comparators [{Key}] on '{BaseName}', which is not a "
            + "recognised shape. Known shapes: [gt gte lt lte], [any_of], [any_of gt gte lt lte], "
            + "[all_of any_of]. A new combination needs a filter type designed for it, not a guess."),
    };

    /// <summary>The sentence appended to the field's description, naming the forms it accepts.</summary>
    public string DocSentence(string operationId) => FilterType(operationId) switch
    {
        "RangeFilter" => "Accepts an exact value or a range.",
        "SetFilter" => HasExactForm
            ? "Accepts an exact value or a set of values."
            : "Accepts one or more values.",
        "Filter" => "Accepts an exact value, a range, or a set of values.",
        _ => "Matches arrays containing the value, any of the values, or all of the values.",
    };

    private string Key => string.Join(' ', Suffixes.Order(StringComparer.Ordinal));
}

/// <summary>
/// One generated parameter: a plain spec parameter, or a comparator group standing in for several.
/// </summary>
/// <param name="WireName">The parameter name, or the group's base name. Map rows are keyed by this.</param>
/// <param name="Parameter">
/// The spec parameter that supplies the schema and prose: the plain parameter itself, the
/// group's base field, or its first variant when the description declares no base.
/// </param>
/// <param name="Group">The comparator group, or <see langword="null"/> for a plain parameter.</param>
internal sealed record ParameterSlot(string WireName, SpecParameter Parameter, ComparatorGroup? Group);
```

Then, inside the `Spec` class, add a field and two methods after `Parameters`:

```csharp
    private static readonly HashSet<string> ComparatorSuffixes =
        new(StringComparer.Ordinal) { "gt", "gte", "lt", "lte", "any_of", "all_of" };

    /// <summary>
    /// Collapses an operation's parameters into slots. Comparator variants such as
    /// <c>ticker.gte</c> fold into one group keyed by their base name, placed where the base was
    /// declared, or where the first variant was when the description declares no base. Everything
    /// else passes through unchanged.
    /// </summary>
    /// <remarks>
    /// Only the six known suffixes count. The SEC filings endpoint declares nested field paths
    /// such as <c>entities.company_data.name</c>, which contain a dot but are not comparators and
    /// must stay plain parameters.
    /// </remarks>
    public static List<ParameterSlot> Slots(List<SpecParameter> parameters)
    {
        Dictionary<string, HashSet<string>> suffixesByBase = new(StringComparer.Ordinal);

        foreach (SpecParameter parameter in parameters)
        {
            if (SplitComparator(parameter.Name) is (string baseName, string suffix))
            {
                if (!suffixesByBase.TryGetValue(baseName, out HashSet<string>? suffixes))
                {
                    suffixes = new HashSet<string>(StringComparer.Ordinal);
                    suffixesByBase[baseName] = suffixes;
                }

                suffixes.Add(suffix);
            }
        }

        HashSet<string> declaredNames = new(parameters.Select(p => p.Name), StringComparer.Ordinal);
        HashSet<string> placed = new(StringComparer.Ordinal);
        List<ParameterSlot> slots = [];

        foreach (SpecParameter parameter in parameters)
        {
            string key = SplitComparator(parameter.Name) is (string baseName, _) ? baseName : parameter.Name;

            if (!suffixesByBase.TryGetValue(key, out HashSet<string>? suffixes))
            {
                slots.Add(new ParameterSlot(parameter.Name, parameter, Group: null));
                continue;
            }

            // A later variant of a group that is already in place.
            if (!placed.Add(key))
            {
                continue;
            }

            slots.Add(new ParameterSlot(key, parameter, new ComparatorGroup(key, suffixes, declaredNames.Contains(key))));
        }

        return slots;
    }

    private static (string BaseName, string Suffix)? SplitComparator(string name)
    {
        int dot = name.LastIndexOf('.');

        if (dot <= 0)
        {
            return null;
        }

        string suffix = name[(dot + 1)..];

        return ComparatorSuffixes.Contains(suffix) ? (name[..dot], suffix) : null;
    }
```

- [ ] **Step 2: Teach `TypeBinding` about dates and filters**

In `tools/MassiveDotNet.CodeGen/TypeBinding.cs`, change the `FromSchema` switch so a date-formatted string becomes a `LocalDate`:

```csharp
        return type.GetString() switch
        {
            "integer" => format == "int64" ? "long" : "int",
            "number" => "double",
            "boolean" => "bool",
            "array" => "string[]",
            // A calendar date is a LocalDate on both parameters and model properties (D-F7, D-F9).
            "string" => format == "date" ? "LocalDate" : "string",
            _ => "string",
        };
```

Add the allowlist and the filter resolver to the same class, after `Resolve`:

```csharp
    /// <summary>
    /// The element types a filter can render. This mirrors the dispatch in
    /// <c>RequestUriBuilder.AppendElement</c>; extend the two together.
    /// </summary>
    private static readonly HashSet<string> FilterElementTypes =
        new(StringComparer.Ordinal) { "string", "int", "long", "double", "LocalDate", "DateOrTimestamp" };

    /// <summary>
    /// Resolves the binding for a comparator group: the filter type over the field's element
    /// type, which the map may override on the base field's row.
    /// </summary>
    /// <exception cref="InvalidOperationException">The element type is outside the set the builder renders.</exception>
    public static TypeBinding ResolveFilter(ComparatorGroup group, string? mapType, JsonElement schema, string operationId)
    {
        string element = mapType ?? FromSchema(schema);

        if (!FilterElementTypes.Contains(element))
        {
            throw new InvalidOperationException(
                $"Operation '{operationId}' filters on '{group.BaseName}' with element type '{element}', which "
                + $"RequestUriBuilder cannot render. Supported: {string.Join(", ", FilterElementTypes.Order(StringComparer.Ordinal))}. "
                + $"Set 'type' on the '{group.BaseName}' row in specs/endpoints.map.json to one of these -- the element "
                + "type, never the filter type.");
        }

        // The builder overload takes the filter itself, so there is no wire conversion to name.
        // Filters are query-only by construction, so the path method is never consulted.
        return new TypeBinding($"{group.FilterType(operationId)}<{element}>", "AppendPathSegment", null);
    }
```

- [ ] **Step 3: Create filter arguments from slots**

Replace the whole of `tools/MassiveDotNet.CodeGen/Argument.cs` with:

```csharp
namespace MassiveDotNet.CodeGen;

/// <summary>A single generated method parameter.</summary>
/// <param name="QueryCallSuffix">
/// Extra arguments appended to the builder call, after the value. Used only by the one base-less
/// set group, which passes <c>hasExactForm: false</c>.
/// </param>
internal sealed record Argument(
    string WireName,
    string Identifier,
    bool Required,
    string In,
    string? Description,
    TypeBinding Binding,
    string QueryCallSuffix = "")
{
    public string CSharpType => Binding.CSharpType;

    /// <summary>The parameter as it appears in the method signature.</summary>
    public string Declaration => Required
        ? $"{CSharpType} {Identifier}"
        : $"{CSharpType}? {Identifier} = null";

    /// <summary>The parameter without a default, for private helpers that always pass every value.</summary>
    public string RequiredDeclaration => Required ? $"{CSharpType} {Identifier}" : $"{CSharpType}? {Identifier}";

    public string PathExpression => Binding.PathExpression(Identifier);

    public string QueryExpression => Required
        ? Binding.PathExpression(Identifier)
        : Binding.QueryExpression(Identifier) + QueryCallSuffix;

    public string PathAppendMethod => Binding.PathAppendMethod;

    /// <summary>Creates the argument for a slot: a plain parameter, or one filter for a comparator group.</summary>
    public static Argument Create(ParameterSlot slot, MapParameter? mapped, string operationId)
    {
        if (slot.Group is null)
        {
            return Create(slot.Parameter, mapped);
        }

        ComparatorGroup group = slot.Group;

        // Nothing in the description requires a field that also carries comparators, and a
        // required filter is a shape this generator does not emit. Refusing is better than
        // silently making it optional.
        if (slot.Parameter.Required)
        {
            throw new InvalidOperationException(
                $"Operation '{operationId}' requires '{group.BaseName}', which also carries comparators. "
                + "Required filter parameters are not supported; this needs a design, not a default.");
        }

        // The variants' own descriptions are boilerplate ("Range by ticker."), so the base field's
        // prose carries the meaning and one generated sentence names the accepted forms.
        string? prose = Prose.Clean(slot.Parameter.Description);
        string sentence = group.DocSentence(operationId);
        string description = prose is null ? sentence : $"{prose.TrimEnd().TrimEnd('.')}. {sentence}";

        return new Argument(
            group.BaseName,
            mapped?.Name ?? group.BaseName,
            Required: false,
            In: "query",
            description,
            TypeBinding.ResolveFilter(group, mapped?.Type, slot.Parameter.Schema, operationId),
            QueryCallSuffix: group.HasExactForm ? "" : ", hasExactForm: false");
    }

    public static Argument Create(SpecParameter parameter, MapParameter? mapped) => new(
        parameter.Name,
        mapped?.Name ?? parameter.Name,
        parameter.Required,
        parameter.In,
        Prose.Clean(parameter.Description),
        TypeBinding.Resolve(mapped?.Type, parameter.Schema));
}
```

- [ ] **Step 4: Wire the emitter to slots, validate map keys, and emit usings conditionally**

In `tools/MassiveDotNet.CodeGen/Emitter.cs`:

**4a.** In `EmitEndpoint`, replace

```csharp
        List<SpecParameter> parameters = spec.Parameters(operation);
```

and, further down,

```csharp
        List<Argument> arguments = [.. parameters
            .Select(p => Argument.Create(p, endpoint.Parameters.GetValueOrDefault(p.Name)))
            .OrderByDescending(a => a.Required)];
```

with a single call at the former's position:

```csharp
        List<Argument> arguments = Arguments(endpoint, operation);
```

and add these two members to the class:

```csharp
    /// <summary>
    /// Every parameter the endpoint's methods take, required first, otherwise in the
    /// description's declaration order with each comparator group standing where its base
    /// field was declared.
    /// </summary>
    private List<Argument> Arguments(MapEndpoint endpoint, SpecOperation operation)
    {
        List<ParameterSlot> slots = Spec.Slots(spec.Parameters(operation));
        ValidateMapKeys(endpoint, slots);

        return [.. slots
            .Select(s => Argument.Create(s, endpoint.Parameters.GetValueOrDefault(s.WireName), endpoint.OperationId))
            .OrderByDescending(a => a.Required)];
    }

    /// <summary>
    /// A map row that names nothing the operation declares is a typo that would otherwise be
    /// ignored without a word. Grouping adds a second way to be wrong that looks right: a row
    /// keyed by a variant such as <c>ticker.gte</c> does nothing, because the group is keyed by
    /// its base name.
    /// </summary>
    private static void ValidateMapKeys(MapEndpoint endpoint, List<ParameterSlot> slots)
    {
        foreach (string key in endpoint.Parameters.Keys.Order(StringComparer.Ordinal))
        {
            if (!slots.Exists(s => s.WireName == key))
            {
                throw new InvalidOperationException(
                    $"Operation '{endpoint.OperationId}' maps parameter '{key}', which it does not declare. "
                    + "Rows are keyed by the field's base name: comparator variants such as 'ticker.gte' "
                    + "are grouped under 'ticker'.");
            }
        }
    }

    /// <summary>Whether a C# type name needs <c>using NodaTime;</c> in the file that declares it.</summary>
    private static bool NamesNodaTime(string type) =>
        type.Contains("LocalDate", StringComparison.Ordinal) || type.Contains("Instant", StringComparison.Ordinal);
```

**4b.** In `EmitGroup`, the `using` lines become conditional on NodaTime. Replace

```csharp
        writer.Line("using MassiveDotNet.Http;");
        writer.Line("using MassiveDotNet.Rest.Models;");
        writer.Line("using MassiveDotNet.Rest.Serialization;");
```

with

```csharp
        // Emitted conditionally: an unused using fails the build under EnforceCodeStyleInBuild.
        // Any is order-independent, so rule 6 holds.
        bool needsNodaTime = endpoints.Any(e =>
            Arguments(e, spec.Operation(e.OperationId)).Exists(a => NamesNodaTime(a.CSharpType)));

        writer.Line("using MassiveDotNet.Http;");
        writer.Line("using MassiveDotNet.Rest.Models;");
        writer.Line("using MassiveDotNet.Rest.Serialization;");

        if (needsNodaTime)
        {
            writer.Line("using NodaTime;");
        }
```

**4c.** In `EmitModel`, the property loop computes each type inline. Hoist the computation so the `using` can be decided first. Replace everything from `CodeWriter writer = new();` to the end of the method with:

```csharp
        List<(SpecProperty Property, string Name, string Type, string? Summary)> members = [.. properties.Select(property =>
        {
            model.Properties.TryGetValue(property.Name, out MapProperty? mapped);

            return (
                property,
                mapped?.Name ?? Naming.Pascal(property.Name),
                mapped?.Type ?? DefaultPropertyType(property),
                mapped?.Summary ?? Prose.Clean(property.Description));
        })];

        CodeWriter writer = new();
        writer.Line(Header);
        writer.Line();
        writer.Line("using System.Text.Json.Serialization;");

        // Emitted conditionally: an unused using fails the build under EnforceCodeStyleInBuild.
        if (members.Exists(m => NamesNodaTime(m.Type)))
        {
            writer.Line("using NodaTime;");
        }

        writer.Line();
        writer.Line("namespace MassiveDotNet.Rest.Models;");
        writer.Line();

        writer.Doc("summary", model.Summary, preserveMarkup: true);
        writer.Doc("remarks", model.Remarks, preserveMarkup: true);

        string declaration = model.Kind == "struct"
            ? $"public readonly partial record struct {model.Name}"
            : $"public sealed partial record {model.Name}";

        using (writer.Block(declaration))
        {
            bool first = true;

            foreach ((SpecProperty property, string name, string type, string? summary) in members)
            {
                if (!first)
                {
                    writer.Line();
                }

                first = false;

                writer.Doc("summary", summary);
                writer.Line($"[JsonPropertyName(\"{property.Name}\")]");
                writer.Line($"public {type} {name} {{ get; init; }}");
            }
        }

        return writer.ToString();
```

**4d.** In `EmitJsonContext`, register the converter. Replace the method body's `using` line and the attribute loop so it reads:

```csharp
        CodeWriter writer = new();
        writer.Line(Header);
        writer.Line();
        writer.Line("using System.Text.Json.Serialization;");
        writer.Line("using MassiveDotNet.Serialization;");
        writer.Line();
        writer.Line("namespace MassiveDotNet.Rest.Serialization;");
        writer.Line();
        writer.Doc("summary", "Source-generated serialization metadata for every REST response envelope. Using a context rather than reflection keeps the SDK Native AOT compatible.");
        writer.Doc("remarks", "Calendar dates are read by <see cref=\"LocalDateJsonConverter\"/>, registered here once so no model property needs its own attribute.", preserveMarkup: true);
        writer.Line("[JsonSourceGenerationOptions(Converters = new[] { typeof(LocalDateJsonConverter) })]");

        foreach (MapEndpoint endpoint in map.Endpoints)
        {
            writer.Line($"[JsonSerializable(typeof({EnvelopeName(endpoint)}))]");
        }

        writer.Line("internal sealed partial class MassiveRestJsonContext : JsonSerializerContext;");

        return writer.ToString();
```

The array is written as `new[] { ... }` rather than a collection expression because attribute arguments must be array-creation expressions.

- [ ] **Step 5: Regenerate and inspect the diff**

Run:

```bash
dotnet run --project tools/MassiveDotNet.CodeGen
git status --short src/
git diff src/
```

Expected: exactly one modified file, `src/MassiveDotNet.Rest/Generated/MassiveRestJsonContext.g.cs`, gaining the `using MassiveDotNet.Serialization;` line, the `<remarks>`, and the `JsonSourceGenerationOptions` attribute. `StocksGroup.g.cs`, `Agg.g.cs`, and `Envelopes.g.cs` are byte-identical to before: the aggregates operation has no comparators and no dates, and its map keys all exist.

- [ ] **Step 6: Build, run the suite, and confirm idempotency**

Run:

```bash
dotnet build MassiveDotNet.slnx
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code --stat -- src/MassiveDotNet.Rest/Generated/
```

Expected: warning-free build, all tests pass, and the second regeneration reports no diff against the first (the `--exit-code` check is against the working tree, which now already holds the regenerated file, so any output here means the generator is not deterministic).

- [ ] **Step 7: Commit**

```bash
git add tools/MassiveDotNet.CodeGen/Spec.cs tools/MassiveDotNet.CodeGen/TypeBinding.cs tools/MassiveDotNet.CodeGen/Argument.cs tools/MassiveDotNet.CodeGen/Emitter.cs src/MassiveDotNet.Rest/Generated/MassiveRestJsonContext.g.cs
git commit -m "feat: generate one filter parameter per comparator field

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 6: Prove it on `/stocks/v1/dividends`

**Files:**
- Modify: `specs/endpoints.map.json` (`models` and `endpoints`)
- Regenerate: `src/MassiveDotNet.Rest/Generated/` (new `Models/Dividend.g.cs`; `StocksGroup.g.cs`, `Envelopes.g.cs`, `MassiveRestJsonContext.g.cs` change)
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/StocksDividendsTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs:17`

**Interfaces:**
- Consumes: everything above.
- Produces: `public sealed partial record Dividend` in `MassiveDotNet.Rest.Models` with `Ticker` (`string?`), `ExDividendDate`/`DeclarationDate`/`RecordDate`/`PayDate` (`LocalDate?`), `CashAmount`/`SplitAdjustedCashAmount`/`HistoricalAdjustmentFactor` (`double?`), `Currency` (`string?`), `Frequency` (`long?`), `DistributionType` (`string`, required by the schema), `Id` (`string?`). On `StocksGroup`:

```csharp
public Task<MassivePage<Dividend>> ListDividendsAsync(
    Filter<string>? ticker = null,
    RangeFilter<LocalDate>? exDividendDate = null,
    RangeFilter<long>? frequency = null,
    SetFilter<string>? distributionType = null,
    int? limit = null,
    string? sort = null,
    CancellationToken cancellationToken = default)

public IAsyncEnumerable<Dividend> EnumerateDividendsAsync(/* same parameters */)
```

- [ ] **Step 1: Add the fixture**

In `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, add after `StocksAggregates`:

```csharp
    /// <summary>
    /// The documented sample for GET /stocks/v1/dividends, with one departure from the published
    /// text: the sample shows <c>"request_id": 1</c>, a number, while the envelope schema declares
    /// a string and every other endpoint returns one. The fixture uses the string so it matches the
    /// schema the SDK is generated from; a numeric id would fail deserialization, which is the
    /// sample's error rather than the service's.
    /// </summary>
    public const string StocksDividends = """
        {
          "request_id": "1",
          "results": [
            {
              "cash_amount": 0.26,
              "currency": "USD",
              "declaration_date": "2025-07-31",
              "distribution_type": "recurring",
              "ex_dividend_date": "2025-08-11",
              "frequency": 4,
              "historical_adjustment_factor": 0.997899,
              "id": "Ed2c9da60abda1e3f0e99a43f6465863c137b671e1f5cd3f833d1fcb4f4eb27fe",
              "pay_date": "2025-08-14",
              "record_date": "2025-08-11",
              "split_adjusted_cash_amount": 0.26,
              "ticker": "AAPL"
            }
          ],
          "status": "OK"
        }
        """;
```

- [ ] **Step 2: Write the failing endpoint tests**

Create `tests/MassiveDotNet.Rest.Tests/StocksDividendsTests.cs`:

```csharp
using System.Text.Json;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The generated filter surface, end to end: the first mapped operation with comparator fields,
/// and the first whose result carries calendar dates.
/// </summary>
public sealed class StocksDividendsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(StubHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task BuildsTheDocumentedRequestPathWithNoFilters()
    {
        StubHandler handler = new(Fixtures.StocksDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListDividendsAsync(cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/stocks/v1/dividends", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task RendersEqualityRangeAndSetFiltersInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.StocksDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListDividendsAsync(
                ticker: "AAPL",
                exDividendDate: RangeFilter.Between(new LocalDate(2025, 1, 1), new LocalDate(2025, 12, 31)),
                frequency: RangeFilter.Gte(4L),
                distributionType: SetFilter.AnyOf("recurring", "special"),
                limit: 50,
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/v1/dividends"
                + "?ticker=AAPL"
                + "&ex_dividend_date.gte=2025-01-01&ex_dividend_date.lte=2025-12-31"
                + "&frequency.gte=4"
                + "&distribution_type.any_of=recurring,special"
                + "&limit=50",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task AcceptsASetOnAFieldThatAlsoTakesARange()
    {
        StubHandler handler = new(Fixtures.StocksDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListDividendsAsync(ticker: SetFilter.AnyOf("AAPL", "MSFT"), cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/v1/dividends?ticker.any_of=AAPL,MSFT",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task AcceptsAHalfOpenDateWindow()
    {
        StubHandler handler = new(Fixtures.StocksDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListDividendsAsync(
                exDividendDate: RangeFilter.Gte(new LocalDate(2025, 1, 1)).Lt(new LocalDate(2025, 7, 1)),
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/v1/dividends?ex_dividend_date.gte=2025-01-01&ex_dividend_date.lt=2025-07-01",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.StocksDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Dividend> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListDividendsAsync(ticker: "AAPL", cancellationToken: Ct);
        }

        Dividend dividend = Assert.Single(page.Results);

        Assert.Equal("AAPL", dividend.Ticker);
        Assert.Equal(new LocalDate(2025, 8, 11), dividend.ExDividendDate);
        Assert.Equal(new LocalDate(2025, 7, 31), dividend.DeclarationDate);
        Assert.Equal(new LocalDate(2025, 8, 11), dividend.RecordDate);
        Assert.Equal(new LocalDate(2025, 8, 14), dividend.PayDate);
        Assert.Equal(0.26, dividend.CashAmount);
        Assert.Equal(0.26, dividend.SplitAdjustedCashAmount);
        Assert.Equal(0.997899, dividend.HistoricalAdjustmentFactor);
        Assert.Equal("USD", dividend.Currency);
        Assert.Equal(4L, dividend.Frequency);
        Assert.Equal("recurring", dividend.DistributionType);
        Assert.Equal("Ed2c9da60abda1e3f0e99a43f6465863c137b671e1f5cd3f833d1fcb4f4eb27fe", dividend.Id);
        Assert.False(page.HasMore);
        Assert.Equal("1", page.RequestId);
    }

    [Fact]
    public async Task SurfacesAMalformedDateAsAnApiException()
    {
        // The sample with its ex-dividend and record dates (both 2025-08-11) made non-ISO.
        string body = Fixtures.StocksDividends.Replace("\"2025-08-11\"", "\"2025-8-11\"", StringComparison.Ordinal);
        StubHandler handler = new(body);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Stocks.ListDividendsAsync(cancellationToken: Ct));

            Assert.IsType<JsonException>(exception.InnerException);
        }
    }

    [Fact]
    public async Task EnumerateWalksTheSinglePage()
    {
        StubHandler handler = new(Fixtures.StocksDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string?> tickers = [];

        using (client)
        using (transport)
        {
            await foreach (Dividend dividend in client.Stocks.EnumerateDividendsAsync(ticker: "AAPL", cancellationToken: Ct))
            {
                tickers.Add(dividend.Ticker);
            }
        }

        Assert.Equal("AAPL", Assert.Single(tickers));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~StocksDividendsTests"`
Expected: build FAILS with `CS1061: 'StocksGroup' does not contain a definition for 'ListDividendsAsync'`.

- [ ] **Step 4: Map the model and the endpoint**

In `specs/endpoints.map.json`, add to `models` after `Agg`:

```json
    "Dividend": {
      "summary": "A cash dividend distribution: its declaration, ex-dividend, record, and pay dates, the amount, and the factors that normalise historical prices for it.",
      "remarks": "Reference data, so a class rather than a struct (decision D4). Dates are calendar dates with no time or zone, hence <see cref=\"NodaTime.LocalDate\"/>.",
      "schema": { "operationId": "get_stocks_v1_dividends", "pointer": "results/items" },
      "properties": {
        "ticker":                       { "name": "Ticker" },
        "ex_dividend_date":             { "name": "ExDividendDate" },
        "declaration_date":             { "name": "DeclarationDate" },
        "record_date":                  { "name": "RecordDate" },
        "pay_date":                     { "name": "PayDate" },
        "cash_amount":                  { "name": "CashAmount" },
        "split_adjusted_cash_amount":   { "name": "SplitAdjustedCashAmount" },
        "historical_adjustment_factor": { "name": "HistoricalAdjustmentFactor" },
        "currency":                     { "name": "Currency" },
        "frequency":                    { "name": "Frequency" },
        "distribution_type":            { "name": "DistributionType" },
        "id":                           { "name": "Id" }
      }
    }
```

and to `endpoints` after the aggregates entry:

```json
    {
      "operationId": "get_stocks_v1_dividends",
      "group": "Stocks",
      "method": "ListDividends",
      "summary": "Retrieves cash dividend distributions for US stocks, with declaration, ex-dividend, record, and pay dates.",
      "remarks": "Every filter is optional and defaults to no constraint. Pass a plain value for equality, a <see cref=\"RangeFilter\"/> factory for a range, or <see cref=\"SetFilter\"/> for a set of values.",
      "result": { "kind": "array", "model": "Dividend", "property": "results" },
      "parameters": {
        "ticker":            { "name": "ticker" },
        "ex_dividend_date":  { "name": "exDividendDate", "type": "LocalDate" },
        "frequency":         { "name": "frequency" },
        "distribution_type": { "name": "distributionType" },
        "limit":             { "name": "limit" },
        "sort":              { "name": "sort" }
      }
    }
```

`ex_dividend_date` needs the override because the spec types the *parameter* as a plain `string` (its prose says `yyyy-mm-dd`); the *result* property is `format: date` and needs none. Everything else takes its element type from the schema: `ticker` is `string`, `frequency` is `int64`, `distribution_type` is an enum string.

- [ ] **Step 5: Regenerate and read the output**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`

Then read `src/MassiveDotNet.Rest/Generated/StocksGroup.g.cs` and confirm the `ListDividendsAsync` signature matches the one in this task's Interfaces block, that `using NodaTime;` is present, that `BuildListDividendsUri` contains exactly these query lines in this order:

```csharp
        builder.AppendQuery("ticker", ticker);
        builder.AppendQuery("ex_dividend_date", exDividendDate);
        builder.AppendQuery("frequency", frequency);
        builder.AppendQuery("distribution_type", distributionType);
        builder.AppendQuery("limit", limit);
        builder.AppendQuery("sort", sort);
```

and that the `<param name="ticker">` doc reads `Stock symbol for the company issuing the dividend. Accepts an exact value, a range, or a set of values.` Read `Models/Dividend.g.cs` and confirm the four dates are `LocalDate?` and `DistributionType` is `string`.

- [ ] **Step 6: Raise the coverage baseline**

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, change

```csharp
    private const int CoverageBaseline = 1;
```

to

```csharp
    private const int CoverageBaseline = 2;
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~StocksDividendsTests"`
Expected: 7 passed.

If `DeserializesThePublishedSample` fails on a `LocalDate?` property with a message about no converter for `Nullable<LocalDate>`, the nullable wrapper did not pick the registered converter up under source generation. The fallback that keeps D-F9's shape is to have the generator emit `[JsonConverter(typeof(LocalDateJsonConverter))]` on each `LocalDate` and `LocalDate?` property in `EmitModel` instead of the context-level registration; make that change in `Emitter.cs`, regenerate, and record the reason in the spec's D-F9.

- [ ] **Step 8: Run everything**

Run:

```bash
dotnet build MassiveDotNet.slnx
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code --stat -- src/
```

Expected: warning-free, all pass, no diff after a second regeneration.

- [ ] **Step 9: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated/ tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/StocksDividendsTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs
git commit -m "feat: map stocks dividends as the first endpoint with comparator filters

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 7: Root the filter path in the AOT smoke test

**Files:**
- Modify: `samples/MassiveDotNet.AotSmokeTest/Program.cs`

**Interfaces:**
- Consumes: `ListDividendsAsync` and `Dividend` (Task 6).
- Produces: nothing new. This task exists because an unreferenced generic instantiation is trimmed away, so without a call here the publish would say nothing about `AppendQuery<T>`, the `Unsafe.As` dispatch, or the converter.

- [ ] **Step 1: Teach the sample's stub to answer the dividends route**

In `samples/MassiveDotNet.AotSmokeTest/Program.cs`, inside `StubHandler`, add a third body after `FinalPage`:

```csharp
    private const string Dividends = """
        {
          "request_id": "1",
          "results": [
            {
              "cash_amount": 0.26,
              "currency": "USD",
              "declaration_date": "2025-07-31",
              "distribution_type": "recurring",
              "ex_dividend_date": "2025-08-11",
              "frequency": 4,
              "historical_adjustment_factor": 0.997899,
              "id": "Ed2c9da60abda1e3f0e99a43f6465863c137b671e1f5cd3f833d1fcb4f4eb27fe",
              "pay_date": "2025-08-14",
              "record_date": "2025-08-11",
              "split_adjusted_cash_amount": 0.26,
              "ticker": "AAPL"
            }
          ],
          "status": "OK"
        }
        """;
```

and change `SendAsync` so the route selects the body:

```csharp
        LastRequestUri = request.RequestUri;
        Requests++;

        string body;

        if (request.RequestUri?.AbsolutePath == "/stocks/v1/dividends")
        {
            body = Dividends;
        }
        else
        {
            // Keyed on the cursor rather than on a request counter, so the single-page call and the
            // traversal stay independent of the order they happen to run in.
            bool cursored = request.RequestUri?.Query.Contains("cursor=", StringComparison.Ordinal) == true;
            body = cursored ? FinalPage : FirstPage;
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
```

- [ ] **Step 2: Add the filtered call**

In the top-level statements, insert immediately before `Console.WriteLine($"\nrequests: {handler.Requests}");`:

```csharp
// Filters are rooted for the same reason as the traversal above. RequestUriBuilder.AppendQuery<T>
// dispatches on typeof(T) through Unsafe.As, and the LocalDate converter is reached only through
// the generated context: each is a generic instantiation a clean publish says nothing about
// unless something here calls it. This call covers string, LocalDate, and long elements, a range,
// a set, and a date on the way back in.
Console.WriteLine("\ndividends, filtered:");

MassivePage<Dividend> dividends = await client.Stocks.ListDividendsAsync(
    ticker: "AAPL",
    exDividendDate: RangeFilter.Between(new LocalDate(2025, 1, 1), new LocalDate(2025, 12, 31)),
    frequency: RangeFilter.Gte(4L),
    distributionType: SetFilter.AnyOf("recurring", "special"));

Console.WriteLine($"request : {handler.LastRequestUri}");

foreach (Dividend dividend in dividends.Results)
{
    Console.WriteLine($"  {dividend.Ticker}  ex {dividend.ExDividendDate}  {dividend.CashAmount:F2} {dividend.Currency}");
}

const string ExpectedFilterQuery =
    "?ticker=AAPL&ex_dividend_date.gte=2025-01-01&ex_dividend_date.lte=2025-12-31"
    + "&frequency.gte=4&distribution_type.any_of=recurring,special";

if (handler.LastRequestUri?.Query != ExpectedFilterQuery)
{
    Console.Error.WriteLine($"FAIL: expected the filter query {ExpectedFilterQuery}; got {handler.LastRequestUri?.Query}.");
    return 1;
}

if (dividends.Results.Length != 1 || dividends.Results[0].ExDividendDate != new LocalDate(2025, 8, 11))
{
    Console.Error.WriteLine("FAIL: expected one dividend with ex-dividend date 2025-08-11.");
    return 1;
}
```

Then update the request-count check that follows. Replace

```csharp
// Two pages of the enumeration plus the single-page call above.
if (enumerated != 3 || handler.Requests != 3)
{
    Console.Error.WriteLine(
        $"FAIL: expected 3 enumerated bars over 3 requests; got {enumerated} over {handler.Requests}.");
    return 1;
}
```

with

```csharp
// Two pages of the enumeration, the single-page aggregates call, and the dividends call.
if (enumerated != 3 || handler.Requests != 4)
{
    Console.Error.WriteLine(
        $"FAIL: expected 3 enumerated bars over 4 requests; got {enumerated} over {handler.Requests}.");
    return 1;
}
```

- [ ] **Step 3: Publish and run**

Run:

```bash
dotnet publish samples/MassiveDotNet.AotSmokeTest -r osx-arm64 -c Release 2>&1 | tee /tmp/aot.log | grep -E ': (warning|error) (IL|AOT|Trim)?[0-9]{4}' || echo "no IL warnings"
./samples/MassiveDotNet.AotSmokeTest/bin/Release/net10.0/osx-arm64/publish/MassiveDotNet.AotSmokeTest
```

Expected: `no IL warnings`; the program prints the filter request, one dividend line, and `AOT smoke test passed.`, exiting 0. On another machine substitute the runtime identifier (`linux-x64`, `win-x64`) in both commands; CI uses `linux-x64`.

- [ ] **Step 4: Commit**

```bash
git add samples/MassiveDotNet.AotSmokeTest/Program.cs
git commit -m "test: root the filter overloads and date converter in the AOT smoke test

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 8: Live confirmation, and record the decision

**Files:**
- Create: `tests/MassiveDotNet.IntegrationTests/StocksDividendsLiveTests.cs`
- Modify: `CLAUDE.md` (the decisions table after D14; the Conventions list after the Pagination bullet)

**Interfaces:**
- Consumes: `LiveApiTest`, `LiveCredentials` (existing); `ListDividendsAsync` (Task 6).
- Produces: nothing new.

- [ ] **Step 1: Write the live test**

Create `tests/MassiveDotNet.IntegrationTests/StocksDividendsLiveTests.cs`:

```csharp
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Exercises the filter surface against the live service.
/// </summary>
/// <remarks>
/// A fixture asserts the SDK produces the documented query string; it cannot say whether the
/// service accepts a literal comma in <c>any_of</c> or treats <c>gte</c> and <c>lte</c> as
/// inclusive, neither of which the OpenAPI description states. This is the one test that can.
/// </remarks>
public sealed class StocksDividendsLiveTests : LiveApiTest
{
    // A fixed historical window, so the assertions do not depend on when the suite is run. Both
    // tickers paid at least one dividend inside it.
    private static readonly LocalDate WindowStart = new(2025, 1, 1);
    private static readonly LocalDate WindowEnd = new(2025, 6, 30);
    private static readonly string[] Tickers = ["AAPL", "MSFT"];

    [Fact]
    public async Task HonoursADateRangeAndATickerSet()
    {
        Dividend[] dividends = (await Client.Stocks.ListDividendsAsync(
            ticker: SetFilter.AnyOf(Tickers),
            exDividendDate: RangeFilter.Between(WindowStart, WindowEnd),
            limit: 100,
            cancellationToken: Ct)).Results;

        Assert.NotEmpty(dividends);

        foreach (Dividend dividend in dividends)
        {
            Assert.Contains(dividend.Ticker, Tickers);
            Assert.NotNull(dividend.ExDividendDate);
            Assert.InRange(dividend.ExDividendDate.Value, WindowStart, WindowEnd);
        }
    }
}
```

`Tickers` is a `static readonly` field rather than an inline array so the assertion inside the loop does not trip CA1861.

- [ ] **Step 2: Confirm it compiles and is excluded from the offline run**

Run:

```bash
dotnet build MassiveDotNet.slnx
dotnet test MassiveDotNet.slnx --filter "Category!=Integration" --list-tests | grep -c 'StocksDividendsLiveTests' || true
```

Expected: warning-free build; the count is `0`, so the offline filter excludes it exactly as CI's assertion step requires.

- [ ] **Step 3: Run it against the live service**

With a key in `MASSIVE_API_KEY` or in a gitignored `.env` at the repository root:

Run: `dotnet test tests/MassiveDotNet.IntegrationTests --filter "FullyQualifiedName~StocksDividendsLiveTests"`
Expected: 1 passed. Without a key it skips with the reason naming the variable, and that is what to report.

If it fails on `NotEmpty`, the service rejected the comma-joined `any_of` or the inclusive bounds. Capture the response with the same query through `curl`, and stop: that finding changes D-F5's encoding rule and belongs in front of the user before anything else is committed.

- [ ] **Step 4: Record D15 and the Filters convention in `CLAUDE.md`**

In the Architecture decisions table, add this row directly after D14:

```markdown
| D15 | Comparator variants (`.gt` `.gte` `.lt` `.lte` `.any_of` `.all_of`) collapse to **one filter-typed parameter per field**: `RangeFilter<T>`, `SetFilter<T>`, `Filter<T>`, or `ArrayFilter<T>`, chosen by the generator from the exact suffix set the spec declares. Equality is the implicit conversion from `T`. Rendering lives in `RequestUriBuilder`, not in generated code. | 1,182 flat parameters gave one endpoint a 114-argument method. The field is the unit the platform documents; typing it by capability makes an unsupported comparator a compile error rather than a silently dropped parameter. Grouping is read from the spec so it cannot drift, and an unrecognised suffix set fails generation instead of guessing. One rendering implementation is tested once rather than in 93 generated files. Element types are a closed set (`string`, `int`, `long`, `double`, `LocalDate`, `DateOrTimestamp`); `Instant` is excluded because it carries no wire precision. |
```

In the Conventions list, add this bullet directly after the Pagination bullet:

```markdown
- **Filters**: a field that carries comparator variants becomes one optional parameter typed
  `RangeFilter<T>`, `SetFilter<T>`, `Filter<T>`, or `ArrayFilter<T>` by its suffix set; a plain
  `T` converts implicitly to equality. The map names the **element** type on the base field's row
  (`"ex_dividend_date": { "type": "LocalDate" }`), never the filter type, and never a row keyed by
  a variant. Grouping is detected from the spec, never declared in the map (D15). Calendar dates
  (`format: date`) are `LocalDate` on parameters and models alike, read by
  `LocalDateJsonConverter`.
```

- [ ] **Step 5: Final verification, all four gates**

Run:

```bash
dotnet build MassiveDotNet.slnx
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/
dotnet publish samples/MassiveDotNet.AotSmokeTest -r osx-arm64 -c Release 2>&1 | grep -E ': (warning|error) (IL|AOT|Trim)?[0-9]{4}' || echo "no IL warnings"
```

Expected: warning-free build, all offline tests pass, no generated diff, `no IL warnings`.

- [ ] **Step 6: Commit**

```bash
git add tests/MassiveDotNet.IntegrationTests/StocksDividendsLiveTests.cs CLAUDE.md
git commit -m "feat: add live filter test and record decision D15

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```
