# Reference Core Implementation Plan (Plan A)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Map the seventeen reference-core operations from issue #9 into `client.Reference`: tickers and everything about them, market status, conditions, exchanges, the v3 corporate actions, options contracts, both IPO routes, short interest, short volume, and float, with a `ContractType` enum in core, a generator that sees through a one-branch `oneOf`, fixtures from the published examples or a reviewed live capture, and a live tier that pins what fixtures cannot see.

**Architecture:** Two small generator and core changes come first: `ContractType` registered in `TypeBinding`, and `Spec` reading a one-branch `oneOf` as its branch. Then twenty-seven model rows and seventeen endpoint rows in `specs/endpoints.map.json`, regenerated output, no hand-written partials (every timestamp here is a calendar date or an RFC 3339 string the converters read), and one offline test class per family driven through the public API against the stub handler. The AOT smoke test roots the new instantiations, `CLAUDE.md` records D24 and D26, and the live tier runs last.

**Tech Stack:** .NET 10, C# latest, xUnit v3, System.Text.Json source generation, NodaTime 3.3.3. No new package dependencies.

**Spec:** `docs/superpowers/specs/2026-09-03-reference-group-design.md` (D-R1 through D-R13; this plan is Plan A of D-R1)

## Global Constraints

Copied from `CLAUDE.md` and the spec. Every task inherits these.

- **Rule 2** — A `vX` route ships marked `[Experimental("MASSIVE0001")]`, emitted by the generator from the path (D18, D23); the map never declares stability. The REST and integration test projects already carry `<NoWarn>$(NoWarn);MASSIVE0002;MASSIVE0001</NoWarn>`; the AOT sample does not, so it must not call an experimental method. Nothing is ever suppressed inside generated code.
- **Rule 3** — No reflection-based serialization in shipped code. `System.Text.Json` source generation only; every new envelope and body model is registered on `MassiveRestJsonContext` by the generator.
- **Rule 5** — `*.g.cs` files are never hand-edited. Change `specs/endpoints.map.json` or `tools/MassiveDotNet.CodeGen` and regenerate with `dotnet run --project tools/MassiveDotNet.CodeGen`. Commit the regenerated files with the map change that produced them.
- **Rule 6** — The generator is deterministic. Running it twice on the same inputs produces byte-identical output; CI checks `git diff --exit-code src/` after a regeneration.
- **Rule 7** — `MassiveDotNet` (core) references no external package other than NodaTime.
- **Rule 9** — `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are on. An **unused `using` fails the build** (IDE0005). `AnalysisLevel` is `latest-recommended`: **CA1305** (pass a format provider), **CA1307/CA1310** (pass a `StringComparison` to `Contains`, `StartsWith`, `IndexOf`, `Replace` on strings), **CA1861** (hoist constant arrays to `static readonly`), and the naming rules apply to test code too.
- **Rule 10** — Every public member carries XML documentation, or CS1591 fails the build. Every map row therefore supplies a `summary`. Every property of every model in this plan has a description in the description, so property rows need a `summary` only where the row changes the meaning (a renamed or retyped field).
- **Rule 11** — API keys are never logged, echoed in exception messages, or written to disk. The live tests read the key from the gitignored `.env` through `LiveCredentials`; never open, print, or echo that file. The two captured fixtures in this plan were captured and reviewed before the plan was written; no task captures anything.
- **Rule 12** — NodaTime only. No BCL `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, or `TimeSpan` may be *named* anywhere in `src`, `tests`, `samples`, or `tools`. `TemporalTypeTests` scans every one of those directories. The temporal types this plan touches are `Instant` and `LocalDate`.
- **Rule 13** — CI runs offline only. Live tests derive from `LiveApiTest`, which carries `[Trait("Category", "Integration")]`, and live in `tests/MassiveDotNet.IntegrationTests`. No offline test class may carry `LiveTests` in its name.
- **Spec D-R1** — Seventeen operations ship on this branch. `CoverageBaseline` in `EndpointCoverageTests` ends at 40; each endpoint task raises it to the running count.
- **Spec D-R2** — `Spec.Shape` and `Spec.Collect` read a `oneOf` with exactly one branch as that branch. A `oneOf` whose branches are all scalars stays a scalar, as it always has (the news parameters declare one). A `oneOf` with more than one branch of which any is an object is refused.
- **Spec D-R5** — `/vX/reference/ipos` is `ListIposAsync` / `EnumerateIposAsync`; `/v1/reference/ipos` is `ListIposV1Async` / `EnumerateIposV1Async`.
- **Spec D-R6** — `asset_class` and `market` rows name `MarketType`; `order` rows name `SortOrder`; `contract_type` names the new `ContractType { Call = 0, Put = 1 }`, rendered `call` / `put`. Every other enum parameter stays `string`.
- **Spec D-R7** — Models are `ReferenceDividend`, `ReferenceSplit`, `Exchange`, `Ipo`, `IpoV1`, `Ticker`, `TickerDetails`, `CompanyAddress`, `Branding`. All `partial record` classes; no `kind: struct` anywhere in this plan.
- **Spec D-R9** — Bare-string dates whose example shows `yyyy-MM-dd` bind `LocalDate` from the map: `list_date`, `expiration_date`, `as_of`, the four v3 dividend dates, `execution_date`, `settlement_date`, and short volume's `date`.
- **Spec D-R10** — `TickerEvent.event_type` is typed `string?` in the map. `IpoV1`'s three dates stay the `long?` the schema gives them, named with an `Epoch` suffix, no partial. The three `"request_id": 1` examples become `"1"` in their fixtures, commented. `MarketStatus.serverTime` is typed `Instant?`.
- **Spec D-R12** — Fixtures are the published example verbatim except as D-R10 says. `ReferenceTickerTypes` and `ReferenceExchanges` are live captures from 2026-09-03, reviewed, embedded in this plan.
- **Spec D-R13** — The live tier: tickers cross a page boundary at `limit: 2`; `ListIposV1Async` answers 404, pinned dated; ticker events show `EventType` null; market status deserializes its body; the contract get takes its ticker from the list; every other operation gets one shape call.
- **Style** — Explicit types, never `var`; collection expressions (`[]`, `[.. x]`); `is not { } x` null patterns; file-scoped namespaces; raw string literals for JSON. Match the surrounding code. Comments explain *why*.
- **Convention** — Do not commit or push unless asked. Steps below include commits; the user chose the brainstorm-to-plan workflow, which authorizes them on the feature branch `feat/reference-core`. Commit messages end with the trailer `Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB`.
- **Working tree** — Work happens in place on `feat/reference-core`, not in a worktree, because the live tier in Task 11 needs the repository's gitignored `.env`.

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
| `src/MassiveDotNet/ContractType.cs` | **Create.** `Call` or `Put`, the options contract type (D-R6). | 1 |
| `src/MassiveDotNet/MassiveEnumValues.cs` | **Modify.** `ContractType.ToWireValue`. | 1 |
| `tools/MassiveDotNet.CodeGen/TypeBinding.cs` | **Modify.** `CoreEnums` + `ContractType`. | 1 |
| `tests/MassiveDotNet.Rest.Tests/ContractTypeTests.cs` | **Create.** Wire literals. | 1 |
| `tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs` | **Modify.** A `ContractType` parameter renders; a member the enum lacks is refused. | 1 |
| `tools/MassiveDotNet.CodeGen/Spec.cs` | **Modify.** `Unwrap`: a one-branch `oneOf` reads as its branch in `Shape` and `Collect` (D-R2). | 2 |
| `tests/MassiveDotNet.CodeGen.Tests/ModelBindingTests.cs` | **Modify.** Unwrap, scalar union, and object union refusal. | 2 |
| `docs/superpowers/specs/2026-09-03-reference-group-design.md` | **Modify.** D-R2's diagnostic sentence, per the Task 2 ruling. | 2 |
| `specs/endpoints.map.json` | **Modify.** Twenty-seven model rows and seventeen endpoint rows, appended in task order. | 3–9 |
| `src/MassiveDotNet.Rest/Generated/` | **Regenerate** after every map change. | 3–9 |
| `tests/MassiveDotNet.Rest.Tests/Fixtures.cs` | **Modify.** Seventeen fixtures, appended after `StocksDevTrades` in task order. | 3–9 |
| `tests/MassiveDotNet.Rest.Tests/Reference*Tests.cs` | **Create.** One class per family: rendering, deserialization, traversal. | 3–9 |
| `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` | **Modify.** `CoverageBaseline` 23 → 26 → 28 → 31 → 33 → 35 → 37 → 40. | 3–9 |
| `samples/MassiveDotNet.AotSmokeTest/Program.cs` | **Modify.** Market status, a tickers page with `MarketType`, a contracts page with `ContractType`; three stubs. | 10 |
| `CLAUDE.md` | **Modify.** D24, D26; the Enums convention. | 10 |
| `tests/MassiveDotNet.IntegrationTests/Reference*LiveTests.cs` | **Create.** Six classes: the D-R13 pins and one call per operation. | 11 |

Map anchors: every task appends its model rows after the previous task's last model row and its endpoint rows after the previous task's last endpoint row, adding a comma after the row it follows. Task 3 follows `DevTrade` and `get_stocks_dev_trades_ticker`, the last rows in the file today. Fixtures follow the same rule after `StocksDevTrades`.

Generated names to expect: a `"method": "ListX"` row produces `ListXAsync` and, when the operation paginates, `EnumerateXAsync`; a `"method": "GetX"` row produces `GetXAsync`. The generator names each endpoint's envelope `{Method}Response` and registers it, or the body model, on `MassiveRestJsonContext`. Property nullability comes from the schema: a required reference-typed property is `required T`, an optional one `T?`; a required value type has no modifier.

---

### Task 1: `ContractType` in core and in the generator's enum table

**Files:**
- Create: `src/MassiveDotNet/ContractType.cs`
- Modify: `src/MassiveDotNet/MassiveEnumValues.cs` (after the `SnapshotDirection` arm)
- Modify: `tools/MassiveDotNet.CodeGen/TypeBinding.cs` (`CoreEnums`)
- Create: `tests/MassiveDotNet.Rest.Tests/ContractTypeTests.cs`
- Modify: `tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs`

**Interfaces:**
- Consumes: `SnapshotDirection` (`src/MassiveDotNet/SnapshotDirection.cs`) and its `ToWireValue` arm as the pattern; `TypeBinding.CoreEnums`, the table the #37 check reads.
- Produces: `public enum ContractType { Call = 0, Put = 1 }` in namespace `MassiveDotNet`; `MassiveEnumValues.ToWireValue(this ContractType)` returning `"call"` / `"put"`; a map row `"type": "ContractType"` binding a query parameter to `ContractType?` rendered `contractType?.ToWireValue()`. Task 7 maps it.

- [ ] **Step 1: Write the failing core test**

Create `tests/MassiveDotNet.Rest.Tests/ContractTypeTests.cs`:

```csharp
using Xunit;

namespace MassiveDotNet.Rest.Tests;

public sealed class ContractTypeTests
{
    [Theory]
    [InlineData(ContractType.Call, "call")]
    [InlineData(ContractType.Put, "put")]
    public void RendersTheWireLiteral(ContractType value, string expected)
    {
        Assert.Equal(expected, value.ToWireValue());
    }

    [Fact]
    public void RefusesAnUndefinedMember()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((ContractType)42).ToWireValue());
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~ContractTypeTests"`
Expected: the build fails with CS0246 (`ContractType` not found).

- [ ] **Step 3: Add the enum and its wire value**

Create `src/MassiveDotNet/ContractType.cs`:

```csharp
namespace MassiveDotNet;

/// <summary>
/// Whether an options contract is a call or a put.
/// </summary>
public enum ContractType
{
    /// <summary>The right to buy the underlying at the strike price.</summary>
    Call = 0,

    /// <summary>The right to sell the underlying at the strike price.</summary>
    Put = 1,
}
```

In `src/MassiveDotNet/MassiveEnumValues.cs`, insert after the `ToWireValue(this SnapshotDirection value)` method (before the `ToWireValue(this LocalDate value)` method):

```csharp
    /// <summary>Returns the wire representation of a <see cref="ContractType"/>.</summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>Either <c>"call"</c> or <c>"put"</c>, the query value the contracts route takes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined enum member.</exception>
    public static string ToWireValue(this ContractType value) => value switch
    {
        ContractType.Call => "call",
        ContractType.Put => "put",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
```

- [ ] **Step 4: Run the core test to verify it passes**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~ContractTypeTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Write the failing generator tests**

Append to `tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs`, inside the class after `ADateOrNanosecondsComparatorGroupBindsARangeFilter`:

```csharp
    [Fact]
    public void AContractTypeParameterRendersItsWireValue()
    {
        string spec = Document("""[ { "name": "contract_type", "in": "query", "schema": { "type": "string", "enum": ["call", "put"] } } ]""");

        Dictionary<string, string> files = Harness.Generate(spec, MapDocument("""{ "contract_type": { "name": "contractType", "type": "ContractType" } }"""));

        string group = files["ReferenceGroup.g.cs"];
        Assert.Contains("ContractType? contractType = null", group, StringComparison.Ordinal);
        Assert.Contains("builder.AppendQuery(\"contract_type\", contractType?.ToWireValue());", group, StringComparison.Ordinal);
    }

    [Fact]
    public void AContractTypeMemberTheEnumLacksIsRefused()
    {
        // The #37 check runs for every core enum the table names; this pins that the new row is
        // in the table rather than falling through to the plain-string arm.
        string spec = Document("""[ { "name": "contract_type", "in": "query", "schema": { "type": "string", "enum": ["call", "put", "straddle"] } } ]""");

        string message = Harness.Refusal(spec, MapDocument("""{ "contract_type": { "type": "ContractType" } }"""));

        Assert.Contains("parameter 'contract_type' declares [straddle]", message, StringComparison.Ordinal);
        Assert.Contains("ContractType", message, StringComparison.Ordinal);
    }
```

- [ ] **Step 6: Run them to verify they fail**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests --filter "FullyQualifiedName~ContractType"`
Expected: both FAIL. The first because the generated line is `builder.AppendQuery("contract_type", contractType);` (the type fell through to the plain arm, which does not call `ToWireValue`); the second because no refusal is thrown.

- [ ] **Step 7: Register the enum**

In `tools/MassiveDotNet.CodeGen/TypeBinding.cs`, add a row to `CoreEnums` after the `SnapshotDirection` row:

```csharp
        [nameof(ContractType)] = WireValues<ContractType>(value => value.ToWireValue()),
```

- [ ] **Step 8: Run the generator tests to verify they pass**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests`
Expected: PASS, every test.

- [ ] **Step 9: Commit**

