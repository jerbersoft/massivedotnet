# Singular Results Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Teach the generator the fifty operations whose payload is not an array under `results`: one object under a property, paginated or not, or a body that is itself the object or the array; prove each path on one endpoint.

**Architecture:** The endpoint `result` row gains a second kind, `object`, and an optional `property`; a model row gains `items` and may omit `pointer`. `Emitter` resolves every endpoint to a `ResultShape` and emits from it: a `MassivePage<T>` or `T[]` for arrays as today, a new core `MassivePagedResult<T>` for a paginated object whose model names `items`, a guarded `T` for a paginated object without them, and a plain `T` or `T[]` deserialized straight from the body when there is no envelope. `ValidateResultReuse` resolves the site by kind and location. Every singular `Get` returns `T` and throws `MassiveApiException` on a 200 without its payload.

**Tech Stack:** .NET 10, C# latest, xUnit v3, System.Text.Json source generation, NodaTime 3.3.3. No new package dependencies.

**Spec:** `docs/superpowers/specs/2026-09-02-singular-results-design.md`

## Global Constraints

Copied from `CLAUDE.md` and the spec. Every task inherits these.

- **Rule 3** — No reflection-based serialization in shipped code. `System.Text.Json` source generation only. Body payloads register their model, or `Model[]`, on `MassiveRestJsonContext`; nothing reflects.
- **Rule 5** — `*.g.cs` files are never hand-edited. Change `tools/MassiveDotNet.CodeGen` and regenerate with `dotnet run --project tools/MassiveDotNet.CodeGen`.
- **Rule 6** — The generator is deterministic. Emission decisions use order-independent tests (`Exists`, `Any`); registered context types keep map order and deduplicate.
- **Rule 7** — `MassiveDotNet` (core) references no external package other than NodaTime.
- **Rule 9** — `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are on. An **unused `using` fails the build** (IDE0005). `AnalysisLevel` is `latest-recommended`: **CA1305** (pass a format provider), **CA1307/CA1310** (pass a `StringComparison` to `Contains`, `StartsWith`, `IndexOf` on strings), **CA1861** (hoist constant arrays to `static readonly`), and the naming rules apply to test code too.
- **Rule 10** — Every public member carries XML documentation, or CS1591 fails the build. Internal members are documented too, by convention.
- **Rule 11** — API keys are never logged, echoed in exception messages, or written to disk. The live tests read the key from the gitignored `.env`; never print it.
- **Rule 12** — NodaTime only. No BCL `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, or `TimeSpan` may be *named* anywhere in `src`, `tests`, `samples`, or `tools`. `TemporalTypeTests` scans every one of those directories. The temporal types this plan touches are `Instant`, `LocalDate`, and `Duration`.
- **Rule 13** — CI runs offline only. The live tests in Task 5 derive from `LiveApiTest`, which carries `[Trait("Category", "Integration")]`, and live in `tests/MassiveDotNet.IntegrationTests`. No offline test class may carry `LiveTests` in its name.
- **Spec D-S1** — `kind` is `array` or `object`; any other value fails generation. An omitted `property` means the body is the payload. What each combination returns is the D-S1 table, reproduced in Task 3's `ResultShape.ReturnType`.
- **Spec D-S2** — A paginated object result returns `MassivePagedResult<T>`; its model row names `items`; the envelope implements `IPagedEnvelope<TItem>` explicitly through `Results?.Items`; `Enumerate` yields `TItem`. The `List` prefix rule in `Naming.Enumerate` applies.
- **Spec D-S3** — Pagination stays spec-detected. A paginated object result whose model has no `items` emits a plain `Get` and calls `MassiveHttpTransport.ThrowIfUnfollowableCursor(response?.NextUrl, requestUri, response?.RequestId)`.
- **Spec D-S4** — Every singular method returns `Task<T>`, never `Task<T?>`. A 200 without its payload throws `MassiveApiException` with `HttpStatusCode.OK`, a message naming the request URI, and the envelope's request id when it has one.
- **Spec D-S5** — Body payloads emit no envelope. The model, or `T[]`, is registered on the context. A null body object throws as in D-S4 with no request id; a null body array coalesces to `[]`.
- **Spec D-S6** — `ValidateResultReuse` resolves the site by kind and location: `object` checks `{property}` or the root, `array` checks `{property}/items` or `items`. Refusals: kind versus site shape; `items` absent, not an array of objects, or its row lacking `model`; unknown kind. A body model reused as a nested property is allowed.
- **Spec D-S7** — `SeriesType` is a core enum with `ToWireValue`, registered in `TypeBinding`'s enum list. `timespan` on indicators reuses `AggregateTimespan`.
- **Style** — Explicit types, never `var`; collection expressions (`[]`, `[.. x]`); `is not { } x` null patterns; file-scoped namespaces; raw string literals for JSON. Match the surrounding code. Generated comments explain *why*.
- **Convention** — Do not commit or push unless asked. Steps below include commits; the user chose subagent-driven execution, which authorizes them on the feature branch. Commit messages end with the trailer `Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB`.

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
| `src/MassiveDotNet/MassivePagedResult.cs` | **Create.** One page of a paginated singular result: `Result`, `HasMore`, `RequestId`. | 1 |
| `src/MassiveDotNet/SeriesType.cs` | **Create.** The price series an indicator is computed over. | 1 |
| `src/MassiveDotNet/MassiveEnumValues.cs` | **Modify.** `SeriesType.ToWireValue`. | 1 |
| `src/MassiveDotNet/Http/MassiveHttpTransport.cs` | **Modify.** `ThrowIfUnfollowableCursor`. | 1 |
| `tests/MassiveDotNet.Rest.Tests/MassivePagedResultTests.cs` | **Create.** The struct's contract. | 1 |
| `tests/MassiveDotNet.Rest.Tests/SeriesTypeTests.cs` | **Create.** Wire values. | 1 |
| `tests/MassiveDotNet.Rest.Tests/UnfollowableCursorTests.cs` | **Create.** The guard, directly. | 1 |
| `tools/MassiveDotNet.CodeGen/Map.cs` | **Modify.** `MapResult.Property` nullable; `MapModel.Items`; optional `pointer`. | 2 |
| `tools/MassiveDotNet.CodeGen/TypeBinding.cs` | **Modify.** `SeriesType` in the enum list. | 2 |
| `tests/MassiveDotNet.CodeGen.Tests/HarnessTests.cs` | **Modify.** Map parsing of the new keys. | 2 |
| `tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs` | **Modify.** `SeriesType` renders through `ToWireValue`. | 2 |
| `tools/MassiveDotNet.CodeGen/Emitter.cs` | **Modify.** `ResultShape`; `Shape`; `ItemBinding`; `ValidateResultReuse` by kind and location; envelopes, endpoints, context for every shape; `EnvelopeType` message. | 3 |
| `tests/MassiveDotNet.CodeGen.Tests/SingularResultTests.cs` | **Create.** One generated-shape test per D-S1 row; one refusal test per D-S6 rule. | 3 |
| `tests/MassiveDotNet.CodeGen.Tests/ModelBindingTests.cs` | **Modify.** The envelope-object refusal now says to declare the body. | 3 |
| `specs/endpoints.map.json` | **Modify.** Six models, four endpoints. | 4 |
| `src/MassiveDotNet.Rest/Models/IndicatorValue.cs` | **Create.** `Timestamp` computed from milliseconds. | 4 |
| `src/MassiveDotNet.Rest/Models/LastTrade.cs` | **Create.** Three `Instant`s computed from nanoseconds. | 4 |
| `src/MassiveDotNet.Rest/Generated/` | **Regenerate.** Never hand-edit. | 3, 4 |
| `tests/MassiveDotNet.Rest.Tests/Fixtures.cs` | **Modify.** Four published samples and a scripted SMA last page. | 4 |
| `tests/MassiveDotNet.Rest.Tests/StocksIndicatorsTests.cs` | **Create.** SMA: request, page, traversal. | 4 |
| `tests/MassiveDotNet.Rest.Tests/StocksLastTradeTests.cs` | **Create.** Last trade: nanosecond timestamps, missing payload throws. | 4 |
| `tests/MassiveDotNet.Rest.Tests/StocksOpenCloseTests.cs` | **Create.** Body object. | 4 |
| `tests/MassiveDotNet.Rest.Tests/ReferenceMarketHolidaysTests.cs` | **Create.** Body array. | 4 |
| `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` | **Modify.** `CoverageBaseline` 3 → 7. | 4 |
| `samples/MassiveDotNet.AotSmokeTest/Program.cs` | **Modify.** One call per new shape. | 5 |
| `tests/MassiveDotNet.IntegrationTests/StocksIndicatorsLiveTests.cs` | **Create.** SMA traversal across a real page boundary. | 5 |
| `tests/MassiveDotNet.IntegrationTests/StocksOpenCloseLiveTests.cs` | **Create.** One open/close call. | 5 |
| `CLAUDE.md` | **Modify.** D17, the Pagination and Models bullets, Adding endpoints. | 5 |

---

### Task 1: Core types: `MassivePagedResult<T>`, `SeriesType`, `ThrowIfUnfollowableCursor`

**Files:**
- Create: `src/MassiveDotNet/MassivePagedResult.cs`
- Create: `src/MassiveDotNet/SeriesType.cs`
- Modify: `src/MassiveDotNet/MassiveEnumValues.cs`
- Modify: `src/MassiveDotNet/Http/MassiveHttpTransport.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/MassivePagedResultTests.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/SeriesTypeTests.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/UnfollowableCursorTests.cs`

**Interfaces:**
- Consumes: `MassivePage<T>` as the pattern; `MassiveApiException(HttpStatusCode, string, string?)`.
- Produces: `public readonly record struct MassivePagedResult<T>` in namespace `MassiveDotNet` with constructor `(T result, bool hasMore, string? requestId)` and members `T Result`, `bool HasMore`, `string? RequestId`; `public enum SeriesType { Open, High, Low, Close }` with `ToWireValue()` returning `"open"`, `"high"`, `"low"`, `"close"`; `public static void MassiveHttpTransport.ThrowIfUnfollowableCursor(string? nextUrl, string requestUri, string? requestId)`. Task 3 emits calls to all three; Task 4 maps `series_type` to `SeriesType`.

- [ ] **Step 1: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/MassivePagedResultTests.cs`:

```csharp
using Xunit;

namespace MassiveDotNet.Rest.Tests;

public sealed class MassivePagedResultTests
{
    private sealed record Series(int[] Values);

    [Fact]
    public void ExposesTheResultItWasGiven()
    {
        Series series = new([1, 2, 3]);

        MassivePagedResult<Series> page = new(series, hasMore: true, requestId: "abc");

        Assert.Same(series, page.Result);
        Assert.True(page.HasMore);
        Assert.Equal("abc", page.RequestId);
    }

    [Fact]
    public void ReportsNoRequestIdWhenTheEndpointSendsNone()
    {
        MassivePagedResult<Series> page = new(new Series([]), hasMore: false, requestId: null);

        Assert.False(page.HasMore);
        Assert.Null(page.RequestId);
    }