```bash
git add src/MassiveDotNet/ContractType.cs src/MassiveDotNet/MassiveEnumValues.cs tools/MassiveDotNet.CodeGen/TypeBinding.cs tests/MassiveDotNet.Rest.Tests/ContractTypeTests.cs tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs
git commit -m "feat: add ContractType to core and the generator's enum table

The options contracts list binds contract_type to it (D-R6); adding it
now rather than when the options group arrives keeps that parameter
from changing type under callers later.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 2: Generator: a one-branch `oneOf` reads as its branch

**Files:**
- Modify: `tools/MassiveDotNet.CodeGen/Spec.cs` (`Shape`, `Collect`, a new `Unwrap`)
- Modify: `tests/MassiveDotNet.CodeGen.Tests/ModelBindingTests.cs`
- Modify: `docs/superpowers/specs/2026-09-03-reference-group-design.md` (one sentence in D-R2)

**Interfaces:**
- Consumes: `Spec.Shape(JsonElement)`, `Spec.IsObject(JsonElement)`, `Spec.Collect(...)`, and `Spec.Navigate`, which reaches nested nodes through `Properties`.
- Produces: an array whose `items` is `{ "oneOf": [ <object> ] }` classifies as `ArrayOfObjects`, so a `model` row binds it and a model row can point through it (`results/events/items`, `results/events/items/ticker_change`). Task 4 relies on this for the ticker events models.

**Ruling.** The spec's D-R2 says the multi-branch refusal names the operation and pointer. The refusal fires inside `Spec.Shape`, which every binding site calls and which has no operation in hand; threading one through would touch every caller for a diagnostic no operation triggers today. The diagnostic instead names the branch count and the rule, which is enough to find the site with one search of the description. Step 7 amends the spec's sentence to match. If wrong, the cost is a slower hunt for a site that does not exist.

- [ ] **Step 1: Write the failing tests**

Append to `tests/MassiveDotNet.CodeGen.Tests/ModelBindingTests.cs`, inside the class at the end:

```csharp
    [Fact]
    public void SeesThroughAOneBranchOneOf()
    {
        // The ticker events items are the description's one response-side oneOf, and it has a
        // single branch. Without the unwrap the array has no item type and binds string[] (D-R2).
        string spec = Document("""
            {
              "type": "object",
              "properties": {
                "name":   { "type": "string" },
                "events": {
                  "type": "array",
                  "items": {
                    "oneOf": [
                      {
                        "type": "object",
                        "required": ["date"],
                        "properties": {
                          "date":          { "type": "string", "format": "date" },
                          "ticker_change": { "type": "object", "properties": { "ticker": { "type": "string" } } }
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);

        Dictionary<string, string> files = Harness.Generate(spec, MapDocument(
            """{ "events": { "name": "Events", "model": "Event" } }""",
            """
            "Event":  { "schema": { "operationId": "ListThings", "pointer": "results/items/events/items" }, "properties": { "ticker_change": { "name": "Change", "model": "Change" } } },
            "Change": { "schema": { "operationId": "ListThings", "pointer": "results/items/events/items/ticker_change" } }
            """));

        Assert.Contains("public Event[]? Events { get; init; }", Thing(files), StringComparison.Ordinal);

        string @event = files[Path.Combine("Models", "Event.g.cs")];
        Assert.Contains("public LocalDate Date { get; init; }", @event, StringComparison.Ordinal);
        Assert.Contains("public Change? Change { get; init; }", @event, StringComparison.Ordinal);
        Assert.Contains("public string? Ticker { get; init; }", files[Path.Combine("Models", "Change.g.cs")], StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsAScalarUnionAsAScalar()
    {
        // The news parameters declare a two-branch oneOf of strings, and parameters go through
        // the same Shape. A union of scalars must keep reading as a scalar, or news stops
        // generating. This passed before the unwrap existed and pins that the refusal below is
        // narrower than "any multi-branch oneOf".
        string spec = Document("""
            {
              "type": "object",
              "properties": {
                "when": { "oneOf": [ { "type": "string" }, { "type": "string", "format": "date-time" } ] }
              }
            }
            """);

        Assert.Contains("public string? When { get; init; }", Thing(Harness.Generate(spec, MapDocument())), StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAUnionWithAnObjectBranch()
    {
        string spec = Document("""
            {
              "type": "object",
              "properties": {
                "payload": {
                  "oneOf": [
                    { "type": "object", "properties": { "a": { "type": "string" } } },
                    { "type": "object", "properties": { "b": { "type": "string" } } }
                  ]
                }
              }
            }
            """);

        string message = Harness.Refusal(spec, MapDocument());

        Assert.Contains("oneOf with 2 branches", message, StringComparison.Ordinal);
        Assert.Contains("D24", message, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests --filter "FullyQualifiedName~ModelBindingTests"`
Expected: `SeesThroughAOneBranchOneOf` FAILS with a refusal that `events` names model `Event` "but its schema is an array of scalars"; `RefusesAUnionWithAnObjectBranch` FAILS because generation succeeds (the union binds `string?`); `ReadsAScalarUnionAsAScalar` PASSES already, which is the point of the pin.

- [ ] **Step 3: Add the unwrap**

In `tools/MassiveDotNet.CodeGen/Spec.cs`, change the start of `Shape` so the node is unwrapped before it is classified:

```csharp
    public static SchemaShape Shape(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return SchemaShape.Scalar;
        }

        schema = Unwrap(schema);

        if (IsObject(schema))
        {
            return SchemaShape.Object;
        }
```

(the rest of the method is unchanged.) Change the start of `Collect` the same way:

```csharp
    private static void Collect(
        JsonElement schema,
        Dictionary<string, SpecProperty> properties,
        List<string> order,
        HashSet<string> required)
    {
        schema = Unwrap(schema);

        if (schema.TryGetProperty("allOf", out JsonElement branches))
```

(the rest of the method is unchanged.) Then add the helper immediately after `IsObject`:

```csharp
    /// <summary>
    /// The node a schema binds as: itself, or the single branch of a one-branch <c>oneOf</c>.
    /// The description uses that form once, on the ticker events items, and without this the
    /// array had no item type and bound <c>string[]</c> (D24).
    /// </summary>
    /// <remarks>
    /// A <c>oneOf</c> of scalars still reads as a scalar, as it always has: the news parameters
    /// declare one and go through <see cref="Shape"/> too. A union with an object branch has no
    /// model binding, and reading it as a scalar would bind <c>string</c> where the wire carries
    /// objects, so it is refused. No operation declares one today.
    /// </remarks>
    private static JsonElement Unwrap(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("oneOf", out JsonElement branches)
            || branches.ValueKind != JsonValueKind.Array)
        {
            return schema;
        }

        if (branches.GetArrayLength() == 1)
        {
            return Unwrap(branches[0]);
        }

        foreach (JsonElement branch in branches.EnumerateArray())
        {
            if (IsObject(branch))
            {
                throw new InvalidOperationException(
                    $"A oneOf with {branches.GetArrayLength()} branches, at least one an object, has no model binding. "
                    + "The generator reads only a one-branch oneOf as its branch (D24); a union needs a design, not a guess.");
            }
        }

        return schema;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests`
Expected: PASS, every test, the three new ones included.

- [ ] **Step 5: Prove nothing generated changes**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff. No mapped operation has a `oneOf` on its response, and the news parameters are scalar unions.

- [ ] **Step 6: Run the whole offline suite**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS.

- [ ] **Step 7: Amend D-R2's diagnostic sentence**

In `docs/superpowers/specs/2026-09-03-reference-group-design.md`, under `### D-R2`, replace the sentence

```
A `oneOf` with more than one branch on a response schema is refused with a
diagnostic naming the operation and pointer; none exists today, and a union has no honest model
binding.
```

with

```
A `oneOf` with more than one branch of which any is an object is refused with a diagnostic
naming the branch count and the rule; none exists today, and a union has no honest model
binding. A union of scalars, which the news parameters declare, stays a scalar as it always has.
```

- [ ] **Step 8: Commit**

```bash
git add tools/MassiveDotNet.CodeGen/Spec.cs tests/MassiveDotNet.CodeGen.Tests/ModelBindingTests.cs docs/superpowers/specs/2026-09-03-reference-group-design.md
git commit -m "feat(codegen): read a one-branch oneOf as its branch (D24)

The ticker events items are wrapped in a single-branch oneOf, which
the generator read as an array of strings. A union with an object
branch is refused; a union of scalars stays a scalar, which the news
parameters need.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 3: Tickers: the list, the details, and the types

**Files:**
- Modify: `specs/endpoints.map.json` (five model rows, three endpoint rows)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceTickersTests.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceTickerDetailsTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 23 → 26)

**Interfaces:**
- Consumes: the `ListNews` endpoint row and the `NewsArticle` / `NewsPublisher` model rows as the pattern for a paginated list with nested models; `MarketType` and `SortOrder` as map types; `LocalDate` as a map type on a bare-string date (D-R9).
- Produces: `client.Reference.ListTickersAsync(RangeFilter<string>? ticker = null, string? type = null, MarketType? market = null, string? exchange = null, string? cusip = null, string? cik = null, LocalDate? date = null, string? search = null, bool? active = null, SortOrder? order = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<Ticker>>`, with `EnumerateTickersAsync` returning `IAsyncEnumerable<Ticker>`; `client.Reference.GetTickerAsync(string ticker, LocalDate? date = null, CancellationToken cancellationToken = default)` returning `Task<TickerDetails>`; `client.Reference.ListTickerTypesAsync(MarketType? assetClass = null, string? locale = null, CancellationToken cancellationToken = default)` returning `Task<TickerType[]>`. Models: `sealed partial record Ticker` (`required string Ticker`, `required string Name`, `required string Market`, `required string Locale`, `string? PrimaryExchange`, `string? Type`, `bool? IsActive`, `string? CurrencySymbol`, `string? CurrencyName`, `string? BaseCurrencySymbol`, `string? BaseCurrencyName`, `string? Cik`, `string? CompositeFigi`, `string? ShareClassFigi`, `Instant? LastUpdatedUtc`, `Instant? DelistedUtc`); `sealed partial record TickerDetails` (the same identity members plus `CompanyAddress? Address`, `Branding? Branding`, `string? Description`, `string? HomepageUrl`, `LocalDate? ListDate`, `double? MarketCap`, `string? PhoneNumber`, `double? RoundLot`, `double? ShareClassSharesOutstanding`, `string? SicCode`, `string? SicDescription`, `string? TickerRoot`, `string? TickerSuffix`, `double? TotalEmployees`, `double? WeightedSharesOutstanding`, with `bool IsActive` and `required string CurrencyName`); `sealed partial record CompanyAddress` (`Address1`, `Address2`, `City`, `State`, `PostalCode`, all `string?`); `sealed partial record Branding` (`string? LogoUrl`, `string? IconUrl`); `sealed partial record TickerType` (`required string Code`, `required string Description`, `required string AssetClass`, `required string Locale`). No partials. Tasks 10 and 11 call `ListTickersAsync`; Task 11 calls the other two.

- [ ] **Step 1: Add the fixtures**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, after the `StocksDevTrades` member, before the class's closing brace:

```csharp
    /// <summary>The documented sample for GET /v3/reference/tickers. It carries a cursor of its own.</summary>
    public const string ReferenceTickers = """
        {
          "count": 1,
          "next_url": "https://api.massive.com/v3/reference/tickers?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "e70013d92930de90e089dc8fa098888e",
          "results": [
            {
              "active": true,
              "cik": "0001090872",
              "composite_figi": "BBG000BWQYZ5",
              "currency_name": "usd",
              "last_updated_utc": "2021-04-25T00:00:00Z",
              "locale": "us",
              "market": "stocks",
              "name": "Agilent Technologies Inc.",
              "primary_exchange": "XNYS",
              "share_class_figi": "BBG001SCTQY4",
              "ticker": "A",
              "type": "CS"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// A hand-written final page in the tickers envelope's shape, with no <c>next_url</c>, so a
    /// traversal from <see cref="ReferenceTickers"/> ends after two requests.
    /// </summary>
    public const string ReferenceTickersLastPage = """
        {
          "count": 1,
          "request_id": "e70013d92930de90e089dc8fa098888f",
          "results": [
            {
              "active": true,
              "cik": "0000006201",
              "composite_figi": "BBG005P7Q881",
              "currency_name": "usd",
              "last_updated_utc": "2021-04-25T00:00:00Z",
              "locale": "us",
              "market": "stocks",
              "name": "American Airlines Group Inc.",
              "primary_exchange": "XNAS",
              "share_class_figi": "BBG005P7Q907",
              "ticker": "AAL",
              "type": "CS"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /v3/reference/tickers/{ticker}.</summary>
    public const string ReferenceTickerDetails = """
        {
          "request_id": "31d59dda-80e5-4721-8496-d0d32a654afe",
          "results": {
            "active": true,
            "address": {
              "address1": "One Apple Park Way",
              "city": "Cupertino",
              "postal_code": "95014",
              "state": "CA"
            },
            "branding": {
              "icon_url": "https://api.massive.com/v1/reference/company-branding/d3d3LmFwcGxlLmNvbQ/images/2022-01-10_icon.png",
              "logo_url": "https://api.massive.com/v1/reference/company-branding/d3d3LmFwcGxlLmNvbQ/images/2022-01-10_logo.svg"
            },
            "cik": "0000320193",
            "composite_figi": "BBG000B9XRY4",
            "currency_name": "usd",
            "description": "Apple designs a wide variety of consumer electronic devices, including smartphones (iPhone), tablets (iPad), PCs (Mac), smartwatches (Apple Watch), AirPods, and TV boxes (Apple TV), among others. The iPhone makes up the majority of Apple's total revenue. In addition, Apple offers its customers a variety of services such as Apple Music, iCloud, Apple Care, Apple TV+, Apple Arcade, Apple Card, and Apple Pay, among others. Apple's products run internally developed software and semiconductors, and the firm is well known for its integration of hardware, software and services. Apple's products are distributed online as well as through company-owned stores and third-party retailers. The company generates roughly 40% of its revenue from the Americas, with the remainder earned internationally.",
            "homepage_url": "https://www.apple.com",
            "list_date": "1980-12-12",
            "locale": "us",
            "market": "stocks",
            "market_cap": 2771126040150,
            "name": "Apple Inc.",
            "phone_number": "(408) 996-1010",
            "primary_exchange": "XNAS",
            "round_lot": 100,
            "share_class_figi": "BBG001S5N8V8",
            "share_class_shares_outstanding": 16406400000,
            "sic_code": "3571",
            "sic_description": "ELECTRONIC COMPUTERS",
            "ticker": "AAPL",
            "ticker_root": "AAPL",
            "total_employees": 154000,
            "type": "CS",
            "weighted_shares_outstanding": 16334371000
          },
          "status": "OK"
        }
        """;

    /// <summary>
    /// GET /v3/reference/tickers/types?asset_class=stocks&amp;locale=us, captured from the live
    /// service on 2026-09-03 because the description publishes only a CSV example for it
    /// (D-R12). Reviewed: it carries no account identifier and no URL embeds a key.
    /// </summary>
    public const string ReferenceTickerTypes = """
        {
          "count": 24,
          "request_id": "b226ee899a65f4be25300c7af02eed7d",
          "results": [
            { "asset_class": "stocks", "code": "CS", "description": "Common Stock", "locale": "us" },
            { "asset_class": "stocks", "code": "PFD", "description": "Preferred Stock", "locale": "us" },
            { "asset_class": "stocks", "code": "WARRANT", "description": "Warrant", "locale": "us" },
            { "asset_class": "stocks", "code": "RIGHT", "description": "Rights", "locale": "us" },
            { "asset_class": "stocks", "code": "BOND", "description": "Corporate Bond", "locale": "us" },
            { "asset_class": "stocks", "code": "ETF", "description": "Exchange Traded Fund", "locale": "us" },
            { "asset_class": "stocks", "code": "ETN", "description": "Exchange Traded Note", "locale": "us" },
            { "asset_class": "stocks", "code": "ETV", "description": "Exchange Traded Vehicle", "locale": "us" },
            { "asset_class": "stocks", "code": "SP", "description": "Structured Product", "locale": "us" },
            { "asset_class": "stocks", "code": "ADRC", "description": "American Depository Receipt Common", "locale": "us" },
            { "asset_class": "stocks", "code": "ADRP", "description": "American Depository Receipt Preferred", "locale": "us" },
            { "asset_class": "stocks", "code": "ADRW", "description": "American Depository Receipt Warrants", "locale": "us" },
            { "asset_class": "stocks", "code": "ADRR", "description": "American Depository Receipt Rights", "locale": "us" },
            { "asset_class": "stocks", "code": "FUND", "description": "Fund", "locale": "us" },
            { "asset_class": "stocks", "code": "BASKET", "description": "Basket", "locale": "us" },
            { "asset_class": "stocks", "code": "UNIT", "description": "Unit", "locale": "us" },
            { "asset_class": "stocks", "code": "LT", "description": "Liquidating Trust", "locale": "us" },
            { "asset_class": "stocks", "code": "OS", "description": "Ordinary Shares", "locale": "us" },
            { "asset_class": "stocks", "code": "GDR", "description": "Global Depository Receipts", "locale": "us" },
            { "asset_class": "stocks", "code": "OTHER", "description": "Other Security Type", "locale": "us" },
            { "asset_class": "stocks", "code": "NYRS", "description": "New York Registry Shares", "locale": "us" },
            { "asset_class": "stocks", "code": "AGEN", "description": "Agency Bond", "locale": "us" },
            { "asset_class": "stocks", "code": "EQLK", "description": "Equity Linked Bond", "locale": "us" },
            { "asset_class": "stocks", "code": "ETS", "description": "Single-security ETF", "locale": "us" }
          ],
          "status": "OK"
        }
        """;
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/ReferenceTickersTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The tickers list and the ticker types: a <see cref="MarketType"/> on a query parameter, a
/// calendar date, RFC 3339 timestamps on the model, a two-page traversal, and a captured
/// fixture for the one operation the description gives no JSON example (D-R12).
/// </summary>
public sealed class ReferenceTickersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Cursor =
        "https://api.massive.com/v3/reference/tickers?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy";

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersEveryParameterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceTickers);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListTickersAsync(
                ticker: RangeFilter.Gte("A"),
                type: "CS",
                market: MarketType.Stocks,
                exchange: "XNYS",
                cusip: "00846U101",
                cik: "0001090872",
                date: new LocalDate(2024, 1, 16),
                search: "agilent",
                active: true,
                order: SortOrder.Ascending,
                limit: 2,
                sort: "ticker",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v3/reference/tickers"
                + "?ticker.gte=A&type=CS&market=stocks&exchange=XNYS&cusip=00846U101&cik=0001090872"
                + "&date=2024-01-16&search=agilent&active=true&order=asc&limit=2&sort=ticker",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task OmitsEveryParameterByDefault()
    {
        StubHandler handler = new(Fixtures.ReferenceTickers);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListTickersAsync(cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v3/reference/tickers", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceTickers);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Ticker> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListTickersAsync(cancellationToken: Ct);
        }

        Ticker ticker = Assert.Single(page.Results);
        Assert.Equal("A", ticker.Ticker);
        Assert.Equal("Agilent Technologies Inc.", ticker.Name);
        Assert.Equal("stocks", ticker.Market);
        Assert.Equal("us", ticker.Locale);
        Assert.Equal("XNYS", ticker.PrimaryExchange);
        Assert.Equal("CS", ticker.Type);
        Assert.True(ticker.IsActive);
        Assert.Equal("usd", ticker.CurrencyName);
        Assert.Equal("0001090872", ticker.Cik);
        Assert.Equal("BBG000BWQYZ5", ticker.CompositeFigi);
        Assert.Equal("BBG001SCTQY4", ticker.ShareClassFigi);
        Assert.Equal(Instant.FromUtc(2021, 4, 25, 0, 0), ticker.LastUpdatedUtc);
        Assert.Null(ticker.DelistedUtc);
        Assert.Null(ticker.CurrencySymbol);
        Assert.True(page.HasMore);
        Assert.Equal("e70013d92930de90e089dc8fa098888e", page.RequestId);
    }

    [Fact]
    public async Task EnumerateTraversesTwoPagesFollowingTheCursorVerbatim()
    {
        PagingStubHandler handler = new(Fixtures.ReferenceTickers, Fixtures.ReferenceTickersLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string> tickers = [];

        using (client)
        using (transport)
        {
            await foreach (Ticker ticker in client.Reference.EnumerateTickersAsync(market: MarketType.Stocks, cancellationToken: Ct))
            {
                tickers.Add(ticker.Ticker);
            }
        }

        Assert.Equal(["A", "AAL"], tickers);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("https://api.massive.com/v3/reference/tickers?market=stocks", handler.Requests[0].ToString());
        Assert.Equal(Cursor, handler.Requests[1].ToString());
    }

    [Fact]
    public async Task TickerTypesRenderTheAssetClassAndLocale()
    {
        StubHandler handler = new(Fixtures.ReferenceTickerTypes);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListTickerTypesAsync(assetClass: MarketType.Stocks, locale: "us", cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v3/reference/tickers/types?asset_class=stocks&locale=us", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task TickerTypesDeserializeTheCapturedResponse()
    {
        StubHandler handler = new(Fixtures.ReferenceTickerTypes);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        TickerType[] types;

        using (client)
        using (transport)
        {
            types = await client.Reference.ListTickerTypesAsync(cancellationToken: Ct);
        }

        Assert.Equal(24, types.Length);

        TickerType common = types[0];
        Assert.Equal("CS", common.Code);
        Assert.Equal("Common Stock", common.Description);
        Assert.Equal("stocks", common.AssetClass);
        Assert.Equal("us", common.Locale);

        Assert.Contains(types, type => type.Code == "ETF" && type.Description == "Exchange Traded Fund");
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/ReferenceTickerDetailsTests.cs`:

```csharp
using System.Net;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Ticker details: a singular result under <c>results</c> with two nested models, a bare-string
/// date bound to <see cref="LocalDate"/> from the map (D-R9), and the D17 failure on a 200 with
/// no payload.
/// </summary>
public sealed class ReferenceTickerDetailsTests
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
        StubHandler handler = new(Fixtures.ReferenceTickerDetails);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.GetTickerAsync("AAPL", date: new LocalDate(2024, 1, 16), cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v3/reference/tickers/AAPL?date=2024-01-16", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSampleThroughBothNestedModels()
    {
        StubHandler handler = new(Fixtures.ReferenceTickerDetails);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        TickerDetails details;

        using (client)
        using (transport)
        {
            details = await client.Reference.GetTickerAsync("AAPL", cancellationToken: Ct);
        }

        Assert.Equal("AAPL", details.Ticker);
        Assert.Equal("Apple Inc.", details.Name);
        Assert.True(details.IsActive);
        Assert.Equal("usd", details.CurrencyName);
        Assert.Equal("stocks", details.Market);
        Assert.Equal("us", details.Locale);
        Assert.Equal("XNAS", details.PrimaryExchange);
        Assert.Equal("CS", details.Type);
        Assert.Equal(new LocalDate(1980, 12, 12), details.ListDate);
        Assert.Equal(2771126040150d, details.MarketCap);
        Assert.Equal(100d, details.RoundLot);
        Assert.Equal(16406400000d, details.ShareClassSharesOutstanding);
        Assert.Equal(16334371000d, details.WeightedSharesOutstanding);
        Assert.Equal(154000d, details.TotalEmployees);
        Assert.Equal("3571", details.SicCode);
        Assert.Equal("ELECTRONIC COMPUTERS", details.SicDescription);
        Assert.Equal("AAPL", details.TickerRoot);
        Assert.Null(details.TickerSuffix);
        Assert.Null(details.DelistedUtc);
        Assert.Equal("(408) 996-1010", details.PhoneNumber);
        Assert.Equal("https://www.apple.com", details.HomepageUrl);
        Assert.StartsWith("Apple designs", details.Description, StringComparison.Ordinal);

        Assert.NotNull(details.Address);
        Assert.Equal("One Apple Park Way", details.Address.Address1);
        Assert.Null(details.Address.Address2);
        Assert.Equal("Cupertino", details.Address.City);
        Assert.Equal("CA", details.Address.State);
        Assert.Equal("95014", details.Address.PostalCode);

        Assert.NotNull(details.Branding);
        Assert.EndsWith("2022-01-10_logo.svg", details.Branding.LogoUrl, StringComparison.Ordinal);
        Assert.EndsWith("2022-01-10_icon.png", details.Branding.IconUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASuccessWithoutAPayloadIsReported()
    {
        StubHandler handler = new(Fixtures.SingularWithoutResults);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Reference.GetTickerAsync("AAPL", cancellationToken: Ct));

            Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
            Assert.Contains("carried no 'results' payload", exception.Message, StringComparison.Ordinal);
        }
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, raise the baseline:

```csharp
    private const int CoverageBaseline = 26;
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~ReferenceTickers|FullyQualifiedName~ReferenceTickerDetails"`
Expected: the build fails with CS1061 (`ReferenceGroup` has no `ListTickersAsync`) and CS0246 for `Ticker`, `TickerDetails`, and `TickerType`.

- [ ] **Step 4: Add the model rows**

In `specs/endpoints.map.json`, append inside the `models` object after the `DevTrade` row (add a comma after its closing brace):

```json
    "Ticker": {
      "summary": "One row of the ticker list: the symbol, its name, market, locale, and primary exchange, and the identifiers that tie it to other reference data.",
      "remarks": "Reference data, so a class rather than a struct (decision D4), and the model the constitution's own example names. <see cref=\"TickerDetails\"/> is the same identity with the company profile added; the two are separate models because the details carry twelve properties the list does not (decision D16). Both timestamps are RFC 3339 strings read by the <see cref=\"NodaTime.Instant\"/> converter.",
      "schema": { "operationId": "ListTickers", "pointer": "results/items" },
      "properties": {
        "ticker":               { "name": "Ticker" },
        "name":                 { "name": "Name" },
        "market":               { "name": "Market" },
        "locale":               { "name": "Locale" },
        "primary_exchange":     { "name": "PrimaryExchange" },
        "type":                 { "name": "Type" },
        "active":               { "name": "IsActive" },
        "currency_symbol":      { "name": "CurrencySymbol" },
        "currency_name":        { "name": "CurrencyName" },
        "base_currency_symbol": { "name": "BaseCurrencySymbol" },
        "base_currency_name":   { "name": "BaseCurrencyName" },
        "cik":                  { "name": "Cik" },
        "composite_figi":       { "name": "CompositeFigi" },
        "share_class_figi":     { "name": "ShareClassFigi" },
        "last_updated_utc":     { "name": "LastUpdatedUtc" },
        "delisted_utc":         { "name": "DelistedUtc" }
      }
    },

    "TickerDetails": {
      "summary": "Everything the platform knows about one ticker: its identity, the company's profile and address, its branding, and its share counts.",
      "remarks": "Reference data, so a class (decision D4). <see cref=\"ListDate\"/> is a calendar date the description types as a bare string; the map binds it to <see cref=\"NodaTime.LocalDate\"/> because the published example shows the ISO form (D-R9). The share counts and <see cref=\"TotalEmployees\"/> are doubles because the description declares them numbers; safety over ergonomics is the design goals' order.",
      "schema": { "operationId": "GetTicker", "pointer": "results" },
      "properties": {
        "ticker":                         { "name": "Ticker" },
        "name":                           { "name": "Name" },
        "market":                         { "name": "Market" },
        "locale":                         { "name": "Locale" },
        "primary_exchange":               { "name": "PrimaryExchange" },
        "type":                           { "name": "Type" },
        "active":                         { "name": "IsActive" },
        "currency_name":                  { "name": "CurrencyName" },
        "cik":                            { "name": "Cik" },
        "composite_figi":                 { "name": "CompositeFigi" },
        "share_class_figi":               { "name": "ShareClassFigi" },
        "market_cap":                     { "name": "MarketCap" },
        "phone_number":                   { "name": "PhoneNumber" },
        "address":                        { "name": "Address", "model": "CompanyAddress" },
        "description":                    { "name": "Description" },
        "sic_code":                       { "name": "SicCode" },
        "sic_description":                { "name": "SicDescription" },
        "ticker_root":                    { "name": "TickerRoot" },
        "ticker_suffix":                  { "name": "TickerSuffix" },
        "homepage_url":                   { "name": "HomepageUrl" },
        "total_employees":                { "name": "TotalEmployees" },
        "list_date":                      { "name": "ListDate", "type": "LocalDate?", "summary": "The date the ticker was first listed. The description types this a bare string; the published example carries an ISO calendar date (D-R9)." },
        "branding":                       { "name": "Branding", "model": "Branding" },
        "share_class_shares_outstanding": { "name": "ShareClassSharesOutstanding" },
        "weighted_shares_outstanding":    { "name": "WeightedSharesOutstanding" },
        "round_lot":                      { "name": "RoundLot" },
        "delisted_utc":                   { "name": "DelistedUtc" }
      }
    },

    "CompanyAddress": {
      "summary": "A company's headquarters address, as the ticker details report it.",
      "schema": { "operationId": "GetTicker", "pointer": "results/address" },
      "properties": {
        "address1":    { "name": "Address1" },
        "address2":    { "name": "Address2" },
        "city":        { "name": "City" },
        "state":       { "name": "State" },
        "postal_code": { "name": "PostalCode" }
      }
    },

    "Branding": {
      "summary": "The URLs of a company's logo and icon, as the ticker details report them.",
      "remarks": "The URLs point at a branding route that requires the same API key as every other request; the SDK does not fetch them.",
      "schema": { "operationId": "GetTicker", "pointer": "results/branding" },
      "properties": {
        "logo_url": { "name": "LogoUrl" },
        "icon_url": { "name": "IconUrl" }
      }
    },

    "TickerType": {
      "summary": "One ticker type the platform recognises: its code, a description, and the asset class and locale it applies to.",
      "remarks": "The codes are what the tickers list's <c>type</c> parameter accepts, which is why that parameter is a string rather than an enum (D-R6): this operation owns the set.",
      "schema": { "operationId": "ListTickerTypes", "pointer": "results/items" },
      "properties": {
        "code":        { "name": "Code" },
        "description": { "name": "Description" },
        "asset_class": { "name": "AssetClass" },
        "locale":      { "name": "Locale" }
      }
    }
```

- [ ] **Step 5: Add the endpoint rows**

In `specs/endpoints.map.json`, append inside the `endpoints` array after the `get_stocks_dev_trades_ticker` row (add a comma after its closing brace):

```json
    {
      "operationId": "ListTickers",
      "group": "Reference",
      "method": "ListTickers",
      "summary": "Retrieves the tickers the platform supports across every asset class, with each one's name, market, and identifiers.",
      "remarks": "Every filter is optional and defaults to no constraint. <paramref name=\"ticker\"/> takes a plain symbol or a lexical range; <paramref name=\"market\"/> is a <see cref=\"MarketType\"/>; <paramref name=\"type\"/> takes a code from <see cref=\"ListTickerTypesAsync\"/>, whose set that operation owns. <paramref name=\"date\"/> asks for the tickers as they stood on a calendar date.",
      "result": { "kind": "array", "model": "Ticker", "property": "results" },
      "parameters": {
        "ticker":   { "name": "ticker" },
        "type":     { "name": "type" },
        "market":   { "name": "market", "type": "MarketType" },
        "exchange": { "name": "exchange" },
        "cusip":    { "name": "cusip" },
        "cik":      { "name": "cik" },
        "date":     { "name": "date" },
        "search":   { "name": "search" },
        "active":   { "name": "active" },
        "order":    { "name": "order", "type": "SortOrder" },
        "limit":    { "name": "limit" },
        "sort":     { "name": "sort" }
      }
    },
    {
      "operationId": "GetTicker",
      "group": "Reference",
      "method": "GetTicker",
      "summary": "Retrieves the details of one ticker: its identity, the company's profile, address, and branding, and its share counts.",
      "remarks": "<paramref name=\"date\"/> asks for the ticker as it stood on a calendar date; the default is the most recent. A ticker the service does not know answers 404, which surfaces as a <see cref=\"MassiveApiException\"/>.",
      "result": { "kind": "object", "model": "TickerDetails", "property": "results" },
      "parameters": {
        "ticker": { "name": "ticker" },
        "date":   { "name": "date" }
      }
    },
    {
      "operationId": "ListTickerTypes",
      "group": "Reference",
      "method": "ListTickerTypes",
      "summary": "Retrieves the ticker types the platform recognises, optionally for one asset class and locale.",
      "remarks": "The list is short and does not page. Its codes are what <see cref=\"ListTickersAsync\"/> accepts for its <c>type</c> parameter.",
      "result": { "kind": "array", "model": "TickerType", "property": "results" },
      "parameters": {
        "asset_class": { "name": "assetClass", "type": "MarketType" },
        "locale":      { "name": "locale" }
      }
    }
```

- [ ] **Step 6: Regenerate**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: `Ticker.g.cs`, `TickerDetails.g.cs`, `CompanyAddress.g.cs`, `Branding.g.cs`, and `TickerType.g.cs` under `Generated/Models`, with `using NodaTime;` on the first two; a paged `ListTickersResponse`, a `GetTickerResponse`, and a `ListTickerTypesResponse` in `Envelopes.g.cs`; and on `ReferenceGroup.g.cs` the methods `EnumerateTickersAsync`, `ListTickersAsync` (with `RangeFilter<string>? ticker`, `MarketType? market`, `LocalDate? date`, `SortOrder? order`), `GetTickerAsync(string ticker, LocalDate? date = null, ...)`, and `ListTickerTypesAsync(MarketType? assetClass = null, string? locale = null, ...)` returning `Task<TickerType[]>`. No partials.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS across every project; `EndpointCoverageTests` reports 26 mapped.

- [ ] **Step 8: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff.

- [ ] **Step 9: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/ReferenceTickersTests.cs tests/MassiveDotNet.Rest.Tests/ReferenceTickerDetailsTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs
git commit -m "feat: map the tickers list, ticker details, and ticker types

Ticker and TickerDetails follow D4's own example; the details' list_date
binds LocalDate from the map (D-R9). Ticker types has no published JSON
example, so its fixture is a reviewed live capture (D-R12). Coverage
reaches 26.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 4: Ticker events through the `oneOf`, and related companies

**Files:**
- Modify: `specs/endpoints.map.json` (four model rows, two endpoint rows)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceTickerEventsTests.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceRelatedCompaniesTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 26 → 28)

**Interfaces:**
- Consumes: the `oneOf` unwrap from Task 2 (`results/events/items` is reachable as an array of objects); the `LastTrade` endpoint row as the pattern for a singular `results` object; `[Experimental("MASSIVE0001")]`, which the generator emits from the `vX` segment and which the REST test project already suppresses.
- Produces: `client.Reference.GetTickerEventsAsync(string id, string? types = null, CancellationToken cancellationToken = default)` returning `Task<TickerEvents>`, marked `[Experimental("MASSIVE0001")]`; `client.Reference.ListRelatedCompaniesAsync(string ticker, CancellationToken cancellationToken = default)` returning `Task<RelatedCompany[]>`. Models: `sealed partial record TickerEvents` (`string? Name`, `TickerEvent[]? Events`); `sealed partial record TickerEvent` (`LocalDate Date`, `string? EventType`, `TickerChange? TickerChange`); `sealed partial record TickerChange` (`string? Ticker`); `sealed partial record RelatedCompany` (`required string Ticker`). No partials. Task 11 calls both.

- [ ] **Step 1: Add the fixtures**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, after the `ReferenceTickerTypes` member:

```csharp
    /// <summary>
    /// The documented sample for GET /vX/reference/tickers/{id}/events. Each event spells its
    /// discriminator <c>type</c>, where the schema declares a required <c>event_type</c>; the
    /// live wire agrees with the sample, so the map types that property nullable and the SDK
    /// reads it as absent (D-R10). The fixture is the sample verbatim.
    /// </summary>
    public const string ReferenceTickerEvents = """
        {
          "request_id": "31d59dda-80e5-4721-8496-d0d32a654afe",
          "results": {
            "events": [
              {
                "date": "2022-06-09",
                "ticker_change": {
                  "ticker": "META"
                },
                "type": "ticker_change"
              },
              {
                "date": "2012-05-18",
                "ticker_change": {
                  "ticker": "FB"
                },
                "type": "ticker_change"
              }
            ],
            "name": "Meta Platforms, Inc. Class A Common Stock"
          },
          "status": "OK"
        }
        """;

    /// <summary>
    /// The documented sample for GET /v1/related-companies/{ticker}. The envelope's
    /// <c>stock_symbol</c> is not in the schema, which declares <c>ticker</c> there instead, and
    /// is ignored.
    /// </summary>
    public const string ReferenceRelatedCompanies = """
        {
          "request_id": "31d59dda-80e5-4721-8496-d0d32a654afe",
          "results": [
            { "ticker": "MSFT" },
            { "ticker": "GOOGL" },
            { "ticker": "AMZN" },
            { "ticker": "FB" },
            { "ticker": "TSLA" },
            { "ticker": "NVDA" },
            { "ticker": "INTC" },
            { "ticker": "ADBE" },
            { "ticker": "NFLX" },
            { "ticker": "PYPL" }
          ],
          "status": "OK",
          "stock_symbol": "AAPL"
        }
        """;
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/ReferenceTickerEventsTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Ticker events: the one response whose items sit behind a one-branch <c>oneOf</c> (D-R2), and
/// the one whose discriminator the description misnames (D-R10). Experimental, so the test
/// project suppresses MASSIVE0001 in its project file.
/// </summary>
public sealed class ReferenceTickerEventsTests
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
        StubHandler handler = new(Fixtures.ReferenceTickerEvents);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.GetTickerEventsAsync("META", types: "ticker_change", cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/vX/reference/tickers/META/events?types=ticker_change", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSampleWithTheDiscriminatorAbsent()
    {
        StubHandler handler = new(Fixtures.ReferenceTickerEvents);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        TickerEvents events;

        using (client)
        using (transport)
        {
            events = await client.Reference.GetTickerEventsAsync("META", cancellationToken: Ct);
        }

        Assert.Equal("Meta Platforms, Inc. Class A Common Stock", events.Name);
        Assert.NotNull(events.Events);
        Assert.Equal(2, events.Events.Length);

        TickerEvent latest = events.Events[0];
        Assert.Equal(new LocalDate(2022, 6, 9), latest.Date);
        Assert.NotNull(latest.TickerChange);
        Assert.Equal("META", latest.TickerChange.Ticker);

        // The wire spells the discriminator "type"; the schema says "event_type". The model
        // follows the schema, so the property reads as absent (D-R10).
        Assert.Null(latest.EventType);

        Assert.Equal("FB", events.Events[1].TickerChange?.Ticker);
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/ReferenceRelatedCompaniesTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>Related companies: an unpaginated array under <c>results</c> of one-property rows.</summary>
public sealed class ReferenceRelatedCompaniesTests
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
        StubHandler handler = new(Fixtures.ReferenceRelatedCompanies);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListRelatedCompaniesAsync("AAPL", Ct);
        }

        Assert.Equal("https://api.massive.com/v1/related-companies/AAPL", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceRelatedCompanies);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        RelatedCompany[] companies;

        using (client)
        using (transport)
        {
            companies = await client.Reference.ListRelatedCompaniesAsync("AAPL", Ct);
        }

        Assert.Equal(10, companies.Length);
        Assert.Equal("MSFT", companies[0].Ticker);
        Assert.Equal("PYPL", companies[^1].Ticker);
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, raise the baseline:

```csharp
    private const int CoverageBaseline = 28;
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~ReferenceTickerEvents|FullyQualifiedName~ReferenceRelatedCompanies"`
Expected: the build fails with CS1061 for both methods and CS0246 for `TickerEvents`, `TickerEvent`, and `RelatedCompany`.

- [ ] **Step 4: Add the model rows**

In `specs/endpoints.map.json`, append inside `models` after the `TickerType` row (add a comma after its closing brace):

```json
    "TickerEvents": {
      "summary": "The history of one asset's identifiers: its current name and the events, such as ticker changes, that led to it.",
      "remarks": "A singular result (decision D17). Its items are declared behind a one-branch <c>oneOf</c>, which the generator reads as the branch (decision D24).",
      "schema": { "operationId": "GetEvents", "pointer": "results" },
      "properties": {
        "name":   { "name": "Name" },
        "events": { "name": "Events", "model": "TickerEvent" }
      }
    },

    "TickerEvent": {
      "summary": "One event in an asset's history: the date it took place and, for a ticker change, the symbol it changed to.",
      "remarks": "The description declares a required <c>event_type</c>; the published example and the live wire both spell it <c>type</c>, so <see cref=\"EventType\"/> is typed nullable and reads as absent until the description or the service moves (D-R10). Nothing here is renamed: the map cannot rename a wire key.",
      "schema": { "operationId": "GetEvents", "pointer": "results/events/items" },
      "properties": {
        "date":          { "name": "Date" },
        "event_type":    { "name": "EventType", "type": "string?", "summary": "The type of the event. The description requires this key, but the wire spells it <c>type</c>, so it is null in practice (D-R10); <see cref=\"TickerChange\"/> being set is the working discriminator." },
        "ticker_change": { "name": "TickerChange", "model": "TickerChange" }
      }
    },

    "TickerChange": {
      "summary": "The symbol an asset took on in a ticker change event.",
      "schema": { "operationId": "GetEvents", "pointer": "results/events/items/ticker_change" },
      "properties": {
        "ticker": { "name": "Ticker" }
      }
    },

    "RelatedCompany": {
      "summary": "A company related to the one queried, by news and returns; only its ticker is reported.",
      "schema": { "operationId": "GetRelatedCompanies", "pointer": "results/items" },
      "properties": {
        "ticker": { "name": "Ticker" }
      }
    }
```

- [ ] **Step 5: Add the endpoint rows**

In `specs/endpoints.map.json`, append inside `endpoints` after the `ListTickerTypes` row (add a comma after its closing brace):

```json
    {
      "operationId": "GetEvents",
      "group": "Reference",
      "method": "GetTickerEvents",
      "summary": "Retrieves the identifier history of one asset: its current name and the ticker changes that led to it.",
      "remarks": "The route's <c>vX</c> segment marks it experimental (decision D18); opt in with <c>MASSIVE0001</c>. <paramref name=\"id\"/> is a ticker, a CUSIP, or a composite FIGI. <paramref name=\"types\"/> is a comma-separated list of event types, of which the description names only <c>ticker_change</c>. The event rows' <see cref=\"TickerEvent.EventType\"/> reads as absent on today's wire (D-R10).",
      "result": { "kind": "object", "model": "TickerEvents", "property": "results" },
      "parameters": {
        "id":    { "name": "id" },
        "types": { "name": "types" }
      }
    },
    {
      "operationId": "GetRelatedCompanies",
      "group": "Reference",
      "method": "ListRelatedCompanies",
      "summary": "Retrieves the tickers of companies related to the one given, as judged by news coverage and return correlation.",
      "remarks": "The list does not page. Only the ticker of each related company is reported; <see cref=\"GetTickerAsync\"/> fetches the rest.",
      "result": { "kind": "array", "model": "RelatedCompany", "property": "results" }
    }
```

- [ ] **Step 6: Regenerate**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: `TickerEvents.g.cs`, `TickerEvent.g.cs` (with `using NodaTime;` and `public LocalDate Date`), `TickerChange.g.cs`, `RelatedCompany.g.cs`; on `ReferenceGroup.g.cs`, `GetTickerEventsAsync` carrying `[Experimental("MASSIVE0001", ...)]` on the public entry point only, and `ListRelatedCompaniesAsync(string ticker, CancellationToken cancellationToken = default)` returning `Task<RelatedCompany[]>`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS; `EndpointCoverageTests` reports 28 mapped, and `StabilityAttributesMatchTheSpecification` passes with the new experimental route.

- [ ] **Step 8: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff.

- [ ] **Step 9: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/ReferenceTickerEventsTests.cs tests/MassiveDotNet.Rest.Tests/ReferenceRelatedCompaniesTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs
git commit -m "feat: map ticker events and related companies

Ticker events is the description's one response behind a one-branch
oneOf (D24) and its discriminator is misnamed there, so event_type is
typed nullable (D-R10). Coverage reaches 28.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 5: Market status, conditions, and exchanges

**Files:**
- Modify: `specs/endpoints.map.json` (nine model rows, three endpoint rows)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceMarketStatusTests.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceConditionsTests.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceExchangesTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 28 → 31)

**Interfaces:**
- Consumes: the `DailyOpenClose` model row and `GetStocksOpenClose` endpoint row as the pattern for a body-object payload (no `pointer`, no `property`); the `TickerSnapshot` rows as the pattern for a model reused at a second site (D16 verifies `UpdateRule` at `market_center`); a verbatim `Instant?` row, which the context's converter reads.
- Produces: `client.Reference.GetMarketStatusAsync(CancellationToken cancellationToken = default)` returning `Task<MarketStatus>`; `client.Reference.ListConditionsAsync(MarketType? assetClass = null, string? dataType = null, int? id = null, string? sip = null, SortOrder? order = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<Condition>>` with `EnumerateConditionsAsync`; `client.Reference.ListExchangesAsync(MarketType? assetClass = null, string? locale = null, CancellationToken cancellationToken = default)` returning `Task<Exchange[]>`. Models: `MarketStatus` (`string? Market`, `bool? IsEarlyHours`, `bool? IsAfterHours`, `Instant? ServerTime`, `MarketStatusExchanges? Exchanges`, `MarketStatusCurrencies? Currencies`, `MarketStatusIndexGroups? IndexGroups`); `MarketStatusExchanges` (`Nyse`, `Nasdaq`, `Otc`, all `string?`); `MarketStatusCurrencies` (`Fx`, `Crypto`); `MarketStatusIndexGroups` (`SAndP`, `SocieteGenerale`, `Msci`, `FtseRussell`, `Mstar`, `Mstarc`, `Cccy`, `Cgi`, `Nasdaq`, `DowJones`); `Condition` (`int Id`, `required string Type`, `required string Name`, `required string AssetClass`, `string? Abbreviation`, `string? Description`, `required string[] DataTypes`, `int? Exchange`, `bool? IsLegacy`, `required SipMapping SipMapping`, `ConditionUpdateRules? UpdateRules`); `SipMapping` (`Cta`, `Utp`, `Opra`, all `string?`); `ConditionUpdateRules` (`required UpdateRule Consolidated`, `required UpdateRule MarketCenter`); `UpdateRule` (`bool UpdatesHighLow`, `bool UpdatesOpenClose`, `bool UpdatesVolume`); `Exchange` (`int Id`, `required string Type`, `required string AssetClass`, `required string Locale`, `required string Name`, `string? Acronym`, `string? Mic`, `string? OperatingMic`, `string? ParticipantId`, `string? Url`). No partials. Task 10 calls `GetMarketStatusAsync`; Task 11 calls all three.

- [ ] **Step 1: Add the fixtures**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, after the `ReferenceRelatedCompanies` member:

```csharp
    /// <summary>
    /// The documented sample for GET /v1/marketstatus/now: the body is the payload, with no
    /// envelope (decision D17). The sample omits <c>indicesGroups</c>, which the live service
    /// sends; the model leaves it null here.
    /// </summary>
    public const string ReferenceMarketStatus = """
        {
          "afterHours": true,
          "currencies": {
            "crypto": "open",
            "fx": "open"
          },
          "earlyHours": false,
          "exchanges": {
            "nasdaq": "extended-hours",
            "nyse": "extended-hours",
            "otc": "closed"
          },
          "market": "extended-hours",
          "serverTime": "2020-11-10T17:37:37-05:00"
        }
        """;

    /// <summary>The documented sample for GET /v3/reference/conditions.</summary>
    public const string ReferenceConditions = """
        {
          "count": 1,
          "request_id": "31d59dda-80e5-4721-8496-d0d32a654afe",
          "results": [
            {
              "asset_class": "stocks",
              "data_types": [
                "trade"
              ],
              "id": 2,
              "name": "Average Price Trade",
              "sip_mapping": {
                "CTA": "B",
                "UTP": "W"
              },
              "type": "condition",
              "update_rules": {
                "consolidated": {
                  "updates_high_low": false,
                  "updates_open_close": false,
                  "updates_volume": true
                },
                "market_center": {
                  "updates_high_low": false,
                  "updates_open_close": false,
                  "updates_volume": true
                }
              }
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// GET /v3/reference/exchanges?asset_class=stocks&amp;locale=us, captured from the live
    /// service on 2026-09-03 because the description publishes only a CSV example for it
    /// (D-R12). Reviewed: it carries no account identifier and no URL embeds a key.
    /// </summary>
    public const string ReferenceExchanges = """
        {
          "count": 27,
          "request_id": "33edc6f450e0bb88f54bb3e1329f10e7",
          "results": [
            { "acronym": "AMEX", "asset_class": "stocks", "id": 1, "locale": "us", "mic": "XASE", "name": "NYSE American, LLC", "operating_mic": "XNYS", "participant_id": "A", "type": "exchange", "url": "https://www.nyse.com/markets/nyse-american" },
            { "asset_class": "stocks", "id": 2, "locale": "us", "mic": "XBOS", "name": "Nasdaq Texas, Inc.", "operating_mic": "XNAS", "participant_id": "B", "type": "exchange", "url": "https://www.nasdaq.com/solutions/nasdaq-bx-stock-market" },
            { "acronym": "NSX", "asset_class": "stocks", "id": 3, "locale": "us", "mic": "XCIS", "name": "NYSE National, Inc.", "operating_mic": "XNYS", "participant_id": "C", "type": "exchange", "url": "https://www.nyse.com/markets/nyse-national" },
            { "asset_class": "stocks", "id": 4, "locale": "us", "mic": "XADF", "name": "FINRA Alternative Display Facility", "operating_mic": "FINR", "participant_id": "D", "type": "TRF", "url": "https://www.finra.org" },
            { "asset_class": "stocks", "id": 5, "locale": "us", "name": "Unlisted Trading Privileges", "operating_mic": "XNAS", "participant_id": "E", "type": "SIP", "url": "https://www.utpplan.com" },
            { "asset_class": "stocks", "id": 6, "locale": "us", "mic": "XISE", "name": "International Securities Exchange, LLC - Stocks", "operating_mic": "XNAS", "participant_id": "I", "type": "TRF", "url": "https://nasdaq.com/solutions/nasdaq-ise" },
            { "asset_class": "stocks", "id": 7, "locale": "us", "mic": "EDGA", "name": "Cboe EDGA", "operating_mic": "XCBO", "participant_id": "J", "type": "exchange", "url": "https://www.cboe.com/us/equities" },
            { "asset_class": "stocks", "id": 8, "locale": "us", "mic": "EDGX", "name": "Cboe EDGX", "operating_mic": "XCBO", "participant_id": "K", "type": "exchange", "url": "https://www.cboe.com/us/equities" },
            { "asset_class": "stocks", "id": 9, "locale": "us", "mic": "XCHI", "name": "NYSE Texas, Inc.", "operating_mic": "XNYS", "participant_id": "M", "type": "exchange", "url": "https://www.nyse.com/markets/nyse-texas" },
            { "asset_class": "stocks", "id": 10, "locale": "us", "mic": "XNYS", "name": "New York Stock Exchange", "operating_mic": "XNYS", "participant_id": "N", "type": "exchange", "url": "https://www.nyse.com" },
            { "asset_class": "stocks", "id": 11, "locale": "us", "mic": "ARCX", "name": "NYSE Arca, Inc.", "operating_mic": "XNYS", "participant_id": "P", "type": "exchange", "url": "https://www.nyse.com/markets/nyse-arca" },
            { "asset_class": "stocks", "id": 12, "locale": "us", "mic": "XNAS", "name": "Nasdaq", "operating_mic": "XNAS", "participant_id": "T", "type": "exchange", "url": "https://www.nasdaq.com" },
            { "asset_class": "stocks", "id": 13, "locale": "us", "name": "Consolidated Tape Association", "operating_mic": "XNYS", "participant_id": "S", "type": "SIP", "url": "https://www.nyse.com/data/cta" },
            { "asset_class": "stocks", "id": 14, "locale": "us", "mic": "LTSE", "name": "Long-Term Stock Exchange", "operating_mic": "LTSE", "participant_id": "L", "type": "exchange", "url": "https://www.ltse.com" },
            { "asset_class": "stocks", "id": 15, "locale": "us", "mic": "IEXG", "name": "Investors Exchange", "operating_mic": "IEXG", "participant_id": "V", "type": "exchange", "url": "https://www.iextrading.com" },
            { "asset_class": "stocks", "id": 16, "locale": "us", "mic": "CBSX", "name": "Cboe Stock Exchange", "operating_mic": "XCBO", "participant_id": "W", "type": "TRF", "url": "https://www.cboe.com" },
            { "asset_class": "stocks", "id": 17, "locale": "us", "mic": "XPHL", "name": "Nasdaq Philadelphia Exchange LLC", "operating_mic": "XNAS", "participant_id": "X", "type": "exchange", "url": "https://www.nasdaq.com/solutions/nasdaq-phlx" },
            { "asset_class": "stocks", "id": 18, "locale": "us", "mic": "BATY", "name": "Cboe BYX", "operating_mic": "XCBO", "participant_id": "Y", "type": "exchange", "url": "https://www.cboe.com/us/equities" },
            { "asset_class": "stocks", "id": 19, "locale": "us", "mic": "BATS", "name": "Cboe BZX", "operating_mic": "XCBO", "participant_id": "Z", "type": "exchange", "url": "https://www.cboe.com/us/equities" },
            { "asset_class": "stocks", "id": 20, "locale": "us", "mic": "EPRL", "name": "MIAX Pearl", "operating_mic": "MIHI", "participant_id": "H", "type": "exchange", "url": "https://www.miaxoptions.com/alerts/pearl-equities" },
            { "asset_class": "stocks", "id": 21, "locale": "us", "mic": "MEMX", "name": "Members Exchange", "operating_mic": "MEMX", "participant_id": "U", "type": "exchange", "url": "https://www.memx.com" },
            { "acronym": "24X", "asset_class": "stocks", "id": 22, "locale": "us", "mic": "24EQ", "name": "24X National Exchange LLC", "operating_mic": "24EQ", "participant_id": "G", "type": "exchange", "url": "https://24exchange.com/" },
            { "acronym": "TXSE", "asset_class": "stocks", "id": 23, "locale": "us", "mic": "TXSE", "name": "Texas Stock Exchange LLC", "operating_mic": "TXSE", "participant_id": "F", "type": "exchange", "url": "https://txse.com/" },
            { "asset_class": "stocks", "id": 62, "locale": "us", "mic": "OOTC", "name": "OTC Equity Security", "operating_mic": "FINR", "type": "ORF", "url": "https://www.finra.org/filing-reporting/over-the-counter-reporting-facility-orf" },
            { "asset_class": "stocks", "id": 201, "locale": "us", "mic": "FINY", "name": "FINRA NYSE TRF", "operating_mic": "FINR", "type": "TRF", "url": "https://www.finra.org" },
            { "asset_class": "stocks", "id": 202, "locale": "us", "mic": "FINN", "name": "FINRA Nasdaq TRF Carteret", "operating_mic": "FINR", "type": "TRF", "url": "https://www.finra.org" },
            { "asset_class": "stocks", "id": 203, "locale": "us", "mic": "FINC", "name": "FINRA Nasdaq TRF Chicago", "operating_mic": "FINR", "type": "TRF", "url": "https://www.finra.org" }
          ],
          "status": "OK"
        }
        """;
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/ReferenceMarketStatusTests.cs`:

```csharp
using System.Net;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Market status: a body-object payload with no envelope (decision D17), three nested models,
/// and a server time carrying a UTC offset that the <see cref="Instant"/> converter reads.
/// </summary>
public sealed class ReferenceMarketStatusTests
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
        StubHandler handler = new(Fixtures.ReferenceMarketStatus);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.GetMarketStatusAsync(Ct);
        }

        Assert.Equal("https://api.massive.com/v1/marketstatus/now", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceMarketStatus);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MarketStatus status;

        using (client)
        using (transport)
        {
            status = await client.Reference.GetMarketStatusAsync(Ct);
        }

        Assert.Equal("extended-hours", status.Market);
        Assert.True(status.IsAfterHours);
        Assert.False(status.IsEarlyHours);

        // 17:37:37 at -05:00 is 22:37:37Z; the offset must be applied, not dropped.
        Assert.Equal(Instant.FromUtc(2020, 11, 10, 22, 37, 37), status.ServerTime);

        Assert.NotNull(status.Exchanges);
        Assert.Equal("extended-hours", status.Exchanges.Nyse);
        Assert.Equal("extended-hours", status.Exchanges.Nasdaq);
        Assert.Equal("closed", status.Exchanges.Otc);

        Assert.NotNull(status.Currencies);
        Assert.Equal("open", status.Currencies.Fx);
        Assert.Equal("open", status.Currencies.Crypto);

        Assert.Null(status.IndexGroups);
    }

    [Fact]
    public async Task ANullBodyIsReported()
    {
        StubHandler handler = new("null");
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Reference.GetMarketStatusAsync(Ct));

            Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
            Assert.Null(exception.RequestId);
            Assert.Contains("carried no payload", exception.Message, StringComparison.Ordinal);
        }
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/ReferenceConditionsTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Conditions: a <see cref="MarketType"/> on <c>asset_class</c> (D-R6), and one model,
/// <see cref="UpdateRule"/>, declared at <c>consolidated</c> and verified by the generator at
/// <c>market_center</c> (decision D16).
/// </summary>
public sealed class ReferenceConditionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersEveryParameterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceConditions);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListConditionsAsync(
                assetClass: MarketType.Stocks,
                dataType: "trade",
                id: 2,
                sip: "CTA",
                order: SortOrder.Ascending,
                limit: 10,
                sort: "id",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v3/reference/conditions?asset_class=stocks&data_type=trade&id=2&sip=CTA&order=asc&limit=10&sort=id",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSampleThroughEveryNestedModel()
    {
        StubHandler handler = new(Fixtures.ReferenceConditions);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Condition> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListConditionsAsync(cancellationToken: Ct);
        }

        Condition condition = Assert.Single(page.Results);
        Assert.Equal(2, condition.Id);
        Assert.Equal("Average Price Trade", condition.Name);
        Assert.Equal("condition", condition.Type);
        Assert.Equal("stocks", condition.AssetClass);
        Assert.Equal(["trade"], condition.DataTypes);
        Assert.Null(condition.Abbreviation);
        Assert.Null(condition.Exchange);
        Assert.Null(condition.IsLegacy);

        Assert.Equal("B", condition.SipMapping.Cta);
        Assert.Equal("W", condition.SipMapping.Utp);
        Assert.Null(condition.SipMapping.Opra);

        Assert.NotNull(condition.UpdateRules);
        Assert.False(condition.UpdateRules.Consolidated.UpdatesHighLow);
        Assert.False(condition.UpdateRules.Consolidated.UpdatesOpenClose);
        Assert.True(condition.UpdateRules.Consolidated.UpdatesVolume);
        Assert.True(condition.UpdateRules.MarketCenter.UpdatesVolume);

        Assert.False(page.HasMore);
        Assert.Equal("31d59dda-80e5-4721-8496-d0d32a654afe", page.RequestId);
    }

    [Fact]
    public async Task EnumerateWalksTheSinglePage()
    {
        StubHandler handler = new(Fixtures.ReferenceConditions);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<int> ids = [];

        using (client)
        using (transport)
        {
            await foreach (Condition condition in client.Reference.EnumerateConditionsAsync(assetClass: MarketType.Stocks, cancellationToken: Ct))
            {
                ids.Add(condition.Id);
            }
        }

        Assert.Equal(2, Assert.Single(ids));
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/ReferenceExchangesTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The cross-asset exchanges list: an unpaginated array whose fixture is a reviewed live
/// capture (D-R12), distinct from the stocks-only <c>StockExchange</c> (D-R7).
/// </summary>
public sealed class ReferenceExchangesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersTheAssetClassAndLocale()
    {
        StubHandler handler = new(Fixtures.ReferenceExchanges);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListExchangesAsync(assetClass: MarketType.Stocks, locale: "us", cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v3/reference/exchanges?asset_class=stocks&locale=us", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesTheCapturedResponse()
    {
        StubHandler handler = new(Fixtures.ReferenceExchanges);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        Exchange[] exchanges;

        using (client)
        using (transport)
        {
            exchanges = await client.Reference.ListExchangesAsync(cancellationToken: Ct);
        }

        Assert.Equal(27, exchanges.Length);

        Exchange amex = exchanges[0];
        Assert.Equal(1, amex.Id);
        Assert.Equal("exchange", amex.Type);
        Assert.Equal("stocks", amex.AssetClass);
        Assert.Equal("us", amex.Locale);
        Assert.Equal("NYSE American, LLC", amex.Name);
        Assert.Equal("AMEX", amex.Acronym);
        Assert.Equal("XASE", amex.Mic);
        Assert.Equal("XNYS", amex.OperatingMic);
        Assert.Equal("A", amex.ParticipantId);
        Assert.Equal("https://www.nyse.com/markets/nyse-american", amex.Url);

        Exchange utp = exchanges[4];
        Assert.Equal("SIP", utp.Type);
        Assert.Null(utp.Mic);
        Assert.Null(utp.Acronym);

        Assert.Contains(exchanges, exchange => exchange.Mic == "XNYS" && exchange.Name == "New York Stock Exchange");
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, raise the baseline:

```csharp
    private const int CoverageBaseline = 31;
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~ReferenceMarketStatus|FullyQualifiedName~ReferenceConditions|FullyQualifiedName~ReferenceExchanges"`
Expected: the build fails with CS1061 for the three methods and CS0246 for `MarketStatus`, `Condition`, and `Exchange`.

- [ ] **Step 4: Add the model rows**

In `specs/endpoints.map.json`, append inside `models` after the `RelatedCompany` row (add a comma after its closing brace):

```json
    "MarketStatus": {
      "summary": "The trading status of the markets right now: the overall state, each exchange's, the currency markets', and the index groups', with the server's own clock.",
      "remarks": "The response body is the payload (decision D17). <see cref=\"ServerTime\"/> is an RFC 3339 timestamp carrying the server's UTC offset, which the description types as a bare string; the map binds it to <see cref=\"NodaTime.Instant\"/> and the converter applies the offset (D-R10).",
      "schema": { "operationId": "GetMarketStatus" },
      "properties": {
        "market":        { "name": "Market" },
        "earlyHours":    { "name": "IsEarlyHours" },
        "afterHours":    { "name": "IsAfterHours" },
        "serverTime":    { "name": "ServerTime", "type": "Instant?", "summary": "The server's current time. The description types this a bare string; the wire carries an RFC 3339 timestamp with the server's UTC offset (D-R10)." },
        "exchanges":     { "name": "Exchanges", "model": "MarketStatusExchanges" },
        "currencies":    { "name": "Currencies", "model": "MarketStatusCurrencies" },
        "indicesGroups": { "name": "IndexGroups", "model": "MarketStatusIndexGroups" }
      }
    },

    "MarketStatusExchanges": {
      "summary": "The status of each US equities venue in a market status: NYSE, Nasdaq, and OTC.",
      "schema": { "operationId": "GetMarketStatus", "pointer": "exchanges" },
      "properties": {
        "nyse":   { "name": "Nyse" },
        "nasdaq": { "name": "Nasdaq" },
        "otc":    { "name": "Otc" }
      }
    },

    "MarketStatusCurrencies": {
      "summary": "The status of the currency markets in a market status: forex and crypto.",
      "schema": { "operationId": "GetMarketStatus", "pointer": "currencies" },
      "properties": {
        "fx":     { "name": "Fx" },
        "crypto": { "name": "Crypto" }
      }
    },

    "MarketStatusIndexGroups": {
      "summary": "The status of each index provider's group in a market status.",
      "remarks": "The published example omits this object; the live service sends it. Each member is the provider's status string.",
      "schema": { "operationId": "GetMarketStatus", "pointer": "indicesGroups" },
      "properties": {
        "s_and_p":          { "name": "SAndP" },
        "societe_generale": { "name": "SocieteGenerale" },
        "msci":             { "name": "Msci" },
        "ftse_russell":     { "name": "FtseRussell" },
        "mstar":            { "name": "Mstar" },
        "mstarc":           { "name": "Mstarc" },
        "cccy":             { "name": "Cccy" },
        "cgi":              { "name": "Cgi" },
        "nasdaq":           { "name": "Nasdaq" },
        "dow_jones":        { "name": "DowJones" }
      }
    },

    "Condition": {
      "summary": "A trade or quote condition code: its identifier, name, and type, the SIPs that carry it, and how it affects aggregates.",
      "remarks": "Reference data, so a class (decision D4). <see cref=\"SipMapping\"/> names the code each SIP uses for this condition; <see cref=\"UpdateRules\"/> says whether a trade carrying it updates the high, low, open, close, and volume of consolidated and per-venue aggregates.",
      "schema": { "operationId": "ListConditions", "pointer": "results/items" },
      "properties": {
        "id":           { "name": "Id" },
        "type":         { "name": "Type" },
        "name":         { "name": "Name" },
        "asset_class":  { "name": "AssetClass" },
        "abbreviation": { "name": "Abbreviation" },
        "description":  { "name": "Description" },
        "data_types":   { "name": "DataTypes" },
        "exchange":     { "name": "Exchange" },
        "legacy":       { "name": "IsLegacy" },
        "sip_mapping":  { "name": "SipMapping", "model": "SipMapping" },
        "update_rules": { "name": "UpdateRules", "model": "ConditionUpdateRules" }
      }
    },

    "SipMapping": {
      "summary": "The code each securities information processor uses for one condition; a SIP that does not carry the condition is null.",
      "schema": { "operationId": "ListConditions", "pointer": "results/items/sip_mapping" },
      "properties": {
        "CTA":  { "name": "Cta" },
        "UTP":  { "name": "Utp" },
        "OPRA": { "name": "Opra" }
      }
    },

    "ConditionUpdateRules": {
      "summary": "How a condition affects aggregates, for the consolidated tape and for a single market center.",
      "remarks": "Both halves are the same shape, so one model, <see cref=\"UpdateRule\"/>, is declared from the consolidated half and verified by the generator at the other (decision D16).",
      "schema": { "operationId": "ListConditions", "pointer": "results/items/update_rules" },
      "properties": {
        "consolidated":  { "name": "Consolidated", "model": "UpdateRule" },
        "market_center": { "name": "MarketCenter", "model": "UpdateRule" }
      }
    },

    "UpdateRule": {
      "summary": "Whether a trade carrying a condition updates an aggregate's high and low, open and close, and volume.",
      "schema": { "operationId": "ListConditions", "pointer": "results/items/update_rules/consolidated" },
      "properties": {
        "updates_high_low":   { "name": "UpdatesHighLow" },
        "updates_open_close": { "name": "UpdatesOpenClose" },
        "updates_volume":     { "name": "UpdatesVolume" }
      }
    },

    "Exchange": {
      "summary": "An exchange, trade reporting facility, or SIP for any asset class, with its MIC codes and identifiers.",
      "remarks": "Reference data, so a class (decision D4). Unprefixed because it spans every asset class; <see cref=\"StockExchange\"/>, from the stocks-only route, lacks <see cref=\"AssetClass\"/> and so is a separate model (decision D16, D-R7).",
      "schema": { "operationId": "ListExchanges", "pointer": "results/items" },
      "properties": {
        "id":             { "name": "Id" },
        "type":           { "name": "Type" },
        "asset_class":    { "name": "AssetClass" },
        "locale":         { "name": "Locale" },
        "name":           { "name": "Name" },
        "acronym":        { "name": "Acronym" },
        "mic":            { "name": "Mic" },
        "operating_mic":  { "name": "OperatingMic" },
        "participant_id": { "name": "ParticipantId" },
        "url":            { "name": "Url" }
      }
    }
```

- [ ] **Step 5: Add the endpoint rows**

In `specs/endpoints.map.json`, append inside `endpoints` after the `GetRelatedCompanies` row (add a comma after its closing brace):

```json
    {
      "operationId": "GetMarketStatus",
      "group": "Reference",
      "method": "GetMarketStatus",
      "summary": "Retrieves the current trading status of the markets: overall, per exchange, for the currency markets, and for each index group.",
      "remarks": "The body is the payload, with no envelope, so the response carries no request id (decision D17). Pair with <see cref=\"ListMarketHolidaysAsync\"/> for what is coming rather than what is now.",
      "result": { "kind": "object", "model": "MarketStatus" }
    },
    {
      "operationId": "ListConditions",
      "group": "Reference",
      "method": "ListConditions",
      "summary": "Retrieves the trade and quote condition codes, with each one's SIP mappings and its effect on aggregates.",
      "remarks": "Every filter is optional and defaults to no constraint. <paramref name=\"assetClass\"/> is a <see cref=\"MarketType\"/>; the description declares four of its members here and the server rejects the rest. <paramref name=\"dataType\"/> is <c>trade</c>, <c>bbo</c>, or <c>nbbo</c>; <paramref name=\"sip\"/> is <c>CTA</c>, <c>UTP</c>, or <c>OPRA</c>.",
      "result": { "kind": "array", "model": "Condition", "property": "results" },
      "parameters": {
        "asset_class": { "name": "assetClass", "type": "MarketType" },
        "data_type":   { "name": "dataType" },
        "id":          { "name": "id" },
        "sip":         { "name": "sip" },
        "order":       { "name": "order", "type": "SortOrder" },
        "limit":       { "name": "limit" },
        "sort":        { "name": "sort" }
      }
    },
    {
      "operationId": "ListExchanges",
      "group": "Reference",
      "method": "ListExchanges",
      "summary": "Retrieves the exchanges, trade reporting facilities, and SIPs the platform knows, optionally for one asset class and locale.",
      "remarks": "The list does not page. <paramref name=\"assetClass\"/> is a <see cref=\"MarketType\"/>; the description declares five of its members here and the server rejects the rest.",
      "result": { "kind": "array", "model": "Exchange", "property": "results" },
      "parameters": {
        "asset_class": { "name": "assetClass", "type": "MarketType" },
        "locale":      { "name": "locale" }
      }
    }
```

- [ ] **Step 6: Regenerate**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: nine new model files; `MarketStatus` registered on the context as a body type; `GetMarketStatusAsync(CancellationToken cancellationToken = default)` returning `Task<MarketStatus>` and throwing on a null body; `ListConditionsAsync` / `EnumerateConditionsAsync` with `MarketType? assetClass`; `ListExchangesAsync` returning `Task<Exchange[]>`. The generator's structural check accepts `UpdateRule` at `market_center`. No partials.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS; `EndpointCoverageTests` reports 31 mapped.

- [ ] **Step 8: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff.

- [ ] **Step 9: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/ReferenceMarketStatusTests.cs tests/MassiveDotNet.Rest.Tests/ReferenceConditionsTests.cs tests/MassiveDotNet.Rest.Tests/ReferenceExchangesTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs
git commit -m "feat: map market status, conditions, and the cross-asset exchanges

Market status is a body object whose serverTime binds Instant from
the map (D-R10); conditions reuse one UpdateRule at both halves of
update_rules; exchanges takes a reviewed live capture (D-R12) and its
own model beside StockExchange (D-R7). Coverage reaches 31.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 6: The v3 dividends and splits

**Files:**
- Modify: `specs/endpoints.map.json` (two model rows, two endpoint rows)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceDividendsTests.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceSplitsTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 31 → 33)

**Interfaces:**
- Consumes: the `get_stocks_v1_dividends` and `get_stocks_v1_splits` rows as the pattern; `RangeFilter<T>` for a plain-plus-four-bounds group; `LocalDate` on a bare-string date (D-R9).
- Produces: `client.Reference.ListDividendsAsync(RangeFilter<string>? ticker = null, RangeFilter<LocalDate>? exDividendDate = null, RangeFilter<LocalDate>? recordDate = null, RangeFilter<LocalDate>? declarationDate = null, RangeFilter<LocalDate>? payDate = null, int? frequency = null, RangeFilter<double>? cashAmount = null, string? dividendType = null, SortOrder? order = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<ReferenceDividend>>` with `EnumerateDividendsAsync`; `client.Reference.ListSplitsAsync(RangeFilter<string>? ticker = null, RangeFilter<LocalDate>? executionDate = null, bool? reverseSplit = null, SortOrder? order = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<ReferenceSplit>>` with `EnumerateSplitsAsync`. Models: `ReferenceDividend` (`required string Id`, `required string Ticker`, `double CashAmount`, `string? Currency`, `LocalDate? DeclarationDate`, `required string DividendType`, `LocalDate ExDividendDate`, `int Frequency`, `LocalDate? PayDate`, `LocalDate? RecordDate`); `ReferenceSplit` (`required string Id`, `required string Ticker`, `LocalDate ExecutionDate`, `double SplitFrom`, `double SplitTo`). No partials. Task 11 calls both.

- [ ] **Step 1: Add the fixtures**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, after the `ReferenceExchanges` member:

```csharp
    /// <summary>The documented sample for GET /v3/reference/dividends. It carries a cursor of its own.</summary>
    public const string ReferenceDividends = """
        {
          "next_url": "https://api.massive.com/v3/reference/dividends/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "6a7e466379af0a71039d60cc78e72282",
          "results": [
            {
              "cash_amount": 0.22,
              "declaration_date": "2021-10-28",
              "dividend_type": "CD",
              "ex_dividend_date": "2021-11-05",
              "frequency": 4,
              "id": "E8e3c4f794613e9205e2f178a36c53fcc57cdabb55e1988c87b33f9e52e221444",
              "pay_date": "2021-11-11",
              "record_date": "2021-11-08",
              "ticker": "AAPL"
            },
            {
              "cash_amount": 0.22,
              "declaration_date": "2021-07-27",
              "dividend_type": "CD",
              "ex_dividend_date": "2021-08-06",
              "frequency": 4,
              "id": "E6436c5475706773f03490acf0b63fdb90b2c72bfeed329a6eb4afc080acd80ae",
              "pay_date": "2021-08-12",
              "record_date": "2021-08-09",
              "ticker": "AAPL"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /v3/reference/splits. It carries a cursor of its own.</summary>
    public const string ReferenceSplits = """
        {
          "next_url": "https://api.massive.com/v3/splits/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "6a7e466379af0a71039d60cc78e72282",
          "results": [
            {
              "execution_date": "2020-08-31",
              "id": "E36416cce743c3964c5da63e1ef1626c0aece30fb47302eea5a49c0055c04e8d0",
              "split_from": 1,
              "split_to": 4,
              "ticker": "AAPL"
            },
            {
              "execution_date": "2005-02-28",
              "id": "E90a77bdf742661741ed7c8fc086415f0457c2816c45899d73aaa88bdc8ff6025",
              "split_from": 1,
              "split_to": 2,
              "ticker": "AAPL"
            }
          ],
          "status": "OK"
        }
        """;
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/ReferenceDividendsTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The v3 dividends: four calendar-date ranges and a cash-amount range, all derived from the
/// spec's suffix sets (D15), on a model separate from the stocks/v1 <c>Dividend</c> (D-R7).
/// </summary>
public sealed class ReferenceDividendsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersEveryFilterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListDividendsAsync(
                ticker: "AAPL",
                exDividendDate: RangeFilter.Between(new LocalDate(2021, 1, 1), new LocalDate(2021, 12, 31)),
                recordDate: RangeFilter.Gt(new LocalDate(2021, 1, 1)),
                declarationDate: RangeFilter.Lt(new LocalDate(2022, 1, 1)),
                payDate: new LocalDate(2021, 11, 11),
                frequency: 4,
                cashAmount: RangeFilter.Gte(0.2),
                dividendType: "CD",
                order: SortOrder.Descending,
                limit: 2,
                sort: "ex_dividend_date",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v3/reference/dividends"
                + "?ticker=AAPL"
                + "&ex_dividend_date.gte=2021-01-01&ex_dividend_date.lte=2021-12-31"
                + "&record_date.gt=2021-01-01"
                + "&declaration_date.lt=2022-01-01"
                + "&pay_date=2021-11-11"
                + "&frequency=4"
                + "&cash_amount.gte=0.2"
                + "&dividend_type=CD&order=desc&limit=2&sort=ex_dividend_date",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<ReferenceDividend> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListDividendsAsync(ticker: "AAPL", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);

        ReferenceDividend dividend = page.Results[0];
        Assert.Equal("AAPL", dividend.Ticker);
        Assert.Equal("E8e3c4f794613e9205e2f178a36c53fcc57cdabb55e1988c87b33f9e52e221444", dividend.Id);
        Assert.Equal(0.22, dividend.CashAmount);
        Assert.Equal("CD", dividend.DividendType);
        Assert.Equal(4, dividend.Frequency);
        Assert.Equal(new LocalDate(2021, 10, 28), dividend.DeclarationDate);
        Assert.Equal(new LocalDate(2021, 11, 5), dividend.ExDividendDate);
        Assert.Equal(new LocalDate(2021, 11, 8), dividend.RecordDate);
        Assert.Equal(new LocalDate(2021, 11, 11), dividend.PayDate);
        Assert.Null(dividend.Currency);

        Assert.Equal(new LocalDate(2021, 8, 6), page.Results[1].ExDividendDate);
        Assert.True(page.HasMore);
        Assert.Equal("6a7e466379af0a71039d60cc78e72282", page.RequestId);
    }

    [Fact]
    public async Task EnumerateFollowsTheSampleCursorThenStops()
    {
        // The sample's cursor points at the same origin, so the traversal follows it verbatim
        // (D14); the stub serves the same page again, which has a cursor too, so the test stops
        // the traversal itself after the seam it set out to cross.
        PagingStubHandler handler = new(Fixtures.ReferenceDividends);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string> ids = [];

        using (client)
        using (transport)
        {
            await foreach (ReferenceDividend dividend in client.Reference.EnumerateDividendsAsync(ticker: "AAPL", cancellationToken: Ct))
            {
                ids.Add(dividend.Id);

                if (ids.Count == 3)
                {
                    break;
                }
            }
        }

        Assert.Equal(3, ids.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.StartsWith("https://api.massive.com/v3/reference/dividends/AAPL?cursor=", handler.Requests[1].ToString(), StringComparison.Ordinal);
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/ReferenceSplitsTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The v3 splits: a lexical ticker range, a calendar-date range, and a boolean, on a model
/// separate from the stocks/v1 <c>Split</c> (D-R7).
/// </summary>
public sealed class ReferenceSplitsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersEveryFilterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceSplits);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListSplitsAsync(
                ticker: RangeFilter.Between("A", "B"),
                executionDate: RangeFilter.Gte(new LocalDate(2020, 1, 1)),
                reverseSplit: false,
                order: SortOrder.Ascending,
                limit: 2,
                sort: "execution_date",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v3/reference/splits"
                + "?ticker.gte=A&ticker.lte=B&execution_date.gte=2020-01-01&reverse_split=false&order=asc&limit=2&sort=execution_date",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceSplits);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<ReferenceSplit> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListSplitsAsync(ticker: "AAPL", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);

        ReferenceSplit split = page.Results[0];
        Assert.Equal("AAPL", split.Ticker);
        Assert.Equal("E36416cce743c3964c5da63e1ef1626c0aece30fb47302eea5a49c0055c04e8d0", split.Id);
        Assert.Equal(new LocalDate(2020, 8, 31), split.ExecutionDate);
        Assert.Equal(1d, split.SplitFrom);
        Assert.Equal(4d, split.SplitTo);

        Assert.Equal(new LocalDate(2005, 2, 28), page.Results[1].ExecutionDate);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task EnumerateYieldsTheFirstPageInOrder()
    {
        PagingStubHandler handler = new(Fixtures.ReferenceSplits);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<double> ratios = [];

        using (client)
        using (transport)
        {
            await foreach (ReferenceSplit split in client.Reference.EnumerateSplitsAsync(ticker: "AAPL", cancellationToken: Ct))
            {
                ratios.Add(split.SplitTo / split.SplitFrom);

                if (ratios.Count == 2)
                {
                    break;
                }
            }
        }

        Assert.Equal([4d, 2d], ratios);
        Assert.Single(handler.Requests);
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, raise the baseline:

```csharp
    private const int CoverageBaseline = 33;
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~ReferenceDividends|FullyQualifiedName~ReferenceSplits"`
Expected: the build fails with CS1061 for both methods and CS0246 for `ReferenceDividend` and `ReferenceSplit`.

- [ ] **Step 4: Add the model rows**

In `specs/endpoints.map.json`, append inside `models` after the `Exchange` row (add a comma after its closing brace):

```json
    "ReferenceDividend": {
      "summary": "A cash dividend from the v3 reference route: the ticker, the four dates that govern it, the amount, its type, and how often it pays.",
      "remarks": "Reference data, so a class (decision D4). Prefixed because the stocks/v1 route's <see cref=\"Dividend\"/> carries two properties this one lacks, so the two cannot share a model (decision D16, D-R7). The four dates are bare strings in the description; the published example carries ISO calendar dates, so the map binds them to <see cref=\"NodaTime.LocalDate\"/> (D-R9).",
      "schema": { "operationId": "ListDividends", "pointer": "results/items" },
      "properties": {
        "id":               { "name": "Id" },
        "ticker":           { "name": "Ticker" },
        "cash_amount":      { "name": "CashAmount" },
        "currency":         { "name": "Currency" },
        "dividend_type":    { "name": "DividendType" },
        "frequency":        { "name": "Frequency" },
        "declaration_date": { "name": "DeclarationDate", "type": "LocalDate?", "summary": "The date the dividend was announced. The description types this a bare string; the wire carries an ISO calendar date (D-R9)." },
        "ex_dividend_date": { "name": "ExDividendDate", "type": "LocalDate", "summary": "The first trading day on which a buyer is not entitled to the dividend. The description types this a bare string; the wire carries an ISO calendar date (D-R9)." },
        "record_date":      { "name": "RecordDate", "type": "LocalDate?", "summary": "The date a holder must be on the register to receive the dividend. The description types this a bare string; the wire carries an ISO calendar date (D-R9)." },
        "pay_date":         { "name": "PayDate", "type": "LocalDate?", "summary": "The date the dividend is paid. The description types this a bare string; the wire carries an ISO calendar date (D-R9)." }
      }
    },

    "ReferenceSplit": {
      "summary": "A stock split from the v3 reference route: the ticker, the execution date, and the ratio.",
      "remarks": "Reference data, so a class (decision D4). Prefixed because the stocks/v1 route's <see cref=\"Split\"/> carries two properties this one lacks (decision D16, D-R7). <see cref=\"ExecutionDate\"/> is a bare string in the description and an ISO calendar date on the wire, hence <see cref=\"NodaTime.LocalDate\"/> from the map (D-R9).",
      "schema": { "operationId": "ListStockSplits", "pointer": "results/items" },
      "properties": {
        "id":             { "name": "Id" },
        "ticker":         { "name": "Ticker" },
        "execution_date": { "name": "ExecutionDate", "type": "LocalDate", "summary": "The date the split took effect. The description types this a bare string; the wire carries an ISO calendar date (D-R9)." },
        "split_from":     { "name": "SplitFrom" },
        "split_to":       { "name": "SplitTo" }
      }
    }
```

- [ ] **Step 5: Add the endpoint rows**

In `specs/endpoints.map.json`, append inside `endpoints` after the `ListExchanges` row (add a comma after its closing brace):

```json
    {
      "operationId": "ListDividends",
      "group": "Reference",
      "method": "ListDividends",
      "summary": "Retrieves cash dividends from the v3 reference route, filtered by ticker, by any of the four dates that govern a dividend, by amount, and by type.",
      "remarks": "Every filter is optional and defaults to no constraint. Pass a plain value for equality or a <see cref=\"RangeFilter\"/> factory for a range. The date filters are bare strings in the description and take <see cref=\"NodaTime.LocalDate\"/> here because that is what the route accepts (D-R9). <see cref=\"StocksGroup.ListDividendsAsync\"/> is the newer stocks route with a wider row; this one is kept because the description declares it.",
      "result": { "kind": "array", "model": "ReferenceDividend", "property": "results" },
      "parameters": {
        "ticker":           { "name": "ticker" },
        "ex_dividend_date": { "name": "exDividendDate" },
        "record_date":      { "name": "recordDate" },
        "declaration_date": { "name": "declarationDate" },
        "pay_date":         { "name": "payDate" },
        "frequency":        { "name": "frequency" },
        "cash_amount":      { "name": "cashAmount" },
        "dividend_type":    { "name": "dividendType" },
        "order":            { "name": "order", "type": "SortOrder" },
        "limit":            { "name": "limit" },
        "sort":             { "name": "sort" }
      }
    },
    {
      "operationId": "ListStockSplits",
      "group": "Reference",
      "method": "ListSplits",
      "summary": "Retrieves stock splits from the v3 reference route, filtered by ticker, execution date, and direction.",
      "remarks": "Every filter is optional and defaults to no constraint. <paramref name=\"reverseSplit\"/> selects splits whose ratio reduces the share count. <see cref=\"StocksGroup.ListSplitsAsync\"/> is the newer stocks route with a wider row; this one is kept because the description declares it.",
      "result": { "kind": "array", "model": "ReferenceSplit", "property": "results" },
      "parameters": {
        "ticker":         { "name": "ticker" },
        "execution_date": { "name": "executionDate" },
        "reverse_split":  { "name": "reverseSplit" },
        "order":          { "name": "order", "type": "SortOrder" },
        "limit":          { "name": "limit" },
        "sort":           { "name": "sort" }
      }
    }
```

The `ex_dividend_date` and `execution_date` parameters carry `format: date` in the description, so their filters bind `RangeFilter<LocalDate>` with no `type` on the row; the `record_date`, `declaration_date`, and `pay_date` parameters do too. Only the model rows need the override.

- [ ] **Step 6: Regenerate**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: `ReferenceDividend.g.cs` and `ReferenceSplit.g.cs` with `using NodaTime;`; `ListDividendsAsync` with five `RangeFilter<LocalDate>?` parameters, `int? frequency`, and `RangeFilter<double>? cashAmount`; `ListSplitsAsync` with `RangeFilter<string>? ticker`, `RangeFilter<LocalDate>? executionDate`, and `bool? reverseSplit`. Both with `Enumerate` counterparts.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS; `EndpointCoverageTests` reports 33 mapped.

- [ ] **Step 8: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff.

- [ ] **Step 9: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/ReferenceDividendsTests.cs tests/MassiveDotNet.Rest.Tests/ReferenceSplitsTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs
git commit -m "feat: map the v3 dividends and splits under Reference

Their rows differ from the stocks/v1 models, so ReferenceDividend and
ReferenceSplit (D-R7); their bare-string dates bind LocalDate from the
map (D-R9). Coverage reaches 33.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 7: Options contracts: the list and the get, with `ContractType`

**Files:**
- Modify: `specs/endpoints.map.json` (two model rows, two endpoint rows)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceOptionsContractsTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 33 → 35)

**Interfaces:**
- Consumes: `ContractType` from Task 1; the `GetStocksSnapshotTicker` rows as the pattern for a model declared from one operation and reused at another's site (D16).
- Produces: `client.Reference.ListOptionsContractsAsync(RangeFilter<string>? underlyingTicker = null, string? ticker = null, ContractType? contractType = null, RangeFilter<LocalDate>? expirationDate = null, LocalDate? asOf = null, RangeFilter<double>? strikePrice = null, bool? expired = null, SortOrder? order = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<OptionsContract>>` with `EnumerateOptionsContractsAsync`; `client.Reference.GetOptionsContractAsync(string optionsTicker, LocalDate? asOf = null, CancellationToken cancellationToken = default)` returning `Task<OptionsContract>`. Models: `OptionsContract` (`string? Ticker`, `string? UnderlyingTicker`, `string? ContractType`, `string? ExerciseStyle`, `LocalDate? ExpirationDate`, `double? StrikePrice`, `double? SharesPerContract`, `string? PrimaryExchange`, `string? Cfi`, `int? Correction`, `AdditionalUnderlying[]? AdditionalUnderlyings`); `AdditionalUnderlying` (`string? Underlying`, `string? Type`, `double? Amount`). No partials. Tasks 10 and 11 call the list; Task 11 calls the get.

- [ ] **Step 1: Add the fixtures**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, after the `ReferenceSplits` member:

```csharp
    /// <summary>The documented sample for GET /v3/reference/options/contracts.</summary>
    public const string ReferenceOptionsContracts = """
        {
          "request_id": "603902c0-a5a5-406f-bd08-f030f92418fa",
          "results": [
            {
              "cfi": "OCASPS",
              "contract_type": "call",
              "exercise_style": "american",
              "expiration_date": "2021-11-19",
              "primary_exchange": "BATO",
              "shares_per_contract": 100,
              "strike_price": 85,
              "ticker": "O:AAPL211119C00085000",
              "underlying_ticker": "AAPL"
            },
            {
              "additional_underlyings": [
                {
                  "amount": 44,
                  "type": "equity",
                  "underlying": "VMW"
                },
                {
                  "amount": 6.53,
                  "type": "currency",
                  "underlying": "USD"
                }
              ],
              "cfi": "OCASPS",
              "contract_type": "call",
              "exercise_style": "american",
              "expiration_date": "2021-11-19",
              "primary_exchange": "BATO",
              "shares_per_contract": 100,
              "strike_price": 90,
              "ticker": "O:AAPL211119C00090000",
              "underlying_ticker": "AAPL"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /v3/reference/options/contracts/{options_ticker}.</summary>
    public const string ReferenceOptionsContract = """
        {
          "request_id": "603902c0-a5a5-406f-bd08-f030f92418fa",
          "results": {
            "additional_underlyings": [
              {
                "amount": 44,
                "type": "equity",
                "underlying": "VMW"
              },
              {
                "amount": 6.53,
                "type": "currency",
                "underlying": "USD"
              }
            ],
            "cfi": "OCASPS",
            "contract_type": "call",
            "exercise_style": "american",
            "expiration_date": "2021-11-19",
            "primary_exchange": "BATO",
            "shares_per_contract": 100,
            "strike_price": 85,
            "ticker": "O:AAPL211119C00085000",
            "underlying_ticker": "AAPL"
          },
          "status": "OK"
        }
        """;
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/ReferenceOptionsContractsTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Options contracts: the first <see cref="ContractType"/> parameter (D-R6), calendar-date and
/// strike ranges, a colon in a path segment, and one model reused between the list and the get
/// (decision D16).
/// </summary>
public sealed class ReferenceOptionsContractsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersEveryParameterInDeclarationOrder()
    {
        // AbsoluteUri rather than ToString(): the deprecated ticker parameter carries a colon,
        // which is percent-escaped on the way out, and AbsoluteUri reports it as sent.
        StubHandler handler = new(Fixtures.ReferenceOptionsContracts);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListOptionsContractsAsync(
                underlyingTicker: "AAPL",
                ticker: "O:AAPL211119C00085000",
                contractType: ContractType.Call,
                expirationDate: RangeFilter.Between(new LocalDate(2021, 11, 1), new LocalDate(2021, 11, 30)),
                asOf: new LocalDate(2021, 11, 1),
                strikePrice: RangeFilter.Lte(90d),
                expired: true,
                order: SortOrder.Ascending,
                limit: 2,
                sort: "strike_price",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v3/reference/options/contracts"
                + "?underlying_ticker=AAPL&ticker=O%3AAAPL211119C00085000&contract_type=call"
                + "&expiration_date.gte=2021-11-01&expiration_date.lte=2021-11-30"
                + "&as_of=2021-11-01&strike_price.lte=90&expired=true&order=asc&limit=2&sort=strike_price",
            handler.LastRequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task DeserializesTheListSampleThroughTheAdditionalUnderlyings()
    {
        StubHandler handler = new(Fixtures.ReferenceOptionsContracts);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<OptionsContract> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListOptionsContractsAsync(underlyingTicker: "AAPL", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);

        OptionsContract plain = page.Results[0];
        Assert.Equal("O:AAPL211119C00085000", plain.Ticker);
        Assert.Equal("AAPL", plain.UnderlyingTicker);
        Assert.Equal("call", plain.ContractType);
        Assert.Equal("american", plain.ExerciseStyle);
        Assert.Equal(new LocalDate(2021, 11, 19), plain.ExpirationDate);
        Assert.Equal(85d, plain.StrikePrice);
        Assert.Equal(100d, plain.SharesPerContract);
        Assert.Equal("BATO", plain.PrimaryExchange);
        Assert.Equal("OCASPS", plain.Cfi);
        Assert.Null(plain.Correction);
        Assert.Null(plain.AdditionalUnderlyings);

        OptionsContract adjusted = page.Results[1];
        Assert.NotNull(adjusted.AdditionalUnderlyings);
        Assert.Equal(2, adjusted.AdditionalUnderlyings.Length);
        Assert.Equal("VMW", adjusted.AdditionalUnderlyings[0].Underlying);
        Assert.Equal("equity", adjusted.AdditionalUnderlyings[0].Type);
        Assert.Equal(44d, adjusted.AdditionalUnderlyings[0].Amount);
        Assert.Equal(6.53, adjusted.AdditionalUnderlyings[1].Amount);

        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task GetEscapesTheColonInThePathSegment()
    {
        StubHandler handler = new(Fixtures.ReferenceOptionsContract);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.GetOptionsContractAsync("O:AAPL211119C00085000", asOf: new LocalDate(2021, 11, 1), cancellationToken: Ct);
        }

        // A caller-supplied path segment is percent-escaped; the colon stays escaped in the
        // absolute form because it is reserved in a path.
        Assert.Equal(
            "https://api.massive.com/v3/reference/options/contracts/O%3AAAPL211119C00085000?as_of=2021-11-01",
            handler.LastRequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task GetDeserializesTheSameModel()
    {
        StubHandler handler = new(Fixtures.ReferenceOptionsContract);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        OptionsContract contract;

        using (client)
        using (transport)
        {
            contract = await client.Reference.GetOptionsContractAsync("O:AAPL211119C00085000", cancellationToken: Ct);
        }

        Assert.Equal("O:AAPL211119C00085000", contract.Ticker);
        Assert.Equal(85d, contract.StrikePrice);
        Assert.NotNull(contract.AdditionalUnderlyings);
        Assert.Equal("USD", contract.AdditionalUnderlyings[1].Underlying);
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, raise the baseline:

```csharp
    private const int CoverageBaseline = 35;
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~ReferenceOptionsContracts"`
Expected: the build fails with CS1061 for both methods and CS0246 for `OptionsContract`.

- [ ] **Step 4: Add the model rows**

In `specs/endpoints.map.json`, append inside `models` after the `ReferenceSplit` row (add a comma after its closing brace):

```json
    "OptionsContract": {
      "summary": "An options contract's terms: its ticker and underlying, whether it is a call or a put, its style, expiration, strike, and share count, and any additional underlyings from a corporate action.",
      "remarks": "Reference data, so a class (decision D4). Declared from the list and reused as the payload of <see cref=\"ReferenceGroup.GetOptionsContractAsync\"/>, where the generator verifies the shape (decision D16). <see cref=\"ContractType\"/> is the wire string, <c>call</c> or <c>put</c>; the request-side enum <see cref=\"MassiveDotNet.ContractType\"/> is for filtering. <see cref=\"ExpirationDate\"/> is a bare string in the description and an ISO calendar date on the wire (D-R9).",
      "schema": { "operationId": "ListOptionsContracts", "pointer": "results/items" },
      "properties": {
        "ticker":                 { "name": "Ticker" },
        "underlying_ticker":      { "name": "UnderlyingTicker" },
        "contract_type":          { "name": "ContractType" },
        "exercise_style":         { "name": "ExerciseStyle" },
        "expiration_date":        { "name": "ExpirationDate", "type": "LocalDate?", "summary": "The contract's expiration date. The description types this a bare string; the wire carries an ISO calendar date (D-R9)." },
        "strike_price":           { "name": "StrikePrice" },
        "shares_per_contract":    { "name": "SharesPerContract" },
        "primary_exchange":       { "name": "PrimaryExchange" },
        "cfi":                    { "name": "Cfi" },
        "correction":             { "name": "Correction" },
        "additional_underlyings": { "name": "AdditionalUnderlyings", "model": "AdditionalUnderlying" }
      }
    },

    "AdditionalUnderlying": {
      "summary": "An extra deliverable on an adjusted options contract, such as shares of a spun-off company or a cash amount, and how much of it.",
      "schema": { "operationId": "ListOptionsContracts", "pointer": "results/items/additional_underlyings/items" },
      "properties": {
        "underlying": { "name": "Underlying" },
        "type":       { "name": "Type" },
        "amount":     { "name": "Amount" }
      }
    }
```

- [ ] **Step 5: Add the endpoint rows**

In `specs/endpoints.map.json`, append inside `endpoints` after the `ListStockSplits` row (add a comma after its closing brace):

```json
    {
      "operationId": "ListOptionsContracts",
      "group": "Reference",
      "method": "ListOptionsContracts",
      "summary": "Retrieves options contracts, filtered by underlying, type, expiration, strike, and whether they have expired, as of a chosen date.",
      "remarks": "Every filter is optional and defaults to no constraint. <paramref name=\"contractType\"/> is a <see cref=\"ContractType\"/>. <paramref name=\"ticker\"/> is a parameter the description itself calls deprecated; use <see cref=\"GetOptionsContractAsync\"/> to fetch one contract by its ticker. <paramref name=\"asOf\"/> and the expiration filter are bare strings in the description and take <see cref=\"NodaTime.LocalDate\"/> here (D-R9).",
      "result": { "kind": "array", "model": "OptionsContract", "property": "results" },
      "parameters": {
        "underlying_ticker": { "name": "underlyingTicker" },
        "ticker":            { "name": "ticker" },
        "contract_type":     { "name": "contractType", "type": "ContractType" },
        "expiration_date":   { "name": "expirationDate", "type": "LocalDate" },
        "as_of":             { "name": "asOf", "type": "LocalDate" },
        "strike_price":      { "name": "strikePrice" },
        "expired":           { "name": "expired" },
        "order":             { "name": "order", "type": "SortOrder" },
        "limit":             { "name": "limit" },
        "sort":              { "name": "sort" }
      }
    },
    {
      "operationId": "GetOptionsContract",
      "group": "Reference",
      "method": "GetOptionsContract",
      "summary": "Retrieves one options contract by its ticker, as of a chosen date.",
      "remarks": "<paramref name=\"optionsTicker\"/> is the <c>O:</c>-prefixed contract symbol; the colon is percent-escaped in the path. A contract the service does not know answers 404, which surfaces as a <see cref=\"MassiveApiException\"/>.",
      "result": { "kind": "object", "model": "OptionsContract", "property": "results" },
      "parameters": {
        "options_ticker": { "name": "optionsTicker" },
        "as_of":          { "name": "asOf", "type": "LocalDate" }
      }
    }
```

- [ ] **Step 6: Regenerate**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: `OptionsContract.g.cs` (with `using NodaTime;`) and `AdditionalUnderlying.g.cs`; `ListOptionsContractsAsync` with `ContractType? contractType` rendered `contractType?.ToWireValue()`, `RangeFilter<LocalDate>? expirationDate`, `LocalDate? asOf`, and `RangeFilter<double>? strikePrice`; `GetOptionsContractAsync(string optionsTicker, LocalDate? asOf = null, ...)`. The structural check accepts `OptionsContract` at the get's `results`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS; `EndpointCoverageTests` reports 35 mapped.

- [ ] **Step 8: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff.

- [ ] **Step 9: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/ReferenceOptionsContractsTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs
git commit -m "feat: map the options contracts list and get

The list is the first parameter bound to ContractType (D-R6); the get
reuses the list's model under D16's structural check. Coverage
reaches 35.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 8: IPOs: the served `vX` revision and the unserved `v1` one

**Files:**
- Modify: `specs/endpoints.map.json` (two model rows, two endpoint rows)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceIposTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 35 → 37)

**Interfaces:**
- Consumes: `DateOrNanoseconds` (D20) as a map element type; `Filter<T>` and `SetFilter<T>` for the `v1` route's groups; the D-R5 naming ruling.
- Produces: `client.Reference.ListIposAsync(string? ticker = null, string? usCode = null, string? isin = null, RangeFilter<LocalDate>? listingDate = null, string? ipoStatus = null, SortOrder? order = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<Ipo>>` with `EnumerateIposAsync`, both `[Experimental("MASSIVE0001")]`; `client.Reference.ListIposV1Async(Filter<string>? ticker = null, Filter<string>? usCode = null, Filter<string>? isin = null, RangeFilter<DateOrNanoseconds>? listingDate = null, SetFilter<string>? ipoStatus = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<IpoV1>>` with `EnumerateIposV1Async`. Models: `Ipo` (`required string Ticker`, `required string IssuerName`, `required string IpoStatus`, `required string SecurityType`, `LocalDate LastUpdated`, `LocalDate? AnnouncedDate`, `LocalDate? ListingDate`, `string? Isin`, `string? UsCode`, `string? CurrencyCode`, `string? PrimaryExchange`, `string? SecurityDescription`, `double? FinalIssuePrice`, `double? LowestOfferPrice`, `double? HighestOfferPrice`, `double? MinSharesOffered`, `double? MaxSharesOffered`, `double? SharesOutstanding`, `double? TotalOfferSize`, `double? LotSize`); `IpoV1` (the same names with every member optional, `long? AnnouncedDateEpoch`, `long? LastUpdatedEpoch`, `long? ListingDateEpoch`, and `long?` for `LotSize`, `MinSharesOffered`, `MaxSharesOffered`, `SharesOutstanding`). No partials. Task 11 calls both.

- [ ] **Step 1: Add the fixtures**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, after the `ReferenceOptionsContract` member:

```csharp
    /// <summary>
    /// The documented sample for GET /vX/reference/ipos. Its <c>issue_start_date</c> and
    /// <c>issue_end_date</c> are not in the schema and are ignored.
    /// </summary>
    public const string ReferenceIpos = """
        {
          "next_url": "https://api.massive.com/vX/reference/ipos?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "6a7e466379af0a71039d60cc78e72282",
          "results": [
            {
              "announced_date": "2024-06-01",
              "currency_code": "USD",
              "final_issue_price": 17,
              "highest_offer_price": 17,
              "ipo_status": "history",
              "isin": "US75383L1026",
              "issue_end_date": "2024-06-06",
              "issue_start_date": "2024-06-01",
              "issuer_name": "Rapport Therapeutics Inc.",
              "last_updated": "2024-06-27",
              "listing_date": "2024-06-07",
              "lot_size": 100,
              "lowest_offer_price": 17,
              "max_shares_offered": 8000000,
              "min_shares_offered": 1000000,
              "primary_exchange": "XNAS",
              "security_description": "Ordinary Shares",
              "security_type": "CS",
              "shares_outstanding": 35376457,
              "ticker": "RAPP",
              "total_offer_size": 136000000,
              "us_code": "75383L102"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// The documented sample for GET /v1/reference/ipos, with three departures from the
    /// published text: the schema types <c>announced_date</c>, <c>last_updated</c>, and
    /// <c>listing_date</c> as 64-bit integers, while the sample shows the same calendar dates as
    /// the <c>vX</c> sample. The model follows the schema, so the fixture carries each date as
    /// the Unix nanosecond count of its midnight UTC, the unit the operation's own
    /// <c>listing_date</c> filter documents (D-R10). The route answered a plain-text 404 on
    /// 2026-09-03, so the wire cannot settle this; the live pin flips when it can.
    /// </summary>
    public const string ReferenceIposV1 = """
        {
          "request_id": "6a7e466379af0a71039d60cc78e72282",
          "results": [
            {
              "announced_date": 1717200000000000000,
              "currency_code": "USD",
              "final_issue_price": 17,
              "highest_offer_price": 17,
              "ipo_status": "history",
              "isin": "US75383L1026",
              "issuer_name": "Rapport Therapeutics Inc.",
              "last_updated": 1719446400000000000,
              "listing_date": 1717718400000000000,
              "lot_size": 100,
              "lowest_offer_price": 17,
              "max_shares_offered": 8000000,
              "min_shares_offered": 1000000,
              "primary_exchange": "XNAS",
              "security_description": "Ordinary Shares",
              "security_type": "CS",
              "shares_outstanding": 35376457,
              "ticker": "RAPP",
              "total_offer_size": 136000000,
              "us_code": "75383L102"
            }
          ],
          "status": "OK"
        }
        """;
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/ReferenceIposTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Two revisions of one route (D-R5): the served <c>vX</c> one with plain parameters and a
/// calendar-date range, and the declared <c>v1</c> one with comparator groups and a filter that
/// takes a date or a nanosecond timestamp (D20). The <c>vX</c> methods are experimental, which
/// the test project's project file suppresses.
/// </summary>
public sealed class ReferenceIposTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task TheServedRevisionRendersEveryParameterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceIpos);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListIposAsync(
                ticker: "RAPP",
                usCode: "75383L102",
                isin: "US75383L1026",
                listingDate: RangeFilter.Between(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 31)),
                ipoStatus: "history",
                order: SortOrder.Descending,
                limit: 1,
                sort: "listing_date",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/vX/reference/ipos"
                + "?ticker=RAPP&us_code=75383L102&isin=US75383L1026"
                + "&listing_date.gte=2024-01-01&listing_date.lte=2024-12-31"
                + "&ipo_status=history&order=desc&limit=1&sort=listing_date",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task TheServedRevisionDeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceIpos);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Ipo> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListIposAsync(cancellationToken: Ct);
        }

        Ipo ipo = Assert.Single(page.Results);
        Assert.Equal("RAPP", ipo.Ticker);
        Assert.Equal("Rapport Therapeutics Inc.", ipo.IssuerName);
        Assert.Equal("history", ipo.IpoStatus);
        Assert.Equal("CS", ipo.SecurityType);
        Assert.Equal(new LocalDate(2024, 6, 27), ipo.LastUpdated);
        Assert.Equal(new LocalDate(2024, 6, 1), ipo.AnnouncedDate);
        Assert.Equal(new LocalDate(2024, 6, 7), ipo.ListingDate);
        Assert.Equal("US75383L1026", ipo.Isin);
        Assert.Equal("75383L102", ipo.UsCode);
        Assert.Equal("USD", ipo.CurrencyCode);
        Assert.Equal("XNAS", ipo.PrimaryExchange);
        Assert.Equal("Ordinary Shares", ipo.SecurityDescription);
        Assert.Equal(17d, ipo.FinalIssuePrice);
        Assert.Equal(17d, ipo.LowestOfferPrice);
        Assert.Equal(17d, ipo.HighestOfferPrice);
        Assert.Equal(1000000d, ipo.MinSharesOffered);
        Assert.Equal(8000000d, ipo.MaxSharesOffered);
        Assert.Equal(35376457d, ipo.SharesOutstanding);
        Assert.Equal(136000000d, ipo.TotalOfferSize);
        Assert.Equal(100d, ipo.LotSize);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task TheDeclaredRevisionRendersItsComparatorGroups()
    {
        StubHandler handler = new(Fixtures.ReferenceIposV1);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListIposV1Async(
                ticker: "RAPP",
                usCode: RangeFilter.Gte("75383L102"),
                isin: SetFilter.AnyOf("US75383L1026", "US0378331005"),
                listingDate: RangeFilter.Between(
                    DateOrNanoseconds.FromInstant(Instant.FromUtc(2024, 6, 1, 0, 0)),
                    DateOrNanoseconds.FromDate(new LocalDate(2024, 12, 31))),
                ipoStatus: SetFilter.AnyOf("new", "pending"),
                limit: 1,
                sort: "listing_date.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v1/reference/ipos"
                + "?ticker=RAPP&us_code.gte=75383L102&isin.any_of=US75383L1026,US0378331005"
                + "&listing_date.gte=1717200000000000000&listing_date.lte=2024-12-31"
                + "&ipo_status.any_of=new,pending&limit=1&sort=listing_date.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task TheDeclaredRevisionDeserializesTheCorrectedSampleWithEpochDates()
    {
        StubHandler handler = new(Fixtures.ReferenceIposV1);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<IpoV1> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListIposV1Async(cancellationToken: Ct);
        }

        IpoV1 ipo = Assert.Single(page.Results);
        Assert.Equal("RAPP", ipo.Ticker);
        Assert.Equal("history", ipo.IpoStatus);
        Assert.Equal(1717200000000000000L, ipo.AnnouncedDateEpoch);
        Assert.Equal(1719446400000000000L, ipo.LastUpdatedEpoch);
        Assert.Equal(1717718400000000000L, ipo.ListingDateEpoch);
        Assert.Equal(100L, ipo.LotSize);
        Assert.Equal(8000000L, ipo.MaxSharesOffered);
        Assert.Equal(35376457L, ipo.SharesOutstanding);
        Assert.Equal(17d, ipo.FinalIssuePrice);
        Assert.False(page.HasMore);
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, raise the baseline:

```csharp
    private const int CoverageBaseline = 37;
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~ReferenceIpos"`
Expected: the build fails with CS1061 for both methods and CS0246 for `Ipo` and `IpoV1`.

- [ ] **Step 4: Add the model rows**

In `specs/endpoints.map.json`, append inside `models` after the `AdditionalUnderlying` row (add a comma after its closing brace):

```json
    "Ipo": {
      "summary": "An initial public offering from the vX reference route: the issuer, the security, its status, the offer's price and size, and the dates that mark it.",
      "remarks": "Reference data, so a class (decision D4). The three dates carry <c>format: date</c> and bind <see cref=\"NodaTime.LocalDate\"/> on their own. <see cref=\"IpoV1\"/> is the same offering from the <c>v1</c> route, whose description types the dates as integers; the two are separate models because their wire types differ (D-R7). The route is experimental and this model is the served one (D-R5).",
      "schema": { "operationId": "ListIPOs", "pointer": "results/items" },
      "properties": {
        "ticker":               { "name": "Ticker" },
        "issuer_name":          { "name": "IssuerName" },
        "ipo_status":           { "name": "IpoStatus" },
        "security_type":        { "name": "SecurityType" },
        "security_description": { "name": "SecurityDescription" },
        "isin":                 { "name": "Isin" },
        "us_code":              { "name": "UsCode" },
        "currency_code":        { "name": "CurrencyCode" },
        "primary_exchange":     { "name": "PrimaryExchange" },
        "announced_date":       { "name": "AnnouncedDate" },
        "listing_date":         { "name": "ListingDate" },
        "last_updated":         { "name": "LastUpdated" },
        "final_issue_price":    { "name": "FinalIssuePrice" },
        "lowest_offer_price":   { "name": "LowestOfferPrice" },
        "highest_offer_price":  { "name": "HighestOfferPrice" },
        "min_shares_offered":   { "name": "MinSharesOffered" },
        "max_shares_offered":   { "name": "MaxSharesOffered" },
        "shares_outstanding":   { "name": "SharesOutstanding" },
        "total_offer_size":     { "name": "TotalOfferSize" },
        "lot_size":             { "name": "LotSize" }
      }
    },

    "IpoV1": {
      "summary": "An initial public offering from the v1 reference route: the same offering as <see cref=\"Ipo\"/>, with its dates as epoch integers and its share counts as integers.",
      "remarks": "Reference data, so a class (decision D4). The description types the three dates as 64-bit integers and declares no unit; the route answered a plain-text 404 on 2026-09-03, so the wire cannot settle it. The model follows the description (decision D21) and offers no computed instant until it can (D-R10); the <c>Epoch</c> suffix says the value is a count, not a date. The route's own <c>listing_date</c> filter documents nanoseconds, which is what the fixture assumes.",
      "schema": { "operationId": "get_v1_reference_ipos", "pointer": "results/items" },
      "properties": {
        "ticker":               { "name": "Ticker" },
        "issuer_name":          { "name": "IssuerName" },
        "ipo_status":           { "name": "IpoStatus" },
        "security_type":        { "name": "SecurityType" },
        "security_description": { "name": "SecurityDescription" },
        "isin":                 { "name": "Isin" },
        "us_code":              { "name": "UsCode" },
        "currency_code":        { "name": "CurrencyCode" },
        "primary_exchange":     { "name": "PrimaryExchange" },
        "announced_date":       { "name": "AnnouncedDateEpoch", "summary": "The date the offering was announced, as an epoch count whose unit the description does not declare; its listing_date filter documents nanoseconds (D-R10)." },
        "listing_date":         { "name": "ListingDateEpoch", "summary": "The first trading date, as an epoch count whose unit the description does not declare; its listing_date filter documents nanoseconds (D-R10)." },
        "last_updated":         { "name": "LastUpdatedEpoch", "summary": "When the record was last updated, as an epoch count whose unit the description does not declare; its listing_date filter documents nanoseconds (D-R10)." },
        "final_issue_price":    { "name": "FinalIssuePrice" },
        "lowest_offer_price":   { "name": "LowestOfferPrice" },
        "highest_offer_price":  { "name": "HighestOfferPrice" },
        "min_shares_offered":   { "name": "MinSharesOffered" },
        "max_shares_offered":   { "name": "MaxSharesOffered" },
        "shares_outstanding":   { "name": "SharesOutstanding" },
        "total_offer_size":     { "name": "TotalOfferSize" },
        "lot_size":             { "name": "LotSize" }
      }
    }
```

- [ ] **Step 5: Add the endpoint rows**

In `specs/endpoints.map.json`, append inside `endpoints` after the `GetOptionsContract` row (add a comma after its closing brace):

```json
    {
      "operationId": "ListIPOs",
      "group": "Reference",
      "method": "ListIpos",
      "summary": "Retrieves initial public offerings, past and upcoming, filtered by ticker, identifier, listing date, and status.",
      "remarks": "The route's <c>vX</c> segment marks it experimental (decision D18); opt in with <c>MASSIVE0001</c>. This is the served revision and takes the plain name; <see cref=\"ListIposV1Async\"/> is the <c>v1</c> route the description also declares (decision D26). <paramref name=\"ipoStatus\"/> is one of <c>direct_listing_process</c>, <c>history</c>, <c>new</c>, <c>pending</c>, <c>postponed</c>, <c>rumor</c>, or <c>withdrawn</c>.",
      "result": { "kind": "array", "model": "Ipo", "property": "results" },
      "parameters": {
        "ticker":       { "name": "ticker" },
        "us_code":      { "name": "usCode" },
        "isin":         { "name": "isin" },
        "listing_date": { "name": "listingDate" },
        "ipo_status":   { "name": "ipoStatus" },
        "order":        { "name": "order", "type": "SortOrder" },
        "limit":        { "name": "limit" },
        "sort":         { "name": "sort" }
      }
    },
    {
      "operationId": "get_v1_reference_ipos",
      "group": "Reference",
      "method": "ListIposV1",
      "summary": "Retrieves initial public offerings from the v1 reference route, with comparator filters on ticker, identifiers, listing date, and status.",
      "remarks": "The description declares this route beside <see cref=\"ListIposAsync\"/>, and the service answered a plain-text 404 for it on 2026-09-03; it stays mapped as declared (decision D21) and carries its version segment in its name because the <c>vX</c> revision is the served one (decision D26). <paramref name=\"listingDate\"/> takes a calendar date or an <see cref=\"NodaTime.Instant\"/>, rendered as Unix nanoseconds (decision D20).",
      "result": { "kind": "array", "model": "IpoV1", "property": "results" },
      "parameters": {
        "ticker":       { "name": "ticker" },
        "us_code":      { "name": "usCode" },
        "isin":         { "name": "isin" },
        "listing_date": { "name": "listingDate", "type": "DateOrNanoseconds" },
        "ipo_status":   { "name": "ipoStatus" },
        "limit":        { "name": "limit" },
        "sort":         { "name": "sort" }
      }
    }
```

- [ ] **Step 6: Regenerate**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: `Ipo.g.cs` (with `using NodaTime;`, `LocalDate LastUpdated`, `LocalDate? AnnouncedDate`) and `IpoV1.g.cs` (no NodaTime, `long? AnnouncedDateEpoch`); `ListIposAsync` and `EnumerateIposAsync` carrying `[Experimental("MASSIVE0001", ...)]`; `ListIposV1Async` with `Filter<string>? ticker`, `RangeFilter<DateOrNanoseconds>? listingDate`, and `SetFilter<string>? ipoStatus`, unmarked.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS; `EndpointCoverageTests` reports 37 mapped.

- [ ] **Step 8: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff.

- [ ] **Step 9: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/ReferenceIposTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs
git commit -m "feat: map both IPO routes, the served vX one under the plain name

ListIposAsync is the experimental, served revision; ListIposV1Async is
the declared route that answers 404 today, mapped as declared (D21,
D26). The v1 model keeps the schema's epoch integers and its fixture
converts the example's dates (D-R10). Coverage reaches 37.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 9: Short interest, short volume, and float

**Files:**
- Modify: `specs/endpoints.map.json` (three model rows, three endpoint rows)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceShortDataTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 37 → 40)

**Interfaces:**
- Consumes: `Filter<T>` for a range-and-set group whose base is numeric and whose `any_of` variant is typed `string` (the generator binds by the base, pinned in `ComparatorGroupTests`); `LocalDate` on a bare-string date (D-R9); D-G7's `request_id` correction.
- Produces: `client.Reference.ListShortInterestAsync(Filter<string>? ticker = null, Filter<double>? daysToCover = null, Filter<LocalDate>? settlementDate = null, Filter<long>? avgDailyVolume = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<ShortInterest>>` with `EnumerateShortInterestAsync`; `client.Reference.ListShortVolumeAsync(Filter<string>? ticker = null, Filter<LocalDate>? date = null, Filter<double>? shortVolumeRatio = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<ShortVolume>>` with `EnumerateShortVolumeAsync`; `client.Reference.ListFloatAsync(Filter<string>? ticker = null, RangeFilter<double>? freeFloatPercent = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<ShareFloat>>` with `EnumerateFloatAsync`, both `[Experimental("MASSIVE0001")]`. Models: `ShortInterest` (`string? Ticker`, `LocalDate SettlementDate`, `long? SharesShort`, `long AverageDailyVolume`, `double DaysToCover`); `ShortVolume` (`string? Ticker`, `LocalDate Date`, `double? TotalVolume`, `double? TotalShortVolume`, `double? ShortVolumeRatio`, `double? ExemptVolume`, `double? NonExemptVolume`, and the nine venue counts as `long?`: `AdfShortVolume`, `AdfShortVolumeExempt`, `NasdaqCarteretShortVolume`, `NasdaqCarteretShortVolumeExempt`, `NasdaqChicagoShortVolume`, `NasdaqChicagoShortVolumeExempt`, `NyseShortVolume`, `NyseShortVolumeExempt`); `ShareFloat` (`string? Ticker`, `LocalDate? EffectiveDate`, `long? FreeFloat`, `double? FreeFloatPercent`). No partials. Task 11 calls all three.

Two properties are renamed because a member may not share its enclosing type's name (CS0542): `short_interest` on `ShortInterest` becomes `SharesShort`, and `short_volume` on `ShortVolume` becomes `TotalShortVolume`, beside `TotalVolume`.

- [ ] **Step 1: Add the fixtures**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, after the `ReferenceIposV1` member:

```csharp
    /// <summary>
    /// The documented sample for GET /stocks/v1/short-interest, with one departure from the
    /// published text: <c>"request_id": 1</c> becomes a string, for the reason given on
    /// <see cref="StocksDividends"/>.
    /// </summary>
    public const string ReferenceShortInterest = """
        {
          "count": 1,
          "request_id": "1",
          "results": [
            {
              "avg_daily_volume": 2340158,
              "days_to_cover": 1.67,
              "settlement_date": "2025-03-14",
              "short_interest": 3906231,
              "ticker": "A"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// The documented sample for GET /stocks/v1/short-volume, with one departure from the
    /// published text: <c>"request_id": 1</c> becomes a string, for the reason given on
    /// <see cref="StocksDividends"/>.
    /// </summary>
    public const string ReferenceShortVolume = """
        {
          "count": 1,
          "request_id": "1",
          "results": [
            {
              "adf_short_volume": 0,
              "adf_short_volume_exempt": 0,
              "date": "2025-03-25",
              "exempt_volume": 1,
              "nasdaq_carteret_short_volume": 179943,
              "nasdaq_carteret_short_volume_exempt": 1,
              "nasdaq_chicago_short_volume": 1,
              "nasdaq_chicago_short_volume_exempt": 0,
              "non_exempt_volume": 181218,
              "nyse_short_volume": 1275,
              "nyse_short_volume_exempt": 0,
              "short_volume": 181219,
              "short_volume_ratio": 31.57,
              "ticker": "A",
              "total_volume": 574084
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// The documented sample for GET /stocks/vX/float, with one departure from the published
    /// text: <c>"request_id": 1</c> becomes a string, for the reason given on
    /// <see cref="StocksDividends"/>.
    /// </summary>
    public const string ReferenceFloat = """
        {
          "request_id": "1",
          "results": [
            {
              "effective_date": "2025-11-01",
              "free_float": 15000000000,
              "free_float_percent": 98.5,
              "ticker": "AAPL"
            }
          ],
          "status": "OK"
        }
        """;
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/ReferenceShortDataTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Short interest, short volume, and float: range-and-set filters over numeric and calendar-date
/// fields (D15), with the three <c>request_id</c> corrections of D-G7 in their fixtures. Float is
/// experimental, which the test project's project file suppresses.
/// </summary>
public sealed class ReferenceShortDataTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task ShortInterestRendersNumericAndDateFilters()
    {
        StubHandler handler = new(Fixtures.ReferenceShortInterest);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListShortInterestAsync(
                ticker: "A",
                daysToCover: RangeFilter.Gte(1.5),
                settlementDate: SetFilter.AnyOf(new LocalDate(2025, 3, 14), new LocalDate(2025, 3, 28)),
                avgDailyVolume: RangeFilter.Gt(1_000_000L),
                limit: 10,
                sort: "settlement_date.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/v1/short-interest"
                + "?ticker=A&days_to_cover.gte=1.5&settlement_date.any_of=2025-03-14,2025-03-28"
                + "&avg_daily_volume.gt=1000000&limit=10&sort=settlement_date.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task ShortInterestDeserializesTheCorrectedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceShortInterest);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<ShortInterest> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListShortInterestAsync(ticker: "A", cancellationToken: Ct);
        }

        ShortInterest row = Assert.Single(page.Results);
        Assert.Equal("A", row.Ticker);
        Assert.Equal(new LocalDate(2025, 3, 14), row.SettlementDate);
        Assert.Equal(3906231L, row.SharesShort);
        Assert.Equal(2340158L, row.AverageDailyVolume);
        Assert.Equal(1.67, row.DaysToCover);
        Assert.False(page.HasMore);
        Assert.Equal("1", page.RequestId);
    }

    [Fact]
    public async Task ShortVolumeRendersItsFilters()
    {
        StubHandler handler = new(Fixtures.ReferenceShortVolume);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListShortVolumeAsync(
                ticker: SetFilter.AnyOf("A", "AAPL"),
                date: new LocalDate(2025, 3, 25),
                shortVolumeRatio: RangeFilter.Lt(50d),
                limit: 1,
                sort: "date",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/v1/short-volume?ticker.any_of=A,AAPL&date=2025-03-25&short_volume_ratio.lt=50&limit=1&sort=date",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task ShortVolumeDeserializesTheCorrectedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceShortVolume);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<ShortVolume> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListShortVolumeAsync(ticker: "A", cancellationToken: Ct);
        }

        ShortVolume row = Assert.Single(page.Results);
        Assert.Equal("A", row.Ticker);
        Assert.Equal(new LocalDate(2025, 3, 25), row.Date);
        Assert.Equal(574084d, row.TotalVolume);
        Assert.Equal(181219d, row.TotalShortVolume);
        Assert.Equal(31.57, row.ShortVolumeRatio);
        Assert.Equal(1d, row.ExemptVolume);
        Assert.Equal(181218d, row.NonExemptVolume);
        Assert.Equal(179943L, row.NasdaqCarteretShortVolume);
        Assert.Equal(1L, row.NasdaqCarteretShortVolumeExempt);
        Assert.Equal(1L, row.NasdaqChicagoShortVolume);
        Assert.Equal(0L, row.NasdaqChicagoShortVolumeExempt);
        Assert.Equal(1275L, row.NyseShortVolume);
        Assert.Equal(0L, row.NyseShortVolumeExempt);
        Assert.Equal(0L, row.AdfShortVolume);
        Assert.Equal(0L, row.AdfShortVolumeExempt);
        Assert.Equal("1", page.RequestId);
    }

    [Fact]
    public async Task FloatRendersItsFilters()
    {
        StubHandler handler = new(Fixtures.ReferenceFloat);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListFloatAsync(
                ticker: "AAPL",
                freeFloatPercent: RangeFilter.Gte(90d),
                limit: 1,
                sort: "effective_date",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/vX/float?ticker=AAPL&free_float_percent.gte=90&limit=1&sort=effective_date",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task FloatDeserializesTheCorrectedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceFloat);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<ShareFloat> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListFloatAsync(ticker: "AAPL", cancellationToken: Ct);
        }

        ShareFloat row = Assert.Single(page.Results);
        Assert.Equal("AAPL", row.Ticker);
        Assert.Equal(new LocalDate(2025, 11, 1), row.EffectiveDate);
        Assert.Equal(15000000000L, row.FreeFloat);
        Assert.Equal(98.5, row.FreeFloatPercent);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task EnumerateWalksASinglePageOfShortInterest()
    {
        StubHandler handler = new(Fixtures.ReferenceShortInterest);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<LocalDate> dates = [];

        using (client)
        using (transport)
        {
            await foreach (ShortInterest row in client.Reference.EnumerateShortInterestAsync(ticker: "A", cancellationToken: Ct))
            {
                dates.Add(row.SettlementDate);
            }
        }

        Assert.Equal(new LocalDate(2025, 3, 14), Assert.Single(dates));
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, raise the baseline to its final value:

```csharp
    private const int CoverageBaseline = 40;
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~ReferenceShortData"`
Expected: the build fails with CS1061 for the three methods and CS0246 for `ShortInterest`, `ShortVolume`, and `ShareFloat`.

- [ ] **Step 4: Add the model rows**

In `specs/endpoints.map.json`, append inside `models` after the `IpoV1` row (add a comma after its closing brace):

```json
    "ShortInterest": {
      "summary": "A short interest report for one stock on one settlement date: the shares held short, the average daily volume, and the days to cover.",
      "remarks": "Reference data, so a class (decision D4). <see cref=\"SharesShort\"/> is the wire's <c>short_interest</c>, renamed because a member may not share its type's name. <see cref=\"SettlementDate\"/> is a bare string in the description and an ISO calendar date on the wire (D-R9).",
      "schema": { "operationId": "get_stocks_v1_short-interest", "pointer": "results/items" },
      "properties": {
        "ticker":           { "name": "Ticker" },
        "settlement_date":  { "name": "SettlementDate", "type": "LocalDate", "summary": "The settlement date the report is as of. The description types this a bare string; the wire carries an ISO calendar date (D-R9)." },
        "short_interest":   { "name": "SharesShort", "summary": "The number of shares held short as of the settlement date; the wire's <c>short_interest</c>." },
        "avg_daily_volume": { "name": "AverageDailyVolume" },
        "days_to_cover":    { "name": "DaysToCover" }
      }
    },

    "ShortVolume": {
      "summary": "A day's short sale volume for one stock: the totals, the ratio, and the count at each reporting venue.",
      "remarks": "Reference data, so a class (decision D4). <see cref=\"TotalShortVolume\"/> is the wire's <c>short_volume</c>, renamed because a member may not share its type's name and to sit beside <see cref=\"TotalVolume\"/>. <see cref=\"Date\"/> is a bare string in the description and an ISO calendar date on the wire (D-R9).",
      "schema": { "operationId": "get_stocks_v1_short-volume", "pointer": "results/items" },
      "properties": {
        "ticker":                              { "name": "Ticker" },
        "date":                                { "name": "Date", "type": "LocalDate", "summary": "The trading date the volumes are for. The description types this a bare string; the wire carries an ISO calendar date (D-R9)." },
        "total_volume":                        { "name": "TotalVolume" },
        "short_volume":                        { "name": "TotalShortVolume", "summary": "The total volume sold short across every venue; the wire's <c>short_volume</c>." },
        "short_volume_ratio":                  { "name": "ShortVolumeRatio" },
        "exempt_volume":                       { "name": "ExemptVolume" },
        "non_exempt_volume":                   { "name": "NonExemptVolume" },
        "adf_short_volume":                    { "name": "AdfShortVolume" },
        "adf_short_volume_exempt":             { "name": "AdfShortVolumeExempt" },
        "nasdaq_carteret_short_volume":        { "name": "NasdaqCarteretShortVolume" },
        "nasdaq_carteret_short_volume_exempt": { "name": "NasdaqCarteretShortVolumeExempt" },
        "nasdaq_chicago_short_volume":         { "name": "NasdaqChicagoShortVolume" },
        "nasdaq_chicago_short_volume_exempt":  { "name": "NasdaqChicagoShortVolumeExempt" },
        "nyse_short_volume":                   { "name": "NyseShortVolume" },
        "nyse_short_volume_exempt":            { "name": "NyseShortVolumeExempt" }
      }
    },

    "ShareFloat": {
      "summary": "The free float of one stock as of a date: the shares available to trade and their share of the total outstanding.",
      "remarks": "Reference data, so a class (decision D4). Named for the concept rather than <c>Float</c>, which would read as the numeric type.",
      "schema": { "operationId": "get_stocks_vX_float", "pointer": "results/items" },
      "properties": {
        "ticker":             { "name": "Ticker" },
        "effective_date":     { "name": "EffectiveDate" },
        "free_float":         { "name": "FreeFloat" },
        "free_float_percent": { "name": "FreeFloatPercent" }
      }
    }
```

- [ ] **Step 5: Add the endpoint rows**

In `specs/endpoints.map.json`, append inside `endpoints` after the `get_v1_reference_ipos` row (add a comma after its closing brace):

```json
    {
      "operationId": "get_stocks_v1_short-interest",
      "group": "Reference",
      "method": "ListShortInterest",
      "summary": "Retrieves short interest reports for US stocks, filtered by ticker, settlement date, days to cover, and average daily volume.",
      "remarks": "Every filter is optional and defaults to no constraint. Pass a plain value for equality, a <see cref=\"RangeFilter\"/> factory for a range, or <see cref=\"SetFilter\"/> for a set. <paramref name=\"settlementDate\"/> is a bare string in the description and takes <see cref=\"NodaTime.LocalDate\"/> here (D-R9).",
      "result": { "kind": "array", "model": "ShortInterest", "property": "results" },
      "parameters": {
        "ticker":           { "name": "ticker" },
        "days_to_cover":    { "name": "daysToCover" },
        "settlement_date":  { "name": "settlementDate", "type": "LocalDate" },
        "avg_daily_volume": { "name": "avgDailyVolume" },
        "limit":            { "name": "limit" },
        "sort":             { "name": "sort" }
      }
    },
    {
      "operationId": "get_stocks_v1_short-volume",
      "group": "Reference",
      "method": "ListShortVolume",
      "summary": "Retrieves daily short sale volume for US stocks, by venue, filtered by ticker, date, and short volume ratio.",
      "remarks": "Every filter is optional and defaults to no constraint. Pass a plain value for equality, a <see cref=\"RangeFilter\"/> factory for a range, or <see cref=\"SetFilter\"/> for a set. <paramref name=\"date\"/> is a bare string in the description and takes <see cref=\"NodaTime.LocalDate\"/> here (D-R9).",
      "result": { "kind": "array", "model": "ShortVolume", "property": "results" },
      "parameters": {
        "ticker":             { "name": "ticker" },
        "date":               { "name": "date", "type": "LocalDate" },
        "short_volume_ratio": { "name": "shortVolumeRatio" },
        "limit":              { "name": "limit" },
        "sort":               { "name": "sort" }
      }
    },
    {
      "operationId": "get_stocks_vX_float",
      "group": "Reference",
      "method": "ListFloat",
      "summary": "Retrieves the free float of US stocks, filtered by ticker and by the share of outstanding stock that floats.",
      "remarks": "The route's <c>vX</c> segment marks it experimental (decision D18); opt in with <c>MASSIVE0001</c>. Every filter is optional and defaults to no constraint.",
      "result": { "kind": "array", "model": "ShareFloat", "property": "results" },
      "parameters": {
        "ticker":             { "name": "ticker" },
        "free_float_percent": { "name": "freeFloatPercent" },
        "limit":              { "name": "limit" },
        "sort":               { "name": "sort" }
      }
    }
```

- [ ] **Step 6: Regenerate**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: `ShortInterest.g.cs`, `ShortVolume.g.cs`, and `ShareFloat.g.cs`, each with `using NodaTime;`; `ListShortInterestAsync` with `Filter<string>? ticker`, `Filter<double>? daysToCover`, `Filter<LocalDate>? settlementDate`, and `Filter<long>? avgDailyVolume`; `ListShortVolumeAsync` with `Filter<LocalDate>? date` and `Filter<double>? shortVolumeRatio`; `ListFloatAsync` and `EnumerateFloatAsync` carrying `[Experimental("MASSIVE0001", ...)]`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS; `EndpointCoverageTests` reports 40 mapped.

- [ ] **Step 8: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff.

- [ ] **Step 9: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/ReferenceShortDataTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs
git commit -m "feat: map short interest, short volume, and float

Two properties are renamed away from their types' names; the three
fixtures correct the numeric request_id the published examples share
with dividends. Coverage reaches 40, Plan A's final count (D-R1).

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 10: Root the new instantiations in the AOT smoke test and record D24 and D26

**Files:**
- Modify: `samples/MassiveDotNet.AotSmokeTest/Program.cs`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: `GetMarketStatusAsync` (Task 5), `ListTickersAsync` (Task 3), `ListOptionsContractsAsync` (Task 7), `ContractType` (Task 1). None is experimental, so the sample needs no `NoWarn`.
- Produces: nothing new. The publish is the proof for rules 3 and 4.

The AOT smoke test is the enforcement mechanism for rules 3 and 4, and an unreferenced generic instantiation is simply trimmed away. A body-object payload with nested models, a `MarketType` and a `ContractType` on the query, an `Instant` read from an RFC 3339 string on a model, and an optional array of nested models are each reachable only through these calls.

- [ ] **Step 1: Add the three calls to the sample**

In `samples/MassiveDotNet.AotSmokeTest/Program.cs`, insert the following after the snapshots block (after the `if (snapshots is not [{ Ticker: "BCAT", ... }]) { ... }` statement) and before `Console.WriteLine($"\nrequests: {handler.Requests}");`:

```csharp
// Market status is the first body-object payload with nested models, the tickers page the first
// whose query carries a MarketType and whose rows carry an Instant read from an RFC 3339 string,
// and the contracts page the first ContractType parameter and optional array of nested models;
// each is a generic instantiation or a context entry a clean publish says nothing about unless
// something here reaches it.
Console.WriteLine("\nmarket status:");

MarketStatus status = await client.Reference.GetMarketStatusAsync();
Console.WriteLine(
    $"  market {status.Market}  nyse {status.Exchanges?.Nyse}  fx {status.Currencies?.Fx}"
    + $"  at {(status.ServerTime is { } serverTime ? InstantPattern.ExtendedIso.Format(serverTime) : "unknown")}");

if (status.Market != "extended-hours" || status.Exchanges?.Otc != "closed" || status.ServerTime != Instant.FromUtc(2020, 11, 10, 22, 37, 37))
{
    Console.Error.WriteLine("FAIL: expected an extended-hours market with OTC closed at 2020-11-10T22:37:37Z.");
    return 1;
}

Console.WriteLine("\ntickers, for the stocks market:");

MassivePage<Ticker> tickers = await client.Reference.ListTickersAsync(market: MarketType.Stocks, active: true, limit: 1);

Console.WriteLine($"request : {handler.LastRequestUri}");

foreach (Ticker ticker in tickers.Results)
{
    Console.WriteLine($"  {ticker.Ticker,-6} {ticker.Name}  updated {(ticker.LastUpdatedUtc is { } updated ? InstantPattern.ExtendedIso.Format(updated) : "never")}");
}

const string ExpectedTickersQuery = "?market=stocks&active=true&limit=1";

if (handler.LastRequestUri?.Query != ExpectedTickersQuery)
{
    Console.Error.WriteLine($"FAIL: expected the tickers query {ExpectedTickersQuery}; got {handler.LastRequestUri?.Query}.");
    return 1;
}

if (tickers.Results is not [{ Ticker: "A", LastUpdatedUtc: not null }] || !tickers.HasMore)
{
    Console.Error.WriteLine("FAIL: expected one ticker, A, with an update time, and more pages.");
    return 1;
}

Console.WriteLine("\noptions contracts, calls only:");

MassivePage<OptionsContract> contracts = await client.Reference.ListOptionsContractsAsync(
    underlyingTicker: "AAPL",
    contractType: ContractType.Call,
    strikePrice: RangeFilter.Between(80d, 90d),
    limit: 2);

Console.WriteLine($"request : {handler.LastRequestUri}");

foreach (OptionsContract contract in contracts.Results)
{
    Console.WriteLine($"  {contract.Ticker}  strike {contract.StrikePrice,6:F2}  extras {contract.AdditionalUnderlyings?.Length ?? 0}");
}

const string ExpectedContractsQuery = "?underlying_ticker=AAPL&contract_type=call&strike_price.gte=80&strike_price.lte=90&limit=2";

if (handler.LastRequestUri?.Query != ExpectedContractsQuery)
{
    Console.Error.WriteLine($"FAIL: expected the contracts query {ExpectedContractsQuery}; got {handler.LastRequestUri?.Query}.");
    return 1;
}

if (contracts.Results is not [{ AdditionalUnderlyings: null }, { AdditionalUnderlyings: [{ Underlying: "VMW" }, _] }])
{
    Console.Error.WriteLine("FAIL: expected two contracts, the second with two additional underlyings starting with VMW.");
    return 1;
}
```

Then update the request-count check at the end of the top-level statements. Replace:

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

with:

```csharp
// Two pages of the aggregates enumeration, the single-page aggregates call, the dividends call,
// the news call, one SMA page, two SMA pages enumerated, the last trade, the open/close day, the
// holidays, the trades page, the snapshots, the market status, the tickers page, and the
// contracts page.
if (enumerated != 3 || handler.Requests != 16)
{
    Console.Error.WriteLine(
        $"FAIL: expected 3 enumerated bars over 16 requests; got {enumerated} over {handler.Requests}.");
    return 1;
}
```

- [ ] **Step 2: Add the three stub responses**

In the `StubHandler` class at the bottom of `samples/MassiveDotNet.AotSmokeTest/Program.cs`, add three constants after the last existing body constant (the snapshots one):

```csharp
    private const string MarketStatusBody = """
        {
          "afterHours": true,
          "currencies": { "crypto": "open", "fx": "open" },
          "earlyHours": false,
          "exchanges": { "nasdaq": "extended-hours", "nyse": "extended-hours", "otc": "closed" },
          "market": "extended-hours",
          "serverTime": "2020-11-10T17:37:37-05:00"
        }
        """;

    private const string Tickers = """
        {
          "count": 1,
          "next_url": "https://api.massive.com/v3/reference/tickers?cursor=next",
          "request_id": "e70013d92930de90e089dc8fa098888e",
          "results": [
            {
              "active": true,
              "cik": "0001090872",
              "composite_figi": "BBG000BWQYZ5",
              "currency_name": "usd",
              "last_updated_utc": "2021-04-25T00:00:00Z",
              "locale": "us",
              "market": "stocks",
              "name": "Agilent Technologies Inc.",
              "primary_exchange": "XNYS",
              "share_class_figi": "BBG001SCTQY4",
              "ticker": "A",
              "type": "CS"
            }
          ],
          "status": "OK"
        }
        """;

    private const string Contracts = """
        {
          "request_id": "603902c0-a5a5-406f-bd08-f030f92418fa",
          "results": [
            {
              "cfi": "OCASPS",
              "contract_type": "call",
              "exercise_style": "american",
              "expiration_date": "2021-11-19",
              "primary_exchange": "BATO",
              "shares_per_contract": 100,
              "strike_price": 85,
              "ticker": "O:AAPL211119C00085000",
              "underlying_ticker": "AAPL"
            },
            {
              "additional_underlyings": [
                { "amount": 44, "type": "equity", "underlying": "VMW" },
                { "amount": 6.53, "type": "currency", "underlying": "USD" }
              ],
              "cfi": "OCASPS",
              "contract_type": "call",
              "exercise_style": "american",
              "expiration_date": "2021-11-19",
              "primary_exchange": "BATO",
              "shares_per_contract": 100,
              "strike_price": 90,
              "ticker": "O:AAPL211119C00090000",
              "underlying_ticker": "AAPL"
            }
          ],
          "status": "OK"
        }
        """;
```

and three arms to the `switch` in its `SendAsync`, before the `_ =>` arm:

```csharp
            "/v1/marketstatus/now" => MarketStatusBody,
            "/v3/reference/tickers" => Tickers,
            "/v3/reference/options/contracts" => Contracts,
```

(`MarketStatusBody` rather than `MarketStatus`, because the model type of that name is in scope through `MassiveDotNet.Rest.Models`.)

- [ ] **Step 3: Run the sample and publish it**

Run: `dotnet run --project samples/MassiveDotNet.AotSmokeTest`
Expected: the new sections print, then `AOT smoke test passed.` with exit code 0.

Run: `dotnet publish samples/MassiveDotNet.AotSmokeTest -r osx-arm64 -c Release 2>&1 | grep -c "IL[0-9]"` (on the machine's own RID)
Expected: `0`. Then run the published binary from `samples/MassiveDotNet.AotSmokeTest/bin/Release/net10.0/osx-arm64/publish/` and confirm it also prints `AOT smoke test passed.`.

- [ ] **Step 4: Record D24 and D26 in the constitution**

In `CLAUDE.md`, append two rows to the Architecture decisions table, after the `D23` row:

```markdown
| D24 | A `oneOf` with exactly one branch reads as that branch, in `Spec.Shape` and `Spec.Collect`; a `oneOf` of scalars stays a scalar; a `oneOf` with more than one branch of which any is an object fails generation. | The description uses the one-branch form once, on the ticker events items, and the generator read it as an array of strings, which would have failed on every real response: the silent wrong binding D16 exists to prevent. A scalar union stays a scalar because the news parameters declare one and go through the same classifier. An object union has no honest model binding, so it is refused rather than guessed; none exists today. |
| D26 | When the description declares two revisions of one route, the served, documented revision takes the plain method name and the other carries its version segment: `ListIposAsync` for `/vX/reference/ipos` beside `ListIposV1Async`, `List10KSectionsAsync` for the `vX` sections route beside `List10KSectionsVx0Async`. The rename lands in the D21 removal commit when Massive retires a revision. | Naming is the map's job, so this is not a second stability source: both revisions ship and both are marked from the path (D18). Giving the plain name to the versioned route because the description promotes it was rejected: `ListIposAsync` would 404 today, and the cost at the transition, one breaking rename noted in the changelog, is the same either way. D25 is reserved for the SEC filings plan. |
```

Then, in the Conventions section, insert a new bullet between the **Filters** bullet and the **Models** bullet:

```markdown
- **Enums**: an enum parameter binds a core enum when its member set crosses groups or sits in a
  path: `MarketType` for `asset_class` and `market`, `SortOrder` for `order`, `ContractType` for
  `contract_type`, and `AggregateTimespan`, `SeriesType`, and `SnapshotDirection` where they
  already apply. Every other enum parameter, including per-endpoint `sort` fields and sets one
  operation owns such as the ticker `type`, stays `string`; the server rejects a bad value with a
  400, the same posture as an entitlement (D9). A row naming a core enum is checked one way
  against the members the spec declares (#37).
```

- [ ] **Step 5: Run the full offline verification**

```bash
dotnet build MassiveDotNet.slnx
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/
```

Expected: warning-free build, every test passing, no diff.

- [ ] **Step 6: Commit**

```bash
git add samples/MassiveDotNet.AotSmokeTest/Program.cs CLAUDE.md
git commit -m "chore: root the reference-core instantiations in the AOT sample; record D24 and D26

Market status, a tickers page with a MarketType, and a contracts page
with a ContractType reach the body-object context entry, the RFC 3339
Instant on a model, and the new enum's wire path. The constitution
gains the oneOf reading, the dual-revision naming rule, and the enum
convention.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 11: The live tier: the D-R13 pins and one call per operation

**Files:**
- Create: `tests/MassiveDotNet.IntegrationTests/ReferenceTickersLiveTests.cs`
- Create: `tests/MassiveDotNet.IntegrationTests/ReferenceMarketLiveTests.cs`
- Create: `tests/MassiveDotNet.IntegrationTests/ReferenceCorporateActionsLiveTests.cs`
- Create: `tests/MassiveDotNet.IntegrationTests/ReferenceOptionsContractsLiveTests.cs`
- Create: `tests/MassiveDotNet.IntegrationTests/ReferenceIposLiveTests.cs`
- Create: `tests/MassiveDotNet.IntegrationTests/ReferenceShortDataLiveTests.cs`

**Interfaces:**
- Consumes: every method from Tasks 3–9; `LiveApiTest` (`Client`, `Ct`), which skips when no key is present and carries the `Integration` trait; the integration project's existing `NoWarn` for `MASSIVE0001`.
- Produces: nothing new. These never run in CI (rule 13); they compile there.

These tests call the real service. `LiveCredentials` finds the key in the gitignored `.env` at the repository root on its own; do not open, print, or echo that file, and do not put the key on a command line. A `403` means the account's plan lacks an entitlement the 2026-09-03 sweep found present: report it as a finding, do not retry with diagnostics that could print a URL or a header.

Live tests are not test-first in the red-green sense: there is no implementation to write, and the service is the oracle. Write each class, run it, and fix an assertion only when the service's answer shows the assumption was wrong, saying so in a comment.

- [ ] **Step 1: Write the tickers and market classes**

Create `tests/MassiveDotNet.IntegrationTests/ReferenceTickersLiveTests.cs`:

```csharp
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// The tickers family against the real service: a page boundary at a small limit (D-R13), one
/// shape call for the details, the types, and related companies, and the pin that the events
/// wire spells its discriminator <c>type</c> where the description says <c>event_type</c>.
/// </summary>
public sealed class ReferenceTickersLiveTests : LiveApiTest
{
    [Fact]
    public async Task TickersCrossAPageBoundary()
    {
        // limit is per page, so five tickers at two per page is three requests. A repeated or
        // skipped symbol across the seam is what an incorrectly rebuilt cursor looks like.
        List<string> tickers = [];

        await foreach (Ticker ticker in Client.Reference.EnumerateTickersAsync(
            market: MarketType.Stocks,
            active: true,
            order: SortOrder.Ascending,
            sort: "ticker",
            limit: 2,
            cancellationToken: Ct))
        {
            tickers.Add(ticker.Ticker);

            if (tickers.Count == 5)
            {
                break;
            }
        }

        Assert.Equal(5, tickers.Count);
        Assert.Equal(tickers.Order(StringComparer.Ordinal), tickers);
        Assert.Equal(5, tickers.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task TickerDetailsCarryTheCompanyProfile()
    {
        TickerDetails details = await Client.Reference.GetTickerAsync("AAPL", cancellationToken: Ct);

        Assert.Equal("AAPL", details.Ticker);
        Assert.Contains("Apple", details.Name, StringComparison.Ordinal);
        Assert.Equal(new LocalDate(1980, 12, 12), details.ListDate);
        Assert.NotNull(details.Address);
        Assert.False(string.IsNullOrEmpty(details.Address.City));
        Assert.NotNull(details.Branding);
    }

    [Fact]
    public async Task TickerTypesIncludeCommonStock()
    {
        TickerType[] types = await Client.Reference.ListTickerTypesAsync(assetClass: MarketType.Stocks, cancellationToken: Ct);

        Assert.Contains(types, type => type.Code == "CS" && type.AssetClass == "stocks");
    }

    [Fact]
    public async Task TickerEventsSpellTheDiscriminatorType()
    {
        // The description declares a required event_type; the wire carried "type" on 2026-09-03,
        // so the model's EventType reads as absent (D-R10). This pins the drift so it flips the
        // day the service or the description moves; TickerChange is the working discriminator.
        TickerEvents events = await Client.Reference.GetTickerEventsAsync("META", cancellationToken: Ct);

        Assert.False(string.IsNullOrEmpty(events.Name));
        Assert.NotNull(events.Events);
        Assert.NotEmpty(events.Events);
        Assert.All(events.Events, @event => Assert.Null(@event.EventType));
        Assert.Contains(events.Events, @event => @event.TickerChange?.Ticker == "FB");
    }

    [Fact]
    public async Task RelatedCompaniesReturnTickers()
    {
        RelatedCompany[] companies = await Client.Reference.ListRelatedCompaniesAsync("AAPL", Ct);

        Assert.NotEmpty(companies);
        Assert.All(companies, company => Assert.False(string.IsNullOrEmpty(company.Ticker)));
    }
}
```

Create `tests/MassiveDotNet.IntegrationTests/ReferenceMarketLiveTests.cs`:

```csharp
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Market status, conditions, and exchanges against the real service: the body-object payload
/// deserializes with every nested object the live wire sends (D-R13), and one shape call each
/// for the other two.
/// </summary>
public sealed class ReferenceMarketLiveTests : LiveApiTest
{
    [Fact]
    public async Task MarketStatusDeserializesItsBody()
    {
        MarketStatus status = await Client.Reference.GetMarketStatusAsync(Ct);

        Assert.False(string.IsNullOrEmpty(status.Market));
        Assert.NotNull(status.ServerTime);
        Assert.NotNull(status.Exchanges);
        Assert.False(string.IsNullOrEmpty(status.Exchanges.Nyse));
        Assert.NotNull(status.Currencies);

        // The published example omits indicesGroups; the live wire sent it on 2026-09-03.
        Assert.NotNull(status.IndexGroups);
    }

    [Fact]
    public async Task ConditionsHonourTheAssetClassAndDataType()
    {
        MassivePage<Condition> page = await Client.Reference.ListConditionsAsync(
            assetClass: MarketType.Stocks,
            dataType: "trade",
            limit: 5,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (Condition condition in page.Results)
        {
            Assert.Equal("stocks", condition.AssetClass);
            Assert.Contains("trade", condition.DataTypes);
            Assert.NotNull(condition.SipMapping);
        }
    }

    [Fact]
    public async Task ExchangesIncludeTheNewYorkStockExchange()
    {
        Exchange[] exchanges = await Client.Reference.ListExchangesAsync(assetClass: MarketType.Stocks, cancellationToken: Ct);

        Assert.Contains(exchanges, exchange => exchange.Mic == "XNYS");
        Assert.All(exchanges, exchange => Assert.Equal("stocks", exchange.AssetClass));
    }
}
```

- [ ] **Step 2: Write the corporate actions, contracts, IPOs, and short data classes**

Create `tests/MassiveDotNet.IntegrationTests/ReferenceCorporateActionsLiveTests.cs`:

```csharp
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// The v3 dividends and splits against the real service, on windows whose contents are a matter
/// of record, so the bare-string dates the map binds to <see cref="LocalDate"/> (D-R9) are proven
/// on the way in and the way out.
/// </summary>
public sealed class ReferenceCorporateActionsLiveTests : LiveApiTest
{
    // Apple paid four quarterly dividends in 2021 and split 4-for-1 on 2020-08-31.
    private static readonly LocalDate DividendWindowStart = new(2021, 1, 1);
    private static readonly LocalDate DividendWindowEnd = new(2021, 12, 31);
    private static readonly LocalDate SplitWindowStart = new(2020, 1, 1);
    private static readonly LocalDate SplitWindowEnd = new(2020, 12, 31);

    [Fact]
    public async Task DividendsHonourATickerAndAnExDateRange()
    {
        MassivePage<ReferenceDividend> page = await Client.Reference.ListDividendsAsync(
            ticker: "AAPL",
            exDividendDate: RangeFilter.Between(DividendWindowStart, DividendWindowEnd),
            order: SortOrder.Ascending,
            cancellationToken: Ct);

        Assert.Equal(4, page.Results.Length);

        foreach (ReferenceDividend dividend in page.Results)
        {
            Assert.Equal("AAPL", dividend.Ticker);
            Assert.Equal("CD", dividend.DividendType);
            Assert.InRange(dividend.ExDividendDate, DividendWindowStart, DividendWindowEnd);
            Assert.True(dividend.CashAmount > 0);
        }
    }

    [Fact]
    public async Task SplitsReturnTheAppleSplit()
    {
        MassivePage<ReferenceSplit> page = await Client.Reference.ListSplitsAsync(
            ticker: "AAPL",
            executionDate: RangeFilter.Between(SplitWindowStart, SplitWindowEnd),
            cancellationToken: Ct);

        ReferenceSplit split = Assert.Single(page.Results);
        Assert.Equal(new LocalDate(2020, 8, 31), split.ExecutionDate);
        Assert.Equal(1d, split.SplitFrom);
        Assert.Equal(4d, split.SplitTo);
    }
}
```

Create `tests/MassiveDotNet.IntegrationTests/ReferenceOptionsContractsLiveTests.cs`:

```csharp
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Options contracts against the real service. The get takes its ticker from the list (D-R13):
/// a hard-coded contract expires, and an expired one answers 404.
/// </summary>
public sealed class ReferenceOptionsContractsLiveTests : LiveApiTest
{
    [Fact]
    public async Task ListThenGetRoundTrips()
    {
        MassivePage<OptionsContract> page = await Client.Reference.ListOptionsContractsAsync(
            underlyingTicker: "AAPL",
            contractType: ContractType.Call,
            expired: false,
            limit: 1,
            cancellationToken: Ct);

        OptionsContract listed = Assert.Single(page.Results);
        Assert.NotNull(listed.Ticker);
        Assert.Equal("call", listed.ContractType);
        Assert.Equal("AAPL", listed.UnderlyingTicker);

        OptionsContract fetched = await Client.Reference.GetOptionsContractAsync(listed.Ticker, cancellationToken: Ct);

        Assert.Equal(listed.Ticker, fetched.Ticker);
        Assert.Equal(listed.StrikePrice, fetched.StrikePrice);
        Assert.Equal(listed.ExpirationDate, fetched.ExpirationDate);
    }
}
```

Create `tests/MassiveDotNet.IntegrationTests/ReferenceIposLiveTests.cs`:

```csharp
using System.Net;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Both IPO routes against the real service: one shape call on the served <c>vX</c> revision,
/// and the pin that the declared <c>v1</c> revision answers 404 (D21, D-R13).
/// </summary>
public sealed class ReferenceIposLiveTests : LiveApiTest
{
    private static readonly LocalDate WindowStart = new(2024, 1, 1);
    private static readonly LocalDate WindowEnd = new(2024, 12, 31);

    [Fact]
    public async Task TheServedRevisionHonoursAStatusAndAListingWindow()
    {
        MassivePage<Ipo> page = await Client.Reference.ListIposAsync(
            ipoStatus: "history",
            listingDate: RangeFilter.Between(WindowStart, WindowEnd),
            limit: 2,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (Ipo ipo in page.Results)
        {
            Assert.Equal("history", ipo.IpoStatus);
            Assert.NotNull(ipo.ListingDate);
            Assert.InRange(ipo.ListingDate.Value, WindowStart, WindowEnd);
            Assert.False(string.IsNullOrEmpty(ipo.IssuerName));
        }
    }

    [Fact]
    public async Task TheDeclaredRevisionAnswersNotFound()
    {
        // The description declares GET /v1/reference/ipos beside the vX route, so rule 1 keeps
        // it mapped, and D21 keeps it that way until the description drops it: the service
        // answered a plain-text 404 on 2026-09-03. This pins the drift so it flips the day the
        // route is served, at which point D26 says the plain name moves here.
        MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(() =>
            Client.Reference.ListIposV1Async(limit: 1, cancellationToken: Ct));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }
}
```

Create `tests/MassiveDotNet.IntegrationTests/ReferenceShortDataLiveTests.cs`:

```csharp
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>One shape call each for short interest, short volume, and float (D-R13).</summary>
public sealed class ReferenceShortDataLiveTests : LiveApiTest
{
    [Fact]
    public async Task ShortInterestReturnsASettledReport()
    {
        MassivePage<ShortInterest> page = await Client.Reference.ListShortInterestAsync(ticker: "AAPL", limit: 1, cancellationToken: Ct);

        ShortInterest row = Assert.Single(page.Results);
        Assert.Equal("AAPL", row.Ticker);
        Assert.True(row.AverageDailyVolume > 0);
        Assert.True(row.DaysToCover > 0);
    }

    [Fact]
    public async Task ShortVolumeReturnsADay()
    {
        MassivePage<ShortVolume> page = await Client.Reference.ListShortVolumeAsync(ticker: "AAPL", limit: 1, cancellationToken: Ct);

        ShortVolume row = Assert.Single(page.Results);
        Assert.Equal("AAPL", row.Ticker);
        Assert.True(row.TotalVolume > 0);
    }

    [Fact]
    public async Task FloatReturnsTheFreeFloat()
    {
        MassivePage<ShareFloat> page = await Client.Reference.ListFloatAsync(ticker: "AAPL", limit: 1, cancellationToken: Ct);

        ShareFloat row = Assert.Single(page.Results);
        Assert.Equal("AAPL", row.Ticker);
        Assert.True(row.FreeFloat > 0);
        Assert.NotNull(row.EffectiveDate);
    }
}
```

- [ ] **Step 3: Compile the live project the way CI does**

Run: `dotnet build tests/MassiveDotNet.IntegrationTests`
Expected: warning-free. (CI compiles this project and never runs it, rule 13.)

- [ ] **Step 4: Run the live tier**

Run: `dotnet test tests/MassiveDotNet.IntegrationTests --filter "FullyQualifiedName~Reference"`
Expected: every test passes, none skipped (a skip means `LiveCredentials` found no key, which means `.env` is missing; report that rather than creating one). If an assertion fails because the service's answer differs from the assumption, adjust the assertion to what the service actually does and say why in a comment. If a test fails with `403`, report the entitlement finding and stop.

Then run the whole live suite once, so the older Stocks and Reference classes still pass on today's service:

Run: `dotnet test MassiveDotNet.slnx --filter "Category=Integration"`
Expected: every test passes.

- [ ] **Step 5: Commit**

```bash
git add tests/MassiveDotNet.IntegrationTests/ReferenceTickersLiveTests.cs tests/MassiveDotNet.IntegrationTests/ReferenceMarketLiveTests.cs tests/MassiveDotNet.IntegrationTests/ReferenceCorporateActionsLiveTests.cs tests/MassiveDotNet.IntegrationTests/ReferenceOptionsContractsLiveTests.cs tests/MassiveDotNet.IntegrationTests/ReferenceIposLiveTests.cs tests/MassiveDotNet.IntegrationTests/ReferenceShortDataLiveTests.cs
git commit -m "test: add the reference-core live tier

Tickers cross a page boundary at limit 2; the v1 IPO route's 404 and
the ticker events discriminator are pinned, dated; the contract get
takes its ticker from the list; every other operation gets one shape
call (D-R13).

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 12: Issue bookkeeping (controller only, after the branch lands, with the user's go-ahead)

**Files:** none in the repository.

This task posts to GitHub, which is a side effect outside the working tree. Do not dispatch a subagent for it, and do not run it until the user has chosen how the branch lands (merge or pull request) and has said to post. Present the comment text below and ask once.

- [ ] **Step 1: Comment on #9**

#9 stays open: Plan B closes it. Post this as a comment:

```
Plan A of the Reference group design landed: seventeen operations under `client.Reference`, `CoverageBaseline` 23 → 40.

- `ListTickers` → `ListTickersAsync` / `EnumerateTickersAsync`
- `GetTicker` → `GetTickerAsync`
- `ListTickerTypes` → `ListTickerTypesAsync`
- `GetEvents` → `GetTickerEventsAsync` (`[Experimental]`)
- `GetRelatedCompanies` → `ListRelatedCompaniesAsync`
- `GetMarketStatus` → `GetMarketStatusAsync`
- `ListConditions` → `ListConditionsAsync` / `EnumerateConditionsAsync`
- `ListExchanges` → `ListExchangesAsync`
- `ListDividends` → `ListDividendsAsync` / `EnumerateDividendsAsync`
- `ListStockSplits` → `ListSplitsAsync` / `EnumerateSplitsAsync`
- `ListOptionsContracts` → `ListOptionsContractsAsync` / `EnumerateOptionsContractsAsync`
- `GetOptionsContract` → `GetOptionsContractAsync`
- `ListIPOs` → `ListIposAsync` / `EnumerateIposAsync` (`[Experimental]`)
- `get_v1_reference_ipos` → `ListIposV1Async` / `EnumerateIposV1Async` (404 today, pinned; D21, D26)
- `get_stocks_v1_short-interest` → `ListShortInterestAsync` / `EnumerateShortInterestAsync`
- `get_stocks_v1_short-volume` → `ListShortVolumeAsync` / `EnumerateShortVolumeAsync`
- `get_stocks_vX_float` → `ListFloatAsync` / `EnumerateFloatAsync` (`[Experimental]`)

Core gains `ContractType`; the generator reads a one-branch `oneOf` as its branch (D24). Two live pins: the `v1` IPO route's 404 and the ticker events wire spelling its discriminator `type`. Plan B, the SEC filings surface and financials, closes this issue. Design: `docs/superpowers/specs/2026-09-03-reference-group-design.md`.
```

- [ ] **Step 2: Report**

Confirm the comment posted and relay its URL.