    [Fact]
    public void RefusesANullResult()
    {
        // The generated Send method throws MassiveApiException before constructing a page whose
        // payload is missing (D-S4), so this guard is what keeps Result's "never null" promise from
        // depending on every caller remembering that.
        Assert.Throws<ArgumentNullException>(() => new MassivePagedResult<Series>(null!, hasMore: false, requestId: null));
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/SeriesTypeTests.cs`:

```csharp
using Xunit;

namespace MassiveDotNet.Rest.Tests;

public sealed class SeriesTypeTests
{
    [Theory]
    [InlineData(SeriesType.Open, "open")]
    [InlineData(SeriesType.High, "high")]
    [InlineData(SeriesType.Low, "low")]
    [InlineData(SeriesType.Close, "close")]
    public void RendersTheWireLiteral(SeriesType value, string expected)
    {
        Assert.Equal(expected, value.ToWireValue());
    }

    [Fact]
    public void RefusesAnUndefinedMember()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((SeriesType)42).ToWireValue());
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/UnfollowableCursorTests.cs`:

```csharp
using System.Net;
using MassiveDotNet.Http;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The guard a generated <c>Get</c> calls when its operation's schema declares <c>next_url</c> but
/// its result is one object that cannot be paged (D-S3). Tested directly: the generated line
/// carries no logic of its own.
/// </summary>
public sealed class UnfollowableCursorTests
{
    private const string RequestUri = "/v3/snapshot/options/AAPL/O:AAPL230616C00150000";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PassesABlankCursor(string? nextUrl)
    {
        // A blank next_url is the absence it means, exactly as EnumerateAsync treats it (D-P5).
        MassiveHttpTransport.ThrowIfUnfollowableCursor(nextUrl, RequestUri, requestId: "r");
    }

    [Fact]
    public void ThrowsForARealCursorNamingTheRequestAndCarryingTheRequestId()
    {
        MassiveApiException exception = Assert.Throws<MassiveApiException>(() =>
            MassiveHttpTransport.ThrowIfUnfollowableCursor(
                "https://api.massive.com/v3/snapshot/options/AAPL/O:AAPL230616C00150000?cursor=abc",
                RequestUri,
                requestId: "r"));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("r", exception.RequestId);
        Assert.Contains(RequestUri, exception.Message, StringComparison.Ordinal);
        Assert.Contains("next_url", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThrowsWithoutARequestIdWhenTheEnvelopeHasNone()
    {
        MassiveApiException exception = Assert.Throws<MassiveApiException>(() =>
            MassiveHttpTransport.ThrowIfUnfollowableCursor("https://api.massive.com/x?cursor=abc", RequestUri, requestId: null));

        Assert.Null(exception.RequestId);
    }

    [Fact]
    public void RequiresARequestUriToName()
    {
        Assert.Throws<ArgumentException>(() =>
            MassiveHttpTransport.ThrowIfUnfollowableCursor("https://api.massive.com/x?cursor=abc", " ", requestId: null));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~MassivePagedResultTests|FullyQualifiedName~SeriesTypeTests|FullyQualifiedName~UnfollowableCursorTests"`
Expected: build FAILS with `CS0246: The type or namespace name 'MassivePagedResult<>' could not be found`, `CS0246` for `SeriesType`, and `CS0117: 'MassiveHttpTransport' does not contain a definition for 'ThrowIfUnfollowableCursor'`.

- [ ] **Step 3: Create `MassivePagedResult<T>`**

Create `src/MassiveDotNet/MassivePagedResult.cs`:

```csharp
namespace MassiveDotNet;

/// <summary>
/// One page of a paginated Massive endpoint whose page is a single object rather than an array,
/// such as a technical indicator whose values continue across pages while each page carries its
/// own underlying aggregates.
/// </summary>
/// <typeparam name="T">The result object type.</typeparam>
/// <remarks>
/// <para>
/// The sibling of <see cref="MassivePage{T}"/> with <typeparamref name="T"/> in place of
/// <c>T[]</c>. Returned by the <c>List</c> methods of such endpoints; the matching <c>Enumerate</c>
/// method walks every page and yields the items inside each result object as one flat sequence,
/// which is why only <c>List</c> can show the rest of the object.
/// </para>
/// <para>
/// The cursor itself is deliberately not exposed, for the reason <see cref="MassivePage{T}"/>
/// gives: there is no public API that accepts one back.
/// </para>
/// <para>
/// Equality is the compiler-synthesized record equality, which compares <see cref="Result"/> with
/// its own equality. A <see langword="default"/> instance, which any struct permits, has a
/// <see langword="null"/> <see cref="Result"/>; the SDK never constructs one, and the constructor
/// refuses a null result so that the only way to obtain one is to ask for <see langword="default"/>.
/// </para>
/// </remarks>
public readonly record struct MassivePagedResult<T>
{
    /// <summary>Initializes a new instance of the <see cref="MassivePagedResult{T}"/> struct.</summary>
    /// <param name="result">The page's result object.</param>
    /// <param name="hasMore">Whether the server offered a cursor to a further page.</param>
    /// <param name="requestId">The server-assigned request identifier, when present.</param>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    public MassivePagedResult(T result, bool hasMore, string? requestId)
    {
        ArgumentNullException.ThrowIfNull(result);

        Result = result;
        HasMore = hasMore;
        RequestId = requestId;
    }

    /// <summary>The result object in this page. Never <see langword="null"/> when constructed through the SDK.</summary>
    public T Result { get; }

    /// <summary>
    /// Whether more pages exist. When <see langword="true"/>, the matching <c>Enumerate</c> method
    /// will retrieve the remainder.
    /// </summary>
    public bool HasMore { get; }

    /// <summary>
    /// The server-assigned request identifier. Include this when contacting Massive support.
    /// <see langword="null"/> when the endpoint does not return one.
    /// </summary>
    public string? RequestId { get; }
}
```

- [ ] **Step 4: Create `SeriesType` and its wire value**

Create `src/MassiveDotNet/SeriesType.cs`:

```csharp
namespace MassiveDotNet;

/// <summary>
/// The price in each aggregate that a technical indicator is calculated over.
/// </summary>
public enum SeriesType
{
    /// <summary>The open price of each aggregate.</summary>
    Open = 0,

    /// <summary>The high price of each aggregate.</summary>
    High = 1,

    /// <summary>The low price of each aggregate.</summary>
    Low = 2,

    /// <summary>The close price of each aggregate.</summary>
    Close = 3,
}
```

In `src/MassiveDotNet/MassiveEnumValues.cs`, after the `MarketType` overload and before the `LocalDate` one, add:

```csharp
    /// <summary>Returns the wire representation of a <see cref="SeriesType"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The literal accepted by the API, for example <c>"close"</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined enum member.</exception>
    public static string ToWireValue(this SeriesType value) => value switch
    {
        SeriesType.Open => "open",
        SeriesType.High => "high",
        SeriesType.Low => "low",
        SeriesType.Close => "close",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
```

- [ ] **Step 5: Add the guard to the transport**

In `src/MassiveDotNet/Http/MassiveHttpTransport.cs`, after `EnumerateAsync` and before `EnumerateCoreAsync`, add:

```csharp
    /// <summary>
    /// Throws when a response offered a pagination cursor that this SDK cannot follow, because the
    /// operation's result is a single object rather than a page of items.
    /// </summary>
    /// <param name="nextUrl">The response's <c>next_url</c>, or <see langword="null"/> when it sent none.</param>
    /// <param name="requestUri">The request that produced the response, named in the exception.</param>
    /// <param name="requestId">The response's request identifier, carried by the exception when present.</param>
    /// <remarks>
    /// Called by generated code for the operations whose OpenAPI success schema declares
    /// <c>next_url</c> on a result that is one object (decision D17). A blank cursor is the absence
    /// it means, as it is everywhere else in this transport. A real one is a page the caller will
    /// never receive, and missing data is reported loudly in this SDK rather than dropped.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="requestUri"/> is empty or whitespace.</exception>
    /// <exception cref="MassiveApiException"><paramref name="nextUrl"/> is a cursor.</exception>
    public static void ThrowIfUnfollowableCursor(string? nextUrl, string requestUri, string? requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);

        if (string.IsNullOrWhiteSpace(nextUrl))
        {
            return;
        }

        throw new MassiveApiException(
            HttpStatusCode.OK,
            $"The response from '{requestUri}' offered a 'next_url' cursor, but this operation returns a "
            + "single object that cannot be paged, so the further page was not retrieved.",
            requestId);
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~MassivePagedResultTests|FullyQualifiedName~SeriesTypeTests|FullyQualifiedName~UnfollowableCursorTests"`
Expected: PASS, 10 tests. Then run the whole offline suite for the Rest tests, `dotnet test tests/MassiveDotNet.Rest.Tests`, and confirm `TemporalTypeTests` still passes: the new struct and enum are on the exported surface it reflects over.

- [ ] **Step 7: Commit**

```bash
git add src/MassiveDotNet/MassivePagedResult.cs src/MassiveDotNet/SeriesType.cs src/MassiveDotNet/MassiveEnumValues.cs src/MassiveDotNet/Http/MassiveHttpTransport.cs tests/MassiveDotNet.Rest.Tests/MassivePagedResultTests.cs tests/MassiveDotNet.Rest.Tests/SeriesTypeTests.cs tests/MassiveDotNet.Rest.Tests/UnfollowableCursorTests.cs
git commit -m "feat: add MassivePagedResult, SeriesType, and the unfollowable-cursor guard

MassivePagedResult<T> is MassivePage<T>'s sibling for a paginated
endpoint whose page is one object. SeriesType names the price series
an indicator is computed over. ThrowIfUnfollowableCursor is the one
static guard generated code will call where a schema declares next_url
on a result that cannot be paged (D-S2, D-S3, D-S7).

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 2: Map: nullable `property`, `items`, optional `pointer`; `SeriesType` binding

**Files:**
- Modify: `tools/MassiveDotNet.CodeGen/Map.cs`
- Modify: `tools/MassiveDotNet.CodeGen/TypeBinding.cs:37`
- Modify: `tests/MassiveDotNet.CodeGen.Tests/HarnessTests.cs`
- Modify: `tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs`

**Interfaces:**
- Consumes: `Map.Parse(string)`, `MapResult`, `MapModel`, `TypeBinding.Resolve`.
- Produces: `MapResult(string Kind, string Model, string? Property)`; `MapModel` gains `string? Items` as its last constructor parameter; a model row without `schema.pointer` parses with `SchemaPointer == ""`; a parameter row typed `SeriesType` renders `identifier?.ToWireValue()` in the query. Task 3 reads `Result.Property` and `Items`; Task 4 writes rows that use all three.

- [ ] **Step 1: Write the failing tests**

Append to the `HarnessTests` class in `tests/MassiveDotNet.CodeGen.Tests/HarnessTests.cs`, after `ReadsAModelReferenceFromAPropertyRow`:

```csharp
    [Fact]
    public void ReadsAnOmittedPropertyAsTheBody()
    {
        Map map = Map.Parse(Harness.MapDocument(
            """
            "Thing": { "schema": { "operationId": "GetThing" } }
            """,
            """
            {
              "operationId": "GetThing",
              "group": "Reference",
              "method": "GetThing",
              "result": { "kind": "object", "model": "Thing" }
            }
            """));

        Assert.Null(map.Endpoints[0].Result.Property);
        Assert.Equal("object", map.Endpoints[0].Result.Kind);

        // An omitted pointer is the success schema's root, which Spec.Navigate reads as "" (D-S1).
        Assert.Equal("", map.Models[0].SchemaPointer);
        Assert.Null(map.Models[0].Items);
    }

    [Fact]
    public void ReadsItemsFromAModelRow()
    {
        Map map = Map.Parse(Harness.MapDocument(
            """
            "Series": {
              "schema": { "operationId": "ListSeries", "pointer": "results" },
              "items": "values",
              "properties": { "values": { "name": "Values", "model": "Value" } }
            }
            """,
            Harness.Endpoint("ListSeries", "Series")));

        Assert.Equal("values", map.Models[0].Items);
        Assert.Equal("results", map.Endpoints[0].Result.Property);
    }
```

Append to `ParameterBindingTests` in `tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs`:

```csharp
    [Fact]
    public void ASeriesTypeParameterRendersItsWireValue()
    {
        string spec = Document("""[ { "name": "series_type", "in": "query", "schema": { "type": "string", "enum": ["open", "high", "low", "close"] } } ]""");

        Dictionary<string, string> files = Harness.Generate(spec, MapDocument("""{ "series_type": { "name": "seriesType", "type": "SeriesType" } }"""));

        string group = files["ReferenceGroup.g.cs"];
        Assert.Contains("SeriesType? seriesType = null", group, StringComparison.Ordinal);
        Assert.Contains("builder.AppendQuery(\"series_type\", seriesType?.ToWireValue());", group, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests --filter "FullyQualifiedName~HarnessTests|FullyQualifiedName~ParameterBindingTests"`
Expected: `ReadsAnOmittedPropertyAsTheBody` FAILS with `KeyNotFoundException` (from `result.GetProperty("property")` or `schema.GetProperty("pointer")`); `ReadsItemsFromAModelRow` FAILS to compile with `CS1061: 'MapModel' does not contain a definition for 'Items'`; `ASeriesTypeParameterRendersItsWireValue` FAILS because the generated line is `builder.AppendQuery("series_type", seriesType);` with no conversion. Fix the compile error first by doing Step 3, then confirm the two runtime failures before Step 4 if you want to see them; either order is acceptable.

- [ ] **Step 3: Extend the map records and parser**

In `tools/MassiveDotNet.CodeGen/Map.cs`, replace the `MapModel` record with:

```csharp
/// <summary>A model row: the schema it is generated from, its properties, and, for the page object of a paginated singular result, the property that carries the page's items (D-S2).</summary>
/// <param name="SchemaPointer">The path from the success schema root, or empty for the root itself, which a body payload binds to (D-S1).</param>
/// <param name="Items">The wire name of the array property whose elements <c>Enumerate</c> yields, or <see langword="null"/>.</param>
internal sealed record MapModel(
    string Name,
    string Kind,
    string? Summary,
    string? Remarks,
    string SchemaOperationId,
    string SchemaPointer,
    Dictionary<string, MapProperty> Properties,
    string? Items);
```

Replace the `MapResult` record with:

```csharp
/// <summary>An endpoint's payload: one model or an array of it, on a named envelope property or as the body itself (D-S1).</summary>
/// <param name="Kind"><c>array</c> or <c>object</c>.</param>
/// <param name="Property">The envelope property that holds the payload, or <see langword="null"/> when the body is the payload.</param>
internal sealed record MapResult(string Kind, string Model, string? Property);
```

In `Map.Parse`, change the model construction to pass the optional pointer and `items`:

```csharp
            models.Add(new MapModel(
                model.Name,
                String(model.Value, "kind") ?? "class",
                String(model.Value, "summary"),
                String(model.Value, "remarks"),
                schema.GetProperty("operationId").GetString()!,
                // An omitted pointer is the success schema root, which Spec.Navigate reads as an
                // empty path: the model is the response body itself (D-S1).
                String(schema, "pointer") ?? string.Empty,
                properties,
                String(model.Value, "items")));
```

and the endpoint's result construction to read the property optionally:

```csharp
                new MapResult(
                    result.GetProperty("kind").GetString()!,
                    result.GetProperty("model").GetString()!,
                    String(result, "property")),
```

- [ ] **Step 4: Register `SeriesType` as an enum binding**

In `tools/MassiveDotNet.CodeGen/TypeBinding.cs`, change the enum arm of `Resolve`:

```csharp
            // Enum wire values are fixed literals, so they need no percent-escaping.
            "AggregateTimespan" or "SortOrder" or "MarketType" or "SeriesType" =>
                new TypeBinding(type, "AppendPathLiteral", "ToWireValue()"),
```

- [ ] **Step 5: Build the generator and fix the one compile error the nullable property introduces**

Run: `dotnet build tools/MassiveDotNet.CodeGen`
Expected: FAILS in `Emitter.cs` where `endpoint.Result.Property` is passed to `Naming.Pascal(string)` (CS8604, possible null reference argument) and interpolated into `$"{endpoint.Result.Property}/items"`. Task 3 rewrites those call sites. For this task, make the minimal change that keeps the existing behaviour for `array` rows and refuses what Task 3 will handle: at the top of `Emitter.EmitEnvelopes`'s per-endpoint lambda, before `ValidateResultReuse(endpoint, operation);`, add

```csharp
                // A body payload has no envelope; Task 3 of the singular-results plan emits it.
                // Until then, refuse rather than emit an envelope with a null property name.
                if (endpoint.Result.Property is null)
                {
                    throw new InvalidOperationException(
                        $"Endpoint '{endpoint.Method}' (operation '{endpoint.OperationId}'): a result with no "
                        + "\"property\" is not yet supported by the emitter.");
                }
```

and add `!` to the two reads the compiler rejects, both calls of `Naming.Pascal(endpoint.Result.Property)`: the `resultsProperty` local in `EmitEnvelopes` and the `Send` body in `EmitEndpoint`. The string interpolations of `endpoint.Result.Property` compile as they are. Task 3 removes both `!` again; this task only has to compile and leave `git diff --exit-code src/` clean after regeneration.

- [ ] **Step 6: Run the tests to verify they pass, and regenerate**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests`
Expected: PASS, every test including the three new ones.

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: regeneration prints the same file list as before and the diff is empty. The three mapped endpoints all declare `property`, so nothing they emit changes.

- [ ] **Step 7: Commit**

```bash
git add tools/MassiveDotNet.CodeGen/Map.cs tools/MassiveDotNet.CodeGen/TypeBinding.cs tools/MassiveDotNet.CodeGen/Emitter.cs tests/MassiveDotNet.CodeGen.Tests/HarnessTests.cs tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs
git commit -m "feat: read the result kind, an optional property, items, and an optional pointer from the map

A result row may omit property, meaning the body is the payload; a
model row may omit pointer, meaning the success schema root, and may
name items, the array a paginated object enumerates (D-S1, D-S2).
SeriesType joins the enum bindings (D-S7). The emitter still refuses a
body payload until the next task teaches it the singular shapes.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 3: Emitter: every result shape in the D-S1 table, verified by kind and location

**Files:**
- Modify: `tools/MassiveDotNet.CodeGen/Emitter.cs`
- Create: `tests/MassiveDotNet.CodeGen.Tests/SingularResultTests.cs`
- Modify: `tests/MassiveDotNet.CodeGen.Tests/ModelBindingTests.cs:142-159`
- Regenerate: `src/MassiveDotNet.Rest/Generated/` (expected: no diff)

**Interfaces:**
- Consumes: `MapResult.Property` (nullable), `MapModel.Items`, `MapModel.SchemaPointer` possibly empty (Task 2); `MassivePagedResult<T>`, `MassiveHttpTransport.ThrowIfUnfollowableCursor` (Task 1); `Spec.Navigate`, `Spec.Shape`, `Spec.Describe`, `Spec.StructuralDifferences`, `Spec.IsPaginated`, `Naming.Enumerate`.
- Produces: generated code in these shapes, which Task 4's map rows rely on:
  - paginated object with `items`: `Task<MassivePagedResult<Model>> ListXAsync(...)`, `IAsyncEnumerable<Item> EnumerateXAsync(...)`, envelope `: IPagedEnvelope<Item>` with `Item[]? IPagedEnvelope<Item>.Results => Results?.Values;`
  - paginated object without `items`: `Task<Model> GetXAsync(...)` calling `MassiveHttpTransport.ThrowIfUnfollowableCursor(response?.NextUrl, requestUri, response?.RequestId);`
  - object: `Task<Model> GetXAsync(...)` throwing `MassiveApiException(HttpStatusCode.OK, ...)` when `response?.Results` is null
  - body object: `Task<Model>` deserialized with `MassiveRestJsonContext.Default.Model`, throwing on null
  - body array: `Task<Model[]>` deserialized with `MassiveRestJsonContext.Default.ModelArray`, coalescing null to `[]`
  - the group file gains `using System.Net;` when any endpoint can throw for a missing payload; `Envelopes.g.cs` is not emitted when no endpoint has an envelope.

- [ ] **Step 1: Write the failing tests**

Create `tests/MassiveDotNet.CodeGen.Tests/SingularResultTests.cs`:

```csharp
using Xunit;

namespace MassiveDotNet.CodeGen.Tests;

/// <summary>
/// One generated-shape test per row of the D-S1 table: what the envelope, the group methods, and
/// the serialization context look like for a paginated object with items, a paginated object
/// without them, a plain object, a body object, and a body array.
/// </summary>
public sealed class SingularResultTests
{
    /// <summary>A page object: values, which continue across pages, and an underlying that belongs to each page.</summary>
    private const string Series = """
        {
          "type": "object",
          "properties": {
            "values":     { "type": "array", "items": { "type": "object", "properties": { "timestamp": { "type": "integer", "format": "int64" }, "value": { "type": "number" } } } },
            "underlying": { "type": "object", "properties": { "url": { "type": "string" } } }
          }
        }
        """;

    private const string Trade = """
        { "type": "object", "required": ["p"], "properties": { "p": { "type": "number" }, "s": { "type": "number" } } }
        """;

    private const string SeriesModels = """
        "Series": {
          "schema": { "operationId": "ListSeries", "pointer": "results" },
          "items": "values",
          "properties": {
            "values":     { "name": "Values",     "model": "Value" },
            "underlying": { "name": "Underlying", "model": "Underlying" }
          }
        },
        "Value":      { "kind": "struct", "schema": { "operationId": "ListSeries", "pointer": "results/values/items" } },
        "Underlying": { "schema": { "operationId": "ListSeries", "pointer": "results/underlying" } }
        """;

    /// <summary>An envelope whose <c>results</c> is the given object, with or without a cursor.</summary>
    public static string ObjectEnvelope(string result, bool paginated, bool requestId = true) => $$"""
        {
          "type": "object",
          "required": ["status"],
          "properties": {
            {{(paginated ? "\"next_url\": { \"type\": \"string\" }," : "")}}
            {{(requestId ? "\"request_id\": { \"type\": \"string\" }," : "")}}
            "results": {{result}},
            "status": { "type": "string" }
          }
        }
        """;

    /// <summary>An endpoint row of any kind, with or without a payload property.</summary>
    public static string Endpoint(string operationId, string method, string kind, string model, string? property) => $$"""
        {
          "operationId": "{{operationId}}",
          "group": "Reference",
          "method": "{{method}}",
          "result": { "kind": "{{kind}}", "model": "{{model}}"{{(property is null ? "" : $", \"property\": \"{property}\"")}} }
        }
        """;

    private static string Group(Dictionary<string, string> files) => files["ReferenceGroup.g.cs"];

    [Fact]
    public void APaginatedObjectWithItemsReturnsAPagedResultAndEnumeratesItsItems()
    {
        string spec = Harness.Document(new Operation("ListSeries", "/v1/series", ObjectEnvelope(Series, paginated: true)));
        string map = Harness.MapDocument(SeriesModels, Endpoint("ListSeries", "ListSeries", "object", "Series", "results"));

        Dictionary<string, string> files = Harness.Generate(spec, map);

        string envelopes = files["Envelopes.g.cs"];
        Assert.Contains("using MassiveDotNet.Http;", envelopes, StringComparison.Ordinal);
        Assert.Contains("internal sealed class ListSeriesResponse : IPagedEnvelope<Value>", envelopes, StringComparison.Ordinal);
        Assert.Contains("public Series? Results { get; init; }", envelopes, StringComparison.Ordinal);
        Assert.Contains("Value[]? IPagedEnvelope<Value>.Results => Results?.Values;", envelopes, StringComparison.Ordinal);

        string group = Group(files);
        Assert.Contains("using System.Net;", group, StringComparison.Ordinal);
        Assert.Contains("public IAsyncEnumerable<Value> EnumerateSeriesAsync(", group, StringComparison.Ordinal);
        Assert.Contains("return _transport.EnumerateAsync<ListSeriesResponse, Value>(", group, StringComparison.Ordinal);
        Assert.Contains("public Task<MassivePagedResult<Series>> ListSeriesAsync(", group, StringComparison.Ordinal);
        Assert.Contains("Series result = response?.Results", group, StringComparison.Ordinal);
        Assert.Contains("$\"The response from '{requestUri}' carried no 'results' payload.\",", group, StringComparison.Ordinal);
        Assert.Contains("return new MassivePagedResult<Series>(", group, StringComparison.Ordinal);
        Assert.Contains("    !string.IsNullOrWhiteSpace(response.NextUrl),", group, StringComparison.Ordinal);
        Assert.Contains("    response.RequestId);", group, StringComparison.Ordinal);
        Assert.Contains("yields each page's <c>values</c> in turn", group, StringComparison.Ordinal);
        Assert.Contains("<returns>Every <c>values</c> entry across every page.</returns>", group, StringComparison.Ordinal);

        Assert.Contains("[JsonSerializable(typeof(ListSeriesResponse))]", files["MassiveRestJsonContext.g.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void APaginatedObjectWithoutItemsEmitsAGuardedGet()
    {
        string spec = Harness.Document(new Operation("GetTrade", "/v1/trade", ObjectEnvelope(Trade, paginated: true)));
        string map = Harness.MapDocument(
            """
            "Trade": { "schema": { "operationId": "GetTrade", "pointer": "results" } }
            """,
            Endpoint("GetTrade", "GetTrade", "object", "Trade", "results"));

        Dictionary<string, string> files = Harness.Generate(spec, map);

        string envelopes = files["Envelopes.g.cs"];
        Assert.DoesNotContain("IPagedEnvelope", envelopes, StringComparison.Ordinal);
        Assert.DoesNotContain("using MassiveDotNet.Http;", envelopes, StringComparison.Ordinal);
        Assert.Contains("public string? NextUrl { get; init; }", envelopes, StringComparison.Ordinal);

        string group = Group(files);
        Assert.Contains("public Task<Trade> GetTradeAsync(", group, StringComparison.Ordinal);
        Assert.Contains("MassiveHttpTransport.ThrowIfUnfollowableCursor(response?.NextUrl, requestUri, response?.RequestId);", group, StringComparison.Ordinal);
        Assert.Contains("return response?.Results", group, StringComparison.Ordinal);
        // No Enumerate is emitted, so the List-prefix rule does not apply and a Get name is accepted (D-S3).
        Assert.DoesNotContain("Enumerate", group, StringComparison.Ordinal);
    }

    [Fact]
    public void AnObjectReturnsTheModelAndThrowsWithoutIt()
    {
        string spec = Harness.Document(new Operation("GetTrade", "/v1/trade", ObjectEnvelope(Trade, paginated: false)));
        string map = Harness.MapDocument(
            """
            "Trade": { "kind": "struct", "schema": { "operationId": "GetTrade", "pointer": "results" } }
            """,
            Endpoint("GetTrade", "GetTrade", "object", "Trade", "results"));

        Dictionary<string, string> files = Harness.Generate(spec, map);

        Assert.Contains("public Trade? Results { get; init; }", files["Envelopes.g.cs"], StringComparison.Ordinal);

        string group = Group(files);
        Assert.Contains("public Task<Trade> GetTradeAsync(", group, StringComparison.Ordinal);
        Assert.Contains("return response?.Results\n", group, StringComparison.Ordinal);
        Assert.Contains("?? throw new MassiveApiException(", group, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.OK,", group, StringComparison.Ordinal);
        Assert.Contains("$\"The response from '{requestUri}' carried no 'results' payload.\",", group, StringComparison.Ordinal);
        Assert.Contains("response?.RequestId);", group, StringComparison.Ordinal);
        Assert.Contains("or with a success that carried no payload.</exception>", group, StringComparison.Ordinal);
        Assert.DoesNotContain("ThrowIfUnfollowableCursor", group, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEnvelopeWithoutARequestIdThrowsWithoutOne()
    {
        string spec = Harness.Document(new Operation("GetTrade", "/v1/trade", ObjectEnvelope(Trade, paginated: false, requestId: false)));
        string map = Harness.MapDocument(
            """
            "Trade": { "schema": { "operationId": "GetTrade", "pointer": "results" } }
            """,
            Endpoint("GetTrade", "GetTrade", "object", "Trade", "results"));

        string group = Group(Harness.Generate(spec, map));

        Assert.Contains("$\"The response from '{requestUri}' carried no 'results' payload.\");", group, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestId", group, StringComparison.Ordinal);
    }

    [Fact]
    public void ABodyObjectDeserializesTheModelDirectly()
    {
        string spec = Harness.Document(new Operation("GetDay", "/v1/day", """
            { "type": "object", "required": ["symbol"], "properties": { "symbol": { "type": "string" }, "status": { "type": "string" }, "open": { "type": "number" } } }
            """));
        string map = Harness.MapDocument(
            """
            "Day": { "schema": { "operationId": "GetDay" } }
            """,
            Endpoint("GetDay", "GetDay", "object", "Day", property: null));

        Dictionary<string, string> files = Harness.Generate(spec, map);

        // No envelope exists, so none is emitted: a file of nothing but usings fails the build (D-S5).
        Assert.False(files.ContainsKey("Envelopes.g.cs"));

        string model = files[Path.Combine("Models", "Day.g.cs")];
        Assert.Contains("public required string Symbol { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("public string? Status { get; init; }", model, StringComparison.Ordinal);

        string context = files["MassiveRestJsonContext.g.cs"];
        Assert.Contains("using MassiveDotNet.Rest.Models;", context, StringComparison.Ordinal);
        Assert.Contains("[JsonSerializable(typeof(Day))]", context, StringComparison.Ordinal);

        string group = Group(files);
        Assert.Contains("using System.Net;", group, StringComparison.Ordinal);
        Assert.Contains("public Task<Day> GetDayAsync(", group, StringComparison.Ordinal);
        Assert.Contains("Day? response = await _transport", group, StringComparison.Ordinal);
        Assert.Contains("MassiveRestJsonContext.Default.Day, cancellationToken)", group, StringComparison.Ordinal);
        Assert.Contains("return response\n", group, StringComparison.Ordinal);
        Assert.Contains("?? throw new MassiveApiException(", group, StringComparison.Ordinal);
        Assert.Contains("$\"The response from '{requestUri}' carried no payload.\");", group, StringComparison.Ordinal);
        Assert.Contains("<returns>The response body, deserialized as one object.</returns>", group, StringComparison.Ordinal);
    }

    [Fact]
    public void ABodyArrayDeserializesAnArrayOfTheModel()
    {
        string spec = Harness.Document(new Operation("GetHolidays", "/v1/holidays", """
            { "type": "array", "items": { "type": "object", "properties": { "name": { "type": "string" } } } }
            """));
        string map = Harness.MapDocument(
            """
            "Holiday": { "schema": { "operationId": "GetHolidays", "pointer": "items" } }
            """,
            Endpoint("GetHolidays", "ListHolidays", "array", "Holiday", property: null));

        Dictionary<string, string> files = Harness.Generate(spec, map);

        Assert.False(files.ContainsKey("Envelopes.g.cs"));
        Assert.Contains("[JsonSerializable(typeof(Holiday[]))]", files["MassiveRestJsonContext.g.cs"], StringComparison.Ordinal);

        string group = Group(files);
        Assert.DoesNotContain("using System.Net;", group, StringComparison.Ordinal);
        Assert.Contains("public Task<Holiday[]> ListHolidaysAsync(", group, StringComparison.Ordinal);
        Assert.Contains("Holiday[]? response = await _transport", group, StringComparison.Ordinal);
        Assert.Contains("MassiveRestJsonContext.Default.HolidayArray, cancellationToken)", group, StringComparison.Ordinal);
        Assert.Contains("return response ?? [];", group, StringComparison.Ordinal);
        Assert.Contains("<returns>The response body, an array that is empty when the server returned none.</returns>", group, StringComparison.Ordinal);
    }

    [Fact]
    public void ABodyModelSharedByTwoOperationsIsRegisteredOnce()
    {
        const string Day = """{ "type": "object", "properties": { "symbol": { "type": "string" } } }""";

        string spec = Harness.Document(
            new Operation("GetDay", "/v1/day", Day),
            new Operation("GetOtherDay", "/v1/other-day", Day));
        string map = Harness.MapDocument(
            """
            "Day": { "schema": { "operationId": "GetDay" } }
            """,
            Endpoint("GetDay", "GetDay", "object", "Day", property: null) + "," + Endpoint("GetOtherDay", "GetOtherDay", "object", "Day", property: null));

        string context = Harness.Generate(spec, map)["MassiveRestJsonContext.g.cs"];

        Assert.Equal(1, context.Split("[JsonSerializable(typeof(Day))]").Length - 1);
    }

    [Fact]
    public void ABodyModelMayBeReusedAsANestedProperty()
    {
        const string Day = """{ "type": "object", "properties": { "symbol": { "type": "string" } } }""";

        string spec = Harness.Document(
            new Operation("GetDay", "/v1/day", Day),
            new Operation("ListThings", "/v1/things", Harness.Envelope($$"""{ "type": "object", "properties": { "day": {{Day}} } }""")));
        string map = Harness.MapDocument(
            $$"""
            "Day":   { "schema": { "operationId": "GetDay" } },
            "Thing": { "schema": { "operationId": "ListThings", "pointer": "results/items" }, "properties": { "day": { "model": "Day" } } }
            """,
            Endpoint("GetDay", "GetDay", "object", "Day", property: null) + "," + Harness.Endpoint("ListThings", "Thing"));

        Dictionary<string, string> files = Harness.Generate(spec, map);

        Assert.Contains("public Day? Day { get; init; }", files[Path.Combine("Models", "Thing.g.cs")], StringComparison.Ordinal);
    }

    [Fact]
    public void ABodyModelReuseThatDiffersNamesTheBodyAsItsOrigin()
    {
        string spec = Harness.Document(
            new Operation("GetDay", "/v1/day", """{ "type": "object", "properties": { "symbol": { "type": "string" } } }"""),
            new Operation("ListThings", "/v1/things", Harness.Envelope("""{ "type": "object", "properties": { "day": { "type": "object", "properties": { "symbol": { "type": "string" }, "extra": { "type": "string" } } } } }""")));
        string map = Harness.MapDocument(
            """
            "Day":   { "schema": { "operationId": "GetDay" } },
            "Thing": { "schema": { "operationId": "ListThings", "pointer": "results/items" }, "properties": { "day": { "model": "Day" } } }
            """,
            Endpoint("GetDay", "GetDay", "object", "Day", property: null) + "," + Harness.Endpoint("ListThings", "Thing"));

        string message = Harness.Refusal(spec, map);

        Assert.Contains("generated from operation 'GetDay' at the response body", message, StringComparison.Ordinal);
        Assert.Contains("'extra' is declared at the site but not on the model", message, StringComparison.Ordinal);
    }
}

/// <summary>One refusal per rule in D-S6, each naming the fix.</summary>
public sealed class SingularResultRefusalTests
{
    private const string Trade = """
        { "type": "object", "required": ["p"], "properties": { "p": { "type": "number" }, "s": { "type": "number" } } }
        """;

    private const string TradeModel = """
        "Trade": { "schema": { "operationId": "GetTrade", "pointer": "results" } }
        """;

    private static string SeriesDocument(string values) => Harness.Document(new Operation("ListSeries", "/v1/series", SingularResultTests.ObjectEnvelope($$"""
        { "type": "object", "properties": { "values": {{values}} } }
        """, paginated: true)));

    private const string ObjectValues = """
        { "type": "array", "items": { "type": "object", "properties": { "value": { "type": "number" } } } }
        """;

    private static string SeriesMap(string items, string valuesRow) => Harness.MapDocument(
        $$"""
        "Series": {
          "schema": { "operationId": "ListSeries", "pointer": "results" },
          "items": "{{items}}",
          "properties": { "values": {{valuesRow}} }
        },
        "Value": { "schema": { "operationId": "ListSeries", "pointer": "results/values/items" } }
        """,
        SingularResultTests.Endpoint("ListSeries", "ListSeries", "object", "Series", "results"));

    [Fact]
    public void AnObjectKindOnAnArraySiteIsRefused()
    {
        string spec = Harness.Document(new Operation("GetTrade", "/v1/trade", Harness.Envelope(Trade)));
        string map = Harness.MapDocument(
            """
            "Trade": { "schema": { "operationId": "GetTrade", "pointer": "results/items" } }
            """,
            SingularResultTests.Endpoint("GetTrade", "GetTrade", "object", "Trade", "results"));

        string message = Harness.Refusal(spec, map);

        Assert.Contains("Endpoint 'GetTrade' (operation 'GetTrade'): result kind 'object' expects an object at 'results', but the schema there is an array of objects.", message, StringComparison.Ordinal);
        Assert.Contains("Use kind \"array\" for an array of the model.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnArrayKindOnAnObjectSiteIsRefused()
    {
        string spec = Harness.Document(new Operation("GetTrade", "/v1/trade", SingularResultTests.ObjectEnvelope(Trade, paginated: false)));
        string map = Harness.MapDocument(TradeModel, SingularResultTests.Endpoint("GetTrade", "ListTrades", "array", "Trade", "results"));

        string message = Harness.Refusal(spec, map);

        Assert.Contains("result kind 'array' expects an array of objects at 'results', but the schema there is an object.", message, StringComparison.Ordinal);
        Assert.Contains("Use kind \"object\" for a single model.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AScalarBodyIsRefused()
    {
        string spec = Harness.Document(new Operation("GetText", "/v1/text", """{ "type": "string" }"""));
        string map = Harness.MapDocument(
            """
            "Text": { "schema": { "operationId": "GetText" } }
            """,
            SingularResultTests.Endpoint("GetText", "GetText", "object", "Text", property: null));

        string message = Harness.Refusal(spec, map);

        Assert.Contains("expects an object at the response body, but the schema there is a scalar.", message, StringComparison.Ordinal);
        Assert.Contains("Only an object or an array of objects can be a result.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownKindIsRefused()
    {
        string spec = Harness.Document(new Operation("GetTrade", "/v1/trade", SingularResultTests.ObjectEnvelope(Trade, paginated: false)));
        string map = Harness.MapDocument(TradeModel, SingularResultTests.Endpoint("GetTrade", "GetTrade", "envelope", "Trade", "results"));

        string message = Harness.Refusal(spec, map);

        Assert.Contains("result kind 'envelope' is not recognised. Use \"array\" for an array of the model or \"object\" for a single one", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AResultPropertyTheSchemaDoesNotDeclareIsRefused()
    {
        string spec = Harness.Document(new Operation("GetTrade", "/v1/trade", SingularResultTests.ObjectEnvelope(Trade, paginated: false)));
        string map = Harness.MapDocument(TradeModel, SingularResultTests.Endpoint("GetTrade", "GetTrade", "object", "Trade", "payload"));

        string message = Harness.Refusal(spec, map);

        Assert.Contains("result names property 'payload', which the success schema does not declare.", message, StringComparison.Ordinal);
        Assert.Contains("omit \"property\" when the body itself is the payload", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ItemsNamingAnAbsentPropertyIsRefused()
    {
        string message = Harness.Refusal(SeriesDocument(ObjectValues), SeriesMap("points", """{ "model": "Value" }"""));

        Assert.Contains("Model 'Series' (operation 'ListSeries'): \"items\" names 'points', which the schema at 'results' does not declare.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ItemsNamingAnArrayOfScalarsIsRefused()
    {
        string spec = SeriesDocument("""{ "type": "array", "items": { "type": "number" } }""");

        string message = Harness.Refusal(spec, SeriesMap("values", """{ "type": "double[]" }"""));

        Assert.Contains("\"items\" names 'values', which is an array of scalars, not an array of objects.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ItemsWhoseRowNamesNoModelIsRefused()
    {
        // A verbatim type keeps the array-of-objects property bound (D-N3), so this reaches the
        // items check rather than the unbound-object refusal that runs first.
        string message = Harness.Refusal(SeriesDocument(ObjectValues), SeriesMap("values", """{ "type": "object[]" }"""));

        Assert.Contains("\"items\" names 'values', whose row does not name a \"model\".", message, StringComparison.Ordinal);
        Assert.Contains("that model is what Enumerate yields", message, StringComparison.Ordinal);
    }

    [Fact]
    public void APaginatedObjectWithItemsNeedsAListName()
    {
        string message = Harness.Refusal(SeriesDocument(ObjectValues), SeriesMap("values", """{ "model": "Value" }""")
            .Replace("\"method\": \"ListSeries\"", "\"method\": \"GetSeries\"", StringComparison.Ordinal));

        Assert.Contains("Operation 'ListSeries' is paginated, so it emits an Enumerate counterpart, but its mapped method 'GetSeries' is not List-prefixed", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABodyPayloadWithACursorIsRefused()
    {
        string spec = Harness.Document(new Operation("GetDay", "/v1/day", """
            { "type": "object", "properties": { "symbol": { "type": "string" }, "next_url": { "type": "string" } } }
            """));
        string map = Harness.MapDocument(
            """
            "Day": { "schema": { "operationId": "GetDay" } }
            """,
            SingularResultTests.Endpoint("GetDay", "GetDay", "object", "Day", property: null));

        string message = Harness.Refusal(spec, map);

        Assert.Contains("result declares no \"property\", so the body is the payload, but the success schema declares next_url at its root.", message, StringComparison.Ordinal);
    }
}
```

In `tests/MassiveDotNet.CodeGen.Tests/ModelBindingTests.cs`, replace `AnUnboundEnvelopeObjectPointsAtSingularResults` with:

```csharp
    [Fact]
    public void AnUnboundEnvelopeObjectSaysHowToBindIt()
    {
        string spec = Harness.Document(new Operation("ListThings", "/v1/things", """
            {
              "type": "object",
              "properties": {
                "results": { "type": "array", "items": { "type": "object", "properties": { "name": { "type": "string" } } } },
                "meta":    { "type": "object", "properties": { "count": { "type": "integer" } } }
              }
            }
            """));

        string message = Harness.Refusal(spec, MapDocument());

        Assert.Contains("Operation 'ListThings': envelope property 'meta' is an object", message, StringComparison.Ordinal);
        Assert.Contains("set \"property\": \"meta\" on the result row", message, StringComparison.Ordinal);
        Assert.Contains("omit \"property\" and declare the body as the result", message, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests --filter "FullyQualifiedName~SingularResult|FullyQualifiedName~AnUnboundEnvelopeObjectSaysHowToBindIt"`
Expected: every `SingularResultTests` case FAILS: the body cases with the Task 2 placeholder refusal `a result with no "property" is not yet supported`, the object cases because the envelope emits `Series[]?` and the group emits `Task<Series[]>`. Every refusal test FAILS either with a different message or, for the items cases, by generating without throwing. `AnUnboundEnvelopeObjectSaysHowToBindIt` FAILS on the `set "property"` assertion.

- [ ] **Step 3: Add the shape records and the resolvers to `Emitter`**

In `tools/MassiveDotNet.CodeGen/Emitter.cs`, add these members after the `ValueTypes` field. They are the one place the D-S1 table lives; every emitter below reads a `ResultShape` rather than the row.

```csharp
    /// <summary>The array inside a paginated object that carries the page's items (D-S2).</summary>
    /// <param name="WireName">The property's wire name, as the model row's <c>items</c> declares it.</param>
    /// <param name="Property">The property's C# name on the model.</param>
    /// <param name="Model">The item model, which <c>Enumerate</c> yields.</param>
    private sealed record ItemBinding(string WireName, string Property, string Model);

    /// <summary>How an endpoint delivers its payload, and therefore what its methods return (D-S1).</summary>
    /// <param name="Model">The row of the model the payload is made of.</param>
    /// <param name="IsArray">Whether the payload is an array of the model rather than one of it.</param>
    /// <param name="Property">The envelope property holding the payload, or <see langword="null"/> when the body is the payload (D-S5).</param>
    /// <param name="Paginated">Whether the success schema declares <c>next_url</c> (D-P6).</param>
    /// <param name="Items">For a paginated object whose model row names <c>items</c>: the array <c>Enumerate</c> walks. Otherwise <see langword="null"/>.</param>
    private sealed record ResultShape(MapModel Model, bool IsArray, string? Property, bool Paginated, ItemBinding? Items)
    {
        /// <summary>Whether the payload sits on an envelope. Body payloads have none.</summary>
        public bool HasEnvelope => Property is not null;

        /// <summary>Whether an <c>Enumerate</c> counterpart is emitted: a paginated array, or a paginated object with <c>items</c>.</summary>
        public bool Enumerates => Paginated && (IsArray || Items is not null);

        /// <summary>The type <c>Enumerate</c> yields, and the envelope's <c>IPagedEnvelope</c> argument.</summary>
        public string ItemType => Items?.Model ?? Model.Name;

        /// <summary>The C# type of the payload itself: the model, or an array of it.</summary>
        public string PayloadType => IsArray ? $"{Model.Name}[]" : Model.Name;

        /// <summary>What the <c>List</c> or <c>Get</c> method returns: the D-S1 table, one arm per row.</summary>
        public string ReturnType => (IsArray, Paginated, Items) switch
        {
            (true, true, _) => $"MassivePage<{Model.Name}>",
            (true, false, _) => $"{Model.Name}[]",
            (false, true, not null) => $"MassivePagedResult<{Model.Name}>",
            _ => Model.Name,
        };

        /// <summary>Whether the <c>Send</c> method throws for an absent payload: every singular shape does (D-S4); arrays coalesce to empty.</summary>
        public bool ThrowsOnMissingPayload => !IsArray;
    }

    /// <summary>
    /// Resolves an endpoint's result row into the shape its methods take (D-S1). Every emitter that
    /// touches an endpoint reads this rather than the row, so the table lives in one place.
    /// </summary>
    private ResultShape Shape(MapEndpoint endpoint, SpecOperation operation)
    {
        MapModel model = ResultModel(endpoint);
        bool isArray = IsArrayKind(endpoint);
        bool paginated = Spec.IsPaginated(operation);

        // A body payload has no envelope to carry a cursor, so a root-level next_url is a shape the
        // D-S1 table has no row for. Refusing is what keeps that cursor from being dropped silently.
        if (paginated && endpoint.Result.Property is null)
        {
            throw new InvalidOperationException(
                $"Endpoint '{endpoint.Method}' (operation '{endpoint.OperationId}'): result declares no \"property\", so the "
                + "body is the payload, but the success schema declares next_url at its root. A body payload cannot "
                + "carry a cursor; name the payload property in \"result\" instead.");
        }

        // items matters only where the spec says there is a cursor and the payload is one object;
        // on any other model the key is harmless and ignored (D-S2).
        ItemBinding? items = !isArray && paginated ? ItemsOf(model) : null;

        return new ResultShape(model, isArray, endpoint.Result.Property, paginated, items);
    }

    /// <summary>The model an endpoint's result row names, which must be declared.</summary>
    private MapModel ResultModel(MapEndpoint endpoint) =>
        map.Models.Find(m => m.Name == endpoint.Result.Model)
            ?? throw new InvalidOperationException(
                $"Endpoint '{endpoint.Method}' (operation '{endpoint.OperationId}'): result names model "
                + $"'{endpoint.Result.Model}', which is not declared in \"models\" in specs/endpoints.map.json.");

    /// <summary>Whether the result row's kind is <c>array</c>; <c>object</c> is the only other kind, and anything else is refused (D-S1).</summary>
    private static bool IsArrayKind(MapEndpoint endpoint) => endpoint.Result.Kind switch
    {
        "array" => true,
        "object" => false,
        _ => throw new InvalidOperationException(
            $"Endpoint '{endpoint.Method}' (operation '{endpoint.OperationId}'): result kind '{endpoint.Result.Kind}' is not "
            + "recognised. Use \"array\" for an array of the model or \"object\" for a single one (D-S1)."),
    };

    /// <summary>
    /// The array a paginated object enumerates, read from the model row's <c>items</c> (D-S2) and
    /// verified against the schema (D-S6): the property exists, is an array of objects, and its
    /// row names a model. <see langword="null"/> when the row declares no <c>items</c>.
    /// </summary>
    private ItemBinding? ItemsOf(MapModel model)
    {
        if (model.Items is not { } items)
        {
            return null;
        }

        JsonElement schema = Spec.Navigate(Spec.SuccessSchema(spec.Operation(model.SchemaOperationId)), model.SchemaPointer);
        SpecProperty? property = Spec.Properties(schema).Find(p => p.Name == items);

        if (property is null)
        {
            throw new InvalidOperationException(
                $"Model '{model.Name}' (operation '{model.SchemaOperationId}'): \"items\" names '{items}', which the schema at "
                + $"{Located(model.SchemaPointer)} does not declare. Name the array property that carries the page's items.");
        }

        if (Spec.Shape(property.Schema) != SchemaShape.ArrayOfObjects)
        {
            throw new InvalidOperationException(
                $"Model '{model.Name}' (operation '{model.SchemaOperationId}'): \"items\" names '{items}', which is "
                + $"{Spec.Describe(Spec.Shape(property.Schema))}, not an array of objects. The items of a paginated object "
                + "are objects with a model of their own.");
        }

        if (!model.Properties.TryGetValue(items, out MapProperty? row) || row.Model is not { } itemModel)
        {
            throw new InvalidOperationException(
                $"Model '{model.Name}' (operation '{model.SchemaOperationId}'): \"items\" names '{items}', whose row does not "
                + $"name a \"model\". Add \"model\" to the '{items}' row; that model is what Enumerate yields.");
        }

        return new ItemBinding(items, row.Name ?? Naming.Pascal(items), itemModel);
    }

    /// <summary>A model's origin as it reads in a diagnostic: its pointer, or the response body for the root.</summary>
    private static string Located(string pointer) =>
        pointer.Length == 0 ? "the response body" : $"'{pointer}'";

    /// <summary>The pointer to a property of the schema at <paramref name="parent"/>, which may be the root.</summary>
    private static string ChildPointer(string parent, string segment) =>
        parent.Length == 0 ? segment : $"{parent}/{segment}";
```

- [ ] **Step 4: Replace `ValidateResultReuse`, `EnvelopeType`, and the two pointer-naming sites**

Replace `ValidateResultReuse` in full:

```csharp
    /// <summary>
    /// Verifies an endpoint's <c>result</c> row against the schema its named model was generated
    /// from, the same structural check <see cref="ModelReferenceType"/> runs where a model is
    /// named on a property (D-N4). The site is resolved by kind and location (D-S6): an object
    /// result is the named property or the body itself; an array result is that array's element.
    /// Runs once per endpoint, from <see cref="EmitEnvelopes"/>, so it is order-independent and
    /// rule 6 holds.
    /// </summary>
    private void ValidateResultReuse(MapEndpoint endpoint, SpecOperation operation)
    {
        MapModel target = ResultModel(endpoint);
        bool isArray = IsArrayKind(endpoint);
        JsonElement success = Spec.SuccessSchema(operation);
        JsonElement located = success;

        if (endpoint.Result.Property is { } property)
        {
            SpecProperty declared = Spec.Properties(success).Find(p => p.Name == property)
                ?? throw new InvalidOperationException(
                    $"Endpoint '{endpoint.Method}' (operation '{endpoint.OperationId}'): result names property '{property}', "
                    + "which the success schema does not declare. Name the property that carries the payload, or omit "
                    + "\"property\" when the body itself is the payload.");

            located = declared.Schema;
        }

        // A kind that disagrees with the schema is refused with the kind that would agree, so the
        // fix is a word rather than a search.
        SchemaShape expected = isArray ? SchemaShape.ArrayOfObjects : SchemaShape.Object;
        SchemaShape actual = Spec.Shape(located);

        if (actual != expected)
        {
            string where = endpoint.Result.Property is { } named ? $"'{named}'" : "the response body";
            string fix = actual switch
            {
                SchemaShape.Object => "Use kind \"object\" for a single model.",
                SchemaShape.ArrayOfObjects => "Use kind \"array\" for an array of the model.",
                _ => "Only an object or an array of objects can be a result.",
            };

            throw new InvalidOperationException(
                $"Endpoint '{endpoint.Method}' (operation '{endpoint.OperationId}'): result kind '{endpoint.Result.Kind}' "
                + $"expects {Spec.Describe(expected)} at {where}, but the schema there is {Spec.Describe(actual)}. {fix}");
        }

        JsonElement site = isArray ? located.GetProperty("items") : located;
        JsonElement origin = Spec.Navigate(Spec.SuccessSchema(spec.Operation(target.SchemaOperationId)), target.SchemaPointer);
        List<string> differences = Spec.StructuralDifferences(origin, site);

        if (differences.Count > 0)
        {
            throw new InvalidOperationException(
                $"Endpoint '{endpoint.Method}' (operation '{endpoint.OperationId}'): result names model "
                + $"'{target.Name}', generated from operation '{target.SchemaOperationId}' at {Located(target.SchemaPointer)}, "
                + "but the schema at this site differs:\n  "
                + string.Join("\n  ", differences)
                + "\nA model name may cover only one shape. Declare a second model for this site, or fix the row.");
        }
    }
```

Replace `EnvelopeType` in full:

```csharp
    /// <summary>
    /// The type of an envelope property other than the payload. Envelopes have no map rows, so an
    /// object here is bound only by naming it as the result, or by making the body the result (D-S5).
    /// </summary>
    private static string EnvelopeType(MapEndpoint endpoint, SpecProperty property)
    {
        if (TypeBinding.NeedsModel(property.Schema))
        {
            throw new InvalidOperationException(
                $"Operation '{endpoint.OperationId}': envelope property '{property.Name}' is {Spec.Describe(Spec.Shape(property.Schema))}, "
                + "and only the payload named by the endpoint's \"result\" row is bound. If this property is the payload, "
                + $"set \"property\": \"{property.Name}\" on the result row; if the whole body is the payload, omit "
                + "\"property\" and declare the body as the result (D-S5).");
        }

        return NullableEnvelopeType(property);
    }
```

In `ModelReferenceType`, change the difference message's origin clause from `at '{target.SchemaPointer}'` to `at {Located(target.SchemaPointer)}`:

```csharp
                $"'{target.Name}', generated from operation '{target.SchemaOperationId}' at {Located(target.SchemaPointer)}, "
```

In `Unbound`, compose the suggested pointer through the root-aware helper:

```csharp
        string pointer = shape == SchemaShape.ArrayOfObjects
            ? $"{ChildPointer(model.SchemaPointer, property.Name)}/items"
            : ChildPointer(model.SchemaPointer, property.Name);
```

- [ ] **Step 5: Replace `Emit`, `EmitEnvelopes`, `EmitGroup`'s usings, `EmitEndpoint`, and `EmitJsonContext`**

Replace `Emit`:

```csharp
    public Dictionary<string, string> Emit()
    {
        Dictionary<string, string> files = new(StringComparer.Ordinal);

        foreach (MapModel model in map.Models)
        {
            files[Path.Combine("Models", $"{model.Name}.g.cs")] = EmitModel(model);
        }

        // A map of body payloads alone has no envelope, and a file holding only usings would
        // fail the build under IDE0005.
        if (EmitEnvelopes() is { } envelopes)
        {
            files["Envelopes.g.cs"] = envelopes;
        }

        foreach (MapGroup group in map.Groups)
        {
            files[$"{group.Name}Group.g.cs"] = EmitGroup(group);
        }

        files["MassiveRestJsonContext.g.cs"] = EmitJsonContext();

        return files;
    }
```

Replace `EmitEnvelopes` in full:

```csharp
    private string? EmitEnvelopes()
    {
        // Hoisted for the reason EmitModel hoists its members: the file's usings depend on the
        // types it emits, so those have to be resolved before the first line is written.
        List<(MapEndpoint Endpoint, SpecOperation Operation, ResultShape Shape, string ResultsProperty, List<(SpecProperty Property, string Type)> Members)> envelopes = [];

        foreach (MapEndpoint endpoint in map.Endpoints)
        {
            SpecOperation operation = spec.Operation(endpoint.OperationId);

            ValidateResultReuse(endpoint, operation);
            ResultShape shape = Shape(endpoint, operation);

            // A body payload deserializes as the model itself, so there is no envelope (D-S5).
            if (shape.Property is not { } property)
            {
                continue;
            }

            List<(SpecProperty Property, string Type)> members = [.. Spec.Properties(Spec.SuccessSchema(operation))
                .Select(p => (p, p.Name == property ? $"{shape.PayloadType}?" : EnvelopeType(endpoint, p)))];

            envelopes.Add((endpoint, operation, shape, Naming.Pascal(property), members));
        }

        if (envelopes.Count == 0)
        {
            return null;
        }

        CodeWriter writer = new();
        writer.Line(Header);
        writer.Line();
        writer.Line("using System.Text.Json.Serialization;");

        // Emitted conditionally: an unused using fails the build under EnforceCodeStyleInBuild.
        // Exists is order-independent, so rule 6 holds.
        if (envelopes.Exists(e => e.Shape.Enumerates))
        {
            writer.Line("using MassiveDotNet.Http;");
        }

        writer.Line("using MassiveDotNet.Rest.Models;");

        // An envelope can name a NodaTime type in its own right, when the description declares a
        // format: date or date-time property beside the payload.
        if (envelopes.Exists(e => e.Members.Exists(m => NamesNodaTime(m.Type))))
        {
            writer.Line("using NodaTime;");
        }

        writer.Line();
        writer.Line("namespace MassiveDotNet.Rest.Serialization;");

        foreach ((MapEndpoint endpoint, SpecOperation operation, ResultShape shape, string resultsProperty, List<(SpecProperty Property, string Type)> members) in envelopes)
        {
            writer.Line();
            writer.Doc("summary", $"The response envelope returned by {operation.Path}.");

            string declaration = shape.Enumerates
                ? $"internal sealed class {EnvelopeName(endpoint)} : IPagedEnvelope<{shape.ItemType}>"
                : $"internal sealed class {EnvelopeName(endpoint)}";

            using (writer.Block(declaration))
            {
                bool first = true;

                foreach ((SpecProperty property, string type) in members)
                {
                    if (!first)
                    {
                        writer.Line();
                    }

                    first = false;

                    writer.Doc("summary", Prose.Clean(property.Description));
                    writer.Line($"[JsonPropertyName(\"{property.Name}\")]");
                    writer.Line($"public {type} {Naming.Pascal(property.Name)} {{ get; init; }}");
                }

                if (shape.Items is { } items)
                {
                    // The page's items sit one level down, inside the result object, so the
                    // interface is satisfied explicitly through it (D-S2).
                    writer.Line();
                    writer.Line($"{items.Model}[]? IPagedEnvelope<{items.Model}>.Results => {resultsProperty}?.{items.Property};");
                }
                else if (shape.Enumerates && resultsProperty != "Results")
                {
                    // The interface names the results property `Results`. When the endpoint's result
                    // property maps to some other name, satisfy it explicitly rather than renaming
                    // the public property away from the wire shape.
                    writer.Line();
                    writer.Line($"{shape.ItemType}[]? IPagedEnvelope<{shape.ItemType}>.Results => {resultsProperty};");
                }
            }
        }

        return writer.ToString();
    }
```

In `EmitGroup`, after `bool needsNodaTime = ...;` and before the first `writer.Line("using ...")`, add the `System.Net` using, which `HttpStatusCode.OK` in a singular `Send` method needs:

```csharp
        // HttpStatusCode is named only by the throw a singular Send method emits (D-S4). Exists is
        // order-independent, so rule 6 holds.
        bool needsSystemNet = endpoints.Exists(e => Shape(e, spec.Operation(e.OperationId)).ThrowsOnMissingPayload);

        if (needsSystemNet)
        {
            writer.Line("using System.Net;");
        }

        writer.Line("using MassiveDotNet.Http;");
```

Replace `EmitEndpoint` in full, and add `Returns`, `EmitSend`, and `EmitBodySend` after it:

```csharp
    private void EmitEndpoint(CodeWriter writer, MapEndpoint endpoint)
    {
        SpecOperation operation = spec.Operation(endpoint.OperationId);
        List<Argument> arguments = Arguments(endpoint, operation);
        ResultShape shape = Shape(endpoint, operation);

        // A request identifier is not universal: the futures AggregatesV1 envelope declares only
        // next_url, results, and status, so emitting response?.RequestId unconditionally would not
        // compile for it. The question asked is exactly the one that matters -- will the emitted
        // envelope have a RequestId property -- and Exists is order-independent, so rule 6 holds.
        bool hasRequestId = shape.HasEnvelope
            && Spec.Properties(Spec.SuccessSchema(operation))
                .Exists(p => Naming.Pascal(p.Name) == "RequestId");

        // Derived once: it is what both generated methods cross-reference, and deriving it is
        // what rejects a paginated endpoint whose mapped method is not List-prefixed.
        string enumerate = shape.Enumerates ? Naming.Enumerate(endpoint.Method, endpoint.OperationId) : string.Empty;
        string callArguments = string.Join(", ", arguments.Select(a => a.Identifier));
        // Nullable because Prose.Clean returns null for blank prose; Doc skips blank content.
        string? summary = endpoint.Summary ?? Prose.Clean(Summary(operation));
        bool preserve = endpoint.Summary is not null;

        if (shape.Enumerates)
        {
            // The two entry points share an endpoint but not a shape, so the enumerating one names
            // its own nature rather than repeating the summary verbatim: identical summaries are
            // indistinguishable in IntelliSense, where remarks are not shown.
            writer.Doc(
                "summary",
                AppendClause(summary, "enumerating every page as a single lazy sequence."),
                preserveMarkup: preserve);

            // A paginated object's other members belong to each page, and a flat sequence has
            // nowhere to attach them; the remark says so and points at List (D-S2).
            string remarks = shape.Items is { } items
                ? "Walks every page, requesting the next only once the previous one has been consumed, and "
                    + $"yields each page's <c>{items.WireName}</c> in turn; the other members of each page's "
                    + $"<c>{shape.Property}</c> are not observable through this sequence. "
                : "Walks every page, requesting the next only once the previous one has been consumed. ";

            remarks += $"Use <see cref=\"{endpoint.Method}Async\"/> to retrieve a single page instead.";

            // Emitted only where the endpoint declares the parameter, since not every paginated
            // operation has one. Exists is order-independent, so rule 6 holds.
            if (arguments.Exists(a => a.Identifier == "limit"))
            {
                remarks += " <paramref name=\"limit\"/> sizes each page rather than the traversal, so "
                    + "lowering it issues more requests rather than returning fewer items; bound the "
                    + "sequence with <c>Take</c> instead.";
            }

            // Added to, never replaced: the map carries what the spec cannot, so dropping its
            // remarks here would lose prose that no regeneration could recover.
            if (endpoint.Remarks is { } authored)
            {
                remarks = $"{remarks} {authored}";
            }

            writer.Doc("remarks", remarks, preserveMarkup: true);

            EmitParameterDocs(writer, arguments, "A token to cancel the traversal.");
            writer.Doc(
                "returns",
                shape.Items is { } enumerated
                    ? $"Every <c>{enumerated.WireName}</c> entry across every page."
                    : $"Every <c>{shape.Property}</c> item across every page.",
                preserveMarkup: true);
            writer.Doc("exception", "The server responded with an error status.", "cref=\"MassiveApiException\"");

            List<string> enumerateSignature = Signature(
                $"public IAsyncEnumerable<{shape.ItemType}> {enumerate}Async",
                [.. arguments.Select(a => a.Declaration), "CancellationToken cancellationToken = default"]);

            using (writer.Block(enumerateSignature))
            {
                EmitGuards(writer, arguments);
                writer.Line($"string requestUri = Build{endpoint.Method}Uri({callArguments});");
                writer.Line($"return _transport.EnumerateAsync<{EnvelopeName(endpoint)}, {shape.ItemType}>(");
                writer.Line($"    requestUri, MassiveRestJsonContext.Default.{EnvelopeName(endpoint)}, cancellationToken);");
            }

            writer.Line();
        }

        writer.Doc("summary", summary, preserveMarkup: preserve);

        // List is the more discoverable of the two names, so it is the one whose reader is most
        // likely not to know the other exists. The cross-reference points both ways.
        string? listRemarks = endpoint.Remarks;

        if (shape.Enumerates)
        {
            string pointer =
                $"Returns the first page only. Use <see cref=\"{enumerate}Async\"/> "
                + "to walk every page without handling cursors yourself.";

            listRemarks = listRemarks is null ? pointer : $"{pointer} {listRemarks}";
        }

        writer.Doc("remarks", listRemarks, preserveMarkup: true);

        EmitParameterDocs(writer, arguments, "A token to cancel the request.");
        writer.Doc("returns", Returns(shape), preserveMarkup: true);
        writer.Doc(
            "exception",
            shape.ThrowsOnMissingPayload
                ? "The server responded with an error status, or with a success that carried no payload."
                : "The server responded with an error status.",
            "cref=\"MassiveApiException\"");

        List<string> signature = Signature(
            $"public Task<{shape.ReturnType}> {endpoint.Method}Async",
            [.. arguments.Select(a => a.Declaration), "CancellationToken cancellationToken = default"]);

        using (writer.Block(signature))
        {
            EmitGuards(writer, arguments);
            writer.Line($"string requestUri = Build{endpoint.Method}Uri({callArguments});");
            writer.Line($"return Send{endpoint.Method}Async(requestUri, cancellationToken);");
        }

        writer.Line();

        // The URI builder is a ref struct, so it cannot live inside an async method.
        // Composition therefore happens in a separate synchronous method.
        List<string> builderSignature = Signature(
            $"private static string Build{endpoint.Method}Uri",
            [.. arguments.Select(a => a.RequiredDeclaration)]);

        using (writer.Block(builderSignature))
        {
            writer.Line($"RequestUriBuilder builder = new(stackalloc char[{UriBufferLength}]);");
            writer.Line();

            EmitPath(writer, operation.Path, arguments);

            foreach (Argument argument in arguments.Where(a => a.In == "query"))
            {
                writer.Line($"builder.AppendQuery(\"{argument.WireName}\", {argument.QueryExpression});");
            }

            writer.Line();
            writer.Line("return builder.ToUriString();");
        }

        writer.Line();

        EmitSend(writer, endpoint, shape, hasRequestId);
    }

    /// <summary>The <c>returns</c> sentence of the <c>List</c> or <c>Get</c> method, one per row of the D-S1 table.</summary>
    private static string Returns(ResultShape shape)
    {
        if (shape.Property is not { } property)
        {
            return shape.IsArray
                ? "The response body, an array that is empty when the server returned none."
                : "The response body, deserialized as one object.";
        }

        return (shape.IsArray, shape.Paginated, shape.Items) switch
        {
            (true, true, _) => $"A single page of <c>{property}</c>, reporting whether more exist.",
            (true, false, _) => $"The <c>{property}</c> array from the response, empty when the server returned none.",
            (false, true, not null) => $"A single page: the <c>{property}</c> object, reporting whether more exist.",
            _ => $"The <c>{property}</c> object from the response.",
        };
    }

    /// <summary>Emits the <c>Send</c> method: the one place a response is turned into the return type.</summary>
    private static void EmitSend(CodeWriter writer, MapEndpoint endpoint, ResultShape shape, bool hasRequestId)
    {
        using (writer.Block($"private async Task<{shape.ReturnType}> Send{endpoint.Method}Async(string requestUri, CancellationToken cancellationToken)"))
        {
            if (shape.Property is not { } property)
            {
                EmitBodySend(writer, shape);
                return;
            }

            string envelope = EnvelopeName(endpoint);
            string results = Naming.Pascal(property);
            string requestId = hasRequestId ? "response?.RequestId" : "requestId: null";

            writer.Line($"{envelope}? response = await _transport");
            writer.Line($"    .GetAsync(requestUri, MassiveRestJsonContext.Default.{envelope}, cancellationToken)");
            writer.Line("    .ConfigureAwait(false);");
            writer.Line();

            if (shape.IsArray && shape.Paginated)
            {
                // Emitted, not just reasoned about here: a reader of the generated file meets
                // a whitespace test on a URL and deserves to know it is load-bearing.
                writer.Line("// A blank next_url is not a cursor. EnumerateAsync stops on one, so this");
                writer.Line("// reports the same thing rather than promising a page that is never fetched.");

                if (!hasRequestId)
                {
                    writer.Line("// This operation's envelope declares no request_id, so there is none to report.");
                }

                writer.Line($"return new MassivePage<{shape.Model.Name}>(");
                writer.Line($"    response?.{results},");
                writer.Line("    !string.IsNullOrWhiteSpace(response?.NextUrl),");
                writer.Line($"    {requestId});");
                return;
            }

            if (shape.IsArray)
            {
                writer.Line($"return response?.{results} ?? [];");
                return;
            }

            if (shape.Paginated && shape.Items is null)
            {
                writer.Line("// The schema declares next_url, but one object cannot be paged, so a cursor here is a");
                writer.Line("// page the caller would never receive. A blank one passes; a real one throws (D17).");
                writer.Line($"MassiveHttpTransport.ThrowIfUnfollowableCursor(response?.NextUrl, requestUri, {requestId});");
                writer.Line();
            }

            // The throw for a missing payload, shared by the paged and plain singular shapes. Its
            // request id line is emitted only where the envelope has one to report.
            string[] missingPayload = hasRequestId
                ? [
                    "    ?? throw new MassiveApiException(",
                    "        HttpStatusCode.OK,",
                    $"        $\"The response from '{{requestUri}}' carried no '{property}' payload.\",",
                    "        response?.RequestId);",
                ]
                : [
                    "    ?? throw new MassiveApiException(",
                    "        HttpStatusCode.OK,",
                    $"        $\"The response from '{{requestUri}}' carried no '{property}' payload.\");",
                ];

            writer.Line("// A 200 without its payload is a success the caller cannot use, so it is reported the");
            writer.Line("// same way as a body that fails to deserialize rather than as null on every call (D17).");

            if (shape.Items is null)
            {
                writer.Line($"return response?.{results}");

                foreach (string line in missingPayload)
                {
                    writer.Line(line);
                }

                return;
            }

            // The local is what lets the page below read `response` without a null-conditional:
            // a non-null `response?.Results` proves `response` non-null to the compiler.
            writer.Line($"{shape.Model.Name} result = response?.{results}");

            foreach (string line in missingPayload)
            {
                writer.Line(line);
            }

            writer.Line();
            writer.Line("// A blank next_url is not a cursor. EnumerateAsync stops on one, so this");
            writer.Line("// reports the same thing rather than promising a page that is never fetched.");
            writer.Line($"return new MassivePagedResult<{shape.Model.Name}>(");
            writer.Line("    result,");
            writer.Line("    !string.IsNullOrWhiteSpace(response.NextUrl),");
            writer.Line(hasRequestId ? "    response.RequestId);" : "    requestId: null);");
        }
    }

    /// <summary>Emits the body of a <c>Send</c> method whose payload is the response body itself (D-S5).</summary>
    private static void EmitBodySend(CodeWriter writer, ResultShape shape)
    {
        // The context names an array's metadata property by its element type plus Array.
        string typeInfo = shape.IsArray ? $"{shape.Model.Name}Array" : shape.Model.Name;

        writer.Line($"{shape.PayloadType}? response = await _transport");
        writer.Line($"    .GetAsync(requestUri, MassiveRestJsonContext.Default.{typeInfo}, cancellationToken)");
        writer.Line("    .ConfigureAwait(false);");
        writer.Line();

        if (shape.IsArray)
        {
            writer.Line("return response ?? [];");
            return;
        }

        writer.Line("// An empty body is a success the caller cannot use, reported the same way as a body that");
        writer.Line("// fails to deserialize (D17). There is no envelope here, so no request id can be reported.");
        writer.Line("return response");
        writer.Line("    ?? throw new MassiveApiException(");
        writer.Line("        HttpStatusCode.OK,");
        writer.Line("        $\"The response from '{requestUri}' carried no payload.\");");
    }
```

Replace `EmitJsonContext` in full:

```csharp
    private string EmitJsonContext()
    {
        // An envelope carries its payload; a body payload has none, so the model itself, or an
        // array of it, is registered instead (D-S5). Kept in map order and deduplicated, since two
        // operations may share a body model, so rule 6 holds.
        List<string> registered = [];

        foreach (MapEndpoint endpoint in map.Endpoints)
        {
            ResultShape shape = Shape(endpoint, spec.Operation(endpoint.OperationId));
            string type = shape.HasEnvelope ? EnvelopeName(endpoint) : shape.PayloadType;

            if (!registered.Contains(type))
            {
                registered.Add(type);
            }
        }

        CodeWriter writer = new();
        writer.Line(Header);
        writer.Line();
        writer.Line("using System.Text.Json.Serialization;");

        // Emitted conditionally: an unused using fails the build under EnforceCodeStyleInBuild.
        if (map.Endpoints.Exists(e => e.Result.Property is null))
        {
            writer.Line("using MassiveDotNet.Rest.Models;");
        }

        writer.Line("using MassiveDotNet.Serialization;");
        writer.Line();
        writer.Line("namespace MassiveDotNet.Rest.Serialization;");
        writer.Line();
        writer.Doc("summary", "Source-generated serialization metadata for every REST response envelope. Using a context rather than reflection keeps the SDK Native AOT compatible.");
        writer.Doc("remarks", "Calendar dates are read by <see cref=\"LocalDateJsonConverter\"/> and RFC 3339 timestamps by <see cref=\"InstantJsonConverter\"/>, registered here once so no model property needs its own attribute.", preserveMarkup: true);
        writer.Line("[JsonSourceGenerationOptions(Converters = new[] { typeof(LocalDateJsonConverter), typeof(InstantJsonConverter) })]");

        foreach (string type in registered)
        {
            writer.Line($"[JsonSerializable(typeof({type}))]");
        }

        writer.Line("internal sealed partial class MassiveRestJsonContext : JsonSerializerContext;");

        return writer.ToString();
    }
```

Finally delete the placeholder refusal Task 2 added at the top of the old `EmitEnvelopes` lambda, if the replacement above has not already removed it, and confirm no `endpoint.Result.Property!` remains: `grep -n 'Property!' tools/MassiveDotNet.CodeGen/Emitter.cs` prints nothing.

- [ ] **Step 6: Run the generator tests, then regenerate and confirm the existing output is byte-identical**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests`
Expected: PASS, every test. If `APaginatedObjectWithItemsReturnsAPagedResultAndEnumeratesItsItems` fails on `response.RequestId);`, the `?.` was left on the paged-result lines; the local `result` proves `response` non-null and the generated code must read it plainly.

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff. The three array endpoints emit exactly what they did before; the only differences this task makes are for shapes the map does not yet contain. If a diff appears, it is a wording drift in a doc string or comment shared with the array path, and the fix is in the emitter, never in `Generated/`.

Run: `dotnet build MassiveDotNet.slnx`
Expected: 0 warnings, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add tools/MassiveDotNet.CodeGen/Emitter.cs tests/MassiveDotNet.CodeGen.Tests/SingularResultTests.cs tests/MassiveDotNet.CodeGen.Tests/ModelBindingTests.cs
git commit -m "feat: emit every result shape in the D-S1 table and verify each site by kind

An endpoint resolves to a ResultShape: an array on an envelope as
before; one object returning MassivePagedResult<T> and enumerating
the array its model names as items; a paginated object without items
returning a guarded T; and a body that is the object or the array,
deserialized directly with no envelope. Every singular Get returns T
and throws on a 200 without its payload. ValidateResultReuse resolves
the site by kind and location and refuses a kind the schema disagrees
with, naming the kind that would agree (D-S1 through D-S6).

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 4: Map the four proof endpoints and test them through the stub

**Files:**
- Modify: `specs/endpoints.map.json`
- Create: `src/MassiveDotNet.Rest/Models/IndicatorValue.cs`
- Create: `src/MassiveDotNet.Rest/Models/LastTrade.cs`
- Regenerate: `src/MassiveDotNet.Rest/Generated/` (adds `Models/IndicatorSeries.g.cs`, `Models/IndicatorValue.g.cs`, `Models/IndicatorUnderlying.g.cs`, `Models/LastTrade.g.cs`, `Models/DailyOpenClose.g.cs`, `Models/MarketHoliday.g.cs`; extends `Envelopes.g.cs`, `StocksGroup.g.cs`, `ReferenceGroup.g.cs`, `MassiveRestJsonContext.g.cs`)
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/StocksIndicatorsTests.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/StocksLastTradeTests.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/StocksOpenCloseTests.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceMarketHolidaysTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs:17`

**Interfaces:**
- Consumes: everything from Tasks 1 to 3; `StubHandler`, `PagingStubHandler`, `MassiveEndpoints.Production`, `RangeFilter.Between`, `DateOrTimestamp.FromDate`.
- Produces, all in `MassiveDotNet.Rest`, which Task 5 calls:
  - `StocksGroup.ListSmaAsync(string ticker, RangeFilter<DateOrTimestamp>? timestamp = null, AggregateTimespan? timespan = null, bool? adjusted = null, int? window = null, SeriesType? seriesType = null, bool? expandUnderlying = null, SortOrder? order = null, int? limit = null, CancellationToken cancellationToken = default)` → `Task<MassivePagedResult<IndicatorSeries>>`, and `EnumerateSmaAsync(...)` with the same parameters → `IAsyncEnumerable<IndicatorValue>`.
  - `StocksGroup.GetLastTradeAsync(string ticker, CancellationToken cancellationToken = default)` → `Task<LastTrade>`.
  - `StocksGroup.GetDailyOpenCloseAsync(string ticker, LocalDate date, bool? adjusted = null, CancellationToken cancellationToken = default)` → `Task<DailyOpenClose>`.
  - `ReferenceGroup.ListMarketHolidaysAsync(CancellationToken cancellationToken = default)` → `Task<MarketHoliday[]>`.
  - Models in `MassiveDotNet.Rest.Models`: `IndicatorSeries` (`IndicatorValue[]? Values`, `IndicatorUnderlying? Underlying`), `IndicatorValue` struct (`long TimestampMilliseconds`, `double Value`, computed `Instant Timestamp`), `IndicatorUnderlying` (`string? Url`, `Agg[]? Aggregates`), `LastTrade` struct (`Ticker`, `SipTimestampNanoseconds`, `ParticipantTimestampNanoseconds`, `TrfTimestampNanoseconds`, `SequenceNumber`, `TradeId`, `Price`, `Size`, `DecimalSize`, `ExchangeId`, `TrfId`, `Conditions`, `CorrectionIndicator`, `Tape`, computed `SipTimestamp`, `ParticipantTimestamp`, `TrfTimestamp`), `DailyOpenClose` (`Symbol`, `From`, `Open`, `High`, `Low`, `Close`, `Volume`, `PreMarket`, `AfterHours`, `IsOtc`, `Status`), `MarketHoliday` (`Date`, `Exchange`, `Name`, `Status`, `Open`, `Close`).

- [ ] **Step 1: Add the fixtures**

Append to the `Fixtures` class in `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, before the `Unauthorized` member:

```csharp
    /// <summary>
    /// The documented sample for GET /v1/indicators/sma/{stockTicker}, verbatim. One value, and
    /// two underlying aggregates as <c>expand_underlying</c> returns them.
    /// </summary>
    public const string StocksSma = """
        {
          "next_url": "https://api.massive.com/v1/indicators/sma/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "a47d1beb8c11b6ae897ab76cdbbf35a3",
          "results": {
            "underlying": {
              "aggregates": [
                {
                  "c": 75.0875,
                  "h": 75.15,
                  "l": 73.7975,
                  "n": 1,
                  "o": 74.06,
                  "t": 1577941200000,
                  "v": 135647456,
                  "vw": 74.6099
                },
                {
                  "c": 74.3575,
                  "h": 75.145,
                  "l": 74.125,
                  "n": 1,
                  "o": 74.2875,
                  "t": 1578027600000,
                  "v": 146535512,
                  "vw": 74.7026
                }
              ],
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

    /// <summary>
    /// A hand-written final page in the SMA envelope's shape, with no <c>next_url</c> and only the
    /// underlying's URL, so a traversal that starts from <see cref="StocksSma"/> ends after two
    /// requests. The service cannot be asked for "the page after the published sample".
    /// </summary>
    public const string StocksSmaLastPage = """
        {
          "request_id": "0d5b6f1e9f3c4a7b8e2d1c0f9a8b7c6d",
          "results": {
            "underlying": {
              "url": "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2003-01-01/2022-07-24"
            },
            "values": [
              {
                "timestamp": 1517475600016,
                "value": 139.871
              }
            ]
          },
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /v2/last/trade/{stocksTicker}, verbatim.</summary>
    public const string StocksLastTrade = """
        {
          "request_id": "f05562305bd26ced64b98ed68b3c5d96",
          "results": {
            "T": "AAPL",
            "c": [
              37
            ],
            "ds": "25.0",
            "f": 1617901342969796400,
            "i": "118749",
            "p": 129.8473,
            "q": 3135876,
            "r": 202,
            "s": 25,
            "t": 1617901342969834000,
            "x": 4,
            "y": 1617901342968000000,
            "z": 3
          },
          "status": "OK"
        }
        """;

    /// <summary>
    /// The documented sample for GET /v1/open-close/{stocksTicker}/{date}, verbatim. The body is
    /// the payload: there is no <c>results</c> wrapper, and <c>status</c> sits beside the prices.
    /// </summary>
    public const string StocksOpenClose = """
        {
          "afterHours": 322.1,
          "close": 325.12,
          "from": "2023-01-09",
          "high": 326.2,
          "low": 322.3,
          "open": 324.66,
          "preMarket": 324.5,
          "status": "OK",
          "symbol": "AAPL",
          "volume": 26122646
        }
        """;

    /// <summary>
    /// The documented sample for GET /v1/marketstatus/upcoming, verbatim. The body is a bare array
    /// with no envelope at all; the early-close entries carry timestamps with a fraction.
    /// </summary>
    public const string MarketHolidays = """
        [
          {
            "date": "2020-11-26",
            "exchange": "NYSE",
            "name": "Thanksgiving",
            "status": "closed"
          },
          {
            "date": "2020-11-26",
            "exchange": "NASDAQ",
            "name": "Thanksgiving",
            "status": "closed"
          },
          {
            "date": "2020-11-26",
            "exchange": "OTC",
            "name": "Thanksgiving",
            "status": "closed"
          },
          {
            "close": "2020-11-27T18:00:00.000Z",
            "date": "2020-11-27",
            "exchange": "NASDAQ",
            "name": "Thanksgiving",
            "open": "2020-11-27T14:30:00.000Z",
            "status": "early-close"
          },
          {
            "close": "2020-11-27T18:00:00.000Z",
            "date": "2020-11-27",
            "exchange": "NYSE",
            "name": "Thanksgiving",
            "open": "2020-11-27T14:30:00.000Z",
            "status": "early-close"
          }
        ]
        """;

    /// <summary>An envelope in the singular shape with its payload missing: a 200 the caller cannot use.</summary>
    public const string SingularWithoutResults = """
        {
          "status": "OK",
          "request_id": "r"
        }
        """;
```

- [ ] **Step 2: Write the failing endpoint tests**

Create `tests/MassiveDotNet.Rest.Tests/StocksIndicatorsTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The first paginated singular result: one object under <c>results</c> whose <c>values</c>
/// continue across pages while its <c>underlying</c> belongs to each page (D-S2).
/// </summary>
public sealed class StocksIndicatorsTests
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
        StubHandler handler = new(Fixtures.StocksSma);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.ListSmaAsync(
                "AAPL",
                timestamp: RangeFilter.Between(
                    DateOrTimestamp.FromDate(new LocalDate(2024, 1, 1)),
                    DateOrTimestamp.FromDate(new LocalDate(2024, 6, 30))),
                timespan: AggregateTimespan.Day,
                adjusted: true,
                window: 10,
                seriesType: SeriesType.Close,
                expandUnderlying: true,
                order: SortOrder.Descending,
                limit: 2,
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v1/indicators/sma/AAPL"
                + "?timestamp.gte=2024-01-01&timestamp.lte=2024-06-30"
                + "&timespan=day&adjusted=true&window=10&series_type=close&expand_underlying=true&order=desc&limit=2",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSampleAsOnePageWithBothHalves()
    {
        StubHandler handler = new(Fixtures.StocksSma);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePagedResult<IndicatorSeries> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListSmaAsync("AAPL", cancellationToken: Ct);
        }

        Assert.NotNull(page.Result.Values);
        IndicatorValue value = Assert.Single(page.Result.Values);
        Assert.Equal(1517562000016, value.TimestampMilliseconds);
        Assert.Equal(140.139, value.Value);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1517562000016), value.Timestamp);

        Assert.NotNull(page.Result.Underlying);
        Assert.Equal("https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2003-01-01/2022-07-25", page.Result.Underlying.Url);
        Assert.NotNull(page.Result.Underlying.Aggregates);
        Assert.Equal(2, page.Result.Underlying.Aggregates.Length);
        Assert.Equal(75.0875, page.Result.Underlying.Aggregates[0].Close);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1577941200000), page.Result.Underlying.Aggregates[0].Timestamp);

        Assert.True(page.HasMore);
        Assert.Equal("a47d1beb8c11b6ae897ab76cdbbf35a3", page.RequestId);
    }

    [Fact]
    public async Task ReportsNoFurtherPagesFromAPageWithoutACursor()
    {
        StubHandler handler = new(Fixtures.StocksSmaLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePagedResult<IndicatorSeries> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListSmaAsync("AAPL", cancellationToken: Ct);
        }

        Assert.False(page.HasMore);
        Assert.NotNull(page.Result.Underlying);
        Assert.Null(page.Result.Underlying.Aggregates);
    }

    [Fact]
    public async Task EnumerateYieldsEveryValueAcrossPagesRequestingEachOnlyWhenNeeded()
    {
        PagingStubHandler handler = new(Fixtures.StocksSma, Fixtures.StocksSmaLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<long> timestamps = [];

        using (client)
        using (transport)
        {
            await foreach (IndicatorValue value in client.Stocks.EnumerateSmaAsync("AAPL", limit: 1, cancellationToken: Ct))
            {
                timestamps.Add(value.TimestampMilliseconds);

                // One page in flight: the second request is issued only after the first page's
                // single value has been consumed, never ahead of it.
                Assert.Equal(timestamps.Count, handler.Requests.Count);
            }
        }

        Assert.Equal([1517562000016, 1517475600016], timestamps);
        Assert.Equal(2, handler.Requests.Count);

        // The cursor is followed verbatim (D14): the second request must match the fixture's own
        // next_url exactly, not merely start with its cursor.
        using JsonDocument firstPage = JsonDocument.Parse(Fixtures.StocksSma);
        Uri nextUrl = new(firstPage.RootElement.GetProperty("next_url").GetString()!);

        Assert.Equal(nextUrl.PathAndQuery, handler.Requests[1].PathAndQuery);
    }

    [Fact]
    public async Task ASuccessWithoutItsPayloadThrowsWithTheRequestId()
    {
        StubHandler handler = new(Fixtures.SingularWithoutResults);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Stocks.ListSmaAsync("AAPL", cancellationToken: Ct));

            Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
            Assert.Equal("r", exception.RequestId);
            Assert.Contains("carried no 'results' payload", exception.Message, StringComparison.Ordinal);
            Assert.Contains("/v1/indicators/sma/AAPL", exception.Message, StringComparison.Ordinal);
        }
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/StocksLastTradeTests.cs`:

```csharp
using System.Net;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The first unpaginated singular result: one struct under <c>results</c>, returned as itself
/// rather than as an array of one, with nanosecond timestamps exposed as instants (D-S4).
/// </summary>
public sealed class StocksLastTradeTests
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
        StubHandler handler = new(Fixtures.StocksLastTrade);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.GetLastTradeAsync("AAPL", Ct);
        }

        Assert.Equal("https://api.massive.com/v2/last/trade/AAPL", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.StocksLastTrade);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        LastTrade trade;

        using (client)
        using (transport)
        {
            trade = await client.Stocks.GetLastTradeAsync("AAPL", Ct);
        }

        Assert.Equal("AAPL", trade.Ticker);
        Assert.Equal(129.8473, trade.Price);
        Assert.Equal(25d, trade.Size);
        Assert.Equal("25.0", trade.DecimalSize);
        Assert.Equal(3135876, trade.SequenceNumber);
        Assert.Equal("118749", trade.TradeId);
        Assert.Equal(4, trade.ExchangeId);
        Assert.Equal(202, trade.TrfId);
        Assert.Equal(3, trade.Tape);
        Assert.Equal([37], trade.Conditions!);
        Assert.Null(trade.CorrectionIndicator);

        // Nanosecond precision survives the conversion: 1617901342969834000 ns is 2021-04-08
        // 16:22:22.969834000 UTC, which FromUnixTimeTicks would have rounded to the 100 ns tick.
        Assert.Equal(1617901342969834000, trade.SipTimestampNanoseconds);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1617901342969834000), trade.SipTimestamp);
        Assert.Equal(new LocalDate(2021, 4, 8), trade.SipTimestamp.InUtc().Date);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1617901342968000000), trade.ParticipantTimestamp);
        Assert.Equal(NodaConstants.UnixEpoch + Duration.FromNanoseconds(1617901342969796400), trade.TrfTimestamp);
    }

    [Fact]
    public async Task AnAbsentTrfTimestampIsNull()
    {
        string body = Fixtures.StocksLastTrade.Replace("\"f\": 1617901342969796400,", "", StringComparison.Ordinal);
        StubHandler handler = new(body);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        LastTrade trade;

        using (client)
        using (transport)
        {
            trade = await client.Stocks.GetLastTradeAsync("AAPL", Ct);
        }

        Assert.Null(trade.TrfTimestampNanoseconds);
        Assert.Null(trade.TrfTimestamp);
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
                () => client.Stocks.GetLastTradeAsync("AAPL", Ct));

            Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
            Assert.Equal("r", exception.RequestId);
            Assert.Contains("/v2/last/trade/AAPL", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ANullBodyThrowsWithoutARequestId()
    {
        StubHandler handler = new("null");
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Stocks.GetLastTradeAsync("AAPL", Ct));

            Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
            Assert.Null(exception.RequestId);
        }
    }

    [Fact]
    public void RejectsABlankTickerBeforeIssuingARequest()
    {
        StubHandler handler = new(Fixtures.StocksLastTrade);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            Assert.Throws<ArgumentException>(() => client.Stocks.GetLastTradeAsync("  ", Ct));
        }

        Assert.Null(handler.LastRequestUri);
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/StocksOpenCloseTests.cs`:

```csharp
using System.Net;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The first body-object result: no envelope, the model deserialized straight from the body, with
/// the envelope-level <c>status</c> as one of its members (D-S5).
/// </summary>
public sealed class StocksOpenCloseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly LocalDate Session = new(2023, 1, 9);

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task BuildsTheDocumentedRequestPath()
    {
        StubHandler handler = new(Fixtures.StocksOpenClose);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Stocks.GetDailyOpenCloseAsync("AAPL", Session, adjusted: true, cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v1/open-close/AAPL/2023-01-09?adjusted=true", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.StocksOpenClose);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        DailyOpenClose day;

        using (client)
        using (transport)
        {
            day = await client.Stocks.GetDailyOpenCloseAsync("AAPL", Session, cancellationToken: Ct);
        }

        Assert.Equal("AAPL", day.Symbol);
        Assert.Equal(Session, day.From);
        Assert.Equal(324.66, day.Open);
        Assert.Equal(326.2, day.High);
        Assert.Equal(322.3, day.Low);
        Assert.Equal(325.12, day.Close);
        Assert.Equal(26122646, day.Volume);
        Assert.Equal(324.5, day.PreMarket);
        Assert.Equal(322.1, day.AfterHours);
        Assert.Equal("OK", day.Status);
        Assert.False(day.IsOtc);
    }

    [Fact]
    public async Task ANullBodyThrowsWithStatusOkAndNoRequestId()
    {
        StubHandler handler = new("null");
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Stocks.GetDailyOpenCloseAsync("AAPL", Session, cancellationToken: Ct));

            Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
            Assert.Null(exception.RequestId);
            Assert.Contains("carried no payload", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnErrorStatusSurfacesAsAnException()
    {
        // The live probe found that a date with no session is a 404. The singular path must
        // let the transport's error handling run before it ever looks for a payload.
        StubHandler handler = new(HttpStatusCode.NotFound, """{"status":"NOT_FOUND","request_id":"r","message":"Data not found."}""");
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Stocks.GetDailyOpenCloseAsync("AAPL", new LocalDate(2023, 1, 7), cancellationToken: Ct));

            Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
            Assert.Equal("r", exception.RequestId);
        }
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/ReferenceMarketHolidaysTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The first body-array result: a bare JSON array with no envelope, deserialized as an array of
/// the model and coalesced to empty when the body is null (D-S5).
/// </summary>
public sealed class ReferenceMarketHolidaysTests
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
        StubHandler handler = new(Fixtures.MarketHolidays);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListMarketHolidaysAsync(Ct);
        }

        Assert.Equal("https://api.massive.com/v1/marketstatus/upcoming", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.MarketHolidays);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MarketHoliday[] holidays;

        using (client)
        using (transport)
        {
            holidays = await client.Reference.ListMarketHolidaysAsync(Ct);
        }

        Assert.Equal(5, holidays.Length);

        Assert.Equal(new LocalDate(2020, 11, 26), holidays[0].Date);
        Assert.Equal("NYSE", holidays[0].Exchange);
        Assert.Equal("Thanksgiving", holidays[0].Name);
        Assert.Equal("closed", holidays[0].Status);
        Assert.Null(holidays[0].Open);
        Assert.Null(holidays[0].Close);

        Assert.Equal("early-close", holidays[3].Status);
        Assert.Equal("NASDAQ", holidays[3].Exchange);
        Assert.Equal(Instant.FromUtc(2020, 11, 27, 14, 30), holidays[3].Open);
        Assert.Equal(Instant.FromUtc(2020, 11, 27, 18, 0), holidays[3].Close);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task AnEmptyOrNullBodyYieldsAnEmptyArray(string body)
    {
        StubHandler handler = new(body);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MarketHoliday[] holidays;

        using (client)
        using (transport)
        {
            holidays = await client.Reference.ListMarketHolidaysAsync(Ct);
        }

        Assert.Empty(holidays);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~StocksIndicatorsTests|FullyQualifiedName~StocksLastTradeTests|FullyQualifiedName~StocksOpenCloseTests|FullyQualifiedName~ReferenceMarketHolidaysTests"`
Expected: build FAILS with `CS1061: 'StocksGroup' does not contain a definition for 'ListSmaAsync'` and `CS0246` for `IndicatorSeries`, `LastTrade`, `DailyOpenClose`, `MarketHoliday`.

- [ ] **Step 4: Add the models and endpoints to the map**

In `specs/endpoints.map.json`, add to `models` after `NewsInsight`:

```json
    "IndicatorSeries": {
      "summary": "One page of a technical indicator: the values computed for the page, and the aggregates they were computed from.",
      "remarks": "The values continue across pages; the underlying belongs to each page alone, which is why <c>Enumerate</c> yields only values and <c>List</c> returns the whole page (decision D17). A container rather than a tick, so a class (decision D4).",
      "schema": { "operationId": "SMA", "pointer": "results" },
      "items": "values",
      "properties": {
        "values":     { "name": "Values",     "model": "IndicatorValue" },
        "underlying": { "name": "Underlying", "model": "IndicatorUnderlying" }
      }
    },

    "IndicatorValue": {
      "kind": "struct",
      "summary": "One point of a technical indicator series: the value, and the timestamp of the last aggregate that produced it.",
      "remarks": "A struct, like <see cref=\"Agg\"/> (decision D4): a series is read as a whole and nobody null-checks a point. <see cref=\"Timestamp\"/> is computed from <see cref=\"TimestampMilliseconds\"/> only when read (decision D5).",
      "schema": { "operationId": "SMA", "pointer": "results/values/items" },
      "properties": {
        "timestamp": { "name": "TimestampMilliseconds", "type": "long", "summary": "The Unix millisecond timestamp of the last aggregate used in this calculation." },
        "value":     { "name": "Value", "type": "double", "summary": "The indicator's value at this point." }
      }
    },

    "IndicatorUnderlying": {
      "summary": "The aggregates a page of indicator values was computed from, and the aggregates request that produced them.",
      "remarks": "<see cref=\"Aggregates\"/> is present only when the request set <c>expand_underlying</c>; <see cref=\"Url\"/> is always present. Each page carries its own, overlapping the previous page's by the indicator's window.",
      "schema": { "operationId": "SMA", "pointer": "results/underlying" },
      "properties": {
        "url":        { "name": "Url" },
        "aggregates": { "name": "Aggregates", "model": "Agg" }
      }
    },

    "LastTrade": {
      "kind": "struct",
      "summary": "The most recent trade for a ticker: price, size, exchange, conditions, and three nanosecond timestamps.",
      "remarks": "A tick-level type, so a struct (decision D4). The v2 wire keys are single letters, which the map names. Timestamps are stored as nanosecond <see cref=\"long\"/> values and exposed as <see cref=\"NodaTime.Instant\"/> only when read (decision D5).",
      "schema": { "operationId": "LastTrade", "pointer": "results" },
      "properties": {
        "T":  { "name": "Ticker" },
        "t":  { "name": "SipTimestampNanoseconds", "type": "long", "summary": "The nanosecond Unix timestamp at which the SIP received this trade from the exchange that produced it." },
        "y":  { "name": "ParticipantTimestampNanoseconds", "type": "long", "summary": "The nanosecond Unix timestamp at which the trade was generated at the exchange." },
        "f":  { "name": "TrfTimestampNanoseconds", "type": "long?", "summary": "The nanosecond Unix timestamp at which the trade reporting facility received this trade, when it passed through one." },
        "q":  { "name": "SequenceNumber" },
        "i":  { "name": "TradeId" },
        "p":  { "name": "Price" },
        "s":  { "name": "Size" },
        "ds": { "name": "DecimalSize" },
        "x":  { "name": "ExchangeId" },
        "r":  { "name": "TrfId" },
        "c":  { "name": "Conditions" },
        "e":  { "name": "CorrectionIndicator" },
        "z":  { "name": "Tape" }
      }
    },

    "DailyOpenClose": {
      "summary": "The open, high, low, close, and volume for one ticker on one trading day, with its pre-market and after-hours prices.",
      "remarks": "The response body is the payload, so the envelope's <see cref=\"Status\"/> is a member here (decision D17). <see cref=\"From\"/> is a calendar date, hence <see cref=\"NodaTime.LocalDate\"/>.",
      "schema": { "operationId": "GetStocksOpenClose" },
      "properties": {
        "symbol":     { "name": "Symbol" },
        "from":       { "name": "From" },
        "open":       { "name": "Open" },
        "high":       { "name": "High" },
        "low":        { "name": "Low" },
        "close":      { "name": "Close" },
        "volume":     { "name": "Volume" },
        "preMarket":  { "name": "PreMarket", "type": "double?", "summary": "The open price in pre-market trading. The description calls this an integer; it is a price." },
        "afterHours": { "name": "AfterHours" },
        "otc":        { "name": "IsOtc", "type": "bool", "summary": "Whether this ticker trades over the counter. The API omits the field entirely when false, which deserializes to false here." },
        "status":     { "name": "Status" }
      }
    },

    "MarketHoliday": {
      "summary": "An upcoming market holiday or early close for one exchange.",
      "remarks": "The response body is a bare array of these (decision D17). <see cref=\"Date\"/> is a calendar date; <see cref=\"Open\"/> and <see cref=\"Close\"/> are moments, present only for an early close.",
      "schema": { "operationId": "GetMarketHolidays", "pointer": "items" },
      "properties": {
        "date":     { "name": "Date",     "type": "LocalDate" },
        "exchange": { "name": "Exchange" },
        "name":     { "name": "Name" },
        "status":   { "name": "Status" },
        "open":     { "name": "Open",  "type": "Instant?" },
        "close":    { "name": "Close", "type": "Instant?" }
      }
    }
```

Add to `endpoints` after `ListNews`:

```json
    {
      "operationId": "SMA",
      "group": "Stocks",
      "method": "ListSma",
      "summary": "Retrieves the simple moving average (SMA) of a stock's price over a window of aggregates.",
      "remarks": "Each page carries the values computed for it and, with <paramref name=\"expandUnderlying\"/>, the aggregates they were computed from. <paramref name=\"timespan\"/> accepts every <see cref=\"AggregateTimespan\"/> except <see cref=\"AggregateTimespan.Second\"/>, which this endpoint does not offer and rejects with a 400.",
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
      "operationId": "LastTrade",
      "group": "Stocks",
      "method": "GetLastTrade",
      "summary": "Retrieves the most recent trade for a stock.",
      "remarks": "An unknown ticker is a 404, surfaced as <see cref=\"MassiveApiException\"/>, not an empty result.",
      "result": { "kind": "object", "model": "LastTrade", "property": "results" },
      "parameters": {
        "stocksTicker": { "name": "ticker" }
      }
    },
    {
      "operationId": "GetStocksOpenClose",
      "group": "Stocks",
      "method": "GetDailyOpenClose",
      "summary": "Retrieves the open, high, low, close, and volume for a stock on one trading day, with its pre-market and after-hours prices.",
      "remarks": "A date with no session, such as a weekend or a holiday, is a 404, surfaced as <see cref=\"MassiveApiException\"/>.",
      "result": { "kind": "object", "model": "DailyOpenClose" },
      "parameters": {
        "stocksTicker": { "name": "ticker" },
        "date":         { "name": "date" },
        "adjusted":     { "name": "adjusted" }
      }
    },
    {
      "operationId": "GetMarketHolidays",
      "group": "Reference",
      "method": "ListMarketHolidays",
      "summary": "Retrieves upcoming market holidays and early closes, one entry per exchange.",
      "result": { "kind": "array", "model": "MarketHoliday" }
    }
```

Also extend the file's leading `"//"` comment array with one line after `"  * property names for anonymous result schemas, and a name for every nested object"`:

```json
    "  * where the payload sits: one model or an array of it, on a named property or as the body",
```

- [ ] **Step 5: Add the partials with the computed instants**

Create `src/MassiveDotNet.Rest/Models/IndicatorValue.cs`:

```csharp
using System.Text.Json.Serialization;
using NodaTime;

namespace MassiveDotNet.Rest.Models;

/// <summary>
/// Hand-written members of <see cref="IndicatorValue"/>, alongside the generated wire properties.
/// </summary>
public readonly partial record struct IndicatorValue
{
    /// <summary>
    /// The moment of the last aggregate used to compute this value, converted from
    /// <see cref="TimestampMilliseconds"/>.
    /// </summary>
    /// <remarks>
    /// The raw <see cref="long"/> is what gets deserialized and stored; this conversion happens
    /// only when read, so a long series costs nothing until the value is actually wanted.
    /// </remarks>
    [JsonIgnore]
    public Instant Timestamp => Instant.FromUnixTimeMilliseconds(TimestampMilliseconds);
}
```

Create `src/MassiveDotNet.Rest/Models/LastTrade.cs`:

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
    public Instant SipTimestamp => FromNanoseconds(SipTimestampNanoseconds);

    /// <summary>The moment the exchange generated this trade, converted from <see cref="ParticipantTimestampNanoseconds"/>.</summary>
    [JsonIgnore]
    public Instant ParticipantTimestamp => FromNanoseconds(ParticipantTimestampNanoseconds);

    /// <summary>
    /// The moment the trade reporting facility received this trade, converted from
    /// <see cref="TrfTimestampNanoseconds"/>, or <see langword="null"/> when the trade did not pass
    /// through one.
    /// </summary>
    [JsonIgnore]
    public Instant? TrfTimestamp => TrfTimestampNanoseconds is { } nanoseconds ? FromNanoseconds(nanoseconds) : null;

    // Nanosecond precision is kept: Instant resolves to the nanosecond and a Duration built from
    // nanoseconds loses nothing, whereas Instant.FromUnixTimeTicks would truncate to 100 ns. The
    // conversion happens only when read (decision D5).
    private static Instant FromNanoseconds(long nanoseconds) =>
        NodaConstants.UnixEpoch + Duration.FromNanoseconds(nanoseconds);
}
```

- [ ] **Step 6: Regenerate and build**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: prints `coverage 7/147 operations (4.8%)` and lists six new model files. If it refuses, the message names the row to fix; the likeliest is an `Agg` reuse difference at `results/underlying/aggregates/items`, which would mean the description changed since this plan was written and needs a second model rather than a forced reuse.

Run: `dotnet build MassiveDotNet.slnx`
Expected: 0 warnings, 0 errors. Check the generated surface by eye before testing: `StocksGroup.g.cs` opens with `using System.Net;`, `SMAResponse` in `Envelopes.g.cs` implements `IPagedEnvelope<IndicatorValue>` through `Results?.Values`, `MassiveRestJsonContext.g.cs` registers `DailyOpenClose` and `MarketHoliday[]`, and `Models/LastTrade.g.cs` declares `public required string Ticker`, `public long SipTimestampNanoseconds`, and `public long? TrfTimestampNanoseconds`.

- [ ] **Step 7: Raise the coverage baseline and run the tests**

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, change:

```csharp
    private const int CoverageBaseline = 7;
```

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS across all three offline projects, including the four new test classes, `EndpointCoverageTests`, and `TemporalTypeTests`, which now reflects over the six new models and their computed instants.

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff; generation is idempotent.

- [ ] **Step 8: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Models/IndicatorValue.cs src/MassiveDotNet.Rest/Models/LastTrade.cs src/MassiveDotNet.Rest/Generated tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/StocksIndicatorsTests.cs tests/MassiveDotNet.Rest.Tests/StocksLastTradeTests.cs tests/MassiveDotNet.Rest.Tests/StocksOpenCloseTests.cs tests/MassiveDotNet.Rest.Tests/ReferenceMarketHolidaysTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs
git commit -m "feat: map SMA, last trade, daily open/close, and market holidays

One endpoint per new generator path: a paginated object with items,
an object under results, a body object, and a body array. Six models,
two partials with computed instants, and fixtures from the published
samples. Coverage baseline rises to 7.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 5: AOT smoke, live tests, and CLAUDE.md

**Files:**
- Modify: `samples/MassiveDotNet.AotSmokeTest/Program.cs`
- Create: `tests/MassiveDotNet.IntegrationTests/StocksIndicatorsLiveTests.cs`
- Create: `tests/MassiveDotNet.IntegrationTests/StocksOpenCloseLiveTests.cs`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: `StocksGroup.ListSmaAsync`, `EnumerateSmaAsync`, `GetLastTradeAsync`, `GetDailyOpenCloseAsync`, `ReferenceGroup.ListMarketHolidaysAsync`, and the six models (Task 4); `MassivePagedResult<T>`, `SeriesType` (Task 1).
- Produces: nothing new.

- [ ] **Step 1: Root every new shape in the AOT sample**

In `samples/MassiveDotNet.AotSmokeTest/Program.cs`, after the news block's `FAIL` check and before `Console.WriteLine($"\nrequests: {handler.Requests}");`, add:

```csharp
// Singular results are new generic instantiations and new context registrations, each of which a
// clean publish says nothing about unless something here reaches it: MassivePagedResult<T>, the
// explicit IPagedEnvelope<IndicatorValue> path that EnumerateAsync walks through Results?.Values,
// a struct payload under results, a body object, and a body array's MarketHoliday[] type info.
Console.WriteLine("\nsma, one page with its underlying:");

MassivePagedResult<IndicatorSeries> sma = await client.Stocks.ListSmaAsync(
    "AAPL",
    timespan: AggregateTimespan.Day,
    window: 10,
    seriesType: SeriesType.Close,
    expandUnderlying: true,
    limit: 1);

Console.WriteLine($"request : {handler.LastRequestUri}");
Console.WriteLine($"values  : {sma.Result.Values?.Length} (more: {sma.HasMore}, underlying: {sma.Result.Underlying?.Aggregates?.Length} aggregates)");

const string ExpectedSmaQuery = "?timespan=day&window=10&series_type=close&expand_underlying=true&limit=1";

if (handler.LastRequestUri?.Query != ExpectedSmaQuery)
{
    Console.Error.WriteLine($"FAIL: expected the SMA query {ExpectedSmaQuery}; got {handler.LastRequestUri?.Query}.");
    return 1;
}

if (!sma.HasMore
    || sma.Result.Values is not [{ TimestampMilliseconds: 1517562000016 }]
    || sma.Result.Underlying?.Aggregates is not { Length: 2 })
{
    Console.Error.WriteLine("FAIL: expected one SMA value at 1517562000016 with two underlying aggregates and more pages.");
    return 1;
}

Console.WriteLine("\nsma, enumerating every value:");

int smaValues = 0;

await foreach (IndicatorValue value in client.Stocks.EnumerateSmaAsync("AAPL", limit: 1))
{
    smaValues++;
    Console.WriteLine($"  {LocalDatePattern.Iso.Format(value.Timestamp.InUtc().Date)}  {value.Value,9:F3}");
}

if (smaValues != 2)
{
    Console.Error.WriteLine($"FAIL: expected 2 SMA values across two pages; got {smaValues}.");
    return 1;
}

Console.WriteLine("\nlast trade:");

LastTrade trade = await client.Stocks.GetLastTradeAsync("AAPL");
Console.WriteLine($"  {trade.Ticker}  {trade.Price:F4} x {trade.Size}  at {InstantPattern.ExtendedIso.Format(trade.SipTimestamp)}");

if (trade.Ticker != "AAPL" || trade.SipTimestampNanoseconds != 1617901342969834000)
{
    Console.Error.WriteLine("FAIL: expected the AAPL trade at 1617901342969834000.");
    return 1;
}

Console.WriteLine("\ndaily open/close:");

DailyOpenClose day = await client.Stocks.GetDailyOpenCloseAsync("AAPL", new LocalDate(2023, 1, 9));
Console.WriteLine($"  {day.Symbol}  {LocalDatePattern.Iso.Format(day.From)}  O {day.Open:F2}  C {day.Close:F2}  {day.Status}");

if (day.From != new LocalDate(2023, 1, 9) || day.Close != 325.12)
{
    Console.Error.WriteLine("FAIL: expected AAPL on 2023-01-09 closing at 325.12.");
    return 1;
}

Console.WriteLine("\nmarket holidays:");

MarketHoliday[] holidays = await client.Reference.ListMarketHolidaysAsync();

foreach (MarketHoliday holiday in holidays)
{
    Console.WriteLine($"  {LocalDatePattern.Iso.Format(holiday.Date)}  {holiday.Exchange,-6}  {holiday.Status,-11}  {holiday.Name}");
}

if (holidays is not [.., { Status: "early-close", Open: not null }])
{
    Console.Error.WriteLine("FAIL: expected the last holiday to be an early close with an open time.");
    return 1;
}
```

Change the final request-count check and its comment to:

```csharp
// Two pages of the aggregates enumeration, the single-page aggregates call, the dividends call,
// the news call, one SMA page, two SMA pages enumerated, the last trade, the open/close day, and
// the holidays.
if (enumerated != 3 || handler.Requests != 11)
{
    Console.Error.WriteLine(
        $"FAIL: expected 3 enumerated bars over 11 requests; got {enumerated} over {handler.Requests}.");
    return 1;
}
```

In the `StubHandler` class, add five constants after `News`:

```csharp
    private const string Sma = """
        {
          "next_url": "https://api.massive.com/v1/indicators/sma/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "a47d1beb8c11b6ae897ab76cdbbf35a3",
          "results": {
            "underlying": {
              "aggregates": [
                { "c": 75.0875, "h": 75.15, "l": 73.7975, "n": 1, "o": 74.06, "t": 1577941200000, "v": 135647456, "vw": 74.6099 },
                { "c": 74.3575, "h": 75.145, "l": 74.125, "n": 1, "o": 74.2875, "t": 1578027600000, "v": 146535512, "vw": 74.7026 }
              ],
              "url": "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2003-01-01/2022-07-25"
            },
            "values": [
              { "timestamp": 1517562000016, "value": 140.139 }
            ]
          },
          "status": "OK"
        }
        """;

    private const string SmaLastPage = """
        {
          "request_id": "0d5b6f1e9f3c4a7b8e2d1c0f9a8b7c6d",
          "results": {
            "underlying": {
              "url": "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2003-01-01/2022-07-24"
            },
            "values": [
              { "timestamp": 1517475600016, "value": 139.871 }
            ]
          },
          "status": "OK"
        }
        """;

    private const string LastTradeBody = """
        {
          "request_id": "f05562305bd26ced64b98ed68b3c5d96",
          "results": {
            "T": "AAPL",
            "c": [ 37 ],
            "ds": "25.0",
            "f": 1617901342969796400,
            "i": "118749",
            "p": 129.8473,
            "q": 3135876,
            "r": 202,
            "s": 25,
            "t": 1617901342969834000,
            "x": 4,
            "y": 1617901342968000000,
            "z": 3
          },
          "status": "OK"
        }
        """;

    private const string OpenClose = """
        {
          "afterHours": 322.1,
          "close": 325.12,
          "from": "2023-01-09",
          "high": 326.2,
          "low": 322.3,
          "open": 324.66,
          "preMarket": 324.5,
          "status": "OK",
          "symbol": "AAPL",
          "volume": 26122646
        }
        """;

    private const string Holidays = """
        [
          { "date": "2020-11-26", "exchange": "NYSE", "name": "Thanksgiving", "status": "closed" },
          { "date": "2020-11-26", "exchange": "NASDAQ", "name": "Thanksgiving", "status": "closed" },
          { "close": "2020-11-27T18:00:00.000Z", "date": "2020-11-27", "exchange": "NYSE", "name": "Thanksgiving", "open": "2020-11-27T14:30:00.000Z", "status": "early-close" }
        ]
        """;
```

and replace the body selection in `SendAsync` (everything from `string body;` through the closing brace of the `else`) with one switch:

```csharp
        // Keyed on the cursor rather than on a request counter, so the single-page calls and the
        // traversals stay independent of the order they happen to run in.
        bool cursored = request.RequestUri?.Query.Contains("cursor=", StringComparison.Ordinal) == true;

        string body = request.RequestUri?.AbsolutePath switch
        {
            "/stocks/v1/dividends" => Dividends,
            "/v2/reference/news" => News,
            "/v1/indicators/sma/AAPL" => cursored ? SmaLastPage : Sma,
            "/v2/last/trade/AAPL" => LastTradeBody,
            "/v1/open-close/AAPL/2023-01-09" => OpenClose,
            "/v1/marketstatus/upcoming" => Holidays,
            _ => cursored ? FinalPage : FirstPage,
        };
```

- [ ] **Step 2: Run the sample under the ordinary runtime, then publish it**

Run: `dotnet run --project samples/MassiveDotNet.AotSmokeTest`
Expected: prints the SMA, last trade, open/close, and holiday lines and ends with `AOT smoke test passed.`

Run: `dotnet publish samples/MassiveDotNet.AotSmokeTest -r osx-arm64 -c Release 2>&1 | tee aot.log; grep -E ': (warning|error) (IL|AOT|Trim)?[0-9]{4}' aot.log; echo "exit=$?"` (use `linux-x64` on Linux)
Expected: the grep prints nothing and reports `exit=1` (no match). Then run the published binary, `samples/MassiveDotNet.AotSmokeTest/bin/Release/net10.0/osx-arm64/publish/MassiveDotNet.AotSmokeTest`, and confirm it ends with `AOT smoke test passed.` Delete `aot.log` afterwards.

- [ ] **Step 3: Write the live tests**

Create `tests/MassiveDotNet.IntegrationTests/StocksIndicatorsLiveTests.cs`:

```csharp
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Verifies the paginated singular shape against the real service. A fixture proves the SDK reads
/// a recording; only a live traversal proves the indicator endpoints still page over
/// <c>results.values</c> and that the cursor contract holds across a real boundary.
/// </summary>
public sealed class StocksIndicatorsLiveTests : LiveApiTest
{
    // A fixed historical window, so the assertions do not depend on when the suite is run.
    private static readonly LocalDate WindowStart = new(2024, 1, 1);
    private static readonly LocalDate WindowEnd = new(2024, 3, 31);

    [Fact]
    public async Task CrossesRealPageBoundaries()
    {
        // limit is per page, so five values at two per page is at least three round trips.
        List<IndicatorValue> values = [];

        await foreach (IndicatorValue value in Client.Stocks.EnumerateSmaAsync(
            "AAPL",
            timestamp: RangeFilter.Between(DateOrTimestamp.FromDate(WindowStart), DateOrTimestamp.FromDate(WindowEnd)),
            timespan: AggregateTimespan.Day,
            window: 10,
            limit: 2,
            cancellationToken: Ct))
        {
            values.Add(value);

            if (values.Count >= 5)
            {
                break;
            }
        }

        Assert.Equal(5, values.Count);

        // Values must be strictly ordered across the page seams, which is where an incorrectly
        // rebuilt cursor would show up as repeated or skipped points. The endpoint's default
        // order is descending.
        Assert.Equal(
            values.Select(v => v.TimestampMilliseconds).OrderDescending(),
            values.Select(v => v.TimestampMilliseconds));
        Assert.Equal(5, values.Select(v => v.TimestampMilliseconds).Distinct().Count());
    }

    [Fact]
    public async Task ListReportsMorePagesAndCarriesTheUnderlying()
    {
        MassivePagedResult<IndicatorSeries> page = await Client.Stocks.ListSmaAsync(
            "AAPL",
            timestamp: RangeFilter.Between(DateOrTimestamp.FromDate(WindowStart), DateOrTimestamp.FromDate(WindowEnd)),
            timespan: AggregateTimespan.Day,
            window: 10,
            expandUnderlying: true,
            limit: 2,
            cancellationToken: Ct);

        Assert.True(page.HasMore);
        Assert.NotNull(page.RequestId);
        Assert.NotNull(page.Result.Values);
        Assert.Equal(2, page.Result.Values.Length);
        Assert.NotNull(page.Result.Underlying);
        Assert.NotNull(page.Result.Underlying.Aggregates);
        Assert.NotEmpty(page.Result.Underlying.Aggregates);

        // The underlying URL is a plain aggregates request and must never carry a key (rule 11).
        Assert.DoesNotContain("apiKey", page.Result.Underlying.Url, StringComparison.OrdinalIgnoreCase);
    }
}
```

Create `tests/MassiveDotNet.IntegrationTests/StocksOpenCloseLiveTests.cs`:

```csharp
using System.Net;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Verifies the body-object shape against the real service, on the endpoint whose published
/// sample is likeliest to have drifted, and confirms the 404 the singular contract rests on.
/// </summary>
public sealed class StocksOpenCloseLiveTests : LiveApiTest
{
    // A fixed historical session, so the assertions do not depend on when the suite is run.
    private static readonly LocalDate Session = new(2024, 1, 5);

    [Fact]
    public async Task ReturnsTheSessionForAKnownTickerAndDate()
    {
        DailyOpenClose day = await Client.Stocks.GetDailyOpenCloseAsync("AAPL", Session, cancellationToken: Ct);

        Assert.Equal("AAPL", day.Symbol);
        Assert.Equal(Session, day.From);
        Assert.Equal("OK", day.Status);
        Assert.True(day.High >= day.Low, $"High {day.High} was below low {day.Low}.");
        Assert.True(day.Open > 0 && day.Close > 0, "Prices should be positive.");
        Assert.True(day.Volume > 0, "A trading day should report volume.");
    }

    [Fact]
    public async Task ADateWithNoSessionIsANotFoundError()
    {
        // The live probe behind decision D17: a 200 always carries its payload, and a day with no
        // session is a 404. This is the assumption the "Get returns T, never T?" contract rests on.
        MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(() =>
            Client.Stocks.GetDailyOpenCloseAsync("AAPL", new LocalDate(2024, 1, 6), cancellationToken: Ct));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }
}
```

- [ ] **Step 4: Run the live tests locally**

Run: `dotnet test tests/MassiveDotNet.IntegrationTests --filter "FullyQualifiedName~StocksIndicatorsLiveTests|FullyQualifiedName~StocksOpenCloseLiveTests"`
Expected: PASS, 4 tests. `LiveCredentials` reads the key from the gitignored `.env` at the repository root; never print it and never add it to any other file. If `CrossesRealPageBoundaries` fails on ordering, report the timestamps it saw; that is a finding about the service's cursor, not something to patch in the test.

- [ ] **Step 5: Update CLAUDE.md**

In the **Architecture decisions** table, add after the D16 row:

```markdown
| D17 | The `result` row has two kinds, `array` and `object`, and an omitted `property` means the body is the payload. A paginated object result returns `MassivePagedResult<T>` and enumerates the array its model row names as `items`; a paginated object with no `items` keeps a `Get` whose cursor, if one ever arrives, throws. Every singular `Get` returns `T`, and a 200 without its payload throws. | Fifty operations are not an array under `results`: 28 return one object there, 20 of which — the indicators — genuinely paginate over `results.values` with a per-page `underlying`, and 22 have no `results` at all. `MassivePage<T>` cannot hold an object with two halves, and discarding `underlying` would silently drop what `expand_underlying` asked for. Pagination stays spec-detected: the map only says where the items are, so it cannot drift. `Task<T?>` on every `Get` was rejected because the description's requiredness is unreliable and every unknown-ticker probe returned 404; a 200 without a payload is the same class of failure as a body that will not deserialize, and is reported the same way. |
```

In **Adding endpoints**, extend step 1 with one sentence after the D16 sentence:

```markdown
   The `result` row says whether the payload is one model or an array of it (`kind`) and where it
   sits (`property`, omitted when the body itself is the payload); a paginated object's model row
   names the array it enumerates as `items` (D17).
```

In **Temporal types › Vocabulary**, change the `Instant` row's example to:

```markdown
| A moment on the global timeline | `Instant` | `Agg.Timestamp`, `LastTrade.SipTimestamp`, `NewsArticle.PublishedUtc` |
```

In **Conventions**, replace the **Pagination** bullet with:

```markdown
- **Pagination**: the 100 operations whose success schema declares `next_url` get two methods —
  `ListXxxAsync` returning `MassivePage<T>` (one page, reporting whether more exist) and
  `EnumerateXxxAsync` returning `IAsyncEnumerable<T>` (every page, one in flight at a time).
  When the page is one object rather than an array, `List` returns `MassivePagedResult<T>` and
  `Enumerate` yields the elements of the array the model row names as `items` (D17). The 47 that
  do not paginate return `T[]`, or `T` for a singular result. Pagination is detected from the
  spec, never declared in the map. `Enumerate`/`List` follows the BCL's `Directory.EnumerateFiles` /
  `Directory.GetFiles` distinction; avoid "Stream", which in this SDK means WebSockets.
```

and extend the **Models** bullet with two sentences before "Names are domain nouns":

```markdown
  A model may also be an endpoint's whole payload: `kind: object` with a `property` binds one
  object on the envelope, and an omitted `property` binds the body itself, whose model row omits
  `pointer` (or points at `items` for a body array). A paginated object's model row names its
  `items` once, and the generator refuses one that is absent, not an array of objects, or unbound
  (D17).
```

- [ ] **Step 6: Run the full verification set**

```bash
dotnet build MassiveDotNet.slnx
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/
```

Expected: 0 warnings; every offline test passes across all three test projects; no diff. `TemporalTypeTests` scans the new live test files too.

- [ ] **Step 7: Commit**

```bash
git add samples/MassiveDotNet.AotSmokeTest/Program.cs tests/MassiveDotNet.IntegrationTests/StocksIndicatorsLiveTests.cs tests/MassiveDotNet.IntegrationTests/StocksOpenCloseLiveTests.cs CLAUDE.md
git commit -m "feat: root every singular shape in the AOT smoke test and record decision D17

The sample now deserializes a paged indicator, enumerates its values
across two pages, and reads a struct payload, a body object, and a
body array, so the publish proves each new instantiation is AOT clean.
Two live tests cross a real SMA page boundary and confirm the 404 the
singular contract rests on. CLAUDE.md records D17 and the map keys.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```
