# Reference SEC Filings and Financials Implementation Plan (Plan B)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Map the sixteen SEC filings and financials operations from issue #9 into `client.Reference`: the four SEC v1 filing operations with a download beside the filing file route, both 10-K sections revisions, both 8-K operations, 13-F, forms 3 and 4, the filing index, risk factors, both taxonomies, and financials, with a generator that treats every path parameter as required, a transport method that copies a body to a stream, fixtures from the published examples or from reviewed live captures embedded in this plan, and a live tier that pins what fixtures cannot see.

**Architecture:** Two small generator and core changes come first: `Spec` reading every path parameter as required (D-R3), and `MassiveHttpTransport.DownloadAsync` copying a body to a caller's stream (D-R4). Then eighteen model rows and sixteen endpoint rows in `specs/endpoints.map.json`, regenerated output, one hand-written method (`DownloadFilingFileAsync` on the `ReferenceGroup` partial, over the generated URI builder), and one offline test class per family driven through the public API against the stub handler. The AOT smoke test roots the dictionary payload and the download, `CLAUDE.md` records D25 and the two new rules, and the live tier runs last.

**Tech Stack:** .NET 10, C# latest, xUnit v3, System.Text.Json source generation, NodaTime 3.3.3. No new package dependencies.

**Spec:** `docs/superpowers/specs/2026-09-03-reference-group-design.md` (D-R1 through D-R13; this plan is Plan B of D-R1. Plan A landed on master at 13063f9.)

## Global Constraints

Copied from `CLAUDE.md` and the spec. Every task inherits these.

- **Rule 2** — Fifteen of the sixteen routes carry a `vX` or `vX_0` segment and ship `[Experimental("MASSIVE0001")]`, emitted by the generator from the path (D18, D23); the map never declares stability. The REST and integration test projects already carry `<NoWarn>$(NoWarn);MASSIVE0002;MASSIVE0001</NoWarn>`. The AOT sample does not today; Task 10 adds `MASSIVE0001` to its `.csproj`, because the only operation whose payload is a dictionary of nested models is the experimental financials route and the sample exists to root exactly such instantiations. CLAUDE.md's Stability convention permits a sample project to suppress the id in its own `.csproj`. Nothing is ever suppressed inside generated code.
- **Rule 3** — No reflection-based serialization in shipped code. `System.Text.Json` source generation only; every new envelope and body model is registered on `MassiveRestJsonContext` by the generator, and a `Dictionary<string, FinancialDataPoint>` property is reached through the model that declares it.
- **Rule 5** — `*.g.cs` files are never hand-edited. Change `specs/endpoints.map.json` or `tools/MassiveDotNet.CodeGen` and regenerate with `dotnet run --project tools/MassiveDotNet.CodeGen`. Commit the regenerated files with the map change that produced them.
- **Rule 6** — The generator is deterministic. Running it twice on the same inputs produces byte-identical output; CI checks `git diff --exit-code src/` after a regeneration.
- **Rule 7** — `MassiveDotNet` (core) references no external package other than NodaTime. `DownloadAsync` uses only `HttpClient` and `Stream`.
- **Rule 9** — `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are on. An **unused `using` fails the build** (IDE0005). `AnalysisLevel` is `latest-recommended`: **CA1305** (pass a format provider), **CA1307/CA1310** (pass a `StringComparison` to `Contains`, `StartsWith`, `IndexOf`, `Replace` on strings), **CA1861** (hoist constant arrays to `static readonly`), and the naming rules apply to test code too.
- **Rule 10** — Every public member carries XML documentation, or CS1591 fails the build. Every map row therefore supplies a `summary`. Four properties in this plan's schemas carry **no description at all** in the description (`FilingFootnote.description` and `.id`, `FilingEntity.company_data`, `FilingCompany.name`), so their rows supply a `summary`; every other property row needs one only where the row changes the meaning (a renamed or retyped field).
- **Rule 11** — API keys are never logged, echoed in exception messages, or written to disk. The live tests read the key from the gitignored `.env` through `LiveCredentials`; never open, print, or echo that file. The four captured fixtures in this plan (filings, a second filings page, one filing, filing files) were captured and reviewed on 2026-09-03 before the plan was written and are embedded below; **no task captures anything**.
- **Rule 12** — NodaTime only. No BCL `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, or `TimeSpan` may be *named* anywhere in `src`, `tests`, `samples`, or `tools`. `TemporalTypeTests` scans every one of those directories. The only temporal type this plan touches is `LocalDate`. The SEC acceptance timestamp property is named `AcceptanceTimestamp`, never `AcceptanceDateTime`, so no identifier in this plan contains a forbidden type name.
- **Rule 13** — CI runs offline only. Live tests derive from `LiveApiTest`, which carries `[Trait("Category", "Integration")]`, and live in `tests/MassiveDotNet.IntegrationTests`. No offline test class may carry `LiveTests` in its name.
- **Spec D-R1** — Sixteen operations ship on this branch. `CoverageBaseline` in `EndpointCoverageTests` ends at 56; each endpoint task raises it to the running count: 40 → 44 → 46 → 48 → 51 → 55 → 56.
- **Spec D-R3** — `Spec.Parameters` reads `Required` as `In == "path" || required`. No operation mapped today is affected, so regeneration after Task 1 produces no diff.
- **Spec D-R4** — `GetFilingFileAsync` is generated from the description as a body payload of `FilingFile`. The hand-written `DownloadFilingFileAsync(string filingId, string fileId, Stream destination, CancellationToken cancellationToken = default)` in `src/MassiveDotNet.Rest/ReferenceGroup.cs` calls the generated `BuildGetFilingFileUri` and the new `MassiveHttpTransport.DownloadAsync(string requestUri, Stream destination, CancellationToken cancellationToken = default)`. `DownloadAsync` validates its arguments and the disposed state as `GetAsync` does, sends with `ResponseHeadersRead`, raises the same exceptions on a non-success status, copies the content to the destination, and never inspects the content type.
- **Spec D-R5 / D26** — `/stocks/filings/10-K/vX/sections` is `List10KSectionsAsync` / `Enumerate10KSectionsAsync`; `/stocks/filings/10-K/vX_0/sections` is `List10KSectionsVx0Async` / `Enumerate10KSectionsVx0Async`. Both bind the one `TenKSection` model.
- **Spec D-R6** — `order` rows name `SortOrder`. Every other enum parameter stays `string`: the filing `type`, the 10-K `section`, the financials `timeframe`, and every per-endpoint `sort`.
- **Spec D-R8** — Models: `Filing`, `FilingEntity`, `FilingCompany`, `FilingFile`, `TenKSection`, `EightKDisclosure`, `EightKText`, `ThirteenFHolding`, `Form3Filing`, `Form4Filing`, `FilingFootnote`, `FilingIndexEntry`, `RiskFactor`, `DisclosureTaxonomyEntry`, `RiskFactorTaxonomyEntry`, `FinancialReport`, `FinancialStatements`, `FinancialDataPoint`. Map methods: `ListFilings`, `GetFiling`, `ListFilingFiles`, `GetFilingFile`, `List10KSections`, `List10KSectionsVx0`, `List8KDisclosures`, `List8KText`, `List13FHoldings`, `ListForm3Filings`, `ListForm4Filings`, `ListFilingIndex`, `ListRiskFactors`, `ListDisclosureTaxonomy`, `ListRiskFactorTaxonomy`, `ListFinancials`. All models are `partial record` classes; no `kind: struct` and no hand-written model partial anywhere in this plan.
- **Spec D-R9** — The SEC v1 dates bind `string`: `filing_date` and `period_of_report_date` are `RangeFilter<string>` on `ListFilings`, and `filing_date`, `period_of_report_date`, and `acceptance_datetime` are `string` on `Filing`, with summaries naming the compact form. Everywhere else a bare-string date whose example shows `yyyy-MM-dd` binds `LocalDate` **from the map**: the `filing_date` and `period_end` filter rows on every `vX` filings route (they carry no `format`), the `filing_date` properties of `EightKDisclosure` and `RiskFactor`, and `start_date`, `end_date`, `filing_date` on `FinancialReport`. A property carrying `format: date` binds `LocalDate` on its own and needs no `type` on its row.
- **Spec D-R11** — `FinancialStatements` has four rows typed verbatim `Dictionary<string, FinancialDataPoint>?`; `FinancialDataPoint` is generated from the pointer `results/items/financials/balance_sheet/*`.
- **Spec D-R12** — Fixtures are the published example verbatim, with two exceptions: the four SEC v1 captures, and `ReferenceRiskFactorTaxonomy`, whose published example spells `"taxonomy": "1.0"` where the schema declares a number and the live wire (2026-09-03) sends `1.0`; the fixture follows the wire, commented (Task 8 ruling). The filing file is a document and gets no fixture; its tests stub an HTML body inline.
- **Spec D-R13** — The live tier: `GetFilingFileAsync` throws on the HTML body and `DownloadFilingFileAsync` copies a body starting with `<`; `List10KSectionsVx0Async` answers 404, pinned dated; the compact-date filter returns only filings on or after the date given; filings cross a page boundary at `limit: 2`; every other operation gets one shape call.
- **Style** — Explicit types, never `var`; collection expressions (`[]`, `[.. x]`); `is not { } x` null patterns; file-scoped namespaces; raw string literals for JSON. Match the surrounding code. Comments explain *why*.
- **Convention** — Do not commit or push unless asked. Steps below include commits; the user chose the brainstorm-to-plan workflow, which authorizes them on the feature branch `feat/reference-sec`. Commit messages end with the trailer `Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB`.
- **Working tree** — Work happens in place on `feat/reference-sec` (created from master), not in a worktree, because the live tier in Task 11 needs the repository's gitignored `.env`.

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
| `tools/MassiveDotNet.CodeGen/Spec.cs` | **Modify.** `Parameters`: a path parameter is required whether or not the description flags it (D-R3). | 1 |
| `tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs` | **Modify.** A path parameter with no flag is emitted required, with no default. | 1 |
| `src/MassiveDotNet/Http/MassiveHttpTransport.cs` | **Modify.** `DownloadAsync` (D-R4). | 2 |
| `tests/MassiveDotNet.Rest.Tests/StubHandler.cs` | **Modify.** A `MediaType` init property, so a stub can serve `text/html`. | 2 |
| `tests/MassiveDotNet.Rest.Tests/DownloadTests.cs` | **Create.** The transport's copy, failure, and argument checks. | 2 |
| `specs/endpoints.map.json` | **Modify.** Eighteen model rows and sixteen endpoint rows, appended in task order. | 3–9 |
| `src/MassiveDotNet.Rest/Generated/` | **Regenerate** after every map change. | 3–9 |
| `tests/MassiveDotNet.Rest.Tests/Fixtures.cs` | **Modify.** Fifteen fixtures, appended after `ReferenceFloat` in task order. | 3, 5–9 |
| `tests/MassiveDotNet.Rest.Tests/Reference*Tests.cs` | **Create.** One class per family: rendering, deserialization, traversal. | 3–9 |
| `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` | **Modify.** `CoverageBaseline` 40 → 44 → 46 → 48 → 51 → 55 → 56. | 3, 5–9 |
| `src/MassiveDotNet.Rest/ReferenceGroup.cs` | **Modify.** `DownloadFilingFileAsync`; the group summary names SEC filings and financials. | 4 |
| `docs/superpowers/specs/2026-09-03-reference-group-design.md` | **Modify.** One sentence in D-R12, per the Task 8 ruling. | 8 |
| `tests/MassiveDotNet.CodeGen.Tests/ModelBindingTests.cs` | **Modify.** A verbatim `type` on an object with declared properties is taken as written (D-R11). | 9 |
| `samples/MassiveDotNet.AotSmokeTest/Program.cs` | **Modify.** A financials page with a data point dictionary; a download into a `MemoryStream`; two stubs. | 10 |
| `samples/MassiveDotNet.AotSmokeTest/MassiveDotNet.AotSmokeTest.csproj` | **Modify.** `NoWarn` for `MASSIVE0001`. | 10 |
| `CLAUDE.md` | **Modify.** D25; the path-parameter generator constraint; the form-name rule in Naming. | 10 |
| `tests/MassiveDotNet.IntegrationTests/Reference*LiveTests.cs` | **Create.** Five classes: the D-R13 pins and one call per operation. | 11 |

Map anchors: every task appends its model rows after the previous task's last model row and its endpoint rows after the previous task's last endpoint row, adding a comma after the row it follows. Task 3 follows `ShareFloat` and `get_stocks_vX_float`, the last rows in the file today. Fixtures follow the same rule after `ReferenceFloat`.

Generated names to expect: a `"method": "ListX"` row produces `ListXAsync` and, when the operation paginates, `EnumerateXAsync`; a `"method": "GetX"` row produces `GetXAsync`. Every operation in this plan except `GetFiling` and `GetFilingFile` paginates. The generator names each endpoint's envelope `{Method}Response` and registers it, or the body model, on `MassiveRestJsonContext`. Property nullability comes from the schema: a required reference-typed property is `required T`, an optional one `T?`; a required value type has no modifier. Each generated endpoint is three methods: the public entry point, a `private static string Build{Method}Uri(...)`, and a `private async Task<...> Send{Method}Async(...)`; the `Build` method is what Task 4's hand-written member calls.

---

### Task 1: Generator: a path parameter is required whether or not the description says so

**Files:**
- Modify: `tools/MassiveDotNet.CodeGen/Spec.cs` (`Parameters`, the `SpecParameter` construction near line 217)
- Modify: `tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs`

**Interfaces:**
- Consumes: `SpecParameter(string Name, string In, bool Required, string? Description, JsonElement Schema)`; the emitter, which renders a required string parameter as `string name,` followed by `ArgumentException.ThrowIfNullOrWhiteSpace(name);`, and an optional one as `string? name = null`.
- Produces: a parameter whose `in` is `path` is `Required` regardless of the flag. Task 3's `filing_id` and `file_id`, which the SEC v1 description does not flag, rely on it.

- [ ] **Step 1: Write the failing test**

Append to `tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs`, inside the class at the end:

```csharp
    [Fact]
    public void APathParameterIsRequiredWhetherOrNotTheDescriptionSaysSo()
    {
        // OpenAPI mandates the flag on a path parameter, and the SEC v1 description omits it on
        // filing_id and file_id. Read as optional, the parameter would default to null and append
        // an empty segment; reading the location instead of the flag makes the omission harmless
        // (D-R3).
        string spec = Harness.Document(new Operation(
            "ListThings",
            "/v1/things/{thing_id}",
            Harness.Envelope(Item),
            """[ { "name": "thing_id", "in": "path", "schema": { "type": "string" } }, { "name": "verbose", "in": "query", "schema": { "type": "boolean" } } ]"""));

        string group = Harness.Generate(spec, MapDocument("""{ "thing_id": { "name": "thingId" }, "verbose": { "name": "verbose" } }"""))["ReferenceGroup.g.cs"];

        Assert.Contains("string thingId,", group, StringComparison.Ordinal);
        Assert.Contains("ArgumentException.ThrowIfNullOrWhiteSpace(thingId);", group, StringComparison.Ordinal);
        Assert.Contains("builder.AppendPathSegment(thingId);", group, StringComparison.Ordinal);
        Assert.DoesNotContain("string? thingId", group, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests --filter "FullyQualifiedName~APathParameterIsRequired"`
Expected: FAIL. The generated group contains `string? thingId = null`, so the first assertion fails.

- [ ] **Step 3: Read the location as the fact**

In `tools/MassiveDotNet.CodeGen/Spec.cs`, inside `Parameters`, replace the `results.Add(new SpecParameter(...))` statement:

```csharp
            results.Add(new SpecParameter(
                resolved.GetProperty("name").GetString()!,
                resolved.GetProperty("in").GetString()!,
                resolved.TryGetProperty("required", out JsonElement required) && required.GetBoolean(),
                resolved.TryGetProperty("description", out JsonElement description) ? description.GetString() : null,
                resolved.TryGetProperty("schema", out JsonElement schema) ? schema : default));
```

with:

```csharp
            string location = resolved.GetProperty("in").GetString()!;

            results.Add(new SpecParameter(
                resolved.GetProperty("name").GetString()!,
                location,
                // OpenAPI mandates required: true on a path parameter, and the SEC v1 description
                // omits it on filing_id and file_id. A segment cannot be left out of a route, so
                // the location is the fact and the flag is read only where the location leaves the
                // question open (D-R3).
                location == "path" || (resolved.TryGetProperty("required", out JsonElement required) && required.GetBoolean()),
                resolved.TryGetProperty("description", out JsonElement description) ? description.GetString() : null,
                resolved.TryGetProperty("schema", out JsonElement schema) ? schema : default));
```

- [ ] **Step 4: Run the generator tests to verify they pass**

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests`
Expected: PASS, including the new test.

- [ ] **Step 5: Prove no mapped operation changes**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff. Every path parameter the description declares on a mapped operation already carries the flag.

- [ ] **Step 6: Commit**

```bash
git add tools/MassiveDotNet.CodeGen/Spec.cs tests/MassiveDotNet.CodeGen.Tests/ParameterBindingTests.cs
git commit -m "feat(codegen): read a path parameter as required whether or not the description flags it

OpenAPI mandates the flag; the SEC v1 description omits it on filing_id
and file_id, which would have defaulted them to null and appended an
empty segment (D-R3). No mapped operation changes.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 2: Transport: `DownloadAsync` copies a body to the caller's stream

**Files:**
- Modify: `src/MassiveDotNet/Http/MassiveHttpTransport.cs` (after `GetAsync`)
- Modify: `tests/MassiveDotNet.Rest.Tests/StubHandler.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/DownloadTests.cs`

**Interfaces:**
- Consumes: `GetAsync`'s argument and disposed-state checks, `HttpCompletionOption.ResponseHeadersRead`, and the private `CreateExceptionAsync(HttpResponseMessage, CancellationToken)`, which builds `MassiveApiException` or `MassiveRateLimitExceededException` from a failure response.
- Produces: `public Task DownloadAsync(string requestUri, Stream destination, CancellationToken cancellationToken = default)` on `MassiveHttpTransport`; `StubHandler.MediaType`, an init property defaulting to `application/json`. Task 4 calls `DownloadAsync`; Tasks 3 and 4 use `MediaType`.

- [ ] **Step 1: Let the stub serve a document**

In `tests/MassiveDotNet.Rest.Tests/StubHandler.cs`, add a property after `RetryAfter`:

```csharp
    /// <summary>The content type of the canned body. The filing file route serves a document, not JSON (D-R4).</summary>
    public string MediaType { get; init; } = "application/json";
```

and change the `StringContent` construction in `SendAsync` from `new StringContent(body, Encoding.UTF8, "application/json")` to:

```csharp
            Content = new StringContent(body, Encoding.UTF8, MediaType),
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/DownloadTests.cs`:

```csharp
using System.Net;
using System.Text;
using MassiveDotNet.Http;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The transport's body copy (D-R4): the bytes land in the caller's stream unchanged, a failure
/// status raises the same exception <c>GetAsync</c> does, and the arguments are checked the same
/// way. Tested on the transport directly, since its one caller adds no logic of its own.
/// </summary>
public sealed class DownloadTests
{
    private const string Html = "<html><body><p>Item 1A. Risk Factors</p></body></html>";
    private const string FileUri = "/v1/reference/sec/filings/0001683168-26-006873/files/myx_i10k-053126.htm";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MassiveHttpTransport Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = MassiveEndpoints.Production });

    [Fact]
    public async Task CopiesTheBodyToTheDestination()
    {
        StubHandler handler = new(Html) { MediaType = "text/html" };
        using MassiveHttpTransport transport = Create(handler);
        using MemoryStream destination = new();

        await transport.DownloadAsync(FileUri, destination, Ct);

        Assert.Equal(Html, Encoding.UTF8.GetString(destination.ToArray()));
        Assert.Equal("https://api.massive.com" + FileUri, handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task RaisesTheApiExceptionOnAFailureStatus()
    {
        StubHandler handler = new(HttpStatusCode.NotFound, """{ "status": "NOT_FOUND", "error": "File not found.", "request_id": "r" }""");
        using MassiveHttpTransport transport = Create(handler);
        using MemoryStream destination = new();

        MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(() =>
            transport.DownloadAsync(FileUri, destination, Ct));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("File not found.", exception.Message);
        Assert.Equal("r", exception.RequestId);
        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task RefusesANullDestination()
    {
        using MassiveHttpTransport transport = Create(new StubHandler(Html));

        await Assert.ThrowsAsync<ArgumentNullException>(() => transport.DownloadAsync(FileUri, null!, Ct));
    }

    [Fact]
    public async Task RefusesABlankRequestUri()
    {
        using MassiveHttpTransport transport = Create(new StubHandler(Html));
        using MemoryStream destination = new();

        await Assert.ThrowsAsync<ArgumentException>(() => transport.DownloadAsync(" ", destination, Ct));
    }

    [Fact]
    public async Task RefusesADisposedTransport()
    {
        MassiveHttpTransport transport = Create(new StubHandler(Html));
        transport.Dispose();
        using MemoryStream destination = new();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.DownloadAsync(FileUri, destination, Ct));
    }
}
```

- [ ] **Step 3: Run them to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~DownloadTests"`
Expected: the build fails with CS1061 (`MassiveHttpTransport` has no `DownloadAsync`).

- [ ] **Step 4: Add the method**

In `src/MassiveDotNet/Http/MassiveHttpTransport.cs`, insert after `GetAsync` (before the `EnumerateAsync` doc comment):

```csharp
    /// <summary>
    /// Issues a GET request and copies the response body to <paramref name="destination"/>
    /// unchanged, for a route that serves a document rather than JSON.
    /// </summary>
    /// <param name="requestUri">The request URI, relative to the configured base address.</param>
    /// <param name="destination">The stream the body is written to. The caller keeps ownership of it.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>A task that completes once the whole body has been written.</returns>
    /// <remarks>
    /// The content type is not inspected: the caller asked for the bytes, and the one route that
    /// needs this, the SEC filing file, names each file's type and size in its listing (decision
    /// D25). The body streams from the network into <paramref name="destination"/> with no
    /// intermediate buffer, as every other response does.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="requestUri"/> or <paramref name="destination"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="requestUri"/> is empty or whitespace.</exception>
    /// <exception cref="ObjectDisposedException">This transport has been disposed.</exception>
    /// <exception cref="MassiveRateLimitExceededException">The server responded with HTTP 429.</exception>
    /// <exception cref="MassiveApiException">The server responded with any other error status.</exception>
    public async Task DownloadAsync(string requestUri, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        using HttpResponseMessage response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken).ConfigureAwait(false);
        }

        await response.Content.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }
```

Then widen the class summary so it still describes the type. Replace:

```csharp
/// <summary>
/// Issues authenticated requests against the Massive platform API and deserializes responses
/// using source-generated metadata.
/// </summary>
```

with:

```csharp
/// <summary>
/// Issues authenticated requests against the Massive platform API and deserializes responses
/// using source-generated metadata, or copies a document body to a caller's stream.
/// </summary>
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS. The `TemporalTypeTests` source scan covers the new method automatically; it names no temporal type.

- [ ] **Step 6: Commit**

```bash
git add src/MassiveDotNet/Http/MassiveHttpTransport.cs tests/MassiveDotNet.Rest.Tests/StubHandler.cs tests/MassiveDotNet.Rest.Tests/DownloadTests.cs
git commit -m "feat(core): add DownloadAsync, copying a document body to a caller's stream

The SEC filing file route serves the file itself where the description
declares JSON. The transport copies the bytes with the same validation
and failure handling as GetAsync and inspects no content type (D-R4).

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 3: SEC v1: filings, one filing, its files, and the declared filing file

**Files:**
- Modify: `specs/endpoints.map.json` (four model rows, four endpoint rows)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceSecFilingsTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 40 → 44)

**Interfaces:**
- Consumes: Task 1 (the two unflagged path parameters are required); `StubHandler.MediaType` (Task 2); the `GetOptionsContract` row as the pattern for a singular `results` object, and the `GetMarketStatus` row for a body payload (`kind: object`, no `property`); `RangeFilter<T>` for a plain-plus-four-bounds group.
- Produces: `client.Reference.ListFilingsAsync(string? type = null, RangeFilter<string>? filingDate = null, RangeFilter<string>? periodOfReportDate = null, bool? hasXbrl = null, string? companyName = null, string? companyCik = null, string? companyTicker = null, string? companySic = null, string? companyNameSearch = null, SortOrder? order = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<Filing>>` with `EnumerateFilingsAsync`; `GetFilingAsync(string filingId, CancellationToken cancellationToken = default)` returning `Task<Filing>`; `ListFilingFilesAsync(string filingId, RangeFilter<long>? sequence = null, RangeFilter<string>? filename = null, SortOrder? order = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<FilingFile>>` with `EnumerateFilingFilesAsync`; `GetFilingFileAsync(string filingId, string fileId, CancellationToken cancellationToken = default)` returning `Task<FilingFile>`, and beside it the generated `private static string BuildGetFilingFileUri(string filingId, string fileId)` that Task 4 calls. Models: `Filing` (`required string Id`, `required string AccessionNumber`, `required string Type`, `required string FilingDate`, `required string PeriodOfReportDate`, `string? AcceptanceTimestamp`, `required FilingEntity[] Entities`, `long FilesCount`, `required string SourceUrl`); `FilingEntity` (`required string Relation`, `FilingCompany? CompanyData`); `FilingCompany` (`required string Cik`, `required string Name`, `required string Sic`, `string? Ticker`); `FilingFile` (`required string Id`, `string? Filename`, `required string Description`, `long Sequence`, `long SizeBytes`, `required string Type`, `required string SourceUrl`). No partials. Tasks 4, 10, and 11 call these.

The dotted parameters `entities.company_data.name`, `.cik`, `.ticker`, `.sic`, and `entities.company_data.name.search` pass through the generator as plain strings: `SplitComparator` takes the text after the last dot and keeps the parameter whole unless that text is one of the six comparator suffixes, and neither `name` nor `search` is. Their rows only rename them.

- [ ] **Step 1: Add the fixtures**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, after the `ReferenceFloat` member (inside the class):

```csharp
    /// <summary>
    /// GET /v1/reference/sec/filings?type=10-K&amp;limit=2&amp;sort=filing_date&amp;order=desc,
    /// captured from the live service on 2026-09-03 because the description publishes no example
    /// for it (D-R12). Reviewed: it carries no account identifier and no URL embeds a key. The
    /// wire carries fields the description does not declare (<c>main_file_url</c>,
    /// <c>xbrl_instance_url</c>, and a <c>tickers</c> array on the company), which the models
    /// ignore, and its dates are the compact digits D-R9 binds as strings.
    /// </summary>
    public const string ReferenceSecFilings = """
        {
          "count": 2,
          "next_url": "https://api.massive.com/v1/reference/sec/filings?cursor=YXA9MjAyNjA5MDImYXM9MDAwMTAxMDQ3MC0yNi0wMDAwMTAmbGltaXQ9MiZvcmRlcj1kZXNjJnNvcnQ9ZmlsaW5nX2RhdGUmdHlwZT0xMC1L",
          "request_id": "18a640368b180ab2bc59c32d803c411c",
          "results": [
            {
              "acceptance_datetime": "20260902103943",
              "accession_number": "0001683168-26-006873",
              "entities": [
                {
                  "company_data": {
                    "cik": "0002087656",
                    "name": "MYX Inc.",
                    "sic": "7374"
                  },
                  "relation": "filer"
                }
              ],
              "files_count": 65,
              "filing_date": "20260902",
              "id": "0001683168-26-006873",
              "main_file_url": "https://api.massive.com/v1/reference/sec/filings/0001683168-26-006873/files/myx_i10k-053126.htm",
              "period_of_report_date": "20260531",
              "source_url": "https://www.sec.gov/Archives/edgar/data/2087656/000168316826006873/0001683168-26-006873.txt",
              "type": "10-K",
              "xbrl_instance_url": "https://api.massive.com/v1/reference/sec/filings/0001683168-26-006873/files/myx_i10k-053126_htm.xml"
            },
            {
              "acceptance_datetime": "20260902151927",
              "accession_number": "0001010470-26-000010",
              "entities": [
                {
                  "company_data": {
                    "cik": "0001010470",
                    "name": "PROVIDENT FINANCIAL HOLDINGS INC",
                    "sic": "6035",
                    "ticker": "PROV",
                    "tickers": [
                      "PROV"
                    ]
                  },
                  "relation": "filer"
                }
              ],
              "files_count": 142,
              "filing_date": "20260902",
              "id": "0001010470-26-000010",
              "main_file_url": "https://api.massive.com/v1/reference/sec/filings/0001010470-26-000010/files/prov-20260630x10k.htm",
              "period_of_report_date": "20260630",
              "source_url": "https://www.sec.gov/Archives/edgar/data/1010470/000101047026000010/0001010470-26-000010.txt",
              "type": "10-K",
              "xbrl_instance_url": "https://api.massive.com/v1/reference/sec/filings/0001010470-26-000010/files/prov-20260630x10k_htm.xml"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// The page after <see cref="ReferenceSecFilings"/>, captured on 2026-09-03 by following its
    /// cursor and trimmed to its first filing with the cursor removed, so a traversal that starts
    /// on the first page ends here. Reviewed as the first page was.
    /// </summary>
    public const string ReferenceSecFilingsLastPage = """
        {
          "count": 1,
          "request_id": "0801eb323ee313fc400388d884bc1020",
          "results": [
            {
              "acceptance_datetime": "20260902163656",
              "accession_number": "0000858877-26-000132",
              "entities": [
                {
                  "company_data": {
                    "cik": "0000858877",
                    "name": "CISCO SYSTEMS, INC.",
                    "sic": "3576",
                    "ticker": "CSCO",
                    "tickers": [
                      "CSCO"
                    ]
                  },
                  "relation": "filer"
                }
              ],
              "files_count": 155,
              "filing_date": "20260902",
              "id": "0000858877-26-000132",
              "main_file_url": "https://api.massive.com/v1/reference/sec/filings/0000858877-26-000132/files/csco-20260725.htm",
              "period_of_report_date": "20260725",
              "source_url": "https://www.sec.gov/Archives/edgar/data/858877/000085887726000132/0000858877-26-000132.txt",
              "type": "10-K",
              "xbrl_instance_url": "https://api.massive.com/v1/reference/sec/filings/0000858877-26-000132/files/csco-20260725_htm.xml"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// GET /v1/reference/sec/filings/0001683168-26-006873, captured from the live service on
    /// 2026-09-03 because the description publishes no example for it (D-R12). Reviewed: it
    /// carries no account identifier and no URL embeds a key. The payload is the first filing of
    /// <see cref="ReferenceSecFilings"/> as one object under <c>results</c>.
    /// </summary>
    public const string ReferenceSecFiling = """
        {
          "count": 1,
          "request_id": "d47da93daf98dacd49b64aa826f9fd40",
          "results": {
            "acceptance_datetime": "20260902103943",
            "accession_number": "0001683168-26-006873",
            "entities": [
              {
                "company_data": {
                  "cik": "0002087656",
                  "name": "MYX Inc.",
                  "sic": "7374"
                },
                "relation": "filer"
              }
            ],
            "files_count": 65,
            "filing_date": "20260902",
            "id": "0001683168-26-006873",
            "main_file_url": "https://api.massive.com/v1/reference/sec/filings/0001683168-26-006873/files/myx_i10k-053126.htm",
            "period_of_report_date": "20260531",
            "source_url": "https://www.sec.gov/Archives/edgar/data/2087656/000168316826006873/0001683168-26-006873.txt",
            "type": "10-K",
            "xbrl_instance_url": "https://api.massive.com/v1/reference/sec/filings/0001683168-26-006873/files/myx_i10k-053126_htm.xml"
          },
          "status": "OK"
        }
        """;

    /// <summary>
    /// GET /v1/reference/sec/filings/0001683168-26-006873/files?limit=2&amp;sort=sequence&amp;order=asc,
    /// captured from the live service on 2026-09-03 because the description publishes no example
    /// for it (D-R12). Reviewed: it carries no account identifier and no URL embeds a key. The
    /// wire carries a <c>filing_id</c> the description does not declare, which the model ignores.
    /// </summary>
    public const string ReferenceSecFilingFiles = """
        {
          "count": 2,
          "next_url": "https://api.massive.com/v1/reference/sec/filings/0001683168-26-006873/files?cursor=YXA9MiZhcz1teXhfZXgyMzAxLmh0bSZsaW1pdD0yJm9yZGVyPWFzYyZzb3J0PXNlcXVlbmNl",
          "request_id": "62c18c45afd33e7312ed59d6aa1d417b",
          "results": [
            {
              "description": "FORM 10-K FOR MAY 2026",
              "filename": "myx_i10k-053126.htm",
              "filing_id": "0001683168-26-006873",
              "id": "myx_i10k-053126.htm",
              "sequence": 1,
              "size_bytes": 377038,
              "source_url": "https://www.sec.gov/Archives/edgar/data/2087656/000168316826006873/myx_i10k-053126.htm",
              "type": "10-K"
            },
            {
              "description": "CONSENT OF INDEPENDENT REGISTERED PUBLIC ACCOUNTING FIRM",
              "filename": "myx_ex2301.htm",
              "filing_id": "0001683168-26-006873",
              "id": "myx_ex2301.htm",
              "sequence": 2,
              "size_bytes": 4408,
              "source_url": "https://www.sec.gov/Archives/edgar/data/2087656/000168316826006873/myx_ex2301.htm",
              "type": "EX-32.1"
            }
          ],
          "status": "OK"
        }
        """;
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/ReferenceSecFilingsTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The SEC v1 surface: the dotted parameter names render verbatim, the compact dates D-R9 binds
/// as strings pass through unchanged, the captured fixtures round-trip through
/// <see cref="FilingEntity"/> and <see cref="FilingCompany"/>, filings traverse two stub pages,
/// and the filing file route, generated as the description declares it, throws on the document
/// the service actually serves (D-R4).
/// </summary>
public sealed class ReferenceSecFilingsTests
{
    private const string FilingId = "0001683168-26-006873";
    private const string FileId = "myx_i10k-053126.htm";
    private const string FileUri = "https://api.massive.com/v1/reference/sec/filings/0001683168-26-006873/files/myx_i10k-053126.htm";

    /// <summary>The opening of what the filing file route served on 2026-09-03: the document, not the declared object.</summary>
    private const string Html = "<?xml version='1.0' encoding='ASCII'?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\"><body>Item 1A. Risk Factors</body></html>";

    /// <summary>The object the description declares at the filing file route, written from the first row of the files capture.</summary>
    private const string DeclaredFile = """
        {
          "description": "FORM 10-K FOR MAY 2026",
          "filename": "myx_i10k-053126.htm",
          "id": "myx_i10k-053126.htm",
          "sequence": 1,
          "size_bytes": 377038,
          "source_url": "https://www.sec.gov/Archives/edgar/data/2087656/000168316826006873/myx_i10k-053126.htm",
          "type": "10-K"
        }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task FilingsRenderEveryFilterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceSecFilings);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListFilingsAsync(
                type: "10-K",
                filingDate: RangeFilter.Between("20260101", "20261231"),
                periodOfReportDate: RangeFilter.Gte("20250630"),
                hasXbrl: true,
                companyName: "Apple",
                companyCik: "0000320193",
                companyTicker: "AAPL",
                companySic: "3571",
                companyNameSearch: "Appl",
                order: SortOrder.Descending,
                limit: 2,
                sort: "filing_date",
                cancellationToken: Ct);
        }

        // The compact dates render as given: the route reads yyyyMMdd, and a LocalDate would have
        // rendered yyyy-MM-dd, which the server accepts and misreads (D-R9). The company filters
        // keep the description's dotted wire names.
        Assert.Equal(
            "https://api.massive.com/v1/reference/sec/filings"
                + "?type=10-K"
                + "&filing_date.gte=20260101&filing_date.lte=20261231"
                + "&period_of_report_date.gte=20250630"
                + "&has_xbrl=true"
                + "&entities.company_data.name=Apple"
                + "&entities.company_data.cik=0000320193"
                + "&entities.company_data.ticker=AAPL"
                + "&entities.company_data.sic=3571"
                + "&entities.company_data.name.search=Appl"
                + "&order=desc&limit=2&sort=filing_date",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task FilingsDeserializeTheCapturedPageThroughTheEntities()
    {
        StubHandler handler = new(Fixtures.ReferenceSecFilings);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Filing> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListFilingsAsync(type: "10-K", limit: 2, cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);
        Assert.Equal("18a640368b180ab2bc59c32d803c411c", page.RequestId);

        Filing filing = page.Results[0];
        Assert.Equal(FilingId, filing.Id);
        Assert.Equal(FilingId, filing.AccessionNumber);
        Assert.Equal("10-K", filing.Type);
        Assert.Equal("20260902", filing.FilingDate);
        Assert.Equal("20260531", filing.PeriodOfReportDate);
        Assert.Equal("20260902103943", filing.AcceptanceTimestamp);
        Assert.Equal(65, filing.FilesCount);
        Assert.Equal("https://www.sec.gov/Archives/edgar/data/2087656/000168316826006873/0001683168-26-006873.txt", filing.SourceUrl);

        FilingEntity entity = Assert.Single(filing.Entities);
        Assert.Equal("filer", entity.Relation);
        Assert.NotNull(entity.CompanyData);
        Assert.Equal("0002087656", entity.CompanyData.Cik);
        Assert.Equal("MYX Inc.", entity.CompanyData.Name);
        Assert.Equal("7374", entity.CompanyData.Sic);
        Assert.Null(entity.CompanyData.Ticker);

        Assert.Equal("PROV", Assert.Single(page.Results[1].Entities).CompanyData?.Ticker);
    }

    [Fact]
    public async Task EnumerateFilingsTraversesTwoPagesFollowingTheCursorVerbatim()
    {
        PagingStubHandler handler = new(Fixtures.ReferenceSecFilings, Fixtures.ReferenceSecFilingsLastPage);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string> ids = [];

        using (client)
        using (transport)
        {
            await foreach (Filing filing in client.Reference.EnumerateFilingsAsync(type: "10-K", limit: 2, cancellationToken: Ct))
            {
                ids.Add(filing.Id);
            }
        }

        Assert.Equal(["0001683168-26-006873", "0001010470-26-000010", "0000858877-26-000132"], ids);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("https://api.massive.com/v1/reference/sec/filings?type=10-K&limit=2", handler.Requests[0].ToString());
        Assert.Equal(
            "https://api.massive.com/v1/reference/sec/filings?cursor=YXA9MjAyNjA5MDImYXM9MDAwMTAxMDQ3MC0yNi0wMDAwMTAmbGltaXQ9MiZvcmRlcj1kZXNjJnNvcnQ9ZmlsaW5nX2RhdGUmdHlwZT0xMC1L",
            handler.Requests[1].ToString());
    }

    [Fact]
    public async Task GetFilingBuildsThePathAndDeserializesTheObject()
    {
        StubHandler handler = new(Fixtures.ReferenceSecFiling);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        Filing filing;

        using (client)
        using (transport)
        {
            filing = await client.Reference.GetFilingAsync(FilingId, Ct);
        }

        Assert.Equal("https://api.massive.com/v1/reference/sec/filings/0001683168-26-006873", handler.LastRequestUri?.ToString());
        Assert.Equal(FilingId, filing.Id);
        Assert.Equal("20260902", filing.FilingDate);
        Assert.Equal("MYX Inc.", Assert.Single(filing.Entities).CompanyData?.Name);
    }

    [Fact]
    public async Task FilingFilesRenderTheSequenceAndFilenameRanges()
    {
        StubHandler handler = new(Fixtures.ReferenceSecFilingFiles);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListFilingFilesAsync(
                FilingId,
                sequence: RangeFilter.Between(1L, 10L),
                filename: RangeFilter.Gte("a"),
                order: SortOrder.Ascending,
                limit: 2,
                sort: "sequence",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v1/reference/sec/filings/0001683168-26-006873/files"
                + "?sequence.gte=1&sequence.lte=10&filename.gte=a&order=asc&limit=2&sort=sequence",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task FilingFilesDeserializeTheCapturedPage()
    {
        StubHandler handler = new(Fixtures.ReferenceSecFilingFiles);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<FilingFile> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListFilingFilesAsync(FilingId, limit: 2, cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);

        FilingFile file = page.Results[0];
        Assert.Equal(FileId, file.Id);
        Assert.Equal(FileId, file.Filename);
        Assert.Equal(1, file.Sequence);
        Assert.Equal(377038, file.SizeBytes);
        Assert.Equal("10-K", file.Type);
        Assert.Equal("FORM 10-K FOR MAY 2026", file.Description);
        Assert.Equal("https://www.sec.gov/Archives/edgar/data/2087656/000168316826006873/myx_i10k-053126.htm", file.SourceUrl);

        Assert.Equal("EX-32.1", page.Results[1].Type);
    }

    [Fact]
    public async Task GetFilingFileDeserializesTheDeclaredObject()
    {
        // The description declares a JSON metadata object as the body (D21); this is the path
        // the generated method takes when the service ever sends it.
        StubHandler handler = new(DeclaredFile);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        FilingFile file;

        using (client)
        using (transport)
        {
            file = await client.Reference.GetFilingFileAsync(FilingId, FileId, Ct);
        }

        Assert.Equal(FileUri, handler.LastRequestUri?.ToString());
        Assert.Equal(FileId, file.Id);
        Assert.Equal(377038, file.SizeBytes);
    }

    [Fact]
    public async Task GetFilingFileThrowsOnTheDocumentTheServiceServes()
    {
        // The service serves the file itself as text/html at this route (2026-09-03). The method
        // ships as declared (D21, D-R4), so the document fails deserialization and surfaces as
        // the API exception every unreadable body does, with the parser's failure inside it.
        StubHandler handler = new(Html) { MediaType = "text/html" };
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassiveApiException exception;

        using (client)
        using (transport)
        {
            exception = await Assert.ThrowsAsync<MassiveApiException>(() => client.Reference.GetFilingFileAsync(FilingId, FileId, Ct));
        }

        Assert.Equal(FileUri, handler.LastRequestUri?.ToString());
        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, raise the baseline:

```csharp
    private const int CoverageBaseline = 44;
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~ReferenceSecFilings"`
Expected: the build fails with CS1061 for the four methods and CS0246 for `Filing`, `FilingEntity`, `FilingCompany`, and `FilingFile`.

- [ ] **Step 4: Add the model rows**

In `specs/endpoints.map.json`, append inside `models` after the `ShareFloat` row (add a comma after its closing brace):

```json
    "Filing": {
      "summary": "One SEC filing from the v1 route: its accession number and form type, the dates that govern it, the entities that filed it, and where its files live.",
      "remarks": "Reference data, so a class (decision D4). Also the payload of <see cref=\"ReferenceGroup.GetFilingAsync\"/>, where decision D16 verifies the shape. The three dates are bare strings in the description and compact digits on the wire, <c>yyyyMMdd</c> and <c>yyyyMMddHHmmss</c>, so they stay strings rather than binding a type that renders another form (D-R9). The wire also carries <c>main_file_url</c> and <c>xbrl_instance_url</c>, which the description does not declare and this model therefore omits.",
      "schema": { "operationId": "ListFilings", "pointer": "results/items" },
      "properties": {
        "id":                    { "name": "Id" },
        "accession_number":      { "name": "AccessionNumber" },
        "type":                  { "name": "Type" },
        "filing_date":           { "name": "FilingDate", "summary": "The date the filing was filed, in the compact form the wire carries, <c>yyyyMMdd</c> (D-R9)." },
        "period_of_report_date": { "name": "PeriodOfReportDate", "summary": "The period the filing reports on, in the compact form the wire carries, <c>yyyyMMdd</c> (D-R9)." },
        "acceptance_datetime":   { "name": "AcceptanceTimestamp", "summary": "When EDGAR accepted the filing, in Eastern Time, in the compact form the wire carries, <c>yyyyMMddHHmmss</c> (D-R9)." },
        "entities":              { "name": "Entities", "model": "FilingEntity" },
        "files_count":           { "name": "FilesCount" },
        "source_url":            { "name": "SourceUrl" }
      }
    },

    "FilingEntity": {
      "summary": "A party to an SEC filing: the company and its relation to the filing.",
      "schema": { "operationId": "ListFilings", "pointer": "results/items/entities/items" },
      "properties": {
        "relation":     { "name": "Relation" },
        "company_data": { "name": "CompanyData", "model": "FilingCompany", "summary": "The company this entity is, when the description has one to give." }
      }
    },

    "FilingCompany": {
      "summary": "The company behind a filing entity: its CIK, name, SIC code, and ticker.",
      "remarks": "The wire also carries a <c>tickers</c> array the description does not declare, which this model omits.",
      "schema": { "operationId": "ListFilings", "pointer": "results/items/entities/items/company_data" },
      "properties": {
        "cik":    { "name": "Cik" },
        "name":   { "name": "Name", "summary": "The company's name as registered with the SEC." },
        "sic":    { "name": "Sic" },
        "ticker": { "name": "Ticker" }
      }
    },

    "FilingFile": {
      "summary": "One file within an SEC filing: its identifier, name, sequence, size, type, and source.",
      "remarks": "Reference data, so a class (decision D4). Also the declared payload of <see cref=\"ReferenceGroup.GetFilingFileAsync\"/>, where decision D16 verifies the shape.",
      "schema": { "operationId": "ListFilingFiles", "pointer": "results/items" },
      "properties": {
        "id":          { "name": "Id" },
        "filename":    { "name": "Filename" },
        "description": { "name": "Description" },
        "sequence":    { "name": "Sequence" },
        "size_bytes":  { "name": "SizeBytes" },
        "type":        { "name": "Type" },
        "source_url":  { "name": "SourceUrl" }
      }
    }
```

- [ ] **Step 5: Add the endpoint rows**

In `specs/endpoints.map.json`, append inside `endpoints` after the `get_stocks_vX_float` row (add a comma after its closing brace):

```json
    {
      "operationId": "ListFilings",
      "group": "Reference",
      "method": "ListFilings",
      "summary": "Retrieves SEC filings, filtered by form type, by filing and report dates, by the presence of XBRL, and by the filing company's name, CIK, ticker, and SIC code.",
      "remarks": "Every filter is optional and defaults to no constraint. The date filters take the compact form the route reads, <c>yyyyMMdd</c>, as strings: the ISO form a <see cref=\"NodaTime.LocalDate\"/> renders is accepted by the server and silently misread, so it is not bindable here (D-R9). The company filters are the description's dotted <c>entities.company_data</c> parameters, rendered under those names; <paramref name=\"companyNameSearch\"/> is a text search where <paramref name=\"companyName\"/> is an exact match.",
      "result": { "kind": "array", "model": "Filing", "property": "results" },
      "parameters": {
        "type":                              { "name": "type" },
        "filing_date":                       { "name": "filingDate" },
        "period_of_report_date":             { "name": "periodOfReportDate" },
        "has_xbrl":                          { "name": "hasXbrl" },
        "entities.company_data.name":        { "name": "companyName" },
        "entities.company_data.cik":         { "name": "companyCik" },
        "entities.company_data.ticker":      { "name": "companyTicker" },
        "entities.company_data.sic":         { "name": "companySic" },
        "entities.company_data.name.search": { "name": "companyNameSearch" },
        "order":                             { "name": "order", "type": "SortOrder" },
        "limit":                             { "name": "limit" },
        "sort":                              { "name": "sort" }
      }
    },
    {
      "operationId": "GetFiling",
      "group": "Reference",
      "method": "GetFiling",
      "summary": "Retrieves one SEC filing by its identifier.",
      "remarks": "<paramref name=\"filingId\"/> is the accession number, as <see cref=\"Filing.Id\"/> reports it. The description does not flag the path parameter required; the generator reads every path parameter as required, since a segment cannot be left out of a route (D-R3). A filing the service does not know answers 404, which surfaces as a <see cref=\"MassiveApiException\"/>.",
      "result": { "kind": "object", "model": "Filing", "property": "results" },
      "parameters": {
        "filing_id": { "name": "filingId" }
      }
    },
    {
      "operationId": "ListFilingFiles",
      "group": "Reference",
      "method": "ListFilingFiles",
      "summary": "Retrieves the files that make up one SEC filing, filtered by sequence number and file name.",
      "remarks": "Every filter is optional and defaults to no constraint. Each row names the file's type and size, which is what a caller consults before retrieving one.",
      "result": { "kind": "array", "model": "FilingFile", "property": "results" },
      "parameters": {
        "filing_id": { "name": "filingId" },
        "sequence":  { "name": "sequence" },
        "filename":  { "name": "filename" },
        "order":     { "name": "order", "type": "SortOrder" },
        "limit":     { "name": "limit" },
        "sort":      { "name": "sort" }
      }
    },
    {
      "operationId": "GetFilingFile",
      "group": "Reference",
      "method": "GetFilingFile",
      "summary": "Retrieves the metadata object the description declares for one file within an SEC filing.",
      "remarks": "Generated as the description declares it (decision D21). The service serves the file's own content at this route, <c>text/html</c> for a filing document, rather than the declared JSON object, so on 2026-09-03 this method threw a <see cref=\"MassiveApiException\"/> whose inner exception is the deserialization failure; that flips the day the service or the description moves.",
      "result": { "kind": "object", "model": "FilingFile" },
      "parameters": {
        "filing_id": { "name": "filingId" },
        "file_id":   { "name": "fileId" }
      }
    }
```

- [ ] **Step 6: Regenerate**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: `Filing.g.cs`, `FilingEntity.g.cs`, `FilingCompany.g.cs`, `FilingFile.g.cs` under `Generated/Models`, none with `using NodaTime;`; in `ReferenceGroup.g.cs`, `ListFilingsAsync` with `RangeFilter<string>? filingDate`, `bool? hasXbrl`, five `string?` company parameters rendered with `builder.AppendQuery("entities.company_data.name", companyName);` and so on, and `SortOrder? order`; `GetFilingAsync(string filingId, ...)` with `ArgumentException.ThrowIfNullOrWhiteSpace(filingId);` and no default; `ListFilingFilesAsync` with `RangeFilter<long>? sequence` and `RangeFilter<string>? filename`; `GetFilingFileAsync(string filingId, string fileId, ...)` whose `SendGetFilingFileAsync` deserializes `MassiveRestJsonContext.Default.FilingFile` directly and throws on a null body, and whose `BuildGetFilingFileUri(string filingId, string fileId)` appends `/v1/reference/sec/filings/`, the filing id, `/files/`, and the file id. `Enumerate` counterparts for `ListFilings` and `ListFilingFiles`. No `[Experimental]` on any of the four: these are `v1` routes.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS; `EndpointCoverageTests` reports 44 mapped.

- [ ] **Step 8: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff.

- [ ] **Step 9: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/ReferenceSecFilingsTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs
git commit -m "feat: map the SEC v1 filings, filing, files, and filing file under Reference

The compact dates bind string on filters and models (D-R9), the dotted
company parameters pass through under their wire names, and the filing
file route ships as declared, throwing on the document the service
serves (D21, D-R4). Fixtures are reviewed live captures from
2026-09-03 (D-R12). Coverage reaches 44.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 4: `DownloadFilingFileAsync`, the method a consumer can use

**Files:**
- Modify: `src/MassiveDotNet.Rest/ReferenceGroup.cs`
- Modify: `specs/endpoints.map.json` (the `FilingFile` remarks and the `GetFilingFile` remarks name the download)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Modify: `tests/MassiveDotNet.Rest.Tests/ReferenceSecFilingsTests.cs` (three more tests)

**Interfaces:**
- Consumes: `BuildGetFilingFileUri(string filingId, string fileId)` (Task 3), a `private static` member of the generated half of the same `partial struct`, so the hand-written half can call it; `MassiveHttpTransport.DownloadAsync` (Task 2); the `_transport` field the hand-written half already declares.
- Produces: `client.Reference.DownloadFilingFileAsync(string filingId, string fileId, Stream destination, CancellationToken cancellationToken = default)` returning `Task`. Tasks 10 and 11 call it.

- [ ] **Step 1: Write the failing tests**

Append to `tests/MassiveDotNet.Rest.Tests/ReferenceSecFilingsTests.cs`, inside the class at the end, and add `using System.Text;` to its usings (after `using System.Net;`):

```csharp
    [Fact]
    public async Task DownloadFilingFileCopiesTheDocumentToTheDestination()
    {
        StubHandler handler = new(Html) { MediaType = "text/html" };
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using MemoryStream destination = new();

        using (client)
        using (transport)
        {
            await client.Reference.DownloadFilingFileAsync(FilingId, FileId, destination, Ct);
        }

        // The same generated URI builder serves both methods, so the download cannot drift from
        // the route the description declares (D-R4).
        Assert.Equal(FileUri, handler.LastRequestUri?.ToString());
        Assert.Equal(Html, Encoding.UTF8.GetString(destination.ToArray()));
    }

    [Theory]
    [InlineData("", FileId)]
    [InlineData(FilingId, " ")]
    public async Task DownloadFilingFileRefusesABlankIdentifier(string filingId, string fileId)
    {
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(new StubHandler(Html));

        using MemoryStream destination = new();

        using (client)
        using (transport)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => client.Reference.DownloadFilingFileAsync(filingId, fileId, destination, Ct));
        }
    }

    [Fact]
    public async Task DownloadFilingFileRefusesANullDestination()
    {
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(new StubHandler(Html));

        using (client)
        using (transport)
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => client.Reference.DownloadFilingFileAsync(FilingId, FileId, null!, Ct));
        }
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~DownloadFilingFile"`
Expected: the build fails with CS1061 (`ReferenceGroup` has no `DownloadFilingFileAsync`).

- [ ] **Step 3: Write the method**

Replace the whole of `src/MassiveDotNet.Rest/ReferenceGroup.cs` with:

```csharp
using MassiveDotNet.Http;

namespace MassiveDotNet.Rest;

/// <summary>
/// Reference data across asset classes: tickers, news, corporate actions, exchanges,
/// conditions, SEC filings, and financials. Reached through <see cref="MassiveRestClient.Reference"/>.
/// </summary>
/// <remarks>
/// This is a <see langword="struct"/> wrapping the shared transport, so navigating to a group
/// costs no allocation. Endpoint methods live in the generated half of this partial type. The one
/// hand-written member is <see cref="DownloadFilingFileAsync"/>, which the description cannot
/// generate because it declares JSON where the service serves a document (decision D25).
/// </remarks>
public readonly partial struct ReferenceGroup
{
    private readonly MassiveHttpTransport _transport;

    internal ReferenceGroup(MassiveHttpTransport transport) => _transport = transport;

    /// <summary>
    /// Downloads one file within an SEC filing, copying its content to
    /// <paramref name="destination"/> unchanged.
    /// </summary>
    /// <param name="filingId">The filing's identifier, as <see cref="Models.Filing.Id"/> reports it.</param>
    /// <param name="fileId">The file's identifier, as <see cref="Models.FilingFile.Id"/> reports it.</param>
    /// <param name="destination">The stream the file is written to. The caller keeps ownership of it.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>A task that completes once the whole file has been written.</returns>
    /// <remarks>
    /// The description declares a JSON metadata object at this route, which
    /// <see cref="GetFilingFileAsync"/> retrieves as declared; the service serves the file itself,
    /// which is what this method is for (decision D25). The bytes are copied as sent, whatever
    /// their type: <see cref="ListFilingFilesAsync"/> names each file's type and size, so the
    /// caller knows what it asked for. The URI comes from the same generated builder the declared
    /// method uses, so the two cannot drift apart.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="filingId"/> or <paramref name="fileId"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="MassiveApiException">The server responded with an error status.</exception>
    public Task DownloadFilingFileAsync(
        string filingId,
        string fileId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentNullException.ThrowIfNull(destination);

        string requestUri = BuildGetFilingFileUri(filingId, fileId);
        return _transport.DownloadAsync(requestUri, destination, cancellationToken);
    }
}
```

- [ ] **Step 4: Point the generated documentation at it**

In `specs/endpoints.map.json`, replace the `FilingFile` model's `remarks` with:

```json
      "remarks": "Reference data, so a class (decision D4). Also the declared payload of <see cref=\"ReferenceGroup.GetFilingFileAsync\"/>, where decision D16 verifies the shape; that route serves the file itself rather than this object, so <see cref=\"ReferenceGroup.DownloadFilingFileAsync\"/> is the method that retrieves one (decision D25).",
```

and replace the `GetFilingFile` endpoint's `remarks` with:

```json
      "remarks": "Generated as the description declares it (decision D21). The service serves the file's own content at this route, <c>text/html</c> for a filing document, rather than the declared JSON object, so on 2026-09-03 this method threw a <see cref=\"MassiveApiException\"/> whose inner exception is the deserialization failure; that flips the day the service or the description moves. Use <see cref=\"DownloadFilingFileAsync\"/> to retrieve the file (decision D25).",
```

Then regenerate: `dotnet run --project tools/MassiveDotNet.CodeGen`. Only the two doc comments change.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS. The build stays warning-free: the crefs resolve because the hand-written method now exists.

- [ ] **Step 6: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff.

- [ ] **Step 7: Commit**

```bash
git add src/MassiveDotNet.Rest/ReferenceGroup.cs specs/endpoints.map.json src/MassiveDotNet.Rest/Generated tests/MassiveDotNet.Rest.Tests/ReferenceSecFilingsTests.cs
git commit -m "feat: add DownloadFilingFileAsync beside the declared filing file route

The hand-written method copies the document the service serves to the
caller's stream through the generated URI builder, so it cannot drift
from the route the description declares (D-R4).

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 5: 10-K sections, both revisions

**Files:**
- Modify: `specs/endpoints.map.json` (one model row, two endpoint rows)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceTenKSectionsTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 44 → 46)

**Interfaces:**
- Consumes: the `get_stocks_vX_float` and `get_v1_reference_ipos` rows as the pattern for an experimental route and for a second revision of one route (D26); `Filter<T>` for the `any_of gt gte lt lte` set, `SetFilter<T>` for `any_of` alone, `RangeFilter<T>` for the four bounds.
- Produces: `client.Reference.List10KSectionsAsync(Filter<string>? cik = null, Filter<string>? ticker = null, SetFilter<string>? section = null, RangeFilter<LocalDate>? filingDate = null, RangeFilter<LocalDate>? periodEnd = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<TenKSection>>` with `Enumerate10KSectionsAsync`, and `List10KSectionsVx0Async` / `Enumerate10KSectionsVx0Async` with the identical signature and return. Both `[Experimental("MASSIVE0001")]`. Model: `TenKSection` (`string? Cik`, `string? Ticker`, `string? Section`, `LocalDate? FilingDate`, `LocalDate? PeriodEnd`, `string? FilingUrl`, `string? Text`). No partial. Task 11 calls both.

- [ ] **Step 1: Add the fixture**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, after the `ReferenceSecFilingFiles` member:

```csharp
    /// <summary>
    /// The documented sample for GET /stocks/filings/10-K/vX/sections. The <c>vX_0</c> revision
    /// publishes the identical sample, so one fixture serves both (D26). It carries a cursor of
    /// its own.
    /// </summary>
    public const string ReferenceTenKSections = """
        {
          "count": 2,
          "next_url": "https://api.massive.com/stocks/filings/10-K/vX/sections?cursor=eyJsaW1pd...",
          "request_id": "a3f8b2c1d4e5f6g7",
          "results": [
            {
              "cik": "0000320193",
              "filing_date": "2023-11-03",
              "filing_url": "https://www.sec.gov/Archives/edgar/data/320193/0000320193-23-000106.txt",
              "period_end": "2023-09-30",
              "section": "risk_factors",
              "text": "Item 1A. Risk Factors\n\nInvesting in our stock involves risk. In addition to the other information in this Annual Report on Form 10-K, the following risk factors should be carefully considered...",
              "ticker": "AAPL"
            },
            {
              "cik": "0000789019",
              "filing_date": "2023-07-27",
              "filing_url": "https://www.sec.gov/Archives/edgar/data/789019/0000950170-23-035122.txt",
              "period_end": "2023-06-30",
              "section": "risk_factors",
              "text": "Item 1A. RISK FACTORS\n\nOur operations and financial results are subject to various risks and uncertainties...",
              "ticker": "MSFT"
            }
          ],
          "status": "OK"
        }
        """;
```

(The `\n` sequences are JSON escapes inside a raw string literal; they reach the deserializer as two characters and come out as newlines, which is what the assertions below expect.)

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/ReferenceTenKSectionsTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The 10-K sections: three filter shapes derived from the spec's suffix sets (D15), two
/// calendar-date ranges bound from the map (D-R9), and two revisions of one route sharing a
/// model, of which the served one keeps the plain name (D26).
/// </summary>
public sealed class ReferenceTenKSectionsTests
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
        StubHandler handler = new(Fixtures.ReferenceTenKSections);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.List10KSectionsAsync(
                cik: SetFilter.AnyOf("0000320193", "0000789019"),
                ticker: RangeFilter.Between("A", "N"),
                section: SetFilter.AnyOf("business", "risk_factors"),
                filingDate: RangeFilter.Gte(new LocalDate(2023, 1, 1)),
                periodEnd: RangeFilter.Lt(new LocalDate(2024, 1, 1)),
                limit: 2,
                sort: "period_end.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/filings/10-K/vX/sections"
                + "?cik.any_of=0000320193,0000789019"
                + "&ticker.gte=A&ticker.lte=N"
                + "&section.any_of=business,risk_factors"
                + "&filing_date.gte=2023-01-01"
                + "&period_end.lt=2024-01-01"
                + "&limit=2&sort=period_end.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceTenKSections);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<TenKSection> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.List10KSectionsAsync(ticker: "AAPL", section: "risk_factors", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);
        Assert.Equal("a3f8b2c1d4e5f6g7", page.RequestId);

        TenKSection section = page.Results[0];
        Assert.Equal("0000320193", section.Cik);
        Assert.Equal("AAPL", section.Ticker);
        Assert.Equal("risk_factors", section.Section);
        Assert.Equal(new LocalDate(2023, 11, 3), section.FilingDate);
        Assert.Equal(new LocalDate(2023, 9, 30), section.PeriodEnd);
        Assert.Equal("https://www.sec.gov/Archives/edgar/data/320193/0000320193-23-000106.txt", section.FilingUrl);
        Assert.StartsWith("Item 1A. Risk Factors\n\n", section.Text, StringComparison.Ordinal);

        Assert.Equal("MSFT", page.Results[1].Ticker);
    }

    [Fact]
    public async Task TheVx0RevisionBuildsItsOwnPathOverTheSameModel()
    {
        // Both revisions are declared, so both ship (rule 2); the vX_0 one carries its segment in
        // its name because the vX one is served (D26), and its path is its own.
        StubHandler handler = new(Fixtures.ReferenceTenKSections);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<TenKSection> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.List10KSectionsVx0Async(ticker: "AAPL", limit: 1, cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/stocks/filings/10-K/vX_0/sections?ticker=AAPL&limit=1", handler.LastRequestUri?.ToString());
        Assert.Equal("AAPL", page.Results[0].Ticker);
    }

    [Fact]
    public async Task EnumerateFollowsTheSampleCursorThenStops()
    {
        // The sample's cursor points at the same origin, so the traversal follows it verbatim
        // (D14); the stub serves the same page again, which has a cursor too, so the test stops
        // the traversal itself after the seam it set out to cross.
        PagingStubHandler handler = new(Fixtures.ReferenceTenKSections);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string?> tickers = [];

        using (client)
        using (transport)
        {
            await foreach (TenKSection section in client.Reference.Enumerate10KSectionsAsync(section: "risk_factors", cancellationToken: Ct))
            {
                tickers.Add(section.Ticker);

                if (tickers.Count == 3)
                {
                    break;
                }
            }
        }

        Assert.Equal(["AAPL", "MSFT", "AAPL"], tickers);
        Assert.Equal(2, handler.Requests.Count);
        Assert.StartsWith("https://api.massive.com/stocks/filings/10-K/vX/sections?cursor=", handler.Requests[1].ToString(), StringComparison.Ordinal);
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, raise the baseline:

```csharp
    private const int CoverageBaseline = 46;
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~ReferenceTenKSections"`
Expected: the build fails with CS1061 for the methods and CS0246 for `TenKSection`.

- [ ] **Step 4: Add the model row**

In `specs/endpoints.map.json`, append inside `models` after the `FilingFile` row (add a comma after its closing brace):

```json
    "TenKSection": {
      "summary": "One standardized section of a 10-K filing: the filer, the filing and period-end dates, the section identifier, and its text.",
      "remarks": "Reference data, so a class (decision D4). Named with the form number spelled out because a C# identifier cannot start with a digit (D-R8). Generated from the <c>vX</c> revision of the route and shared with the <c>vX_0</c> revision, where decision D16 verifies the shape.",
      "schema": { "operationId": "get_stocks_filings_10-K_vX_sections", "pointer": "results/items" },
      "properties": {
        "cik":         { "name": "Cik" },
        "ticker":      { "name": "Ticker" },
        "section":     { "name": "Section" },
        "filing_date": { "name": "FilingDate" },
        "period_end":  { "name": "PeriodEnd" },
        "filing_url":  { "name": "FilingUrl" },
        "text":        { "name": "Text" }
      }
    }
```

`filing_date` and `period_end` carry `format: date` on the model, so they bind `LocalDate?` with no `type` on the row.

- [ ] **Step 5: Add the endpoint rows**

In `specs/endpoints.map.json`, append inside `endpoints` after the `GetFilingFile` row (add a comma after its closing brace):

```json
    {
      "operationId": "get_stocks_filings_10-K_vX_sections",
      "group": "Reference",
      "method": "List10KSections",
      "summary": "Retrieves standardized sections of 10-K filings, filtered by filer, section, filing date, and period end.",
      "remarks": "The route's <c>vX</c> segment marks it experimental (decision D18); opt in with <c>MASSIVE0001</c>. Every filter is optional and defaults to no constraint. <paramref name=\"section\"/> takes the identifiers the description names, <c>business</c> and <c>risk_factors</c>, singly or as a set. The date filters are bare strings in the description whose prose says <c>YYYY-MM-DD</c>, so they bind <see cref=\"NodaTime.LocalDate\"/> from the map (D-R9).",
      "result": { "kind": "array", "model": "TenKSection", "property": "results" },
      "parameters": {
        "cik":         { "name": "cik" },
        "ticker":      { "name": "ticker" },
        "section":     { "name": "section" },
        "filing_date": { "name": "filingDate", "type": "LocalDate" },
        "period_end":  { "name": "periodEnd", "type": "LocalDate" },
        "limit":       { "name": "limit" },
        "sort":        { "name": "sort" }
      }
    },
    {
      "operationId": "get_stocks_filings_10-K_vX_0_sections",
      "group": "Reference",
      "method": "List10KSectionsVx0",
      "summary": "Retrieves standardized sections of 10-K filings from the vX_0 revision of the route, filtered by filer, section, filing date, and period end.",
      "remarks": "The description declares this <c>vX_0</c> revision beside the <c>vX</c> one, and the service answered a plain-text 404 for it on 2026-09-03; it stays mapped as declared (decision D21) and carries its version segment in its name because the <c>vX</c> revision is the served one (decision D26). The segment marks it experimental (decision D23); opt in with <c>MASSIVE0001</c>. The parameters and the payload are those of the <c>vX</c> revision.",
      "result": { "kind": "array", "model": "TenKSection", "property": "results" },
      "parameters": {
        "cik":         { "name": "cik" },
        "ticker":      { "name": "ticker" },
        "section":     { "name": "section" },
        "filing_date": { "name": "filingDate", "type": "LocalDate" },
        "period_end":  { "name": "periodEnd", "type": "LocalDate" },
        "limit":       { "name": "limit" },
        "sort":        { "name": "sort" }
      }
    }
```

- [ ] **Step 6: Regenerate**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: `TenKSection.g.cs` with `using NodaTime;`; in `ReferenceGroup.g.cs`, `List10KSectionsAsync` and `List10KSectionsVx0Async` each with `Filter<string>? cik`, `Filter<string>? ticker`, `SetFilter<string>? section`, `RangeFilter<LocalDate>? filingDate`, `RangeFilter<LocalDate>? periodEnd`, their `Enumerate` counterparts, and `[Experimental("MASSIVE0001")]` on all four public methods; the `Vx0` builder appends `/stocks/filings/10-K/vX_0/sections`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS; `EndpointCoverageTests` reports 46 mapped, and `StabilityAttributesMatchTheSpecification` accepts the two experimental routes.

- [ ] **Step 8: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff.

- [ ] **Step 9: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/ReferenceTenKSectionsTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs
git commit -m "feat: map both 10-K sections revisions under Reference

The served vX revision keeps the plain name and the vX_0 one carries
its segment (D26); both share TenKSection (D16, D-R8). Coverage
reaches 46.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 6: 8-K disclosures and 8-K text

**Files:**
- Modify: `specs/endpoints.map.json` (two model rows, two endpoint rows)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceEightKTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 46 → 48)

**Interfaces:**
- Consumes: the Task 5 rows as the pattern; `ArrayFilter<T>` for the `all_of any_of` set, which the snapshot operations' `tickers` already use; `Filter<LocalDate>` for a date field with the `any_of gt gte lt lte` set.
- Produces: `client.Reference.List8KDisclosuresAsync(SetFilter<string>? cik = null, ArrayFilter<string>? tickers = null, Filter<LocalDate>? filingDate = null, string? tertiaryCategory = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<EightKDisclosure>>` with `Enumerate8KDisclosuresAsync`; `List8KTextAsync(Filter<string>? cik = null, Filter<string>? ticker = null, Filter<string>? formType = null, RangeFilter<LocalDate>? filingDate = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<EightKText>>` with `Enumerate8KTextAsync`. Both `[Experimental]`. Models: `EightKDisclosure` (`string? AccessionNumber`, `string? Cik`, `string[]? Tickers`, `LocalDate? FilingDate`, `string? FilingUrl`, `string? PrimaryCategory`, `string? SecondaryCategory`, `string? TertiaryCategory`, `string? SupportingText`); `EightKText` (`string? AccessionNumber`, `string? Cik`, `string? Ticker`, `string? FormType`, `LocalDate? FilingDate`, `string? FilingUrl`, `string? ItemsText`). No partials. Task 11 calls both.

- [ ] **Step 1: Add the fixtures**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, after the `ReferenceTenKSections` member:

```csharp
    /// <summary>The documented sample for GET /stocks/filings/8-K/vX/disclosures. It carries a cursor of its own.</summary>
    public const string ReferenceEightKDisclosures = """
        {
          "count": 2,
          "next_url": "https://api.massive.com/stocks/filings/8-K/vX/disclosures?cursor=eyJsaW1pd...",
          "request_id": "b4e7c2a1f3d8e9g0",
          "results": [
            {
              "accession_number": "0000320193-25-000010",
              "cik": "0000320193",
              "filing_date": "2025-01-14",
              "filing_url": "https://www.sec.gov/Archives/edgar/data/320193/0000320193-25-000010.txt",
              "primary_category": "financial_results",
              "secondary_category": "earnings_announcement",
              "supporting_text": "On January 14, 2025, Apple Inc. announced financial results for the fiscal quarter ended December 28, 2024.",
              "tertiary_category": "quarterly_results",
              "tickers": [
                "AAPL"
              ]
            },
            {
              "accession_number": "0000004962-25-000002",
              "cik": "0000004962",
              "filing_date": "2025-01-15",
              "filing_url": "https://www.sec.gov/Archives/edgar/data/4962/0000004962-25-000002.txt",
              "primary_category": "regulatory_compliance",
              "secondary_category": "regulation_fd",
              "supporting_text": "American Express Company is hereby furnishing below delinquency and write-off statistics for its U.S. Consumer and Small Business portfolios.",
              "tertiary_category": "financial_data_disclosure",
              "tickers": [
                "AXP"
              ]
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /stocks/filings/8-K/vX/text. It carries a cursor of its own.</summary>
    public const string ReferenceEightKText = """
        {
          "count": 2,
          "next_url": "https://api.massive.com/stocks/filings/8-K/vX/text?cursor=eyJsaW1pd...",
          "request_id": "a3f8b2c1d4e5f6g7",
          "results": [
            {
              "accession_number": "0000004962-25-000002",
              "cik": "0000004962",
              "filing_date": "2025-01-15",
              "filing_url": "https://www.sec.gov/Archives/edgar/data/4962/0000004962-25-000002.txt",
              "form_type": "8-K",
              "items_text": "Item 7.01\tRegulation FD Disclosure\n\nAmerican Express Company is hereby furnishing below delinquency and write-off statistics...",
              "ticker": "AXP"
            },
            {
              "accession_number": "0000320193-25-000010",
              "cik": "0000320193",
              "filing_date": "2025-01-14",
              "filing_url": "https://www.sec.gov/Archives/edgar/data/320193/0000320193-25-000010.txt",
              "form_type": "8-K",
              "items_text": "Item 2.02\tResults of Operations and Financial Condition\n\nOn January 14, 2025, Apple Inc. announced financial results...",
              "ticker": "AAPL"
            }
          ],
          "status": "OK"
        }
        """;
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/ReferenceEightKTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The 8-K disclosures and text: an array filter over <c>tickers</c>, a set filter over
/// <c>cik</c>, a date bound from the map as a full filter on one route and a range on the other
/// (D-R9), and the published samples round-tripping.
/// </summary>
public sealed class ReferenceEightKTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task DisclosuresRenderEveryFilterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceEightKDisclosures);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.List8KDisclosuresAsync(
                cik: SetFilter.AnyOf("0000320193", "0000004962"),
                tickers: ArrayFilter.AllOf("AAPL", "AXP"),
                filingDate: RangeFilter.Between(new LocalDate(2025, 1, 1), new LocalDate(2025, 1, 31)),
                tertiaryCategory: "quarterly_results",
                limit: 2,
                sort: "filing_date.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/filings/8-K/vX/disclosures"
                + "?cik.any_of=0000320193,0000004962"
                + "&tickers.all_of=AAPL,AXP"
                + "&filing_date.gte=2025-01-01&filing_date.lte=2025-01-31"
                + "&tertiary_category=quarterly_results"
                + "&limit=2&sort=filing_date.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DisclosuresRenderTheEqualityForms()
    {
        StubHandler handler = new(Fixtures.ReferenceEightKDisclosures);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.List8KDisclosuresAsync(
                cik: "0000320193",
                tickers: ArrayFilter.Contains("AAPL"),
                filingDate: new LocalDate(2025, 1, 14),
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/filings/8-K/vX/disclosures?cik=0000320193&tickers=AAPL&filing_date=2025-01-14",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DisclosuresDeserializeThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceEightKDisclosures);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<EightKDisclosure> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.List8KDisclosuresAsync(tickers: ArrayFilter.Contains("AAPL"), cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);

        EightKDisclosure disclosure = page.Results[0];
        Assert.Equal("0000320193-25-000010", disclosure.AccessionNumber);
        Assert.Equal("0000320193", disclosure.Cik);
        Assert.Equal(new LocalDate(2025, 1, 14), disclosure.FilingDate);
        Assert.Equal("financial_results", disclosure.PrimaryCategory);
        Assert.Equal("earnings_announcement", disclosure.SecondaryCategory);
        Assert.Equal("quarterly_results", disclosure.TertiaryCategory);
        Assert.Equal(["AAPL"], disclosure.Tickers);
        Assert.StartsWith("On January 14, 2025", disclosure.SupportingText, StringComparison.Ordinal);

        Assert.Equal(["AXP"], page.Results[1].Tickers);
    }

    [Fact]
    public async Task TextRendersEveryFilterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceEightKText);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.List8KTextAsync(
                cik: "0000004962",
                ticker: RangeFilter.Gte("A"),
                formType: SetFilter.AnyOf("8-K", "10-K"),
                filingDate: RangeFilter.Lte(new LocalDate(2025, 1, 31)),
                limit: 2,
                sort: "filing_date.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/filings/8-K/vX/text"
                + "?cik=0000004962&ticker.gte=A&form_type.any_of=8-K,10-K&filing_date.lte=2025-01-31&limit=2&sort=filing_date.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task TextDeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceEightKText);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<EightKText> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.List8KTextAsync(ticker: "AXP", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);

        EightKText text = page.Results[0];
        Assert.Equal("0000004962-25-000002", text.AccessionNumber);
        Assert.Equal("AXP", text.Ticker);
        Assert.Equal("8-K", text.FormType);
        Assert.Equal(new LocalDate(2025, 1, 15), text.FilingDate);
        Assert.StartsWith("Item 7.01\tRegulation FD Disclosure", text.ItemsText, StringComparison.Ordinal);

        Assert.Equal("AAPL", page.Results[1].Ticker);
    }

    [Fact]
    public async Task EnumerateTextYieldsTheFirstPageInOrder()
    {
        PagingStubHandler handler = new(Fixtures.ReferenceEightKText);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string?> tickers = [];

        using (client)
        using (transport)
        {
            await foreach (EightKText text in client.Reference.Enumerate8KTextAsync(cancellationToken: Ct))
            {
                tickers.Add(text.Ticker);

                if (tickers.Count == 2)
                {
                    break;
                }
            }
        }

        Assert.Equal(["AXP", "AAPL"], tickers);
        Assert.Single(handler.Requests);
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, raise the baseline:

```csharp
    private const int CoverageBaseline = 48;
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~ReferenceEightK"`
Expected: the build fails with CS1061 for the methods and CS0246 for `EightKDisclosure` and `EightKText`.

- [ ] **Step 4: Add the model rows**

In `specs/endpoints.map.json`, append inside `models` after the `TenKSection` row (add a comma after its closing brace):

```json
    "EightKDisclosure": {
      "summary": "One classified disclosure from an 8-K filing: the filer, the filing date, the three-level category, and the text that supports the classification.",
      "remarks": "Reference data, so a class (decision D4). Named with the form number spelled out because a C# identifier cannot start with a digit (D-R8). <see cref=\"FilingDate\"/> is a bare string in the description and an ISO calendar date on the wire, hence <see cref=\"NodaTime.LocalDate\"/> from the map (D-R9). The categories come from the taxonomy the disclosure taxonomy route serves.",
      "schema": { "operationId": "get_stocks_filings_8-K_vX_disclosures", "pointer": "results/items" },
      "properties": {
        "accession_number":   { "name": "AccessionNumber" },
        "cik":                { "name": "Cik" },
        "tickers":            { "name": "Tickers" },
        "filing_date":        { "name": "FilingDate", "type": "LocalDate?", "summary": "The date the filing was submitted to the SEC. The description types this a bare string; the wire carries an ISO calendar date (D-R9)." },
        "filing_url":         { "name": "FilingUrl" },
        "primary_category":   { "name": "PrimaryCategory" },
        "secondary_category": { "name": "SecondaryCategory" },
        "tertiary_category":  { "name": "TertiaryCategory" },
        "supporting_text":    { "name": "SupportingText" }
      }
    },

    "EightKText": {
      "summary": "The item text of one 8-K filing: the filer, the form type, the filing date, and the text of the items it reports.",
      "remarks": "Reference data, so a class (decision D4). Named with the form number spelled out because a C# identifier cannot start with a digit (D-R8).",
      "schema": { "operationId": "get_stocks_filings_8-K_vX_text", "pointer": "results/items" },
      "properties": {
        "accession_number": { "name": "AccessionNumber" },
        "cik":              { "name": "Cik" },
        "ticker":           { "name": "Ticker" },
        "form_type":        { "name": "FormType" },
        "filing_date":      { "name": "FilingDate" },
        "filing_url":       { "name": "FilingUrl" },
        "items_text":       { "name": "ItemsText" }
      }
    }
```

`EightKText.filing_date` carries `format: date`, so it binds `LocalDate?` on its own; `EightKDisclosure.filing_date` does not, so its row names the type.

- [ ] **Step 5: Add the endpoint rows**

In `specs/endpoints.map.json`, append inside `endpoints` after the `get_stocks_filings_10-K_vX_0_sections` row (add a comma after its closing brace):

```json
    {
      "operationId": "get_stocks_filings_8-K_vX_disclosures",
      "group": "Reference",
      "method": "List8KDisclosures",
      "summary": "Retrieves classified disclosures from 8-K filings, filtered by filer, ticker, filing date, and the most specific category.",
      "remarks": "The route's <c>vX</c> segment marks it experimental (decision D18); opt in with <c>MASSIVE0001</c>. Every filter is optional and defaults to no constraint. <paramref name=\"tickers\"/> filters an array field: pass a plain value for rows that contain it, or an <see cref=\"ArrayFilter\"/> factory for any or all of several. <paramref name=\"filingDate\"/> is a bare string in the description whose prose says <c>YYYY-MM-DD</c>, so it binds <see cref=\"NodaTime.LocalDate\"/> from the map (D-R9). <paramref name=\"tertiaryCategory\"/> is an exact match against the disclosure taxonomy.",
      "result": { "kind": "array", "model": "EightKDisclosure", "property": "results" },
      "parameters": {
        "cik":               { "name": "cik" },
        "tickers":           { "name": "tickers" },
        "filing_date":       { "name": "filingDate", "type": "LocalDate" },
        "tertiary_category": { "name": "tertiaryCategory" },
        "limit":             { "name": "limit" },
        "sort":              { "name": "sort" }
      }
    },
    {
      "operationId": "get_stocks_filings_8-K_vX_text",
      "group": "Reference",
      "method": "List8KText",
      "summary": "Retrieves the item text of 8-K filings, filtered by filer, form type, and filing date.",
      "remarks": "The route's <c>vX</c> segment marks it experimental (decision D18); opt in with <c>MASSIVE0001</c>. Every filter is optional and defaults to no constraint. <paramref name=\"filingDate\"/> is a bare string in the description whose prose says <c>YYYY-MM-DD</c>, so it binds <see cref=\"NodaTime.LocalDate\"/> from the map (D-R9).",
      "result": { "kind": "array", "model": "EightKText", "property": "results" },
      "parameters": {
        "cik":         { "name": "cik" },
        "ticker":      { "name": "ticker" },
        "form_type":   { "name": "formType" },
        "filing_date": { "name": "filingDate", "type": "LocalDate" },
        "limit":       { "name": "limit" },
        "sort":        { "name": "sort" }
      }
    }
```

- [ ] **Step 6: Regenerate**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: `EightKDisclosure.g.cs` and `EightKText.g.cs` with `using NodaTime;`; `List8KDisclosuresAsync` with `SetFilter<string>? cik`, `ArrayFilter<string>? tickers`, `Filter<LocalDate>? filingDate`, `string? tertiaryCategory`; `List8KTextAsync` with three `Filter<string>?` parameters and `RangeFilter<LocalDate>? filingDate`; `Enumerate` counterparts; `[Experimental]` on all four.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS; `EndpointCoverageTests` reports 48 mapped.

- [ ] **Step 8: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff.

- [ ] **Step 9: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/ReferenceEightKTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs
git commit -m "feat: map the 8-K disclosures and text under Reference

An array filter over tickers, a date bound LocalDate from the map on
both routes (D-R9), and models named with the form number spelled out
(D-R8). Coverage reaches 48.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 7: 13-F holdings, form 3, form 4, and the shared footnote

**Files:**
- Modify: `specs/endpoints.map.json` (four model rows, three endpoint rows)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceOwnershipFilingsTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 48 → 51)

**Interfaces:**
- Consumes: the Task 6 rows as the pattern; the `UpdateRule` row (Plan A) as the pattern for a nested model reused at a second site, which D16 verifies structurally.
- Produces: `client.Reference.List13FHoldingsAsync(SetFilter<string>? filerCik = null, RangeFilter<LocalDate>? filingDate = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<ThirteenFHolding>>` with `Enumerate13FHoldingsAsync`; `ListForm3FilingsAsync(SetFilter<string>? issuerCik = null, SetFilter<string>? ownerCik = null, ArrayFilter<string>? tickers = null, string? formType = null, RangeFilter<LocalDate>? filingDate = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<Form3Filing>>` with `EnumerateForm3FilingsAsync`; `ListForm4FilingsAsync(SetFilter<string>? issuerCik = null, SetFilter<string>? ownerCik = null, ArrayFilter<string>? tickers = null, string? formType = null, RangeFilter<LocalDate>? filingDate = null, string? transactionCode = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<Form4Filing>>` with `EnumerateForm4FilingsAsync`. All `[Experimental]`. Models: `ThirteenFHolding` (twenty optional properties; `long?` for the five counts and the market value, `LocalDate?` for `FilingDate` and `Period`, `string[]? OtherManagers`); `Form3Filing` (twenty-nine optional properties, `FilingFootnote[]? Footnotes`, `string[]? Tickers`, the six `bool?` flags named `IsDirector`, `IsOfficer`, `IsTenPercentOwner`, `IsOther`, `IsNotSubjectToSection16`, `IsRule10b51Plan`); `Form4Filing` (forty, the form 3 set less `SharesOwned` plus the transaction fields and `IsEquitySwapInvolved`); `FilingFootnote` (`string? Id`, `string? Description`). No partials. Task 11 calls all three.

- [ ] **Step 1: Add the fixtures**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, after the `ReferenceEightKText` member:

```csharp
    /// <summary>The documented sample for GET /stocks/filings/vX/13-F. It carries a cursor of its own and a null <c>put_call</c>.</summary>
    public const string ReferenceThirteenFHoldings = """
        {
          "next_url": "https://api.massive.com/stocks/filings/vX/13-F?cursor=eyJsaW1pd...",
          "request_id": "a3f8b2c1d4e5f6g7",
          "results": [
            {
              "accession_number": "0000950123-24-011775",
              "cusip": "023135106",
              "file_number": "028-04545",
              "filer_cik": "0001067983",
              "filing_date": "2024-11-14",
              "filing_url": "https://www.sec.gov/Archives/edgar/data/1067983/0000950123-24-011775.txt",
              "film_number": "241461756",
              "form_type": "13F-HR",
              "investment_discretion": "DFND",
              "issuer_name": "AMAZON COM INC",
              "market_value": 1439212920,
              "other_managers": [
                "Buffett Warren E"
              ],
              "period": "2024-09-30",
              "put_call": null,
              "shares_or_principal_amount": 7724000,
              "shares_or_principal_type": "SH",
              "title_of_class": "COM",
              "voting_authority_none": 0,
              "voting_authority_shared": 0,
              "voting_authority_sole": 7724000
            },
            {
              "accession_number": "0000950123-24-011775",
              "cusip": "025816109",
              "file_number": "028-04545",
              "filer_cik": "0001067983",
              "filing_date": "2024-11-14",
              "filing_url": "https://www.sec.gov/Archives/edgar/data/1067983/0000950123-24-011775.txt",
              "film_number": "241461756",
              "form_type": "13F-HR",
              "investment_discretion": "DFND",
              "issuer_name": "AMERICAN EXPRESS CO",
              "market_value": 311864270,
              "other_managers": [
                "Buffett Warren E"
              ],
              "period": "2024-09-30",
              "put_call": null,
              "shares_or_principal_amount": 1149942,
              "shares_or_principal_type": "SH",
              "title_of_class": "COM",
              "voting_authority_none": 0,
              "voting_authority_shared": 0,
              "voting_authority_sole": 1149942
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /stocks/filings/vX/form-3. It carries a cursor of its own and several explicit nulls.</summary>
    public const string ReferenceForm3Filings = """
        {
          "count": 1,
          "next_url": "https://api.massive.com/stocks/filings/vX/form-3?cursor=eyJsaW1pd...",
          "request_id": "047c7035a86042b1925118e5f68d81b0",
          "results": [
            {
              "accession_number": "0001628280-26-022046",
              "aff_10b5_one": null,
              "date_of_original_submission": null,
              "direct_or_indirect": "D",
              "exercise_date": null,
              "exercise_price": null,
              "filing_date": "2026-03-30",
              "filing_url": "https://www.sec.gov/Archives/edgar/data/1903508/0001628280-26-022046.txt",
              "footnotes": [
                {
                  "description": "The options granted on May 17, 2022 vested 100% on the third anniversary of the grant date.",
                  "id": "F1"
                },
                {
                  "description": "The exercise price of the options granted on May 17, 2022 was GPB 8.85, or approximately $11.80 based on a GBP to USD exchange rate as of March 27, 2026 of 1.3336.",
                  "id": "F2"
                }
              ],
              "form_type": "3",
              "is_director": false,
              "is_officer": true,
              "is_other": false,
              "is_ten_percent_owner": false,
              "issuer_cik": "0001903508",
              "issuer_name": "Public Policy Holding Company, Inc.",
              "nature_of_ownership": null,
              "not_subject_to_section_16": null,
              "officer_title": "Chief Administrative Officer",
              "owner_cik": "0002125791",
              "owner_name": "Mazzanti Matthew Ross",
              "period_of_report": "2026-03-20",
              "remarks": null,
              "security_title": "Options",
              "security_type": "derivative",
              "shares_owned": null,
              "tickers": [
                "PPHC"
              ],
              "underlying_security_shares": 9000,
              "underlying_security_title": "Common Stock, $0.001 par value"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /stocks/filings/vX/form-4. It carries a cursor of its own.</summary>
    public const string ReferenceForm4Filings = """
        {
          "next_url": "https://api.massive.com/stocks/filings/vX/form-4?cursor=eyJsaW1pd...",
          "request_id": "047c7035a86042b1925118e5f68d81b0",
          "results": [
            {
              "accession_number": "0002123147-26-000002",
              "aff_10b5_one": false,
              "date_of_original_submission": null,
              "deemed_execution_date": "2026-03-27",
              "direct_or_indirect": "D",
              "equity_swap_involved": false,
              "exercise_date": "2026-03-27",
              "exercise_price": 88.167,
              "expiration_date": "2026-03-27",
              "filing_date": "2026-03-30",
              "filing_url": "https://www.sec.gov/Archives/edgar/data/1469395/0002123147-26-000002.txt",
              "footnotes": [
                {
                  "description": "These shares were acquired at a price of 4955 argentine pesos per share. For reporting purposes, the exercise price has been converted to US dollars based on the exchange rate reported by Banco de la Nacion Argentina for the date of the acquisition, which was 1405 argentine pesos per US dollar. Then multiplied by 25, the Par value or rate of common shares to one ADR.",
                  "id": "F1"
                }
              ],
              "form_type": "4",
              "is_director": false,
              "is_officer": true,
              "is_other": false,
              "is_ten_percent_owner": false,
              "issuer_cik": "0001469395",
              "issuer_name": "Pampa Energy Inc.",
              "not_subject_to_section_16": false,
              "officer_title": "Chief Financial Officer",
              "owner_cik": "0002123147",
              "owner_name": "Zuberbuhler Adolfo Fernando",
              "period_of_report": "2026-03-27",
              "record_type": "transaction",
              "security_title": "Common Stock, $25 Par Value",
              "security_type": "derivative",
              "shares_owned_following_transaction": 2759,
              "tickers": [
                "PAM"
              ],
              "transaction_acquired_disposed": "A",
              "transaction_code": "A",
              "transaction_date": "2026-03-27",
              "transaction_price_per_share": 88.167,
              "transaction_shares": 12923,
              "transaction_timeliness": "O",
              "transaction_value": 1139382.141,
              "underlying_security_shares": 12923,
              "underlying_security_title": "PAMP"
            }
          ],
          "status": "OK"
        }
        """;
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/ReferenceOwnershipFilingsTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The ownership filings: 13-F holdings and forms 3 and 4. Set filters over the CIKs, an array
/// filter over tickers, calendar-date ranges bound from the map (D-R9), and the published samples
/// round-tripping, the two forms through one <see cref="FilingFootnote"/> model (D16).
/// </summary>
public sealed class ReferenceOwnershipFilingsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task ThirteenFRendersEveryFilterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceThirteenFHoldings);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.List13FHoldingsAsync(
                filerCik: SetFilter.AnyOf("0001067983", "0001166559"),
                filingDate: RangeFilter.Between(new LocalDate(2024, 7, 1), new LocalDate(2024, 12, 31)),
                limit: 2,
                sort: "filing_date.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/filings/vX/13-F"
                + "?filer_cik.any_of=0001067983,0001166559&filing_date.gte=2024-07-01&filing_date.lte=2024-12-31&limit=2&sort=filing_date.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task ThirteenFDeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceThirteenFHoldings);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<ThirteenFHolding> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.List13FHoldingsAsync(filerCik: "0001067983", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);

        ThirteenFHolding holding = page.Results[0];
        Assert.Equal("0000950123-24-011775", holding.AccessionNumber);
        Assert.Equal("0001067983", holding.FilerCik);
        Assert.Equal("AMAZON COM INC", holding.IssuerName);
        Assert.Equal("023135106", holding.Cusip);
        Assert.Equal("13F-HR", holding.FormType);
        Assert.Equal(new LocalDate(2024, 11, 14), holding.FilingDate);
        Assert.Equal(new LocalDate(2024, 9, 30), holding.Period);
        Assert.Equal(1439212920L, holding.MarketValue);
        Assert.Equal(7724000L, holding.SharesOrPrincipalAmount);
        Assert.Equal("SH", holding.SharesOrPrincipalType);
        Assert.Equal(7724000L, holding.VotingAuthoritySole);
        Assert.Equal(0L, holding.VotingAuthorityShared);
        Assert.Equal(["Buffett Warren E"], holding.OtherManagers);
        Assert.Null(holding.PutCall);

        Assert.Equal("AMERICAN EXPRESS CO", page.Results[1].IssuerName);
    }

    [Fact]
    public async Task Form3RendersEveryFilterInDeclarationOrder()
    {
        StubHandler handler = new(Fixtures.ReferenceForm3Filings);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListForm3FilingsAsync(
                issuerCik: "0001903508",
                ownerCik: SetFilter.AnyOf("0002125791", "0000000001"),
                tickers: ArrayFilter.AnyOf("PPHC", "AAPL"),
                formType: "3",
                filingDate: RangeFilter.Gte(new LocalDate(2026, 3, 1)),
                limit: 1,
                sort: "filing_date.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/filings/vX/form-3"
                + "?issuer_cik=0001903508&owner_cik.any_of=0002125791,0000000001&tickers.any_of=PPHC,AAPL&form_type=3&filing_date.gte=2026-03-01&limit=1&sort=filing_date.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task Form3DeserializesThePublishedSampleThroughTheFootnotes()
    {
        StubHandler handler = new(Fixtures.ReferenceForm3Filings);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Form3Filing> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListForm3FilingsAsync(tickers: ArrayFilter.Contains("PPHC"), cancellationToken: Ct);
        }

        Form3Filing filing = Assert.Single(page.Results);
        Assert.True(page.HasMore);
        Assert.Equal("0001628280-26-022046", filing.AccessionNumber);
        Assert.Equal("3", filing.FormType);
        Assert.Equal(new LocalDate(2026, 3, 30), filing.FilingDate);
        Assert.Equal(new LocalDate(2026, 3, 20), filing.PeriodOfReport);
        Assert.Equal("Public Policy Holding Company, Inc.", filing.IssuerName);
        Assert.Equal("Mazzanti Matthew Ross", filing.OwnerName);
        Assert.Equal("Chief Administrative Officer", filing.OfficerTitle);
        Assert.True(filing.IsOfficer);
        Assert.False(filing.IsDirector);
        Assert.Null(filing.IsRule10b51Plan);
        Assert.Null(filing.IsNotSubjectToSection16);
        Assert.Equal("Options", filing.SecurityTitle);
        Assert.Equal("D", filing.DirectOrIndirect);
        Assert.Null(filing.SharesOwned);
        Assert.Null(filing.ExercisePrice);
        Assert.Equal(9000d, filing.UnderlyingSecurityShares);
        Assert.Equal(["PPHC"], filing.Tickers);

        Assert.NotNull(filing.Footnotes);
        Assert.Equal(2, filing.Footnotes.Length);
        Assert.Equal("F1", filing.Footnotes[0].Id);
        Assert.StartsWith("The options granted", filing.Footnotes[0].Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Form4RendersTheEqualityForms()
    {
        StubHandler handler = new(Fixtures.ReferenceForm4Filings);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListForm4FilingsAsync(
                tickers: ArrayFilter.Contains("PAM"),
                filingDate: new LocalDate(2026, 3, 30),
                transactionCode: "A",
                limit: 1,
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/filings/vX/form-4?tickers=PAM&filing_date=2026-03-30&transaction_code=A&limit=1",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task Form4DeserializesThePublishedSampleThroughTheFootnotes()
    {
        StubHandler handler = new(Fixtures.ReferenceForm4Filings);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Form4Filing> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListForm4FilingsAsync(tickers: ArrayFilter.Contains("PAM"), cancellationToken: Ct);
        }

        Form4Filing filing = Assert.Single(page.Results);
        Assert.True(page.HasMore);
        Assert.Equal("0002123147-26-000002", filing.AccessionNumber);
        Assert.Equal("4", filing.FormType);
        Assert.Equal("Pampa Energy Inc.", filing.IssuerName);
        Assert.Equal("Zuberbuhler Adolfo Fernando", filing.OwnerName);
        Assert.Equal("transaction", filing.RecordType);
        Assert.Equal("A", filing.TransactionCode);
        Assert.Equal("A", filing.TransactionAcquiredDisposed);
        Assert.Equal("O", filing.TransactionTimeliness);
        Assert.Equal(new LocalDate(2026, 3, 27), filing.TransactionDate);
        Assert.Equal(new LocalDate(2026, 3, 27), filing.DeemedExecutionDate);
        Assert.Equal(new LocalDate(2026, 3, 27), filing.ExpirationDate);
        Assert.Equal(12923d, filing.TransactionShares);
        Assert.Equal(88.167, filing.TransactionPricePerShare);
        Assert.Equal(1139382.141, filing.TransactionValue);
        Assert.Equal(2759d, filing.SharesOwnedFollowingTransaction);
        Assert.False(filing.IsRule10b51Plan);
        Assert.False(filing.IsEquitySwapInvolved);
        Assert.False(filing.IsNotSubjectToSection16);
        Assert.Null(filing.DateOfOriginalSubmission);
        Assert.Equal(["PAM"], filing.Tickers);

        FilingFootnote footnote = Assert.Single(filing.Footnotes ?? []);
        Assert.Equal("F1", footnote.Id);
    }

    [Fact]
    public async Task EnumerateThirteenFYieldsTheFirstPageInOrder()
    {
        PagingStubHandler handler = new(Fixtures.ReferenceThirteenFHoldings);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string?> issuers = [];

        using (client)
        using (transport)
        {
            await foreach (ThirteenFHolding holding in client.Reference.Enumerate13FHoldingsAsync(filerCik: "0001067983", cancellationToken: Ct))
            {
                issuers.Add(holding.IssuerName);

                if (issuers.Count == 2)
                {
                    break;
                }
            }
        }

        Assert.Equal(["AMAZON COM INC", "AMERICAN EXPRESS CO"], issuers);
        Assert.Single(handler.Requests);
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, raise the baseline:

```csharp
    private const int CoverageBaseline = 51;
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~ReferenceOwnershipFilings"`
Expected: the build fails with CS1061 for the methods and CS0246 for the four models.

- [ ] **Step 4: Add the model rows**

In `specs/endpoints.map.json`, append inside `models` after the `EightKText` row (add a comma after its closing brace):

```json
    "ThirteenFHolding": {
      "summary": "One holding reported on a 13-F filing: the filer, the security, the amount and value held, and the voting authority over it.",
      "remarks": "Reference data, so a class (decision D4). Named with the form number spelled out because a C# identifier cannot start with a digit (D-R8). One filing reports many holdings, so the filing's own fields repeat on every row.",
      "schema": { "operationId": "get_stocks_filings_vX_13-F", "pointer": "results/items" },
      "properties": {
        "accession_number":           { "name": "AccessionNumber" },
        "filer_cik":                  { "name": "FilerCik" },
        "form_type":                  { "name": "FormType" },
        "filing_date":                { "name": "FilingDate" },
        "period":                     { "name": "Period" },
        "filing_url":                 { "name": "FilingUrl" },
        "file_number":                { "name": "FileNumber" },
        "film_number":                { "name": "FilmNumber" },
        "issuer_name":                { "name": "IssuerName" },
        "cusip":                      { "name": "Cusip" },
        "title_of_class":             { "name": "TitleOfClass" },
        "put_call":                   { "name": "PutCall" },
        "market_value":               { "name": "MarketValue" },
        "shares_or_principal_amount": { "name": "SharesOrPrincipalAmount" },
        "shares_or_principal_type":   { "name": "SharesOrPrincipalType" },
        "investment_discretion":      { "name": "InvestmentDiscretion" },
        "other_managers":             { "name": "OtherManagers" },
        "voting_authority_sole":      { "name": "VotingAuthoritySole" },
        "voting_authority_shared":    { "name": "VotingAuthorityShared" },
        "voting_authority_none":      { "name": "VotingAuthorityNone" }
      }
    },

    "FilingFootnote": {
      "summary": "A footnote on an ownership filing: its identifier, as the filing's fields cite it, and its text.",
      "remarks": "Generated from the form 3 route and shared with form 4, where decision D16 verifies the shape. The description gives its two properties no prose, so the summaries here are the map's.",
      "schema": { "operationId": "get_stocks_filings_vX_form-3", "pointer": "results/items/footnotes/items" },
      "properties": {
        "id":          { "name": "Id", "summary": "The footnote's identifier, such as <c>F1</c>, which the filing's fields cite." },
        "description": { "name": "Description", "summary": "The footnote's text." }
      }
    },

    "Form3Filing": {
      "summary": "One initial statement of beneficial ownership, form 3: the issuer, the reporting owner and their relationship to it, and the security held.",
      "remarks": "Reference data, so a class (decision D4). A trailing form number keeps its digits (D-R8). The description marks nothing required, and the published sample carries explicit nulls, so every property is nullable. The six flags take an <c>Is</c> prefix; <see cref=\"IsRule10b51Plan\"/> is the wire's <c>aff_10b5_one</c>.",
      "schema": { "operationId": "get_stocks_filings_vX_form-3", "pointer": "results/items" },
      "properties": {
        "accession_number":            { "name": "AccessionNumber" },
        "form_type":                   { "name": "FormType" },
        "filing_date":                 { "name": "FilingDate" },
        "filing_url":                  { "name": "FilingUrl" },
        "period_of_report":            { "name": "PeriodOfReport" },
        "date_of_original_submission": { "name": "DateOfOriginalSubmission" },
        "issuer_cik":                  { "name": "IssuerCik" },
        "issuer_name":                 { "name": "IssuerName" },
        "tickers":                     { "name": "Tickers" },
        "owner_cik":                   { "name": "OwnerCik" },
        "owner_name":                  { "name": "OwnerName" },
        "is_director":                 { "name": "IsDirector" },
        "is_officer":                  { "name": "IsOfficer" },
        "is_ten_percent_owner":        { "name": "IsTenPercentOwner" },
        "is_other":                    { "name": "IsOther" },
        "officer_title":               { "name": "OfficerTitle" },
        "not_subject_to_section_16":   { "name": "IsNotSubjectToSection16" },
        "aff_10b5_one":                { "name": "IsRule10b51Plan", "summary": "Whether the transaction was made under a Rule 10b5-1 trading plan; the wire's <c>aff_10b5_one</c>." },
        "security_title":              { "name": "SecurityTitle" },
        "security_type":               { "name": "SecurityType" },
        "shares_owned":                { "name": "SharesOwned" },
        "direct_or_indirect":          { "name": "DirectOrIndirect" },
        "nature_of_ownership":         { "name": "NatureOfOwnership" },
        "exercise_date":               { "name": "ExerciseDate" },
        "exercise_price":              { "name": "ExercisePrice" },
        "underlying_security_title":   { "name": "UnderlyingSecurityTitle" },
        "underlying_security_shares":  { "name": "UnderlyingSecurityShares" },
        "footnotes":                   { "name": "Footnotes", "model": "FilingFootnote" },
        "remarks":                     { "name": "Remarks" }
      }
    },

    "Form4Filing": {
      "summary": "One statement of changes in beneficial ownership, form 4: the issuer, the reporting owner, the security, and the transaction reported.",
      "remarks": "Reference data, so a class (decision D4). A trailing form number keeps its digits (D-R8). The description marks nothing required, so every property is nullable; a holding row rather than a transaction row leaves the transaction fields null. The seven flags take an <c>Is</c> prefix; <see cref=\"IsRule10b51Plan\"/> is the wire's <c>aff_10b5_one</c>.",
      "schema": { "operationId": "get_stocks_filings_vX_form-4", "pointer": "results/items" },
      "properties": {
        "accession_number":                   { "name": "AccessionNumber" },
        "form_type":                          { "name": "FormType" },
        "filing_date":                        { "name": "FilingDate" },
        "filing_url":                         { "name": "FilingUrl" },
        "period_of_report":                   { "name": "PeriodOfReport" },
        "date_of_original_submission":        { "name": "DateOfOriginalSubmission" },
        "issuer_cik":                         { "name": "IssuerCik" },
        "issuer_name":                        { "name": "IssuerName" },
        "tickers":                            { "name": "Tickers" },
        "owner_cik":                          { "name": "OwnerCik" },
        "owner_name":                         { "name": "OwnerName" },
        "is_director":                        { "name": "IsDirector" },
        "is_officer":                         { "name": "IsOfficer" },
        "is_ten_percent_owner":               { "name": "IsTenPercentOwner" },
        "is_other":                           { "name": "IsOther" },
        "officer_title":                      { "name": "OfficerTitle" },
        "not_subject_to_section_16":          { "name": "IsNotSubjectToSection16" },
        "aff_10b5_one":                       { "name": "IsRule10b51Plan", "summary": "Whether the transaction was made under a Rule 10b5-1 trading plan; the wire's <c>aff_10b5_one</c>." },
        "record_type":                        { "name": "RecordType" },
        "security_title":                     { "name": "SecurityTitle" },
        "security_type":                      { "name": "SecurityType" },
        "transaction_date":                   { "name": "TransactionDate" },
        "deemed_execution_date":              { "name": "DeemedExecutionDate" },
        "transaction_code":                   { "name": "TransactionCode" },
        "transaction_timeliness":             { "name": "TransactionTimeliness" },
        "transaction_acquired_disposed":      { "name": "TransactionAcquiredDisposed" },
        "transaction_shares":                 { "name": "TransactionShares" },
        "transaction_price_per_share":        { "name": "TransactionPricePerShare" },
        "transaction_value":                  { "name": "TransactionValue" },
        "shares_owned_following_transaction": { "name": "SharesOwnedFollowingTransaction" },
        "direct_or_indirect":                 { "name": "DirectOrIndirect" },
        "nature_of_ownership":                { "name": "NatureOfOwnership" },
        "equity_swap_involved":               { "name": "IsEquitySwapInvolved" },
        "exercise_date":                      { "name": "ExerciseDate" },
        "exercise_price":                     { "name": "ExercisePrice" },
        "expiration_date":                    { "name": "ExpirationDate" },
        "underlying_security_title":          { "name": "UnderlyingSecurityTitle" },
        "underlying_security_shares":         { "name": "UnderlyingSecurityShares" },
        "footnotes":                          { "name": "Footnotes", "model": "FilingFootnote" },
        "remarks":                            { "name": "Remarks" }
      }
    }
```

Every date on these three models carries `format: date`, so none needs a `type`. The `Form4Filing.footnotes` row reuses `FilingFootnote` at a second site; the generator compares the two `footnotes/items` schemas property by property (D16) and accepts them because both declare `description` and `id`, neither required.

- [ ] **Step 5: Add the endpoint rows**

In `specs/endpoints.map.json`, append inside `endpoints` after the `get_stocks_filings_8-K_vX_text` row (add a comma after its closing brace):

```json
    {
      "operationId": "get_stocks_filings_vX_13-F",
      "group": "Reference",
      "method": "List13FHoldings",
      "summary": "Retrieves the holdings reported on 13-F filings, filtered by the filing institution and the filing date.",
      "remarks": "The route's <c>vX</c> segment marks it experimental (decision D18); opt in with <c>MASSIVE0001</c>. Every filter is optional and defaults to no constraint. <paramref name=\"filerCik\"/> is the institution's CIK, singly or as a set. <paramref name=\"filingDate\"/> is a bare string in the description whose prose says <c>YYYY-MM-DD</c>, so it binds <see cref=\"NodaTime.LocalDate\"/> from the map (D-R9).",
      "result": { "kind": "array", "model": "ThirteenFHolding", "property": "results" },
      "parameters": {
        "filer_cik":   { "name": "filerCik" },
        "filing_date": { "name": "filingDate", "type": "LocalDate" },
        "limit":       { "name": "limit" },
        "sort":        { "name": "sort" }
      }
    },
    {
      "operationId": "get_stocks_filings_vX_form-3",
      "group": "Reference",
      "method": "ListForm3Filings",
      "summary": "Retrieves initial statements of beneficial ownership, form 3, filtered by issuer, reporting owner, ticker, form type, and filing date.",
      "remarks": "The route's <c>vX</c> segment marks it experimental (decision D18); opt in with <c>MASSIVE0001</c>. Every filter is optional and defaults to no constraint. <paramref name=\"tickers\"/> filters an array field: pass a plain value for rows that contain it, or an <see cref=\"ArrayFilter\"/> factory for any or all of several. <paramref name=\"formType\"/> is <c>3</c> or <c>3/A</c>. <paramref name=\"filingDate\"/> is a bare string in the description whose prose says <c>YYYY-MM-DD</c>, so it binds <see cref=\"NodaTime.LocalDate\"/> from the map (D-R9).",
      "result": { "kind": "array", "model": "Form3Filing", "property": "results" },
      "parameters": {
        "issuer_cik":  { "name": "issuerCik" },
        "owner_cik":   { "name": "ownerCik" },
        "tickers":     { "name": "tickers" },
        "form_type":   { "name": "formType" },
        "filing_date": { "name": "filingDate", "type": "LocalDate" },
        "limit":       { "name": "limit" },
        "sort":        { "name": "sort" }
      }
    },
    {
      "operationId": "get_stocks_filings_vX_form-4",
      "group": "Reference",
      "method": "ListForm4Filings",
      "summary": "Retrieves statements of changes in beneficial ownership, form 4, filtered by issuer, reporting owner, ticker, form type, filing date, and transaction code.",
      "remarks": "The route's <c>vX</c> segment marks it experimental (decision D18); opt in with <c>MASSIVE0001</c>. Every filter is optional and defaults to no constraint. <paramref name=\"tickers\"/> filters an array field: pass a plain value for rows that contain it, or an <see cref=\"ArrayFilter\"/> factory for any or all of several. <paramref name=\"formType\"/> is <c>4</c> or <c>4/A</c>; <paramref name=\"transactionCode\"/> is the SEC's one-letter code, such as <c>P</c> for a purchase or <c>S</c> for a sale. <paramref name=\"filingDate\"/> is a bare string in the description whose prose says <c>YYYY-MM-DD</c>, so it binds <see cref=\"NodaTime.LocalDate\"/> from the map (D-R9).",
      "result": { "kind": "array", "model": "Form4Filing", "property": "results" },
      "parameters": {
        "issuer_cik":       { "name": "issuerCik" },
        "owner_cik":        { "name": "ownerCik" },
        "tickers":          { "name": "tickers" },
        "form_type":        { "name": "formType" },
        "filing_date":      { "name": "filingDate", "type": "LocalDate" },
        "transaction_code": { "name": "transactionCode" },
        "limit":            { "name": "limit" },
        "sort":             { "name": "sort" }
      }
    }
```

- [ ] **Step 6: Regenerate**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: `ThirteenFHolding.g.cs`, `FilingFootnote.g.cs`, `Form3Filing.g.cs`, `Form4Filing.g.cs`; the first and the two forms with `using NodaTime;`, the footnote without. `Form3Filing` and `Form4Filing` each carry `public FilingFootnote[]? Footnotes { get; init; }` and `public bool? IsRule10b51Plan { get; init; }`. `List13FHoldingsAsync` with `SetFilter<string>? filerCik` and `RangeFilter<LocalDate>? filingDate`; `ListForm3FilingsAsync` and `ListForm4FilingsAsync` with two `SetFilter<string>?`, an `ArrayFilter<string>? tickers`, `string? formType`, `RangeFilter<LocalDate>? filingDate`, and on form 4 `string? transactionCode`; `Enumerate` counterparts; `[Experimental]` on all six.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS; `EndpointCoverageTests` reports 51 mapped.

- [ ] **Step 8: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff.

- [ ] **Step 9: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/ReferenceOwnershipFilingsTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs
git commit -m "feat: map 13-F holdings and forms 3 and 4 under Reference

Forms 3 and 4 share FilingFootnote, verified structurally at the second
site (D16); a leading form number is spelled and a trailing one kept
(D-R8). Coverage reaches 51.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 8: The filing index, risk factors, and both taxonomies

**Files:**
- Modify: `specs/endpoints.map.json` (four model rows, four endpoint rows)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceFilingIndexTests.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceRiskFactorsTests.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceTaxonomiesTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 51 → 55)
- Modify: `docs/superpowers/specs/2026-09-03-reference-group-design.md` (one sentence in D-R12)

**Interfaces:**
- Consumes: the Task 6 rows as the pattern; `RangeFilter<double>` for a numeric four-bound group.
- Produces: `client.Reference.ListFilingIndexAsync(Filter<string>? cik = null, Filter<string>? ticker = null, Filter<string>? formType = null, RangeFilter<LocalDate>? filingDate = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<FilingIndexEntry>>` with `EnumerateFilingIndexAsync`; `ListRiskFactorsAsync(Filter<LocalDate>? filingDate = null, Filter<string>? ticker = null, Filter<string>? cik = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<RiskFactor>>` with `EnumerateRiskFactorsAsync`; `ListDisclosureTaxonomyAsync(Filter<string>? taxonomy = null, Filter<string>? primaryCategory = null, Filter<string>? secondaryCategory = null, Filter<string>? tertiaryCategory = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<DisclosureTaxonomyEntry>>` with `EnumerateDisclosureTaxonomyAsync`; `ListRiskFactorTaxonomyAsync(RangeFilter<double>? taxonomy = null, Filter<string>? primaryCategory = null, Filter<string>? secondaryCategory = null, Filter<string>? tertiaryCategory = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<RiskFactorTaxonomyEntry>>` with `EnumerateRiskFactorTaxonomyAsync`. All `[Experimental]`. Models: `FilingIndexEntry` (seven optional strings, `LocalDate? FilingDate`); `RiskFactor` (six optional strings, `LocalDate? FilingDate` from the map); `DisclosureTaxonomyEntry` (`required string Taxonomy`, four optional strings); `RiskFactorTaxonomyEntry` (`double Taxonomy`, four optional strings). No partials. Task 11 calls all four.

**Ruling.** The published risk-factor taxonomy example spells `"taxonomy": "1.0"`, a string, where the schema declares a required number and the live wire sent the number `1.0` on 2026-09-03. Deserializing the example as published would fail on the `double` the schema and the wire agree on, so the fixture follows them and departs from the example, commented, the way the three `"request_id": 1` corrections did (D-R10's third bullet, in the other direction). The alternative, typing the property `string` from the map, would put the map in the position of correcting the description where the wire agrees with the description, which is nothing the spec sanctions. Step 10 amends D-R12 to record the departure. If wrong, the cost is a fixture that deserializes where the wire would not, which the Task 11 shape call would expose on its first run.

- [ ] **Step 1: Add the fixtures**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, after the `ReferenceForm4Filings` member:

```csharp
    /// <summary>The documented sample for GET /stocks/filings/vX/index. It carries a cursor of its own.</summary>
    public const string ReferenceFilingIndex = """
        {
          "next_url": "https://api.massive.com/stocks/filings/vX/index?cursor=eyJsaW1pd...",
          "request_id": "1daccfd9794e482e96d104dee6ed432b",
          "results": [
            {
              "accession_number": "0000320193-25-000079",
              "cik": "0000320193",
              "filing_date": "2025-10-31",
              "filing_url": "https://www.sec.gov/Archives/edgar/data/320193/0000320193-25-000079.txt",
              "form_type": "10-K",
              "issuer_name": "Apple Inc.",
              "ticker": "AAPL"
            },
            {
              "accession_number": "0000950170-25-010491",
              "cik": "0000789019",
              "filing_date": "2025-01-29",
              "filing_url": "https://www.sec.gov/Archives/edgar/data/789019/0000950170-25-010491.txt",
              "form_type": "10-Q",
              "issuer_name": "MICROSOFT CORP",
              "ticker": "MSFT"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /stocks/filings/vX/risk-factors. It carries no cursor.</summary>
    public const string ReferenceRiskFactors = """
        {
          "request_id": "c7856101f86c20d855b0ea1c5a6d6efa",
          "results": [
            {
              "cik": "0001005101",
              "filing_date": "2025-09-19",
              "primary_category": "financial_and_market",
              "secondary_category": "credit_and_liquidity",
              "supporting_text": "In addition to the net proceeds we received from our recent equity and debt financings, we may need to raise additional equity or debt financing to continue the development and marketing of our Fintech app, to fund ongoing operations, invest in acquisitions, and for working capital purposes. Our inability to raise such additional financing may limit our ability to continue the development of our Fintech app.",
              "tertiary_category": "access_to_capital_and_financing",
              "ticker": "MGLD"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /stocks/taxonomies/vX/disclosures. It carries no cursor.</summary>
    public const string ReferenceDisclosureTaxonomy = """
        {
          "request_id": "a1b2c3d4e5f6a7b8c9d0e1f2",
          "results": [
            {
              "description": "New CEO appointment with background, employment terms, and compensation.",
              "primary_category": "leadership_and_governance",
              "secondary_category": "executive_leadership",
              "taxonomy": "1.0",
              "tertiary_category": "ceo_appointment"
            },
            {
              "description": "Quarterly financial results including revenue, net income, EPS, and key operating metrics with management commentary.",
              "primary_category": "financial_results",
              "secondary_category": "earnings_and_performance",
              "taxonomy": "1.0",
              "tertiary_category": "quarterly_earnings"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// The documented sample for GET /stocks/taxonomies/vX/risk-factors, with one departure from
    /// the published text: <c>"taxonomy": "1.0"</c> becomes the number <c>1.0</c>. The schema
    /// declares a required number, and the live wire sent one on 2026-09-03; the example's string
    /// would fail on the type the schema and the wire agree on (D-R12).
    /// </summary>
    public const string ReferenceRiskFactorTaxonomy = """
        {
          "request_id": "daac836f71724420b66011d55d88b30b",
          "results": [
            {
              "description": "Risk from inadequate performance management systems, unclear accountability structures, or ineffective measurement and incentive systems that could affect employee performance, goal achievement, and organizational effectiveness.",
              "primary_category": "Governance & Stakeholder",
              "secondary_category": "Organizational & Management",
              "taxonomy": 1.0,
              "tertiary_category": "Performance management and accountability"
            },
            {
              "description": "Risk from requirements to monitor, document, and report on compliance with privacy and data protection regulations including risks from compliance program effectiveness, record-keeping requirements, and breach notification obligations.",
              "primary_category": "Regulatory & Compliance",
              "secondary_category": "Data & Privacy",
              "taxonomy": 1.0,
              "tertiary_category": "Compliance monitoring and reporting"
            }
          ],
          "status": "OK"
        }
        """;
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/ReferenceFilingIndexTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The filing index: three full filters over strings, a calendar-date range bound from the map
/// (D-R9), and the published sample round-tripping.
/// </summary>
public sealed class ReferenceFilingIndexTests
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
        StubHandler handler = new(Fixtures.ReferenceFilingIndex);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListFilingIndexAsync(
                cik: "0000320193",
                ticker: SetFilter.AnyOf("AAPL", "MSFT"),
                formType: RangeFilter.Between("10-K", "10-Q"),
                filingDate: RangeFilter.Gte(new LocalDate(2025, 1, 1)),
                limit: 2,
                sort: "filing_date.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/filings/vX/index"
                + "?cik=0000320193&ticker.any_of=AAPL,MSFT&form_type.gte=10-K&form_type.lte=10-Q&filing_date.gte=2025-01-01&limit=2&sort=filing_date.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceFilingIndex);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<FilingIndexEntry> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListFilingIndexAsync(ticker: "AAPL", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.True(page.HasMore);
        Assert.Equal("1daccfd9794e482e96d104dee6ed432b", page.RequestId);

        FilingIndexEntry entry = page.Results[0];
        Assert.Equal("0000320193-25-000079", entry.AccessionNumber);
        Assert.Equal("0000320193", entry.Cik);
        Assert.Equal("10-K", entry.FormType);
        Assert.Equal(new LocalDate(2025, 10, 31), entry.FilingDate);
        Assert.Equal("Apple Inc.", entry.IssuerName);
        Assert.Equal("AAPL", entry.Ticker);
        Assert.Equal("https://www.sec.gov/Archives/edgar/data/320193/0000320193-25-000079.txt", entry.FilingUrl);

        Assert.Equal("10-Q", page.Results[1].FormType);
    }

    [Fact]
    public async Task EnumerateYieldsTheFirstPageInOrder()
    {
        PagingStubHandler handler = new(Fixtures.ReferenceFilingIndex);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string?> forms = [];

        using (client)
        using (transport)
        {
            await foreach (FilingIndexEntry entry in client.Reference.EnumerateFilingIndexAsync(cancellationToken: Ct))
            {
                forms.Add(entry.FormType);

                if (forms.Count == 2)
                {
                    break;
                }
            }
        }

        Assert.Equal(["10-K", "10-Q"], forms);
        Assert.Single(handler.Requests);
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/ReferenceRiskFactorsTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Risk factors: a date field carrying the full comparator set bound from the map (D-R9), so a
/// set of calendar dates renders under <c>any_of</c>, and the published sample round-tripping
/// with no cursor.
/// </summary>
public sealed class ReferenceRiskFactorsTests
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
        StubHandler handler = new(Fixtures.ReferenceRiskFactors);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListRiskFactorsAsync(
                filingDate: SetFilter.AnyOf(new LocalDate(2025, 9, 19), new LocalDate(2025, 9, 20)),
                ticker: "MGLD",
                cik: RangeFilter.Gte("0001000000"),
                limit: 1,
                sort: "filing_date.desc",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/stocks/filings/vX/risk-factors"
                + "?filing_date.any_of=2025-09-19,2025-09-20&ticker=MGLD&cik.gte=0001000000&limit=1&sort=filing_date.desc",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceRiskFactors);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<RiskFactor> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListRiskFactorsAsync(ticker: "MGLD", cancellationToken: Ct);
        }

        RiskFactor factor = Assert.Single(page.Results);
        Assert.False(page.HasMore);
        Assert.Equal("c7856101f86c20d855b0ea1c5a6d6efa", page.RequestId);
        Assert.Equal("0001005101", factor.Cik);
        Assert.Equal("MGLD", factor.Ticker);
        Assert.Equal(new LocalDate(2025, 9, 19), factor.FilingDate);
        Assert.Equal("financial_and_market", factor.PrimaryCategory);
        Assert.Equal("credit_and_liquidity", factor.SecondaryCategory);
        Assert.Equal("access_to_capital_and_financing", factor.TertiaryCategory);
        Assert.StartsWith("In addition to the net proceeds", factor.SupportingText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnumerateStopsWhenTheSampleOffersNoCursor()
    {
        PagingStubHandler handler = new(Fixtures.ReferenceRiskFactors);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        int count = 0;

        using (client)
        using (transport)
        {
            await foreach (RiskFactor factor in client.Reference.EnumerateRiskFactorsAsync(ticker: "MGLD", cancellationToken: Ct))
            {
                count++;
            }
        }

        Assert.Equal(1, count);
        Assert.Single(handler.Requests);
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/ReferenceTaxonomiesTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The two taxonomies: one <c>taxonomy</c> field typed string with the full comparator set, the
/// other typed number with the four bounds, so the same wire name renders a string on one route
/// and a double on the other; and the published samples round-tripping, the risk-factor one
/// corrected to the number its schema and the wire carry (D-R12).
/// </summary>
public sealed class ReferenceTaxonomiesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task DisclosuresRenderAStringTaxonomyAndTheCategoryFilters()
    {
        StubHandler handler = new(Fixtures.ReferenceDisclosureTaxonomy);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListDisclosureTaxonomyAsync(
                taxonomy: RangeFilter.Gte("1.0"),
                primaryCategory: SetFilter.AnyOf("financial_results", "leadership_and_governance"),
                secondaryCategory: RangeFilter.Gte("a"),
                tertiaryCategory: "ceo_appointment",
                limit: 2,
                cancellationToken: Ct);
        }

        // taxonomy.gte carries a string here and a double on the risk-factor route below; the two
        // renderings side by side are what prove the same wire name is two element types.
        Assert.Equal(
            "https://api.massive.com/stocks/taxonomies/vX/disclosures"
                + "?taxonomy.gte=1.0&primary_category.any_of=financial_results,leadership_and_governance&secondary_category.gte=a&tertiary_category=ceo_appointment&limit=2",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DisclosuresDeserializeThePublishedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceDisclosureTaxonomy);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<DisclosureTaxonomyEntry> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListDisclosureTaxonomyAsync(cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.False(page.HasMore);

        DisclosureTaxonomyEntry entry = page.Results[0];
        Assert.Equal("1.0", entry.Taxonomy);
        Assert.Equal("leadership_and_governance", entry.PrimaryCategory);
        Assert.Equal("executive_leadership", entry.SecondaryCategory);
        Assert.Equal("ceo_appointment", entry.TertiaryCategory);
        Assert.StartsWith("New CEO appointment", entry.Description, StringComparison.Ordinal);

        Assert.Equal("quarterly_earnings", page.Results[1].TertiaryCategory);
    }

    [Fact]
    public async Task RiskFactorsRenderANumericTaxonomyRange()
    {
        StubHandler handler = new(Fixtures.ReferenceRiskFactorTaxonomy);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListRiskFactorTaxonomyAsync(
                taxonomy: RangeFilter.Between(1.0, 1.5),
                tertiaryCategory: RangeFilter.Lt("D"),
                limit: 2,
                cancellationToken: Ct);
        }

        // A double renders its shortest round-trip form, so 1.0 is "1" on the wire.
        Assert.Equal(
            "https://api.massive.com/stocks/taxonomies/vX/risk-factors?taxonomy.gte=1&taxonomy.lte=1.5&tertiary_category.lt=D&limit=2",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task RiskFactorsDeserializeTheCorrectedSample()
    {
        StubHandler handler = new(Fixtures.ReferenceRiskFactorTaxonomy);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<RiskFactorTaxonomyEntry> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListRiskFactorTaxonomyAsync(cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);
        Assert.False(page.HasMore);

        RiskFactorTaxonomyEntry entry = page.Results[0];
        Assert.Equal(1.0, entry.Taxonomy);
        Assert.Equal("Governance & Stakeholder", entry.PrimaryCategory);
        Assert.Equal("Organizational & Management", entry.SecondaryCategory);
        Assert.Equal("Performance management and accountability", entry.TertiaryCategory);
        Assert.StartsWith("Risk from inadequate performance", entry.Description, StringComparison.Ordinal);

        Assert.Equal("Data & Privacy", page.Results[1].SecondaryCategory);
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, raise the baseline:

```csharp
    private const int CoverageBaseline = 55;
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~ReferenceFilingIndex|FullyQualifiedName~ReferenceRiskFactors|FullyQualifiedName~ReferenceTaxonomies"`
Expected: the build fails with CS1061 for the methods and CS0246 for the four models.

- [ ] **Step 4: Add the model rows**

In `specs/endpoints.map.json`, append inside `models` after the `Form4Filing` row (add a comma after its closing brace):

```json
    "FilingIndexEntry": {
      "summary": "One entry in the SEC filing index: the filer, the form type, the filing date, and where the filing lives.",
      "remarks": "Reference data, so a class (decision D4). Named for its place in the index rather than <c>Filing</c>, which is the fuller SEC v1 row.",
      "schema": { "operationId": "get_stocks_filings_vX_index", "pointer": "results/items" },
      "properties": {
        "accession_number": { "name": "AccessionNumber" },
        "cik":              { "name": "Cik" },
        "ticker":           { "name": "Ticker" },
        "issuer_name":      { "name": "IssuerName" },
        "form_type":        { "name": "FormType" },
        "filing_date":      { "name": "FilingDate" },
        "filing_url":       { "name": "FilingUrl" }
      }
    },

    "RiskFactor": {
      "summary": "One classified risk factor from a filing: the filer, the filing date, the three-level category, and the text that supports the classification.",
      "remarks": "Reference data, so a class (decision D4). <see cref=\"FilingDate\"/> is a bare string in the description and an ISO calendar date on the wire, hence <see cref=\"NodaTime.LocalDate\"/> from the map (D-R9). The categories come from the taxonomy the risk-factor taxonomy route serves.",
      "schema": { "operationId": "get_stocks_filings_vX_risk-factors", "pointer": "results/items" },
      "properties": {
        "cik":                { "name": "Cik" },
        "ticker":             { "name": "Ticker" },
        "filing_date":        { "name": "FilingDate", "type": "LocalDate?", "summary": "The date the filing was submitted to the SEC. The description types this a bare string; the wire carries an ISO calendar date (D-R9)." },
        "primary_category":   { "name": "PrimaryCategory" },
        "secondary_category": { "name": "SecondaryCategory" },
        "tertiary_category":  { "name": "TertiaryCategory" },
        "supporting_text":    { "name": "SupportingText" }
      }
    },

    "DisclosureTaxonomyEntry": {
      "summary": "One category in the disclosure taxonomy: its three levels, the taxonomy version that defines it, and a description.",
      "remarks": "Reference data, so a class (decision D4). The version is a string here where the risk-factor taxonomy declares a number; each model follows its own route.",
      "schema": { "operationId": "get_stocks_taxonomies_vX_disclosures", "pointer": "results/items" },
      "properties": {
        "taxonomy":           { "name": "Taxonomy" },
        "primary_category":   { "name": "PrimaryCategory" },
        "secondary_category": { "name": "SecondaryCategory" },
        "tertiary_category":  { "name": "TertiaryCategory" },
        "description":        { "name": "Description" }
      }
    },

    "RiskFactorTaxonomyEntry": {
      "summary": "One category in the risk-factor taxonomy: its three levels, the taxonomy version that defines it, and a description.",
      "remarks": "Reference data, so a class (decision D4). The version is a number here where the disclosure taxonomy declares a string; each model follows its own route. The published example spells it as a string; the schema and the live wire carry the number.",
      "schema": { "operationId": "get_stocks_taxonomies_vX_risk-factors", "pointer": "results/items" },
      "properties": {
        "taxonomy":           { "name": "Taxonomy" },
        "primary_category":   { "name": "PrimaryCategory" },
        "secondary_category": { "name": "SecondaryCategory" },
        "tertiary_category":  { "name": "TertiaryCategory" },
        "description":        { "name": "Description" }
      }
    }
```

- [ ] **Step 5: Add the endpoint rows**

In `specs/endpoints.map.json`, append inside `endpoints` after the `get_stocks_filings_vX_form-4` row (add a comma after its closing brace):

```json
    {
      "operationId": "get_stocks_filings_vX_index",
      "group": "Reference",
      "method": "ListFilingIndex",
      "summary": "Retrieves the index of SEC filings, filtered by filer, form type, and filing date.",
      "remarks": "The route's <c>vX</c> segment marks it experimental (decision D18); opt in with <c>MASSIVE0001</c>. Every filter is optional and defaults to no constraint. <paramref name=\"filingDate\"/> is a bare string in the description whose prose says <c>YYYY-MM-DD</c>, so it binds <see cref=\"NodaTime.LocalDate\"/> from the map (D-R9).",
      "result": { "kind": "array", "model": "FilingIndexEntry", "property": "results" },
      "parameters": {
        "cik":         { "name": "cik" },
        "ticker":      { "name": "ticker" },
        "form_type":   { "name": "formType" },
        "filing_date": { "name": "filingDate", "type": "LocalDate" },
        "limit":       { "name": "limit" },
        "sort":        { "name": "sort" }
      }
    },
    {
      "operationId": "get_stocks_filings_vX_risk-factors",
      "group": "Reference",
      "method": "ListRiskFactors",
      "summary": "Retrieves classified risk factors from filings, filtered by filing date and filer.",
      "remarks": "The route's <c>vX</c> segment marks it experimental (decision D18); opt in with <c>MASSIVE0001</c>. Every filter is optional and defaults to no constraint. <paramref name=\"filingDate\"/> is a bare string in the description whose prose says <c>YYYY-MM-DD</c>, so it binds <see cref=\"NodaTime.LocalDate\"/> from the map (D-R9); it carries the full comparator set, so a set of dates is accepted too.",
      "result": { "kind": "array", "model": "RiskFactor", "property": "results" },
      "parameters": {
        "filing_date": { "name": "filingDate", "type": "LocalDate" },
        "ticker":      { "name": "ticker" },
        "cik":         { "name": "cik" },
        "limit":       { "name": "limit" },
        "sort":        { "name": "sort" }
      }
    },
    {
      "operationId": "get_stocks_taxonomies_vX_disclosures",
      "group": "Reference",
      "method": "ListDisclosureTaxonomy",
      "summary": "Retrieves the disclosure taxonomy: the categories 8-K disclosures are classified into, filtered by version and by level.",
      "remarks": "The route's <c>vX</c> segment marks it experimental (decision D18); opt in with <c>MASSIVE0001</c>. Every filter is optional and defaults to no constraint. <paramref name=\"taxonomy\"/> is the version, a string such as <c>1.0</c> on this route.",
      "result": { "kind": "array", "model": "DisclosureTaxonomyEntry", "property": "results" },
      "parameters": {
        "taxonomy":           { "name": "taxonomy" },
        "primary_category":   { "name": "primaryCategory" },
        "secondary_category": { "name": "secondaryCategory" },
        "tertiary_category":  { "name": "tertiaryCategory" },
        "limit":              { "name": "limit" },
        "sort":               { "name": "sort" }
      }
    },
    {
      "operationId": "get_stocks_taxonomies_vX_risk-factors",
      "group": "Reference",
      "method": "ListRiskFactorTaxonomy",
      "summary": "Retrieves the risk-factor taxonomy: the categories risk factors are classified into, filtered by version and by level.",
      "remarks": "The route's <c>vX</c> segment marks it experimental (decision D18); opt in with <c>MASSIVE0001</c>. Every filter is optional and defaults to no constraint. <paramref name=\"taxonomy\"/> is the version, a number on this route where the disclosure taxonomy takes a string.",
      "result": { "kind": "array", "model": "RiskFactorTaxonomyEntry", "property": "results" },
      "parameters": {
        "taxonomy":           { "name": "taxonomy" },
        "primary_category":   { "name": "primaryCategory" },
        "secondary_category": { "name": "secondaryCategory" },
        "tertiary_category":  { "name": "tertiaryCategory" },
        "limit":              { "name": "limit" },
        "sort":               { "name": "sort" }
      }
    }
```

- [ ] **Step 6: Regenerate**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: `FilingIndexEntry.g.cs` and `RiskFactor.g.cs` with `using NodaTime;`, the two taxonomy models without; `DisclosureTaxonomyEntry` with `public required string Taxonomy { get; init; }` and `RiskFactorTaxonomyEntry` with `public double Taxonomy { get; init; }`; `ListFilingIndexAsync` with three `Filter<string>?` and a `RangeFilter<LocalDate>? filingDate`; `ListRiskFactorsAsync` with `Filter<LocalDate>? filingDate` first; `ListDisclosureTaxonomyAsync` with `Filter<string>? taxonomy` and `ListRiskFactorTaxonomyAsync` with `RangeFilter<double>? taxonomy`; `Enumerate` counterparts; `[Experimental]` on all eight.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS; `EndpointCoverageTests` reports 55 mapped.

- [ ] **Step 8: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff.

- [ ] **Step 9: Record the departure in the spec**

In `docs/superpowers/specs/2026-09-03-reference-group-design.md`, in section `### D-R12 · Fixtures: published examples, captured where none exists`, append this sentence to the end of the paragraph that ends "departing only as D-R10 says.":

```markdown
One further departure: the risk-factor taxonomy example spells `taxonomy` as the string `"1.0"`
where the schema declares a required number and the live wire (2026-09-03) sends `1.0`; the
fixture follows the schema and the wire, commented.
```

- [ ] **Step 10: Commit**

```bash
git add specs/endpoints.map.json src/MassiveDotNet.Rest/Generated tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/ReferenceFilingIndexTests.cs tests/MassiveDotNet.Rest.Tests/ReferenceRiskFactorsTests.cs tests/MassiveDotNet.Rest.Tests/ReferenceTaxonomiesTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs docs/superpowers/specs/2026-09-03-reference-group-design.md
git commit -m "feat: map the filing index, risk factors, and both taxonomies under Reference

The same taxonomy wire name renders a string on one route and a double
on the other, as each description declares; the risk-factor fixture
follows the schema and the wire where the example's string would not
deserialize (D-R12). Coverage reaches 55.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 9: Financials: four dictionaries of one data point

**Files:**
- Modify: `tests/MassiveDotNet.CodeGen.Tests/ModelBindingTests.cs`
- Modify: `specs/endpoints.map.json` (three model rows, one endpoint row)
- Regenerate: `src/MassiveDotNet.Rest/Generated/`
- Modify: `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/ReferenceFinancialsTests.cs`
- Modify: `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs` (`CoverageBaseline` 55 → 56)

**Interfaces:**
- Consumes: `Emitter.PropertyType`, whose `mapped?.Type` branch returns a row's verbatim type before `TypeBinding.NeedsModel` is consulted; `Spec.Navigate`, which reads `*` as an ordinary property name; the existing D-N6 test `AFreeFormObjectTakesAMapType` in `ModelBindingTests` as the pattern.
- Produces: `client.Reference.ListFinancialsAsync(string? ticker = null, string? cik = null, string? companyName = null, string? sic = null, RangeFilter<LocalDate>? filingDate = null, RangeFilter<LocalDate>? periodOfReportDate = null, string? timeframe = null, bool? includeSources = null, string? companyNameSearch = null, SortOrder? order = null, int? limit = null, string? sort = null, CancellationToken cancellationToken = default)` returning `Task<MassivePage<FinancialReport>>` with `EnumerateFinancialsAsync`, `[Experimental]`. Models: `FinancialReport` (`required string Cik`, `required string CompanyName`, `string[]? Tickers`, `string? Sic`, `string? FiscalYear`, `required string FiscalPeriod`, `required string Timeframe`, `LocalDate? StartDate`, `LocalDate? EndDate`, `LocalDate? FilingDate`, `string? AcceptanceTimestamp`, `string? SourceFilingUrl`, `string? SourceFilingFileUrl`, `required FinancialStatements Financials`); `FinancialStatements` (`Dictionary<string, FinancialDataPoint>? BalanceSheet`, `? CashFlowStatement`, `? ComprehensiveIncome`, `? IncomeStatement`); `FinancialDataPoint` (`required string Label`, `double Value`, `required string Unit`, `int Order`, `string? Source`, `string[]? DerivedFrom`, `string? Formula`, `string? XPath`). No partials. Tasks 10 and 11 call this.

- [ ] **Step 1: Pin the generator behaviour the map relies on**

Append to `tests/MassiveDotNet.CodeGen.Tests/ModelBindingTests.cs`, inside the class at the end:

```csharp
    [Fact]
    public void AVerbatimTypeOnAnObjectWithDeclaredPropertiesIsTakenAsWritten()
    {
        // The financials balance sheet declares one property literally named "*", the
        // description's way of documenting a data point shape under any key. The row's type wins
        // before the site is asked whether it needs a model, and the "*" pointer reaches the shape
        // as an ordinary property name (D-R11). D-N6 spoke only of free-form objects.
        string spec = Document("""
            {
              "type": "object",
              "properties": {
                "financials": {
                  "type": "object",
                  "properties": {
                    "balance_sheet": {
                      "type": "object",
                      "properties": {
                        "*": {
                          "type": "object",
                          "required": ["label", "value"],
                          "properties": {
                            "label": { "type": "string" },
                            "value": { "type": "number" }
                          }
                        }
                      }
                    },
                    "income_statement": { "type": "object" }
                  }
                }
              }
            }
            """);

        Dictionary<string, string> files = Harness.Generate(spec, MapDocument(
            """{ "financials": { "name": "Financials", "model": "Statements" } }""",
            """
            "Statements": {
              "schema": { "operationId": "ListThings", "pointer": "results/items/financials" },
              "properties": {
                "balance_sheet":    { "name": "BalanceSheet", "type": "Dictionary<string, Point>?" },
                "income_statement": { "name": "IncomeStatement", "type": "Dictionary<string, Point>?" }
              }
            },
            "Point": { "schema": { "operationId": "ListThings", "pointer": "results/items/financials/balance_sheet/*" } }
            """));

        string statements = files[Path.Combine("Models", "Statements.g.cs")];
        Assert.Contains("public Dictionary<string, Point>? BalanceSheet { get; init; }", statements, StringComparison.Ordinal);
        Assert.Contains("public Dictionary<string, Point>? IncomeStatement { get; init; }", statements, StringComparison.Ordinal);

        string point = files[Path.Combine("Models", "Point.g.cs")];
        Assert.Contains("public required string Label { get; init; }", point, StringComparison.Ordinal);
        Assert.Contains("public double Value { get; init; }", point, StringComparison.Ordinal);
    }
```

Run: `dotnet test tests/MassiveDotNet.CodeGen.Tests --filter "FullyQualifiedName~AVerbatimTypeOnAnObjectWithDeclaredProperties"`
Expected: PASS on the first run. This is a pin, not a red-green cycle: the spec asks for the test because D-N6 documented only the free-form case, and the emitter already takes the row's type first. If it fails instead, the fix is in `tools/MassiveDotNet.CodeGen/Emitter.cs`, `PropertyType`: the `if (mapped?.Type is { } type) return type;` branch must come before the `TypeBinding.NeedsModel` check; and in `Spec.Navigate`, which must find `*` through `Properties` like any other segment. Make that change, re-run, and say so in the report.

- [ ] **Step 2: Add the fixture**

Append to `tests/MassiveDotNet.Rest.Tests/Fixtures.cs`, after the `ReferenceRiskFactorTaxonomy` member:

```csharp
    /// <summary>
    /// The documented sample for GET /vX/reference/financials: one quarterly report with four
    /// statements, each a dictionary of data points keyed by the platform's field names (D-R11).
    /// Its <c>next_url</c> is the route with an empty query, which is still a cursor.
    /// </summary>
    /// <remarks>
    /// One departure from the published text: <c>"timeframe": "quarterly"</c> is added. The
    /// description marks <c>timeframe</c> required and the live service sends it, but the
    /// published example omits it, and a required property that never arrives fails
    /// deserialization outright (D-R12). <c>tickers</c> is also absent from the example and is
    /// left absent, since the description makes it optional; the live tier asserts it arrives.
    /// </remarks>
    public const string ReferenceFinancials = """
        {
          "count": 1,
          "next_url": "https://api.massive.com/vX/reference/financials?",
          "request_id": "55eb92ed43b25568ab0cce159830ea34",
          "results": [
            {
              "cik": "0001650729",
              "company_name": "SiteOne Landscape Supply, Inc.",
              "end_date": "2022-04-03",
              "filing_date": "2022-05-04",
              "financials": {
                "balance_sheet": {
                  "assets": {
                    "label": "Assets",
                    "order": 100,
                    "unit": "USD",
                    "value": 2407400000
                  },
                  "current_assets": {
                    "label": "Current Assets",
                    "order": 200,
                    "unit": "USD",
                    "value": 1385900000
                  },
                  "current_liabilities": {
                    "label": "Current Liabilities",
                    "order": 700,
                    "unit": "USD",
                    "value": 597500000
                  },
                  "equity": {
                    "label": "Equity",
                    "order": 1400,
                    "unit": "USD",
                    "value": 1099200000
                  },
                  "equity_attributable_to_noncontrolling_interest": {
                    "label": "Equity Attributable To Noncontrolling Interest",
                    "order": 1500,
                    "unit": "USD",
                    "value": 0
                  },
                  "equity_attributable_to_parent": {
                    "label": "Equity Attributable To Parent",
                    "order": 1600,
                    "unit": "USD",
                    "value": 1099200000
                  },
                  "liabilities": {
                    "label": "Liabilities",
                    "order": 600,
                    "unit": "USD",
                    "value": 1308200000
                  },
                  "liabilities_and_equity": {
                    "label": "Liabilities And Equity",
                    "order": 1900,
                    "unit": "USD",
                    "value": 2407400000
                  },
                  "noncurrent_assets": {
                    "label": "Noncurrent Assets",
                    "order": 300,
                    "unit": "USD",
                    "value": 1021500000
                  },
                  "noncurrent_liabilities": {
                    "label": "Noncurrent Liabilities",
                    "order": 800,
                    "unit": "USD",
                    "value": 710700000
                  }
                },
                "cash_flow_statement": {
                  "exchange_gains_losses": {
                    "label": "Exchange Gains/Losses",
                    "order": 1000,
                    "unit": "USD",
                    "value": 100000
                  },
                  "net_cash_flow": {
                    "label": "Net Cash Flow",
                    "order": 1100,
                    "unit": "USD",
                    "value": -8600000
                  },
                  "net_cash_flow_continuing": {
                    "label": "Net Cash Flow, Continuing",
                    "order": 1200,
                    "unit": "USD",
                    "value": -8700000
                  },
                  "net_cash_flow_from_financing_activities": {
                    "label": "Net Cash Flow From Financing Activities",
                    "order": 700,
                    "unit": "USD",
                    "value": 150600000
                  },
                  "net_cash_flow_from_financing_activities_continuing": {
                    "label": "Net Cash Flow From Financing Activities, Continuing",
                    "order": 800,
                    "unit": "USD",
                    "value": 150600000
                  },
                  "net_cash_flow_from_investing_activities": {
                    "label": "Net Cash Flow From Investing Activities",
                    "order": 400,
                    "unit": "USD",
                    "value": -41000000
                  },
                  "net_cash_flow_from_investing_activities_continuing": {
                    "label": "Net Cash Flow From Investing Activities, Continuing",
                    "order": 500,
                    "unit": "USD",
                    "value": -41000000
                  },
                  "net_cash_flow_from_operating_activities": {
                    "label": "Net Cash Flow From Operating Activities",
                    "order": 100,
                    "unit": "USD",
                    "value": -118300000
                  },
                  "net_cash_flow_from_operating_activities_continuing": {
                    "label": "Net Cash Flow From Operating Activities, Continuing",
                    "order": 200,
                    "unit": "USD",
                    "value": -118300000
                  }
                },
                "comprehensive_income": {
                  "comprehensive_income_loss": {
                    "label": "Comprehensive Income/Loss",
                    "order": 100,
                    "unit": "USD",
                    "value": 40500000
                  },
                  "comprehensive_income_loss_attributable_to_noncontrolling_interest": {
                    "label": "Comprehensive Income/Loss Attributable To Noncontrolling Interest",
                    "order": 200,
                    "unit": "USD",
                    "value": 0
                  },
                  "comprehensive_income_loss_attributable_to_parent": {
                    "label": "Comprehensive Income/Loss Attributable To Parent",
                    "order": 300,
                    "unit": "USD",
                    "value": 40500000
                  },
                  "other_comprehensive_income_loss": {
                    "label": "Other Comprehensive Income/Loss",
                    "order": 400,
                    "unit": "USD",
                    "value": 40500000
                  },
                  "other_comprehensive_income_loss_attributable_to_parent": {
                    "label": "Other Comprehensive Income/Loss Attributable To Parent",
                    "order": 600,
                    "unit": "USD",
                    "value": 8200000
                  }
                },
                "income_statement": {
                  "basic_earnings_per_share": {
                    "label": "Basic Earnings Per Share",
                    "order": 4200,
                    "unit": "USD / shares",
                    "value": 0.72
                  },
                  "benefits_costs_expenses": {
                    "label": "Benefits Costs and Expenses",
                    "order": 200,
                    "unit": "USD",
                    "value": 768400000
                  },
                  "cost_of_revenue": {
                    "label": "Cost Of Revenue",
                    "order": 300,
                    "unit": "USD",
                    "value": 536100000
                  },
                  "costs_and_expenses": {
                    "label": "Costs And Expenses",
                    "order": 600,
                    "unit": "USD",
                    "value": 768400000
                  },
                  "diluted_earnings_per_share": {
                    "label": "Diluted Earnings Per Share",
                    "order": 4300,
                    "unit": "USD / shares",
                    "value": 0.7
                  },
                  "gross_profit": {
                    "label": "Gross Profit",
                    "order": 800,
                    "unit": "USD",
                    "value": 269200000
                  },
                  "income_loss_from_continuing_operations_after_tax": {
                    "label": "Income/Loss From Continuing Operations After Tax",
                    "order": 1400,
                    "unit": "USD",
                    "value": 32300000
                  },
                  "income_loss_from_continuing_operations_before_tax": {
                    "label": "Income/Loss From Continuing Operations Before Tax",
                    "order": 1500,
                    "unit": "USD",
                    "value": 36900000
                  },
                  "income_tax_expense_benefit": {
                    "label": "Income Tax Expense/Benefit",
                    "order": 2200,
                    "unit": "USD",
                    "value": 4600000
                  },
                  "interest_expense_operating": {
                    "label": "Interest Expense, Operating",
                    "order": 2700,
                    "unit": "USD",
                    "value": 4300000
                  },
                  "net_income_loss": {
                    "label": "Net Income/Loss",
                    "order": 3200,
                    "unit": "USD",
                    "value": 32300000
                  },
                  "net_income_loss_attributable_to_noncontrolling_interest": {
                    "label": "Net Income/Loss Attributable To Noncontrolling Interest",
                    "order": 3300,
                    "unit": "USD",
                    "value": 0
                  },
                  "net_income_loss_attributable_to_parent": {
                    "label": "Net Income/Loss Attributable To Parent",
                    "order": 3500,
                    "unit": "USD",
                    "value": 32300000
                  },
                  "net_income_loss_available_to_common_stockholders_basic": {
                    "label": "Net Income/Loss Available To Common Stockholders, Basic",
                    "order": 3700,
                    "unit": "USD",
                    "value": 32300000
                  },
                  "operating_expenses": {
                    "label": "Operating Expenses",
                    "order": 1000,
                    "unit": "USD",
                    "value": 228000000
                  },
                  "operating_income_loss": {
                    "label": "Operating Income/Loss",
                    "order": 1100,
                    "unit": "USD",
                    "value": 41200000
                  },
                  "participating_securities_distributed_and_undistributed_earnings_loss_basic": {
                    "label": "Participating Securities, Distributed And Undistributed Earnings/Loss, Basic",
                    "order": 3800,
                    "unit": "USD",
                    "value": 0
                  },
                  "preferred_stock_dividends_and_other_adjustments": {
                    "label": "Preferred Stock Dividends And Other Adjustments",
                    "order": 3900,
                    "unit": "USD",
                    "value": 0
                  },
                  "revenues": {
                    "label": "Revenues",
                    "order": 100,
                    "unit": "USD",
                    "value": 805300000
                  }
                }
              },
              "fiscal_period": "Q1",
              "fiscal_year": "2022",
              "source_filing_file_url": "https://api.massive.com/v1/reference/sec/filings/0001650729-22-000010/files/site-20220403_htm.xml",
              "source_filing_url": "https://api.massive.com/v1/reference/sec/filings/0001650729-22-000010",
              "start_date": "2022-01-03",
              "timeframe": "quarterly"
            }
          ],
          "status": "OK"
        }
        """;
```

- [ ] **Step 3: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/ReferenceFinancialsTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Financials: the plain and searched company names render under their wire names, the
/// calendar-date ranges the description types render, and the published sample deserializes
/// four dictionaries of one data point model with every line item reachable by its key (D-R11).
/// </summary>
/// <remarks>
/// The published sample omits <c>tickers</c>, which the description makes optional, so
/// <see cref="DeserializesFourDictionariesOfDataPoints"/> asserts it null; the live tier asserts
/// it arrives. The sample also omits the required <c>timeframe</c>, which the fixture supplies
/// (D-R12), because a required property that never arrives fails deserialization.
/// </remarks>
public sealed class ReferenceFinancialsTests
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
        StubHandler handler = new(Fixtures.ReferenceFinancials);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListFinancialsAsync(
                ticker: "SITE",
                cik: "0001650729",
                companyName: "SiteOne",
                sic: "5070",
                filingDate: RangeFilter.Between(new LocalDate(2022, 1, 1), new LocalDate(2022, 12, 31)),
                periodOfReportDate: RangeFilter.Gte(new LocalDate(2022, 1, 1)),
                timeframe: "quarterly",
                includeSources: true,
                companyNameSearch: "Site",
                order: SortOrder.Descending,
                limit: 1,
                sort: "filing_date",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/vX/reference/financials"
                + "?ticker=SITE&cik=0001650729&company_name=SiteOne&sic=5070"
                + "&filing_date.gte=2022-01-01&filing_date.lte=2022-12-31"
                + "&period_of_report_date.gte=2022-01-01"
                + "&timeframe=quarterly&include_sources=true&company_name.search=Site"
                + "&order=desc&limit=1&sort=filing_date",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesFourDictionariesOfDataPoints()
    {
        StubHandler handler = new(Fixtures.ReferenceFinancials);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<FinancialReport> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListFinancialsAsync(ticker: "SITE", limit: 1, cancellationToken: Ct);
        }

        FinancialReport report = Assert.Single(page.Results);
        Assert.True(page.HasMore);
        Assert.Equal("55eb92ed43b25568ab0cce159830ea34", page.RequestId);

        Assert.Equal("0001650729", report.Cik);
        Assert.Equal("SiteOne Landscape Supply, Inc.", report.CompanyName);
        Assert.Equal("Q1", report.FiscalPeriod);
        Assert.Equal("2022", report.FiscalYear);
        Assert.Equal(new LocalDate(2022, 1, 3), report.StartDate);
        Assert.Equal(new LocalDate(2022, 4, 3), report.EndDate);
        Assert.Equal(new LocalDate(2022, 5, 4), report.FilingDate);
        Assert.Equal("quarterly", report.Timeframe);
        Assert.Null(report.Tickers);
        Assert.Null(report.AcceptanceTimestamp);
        Assert.Equal("https://api.massive.com/v1/reference/sec/filings/0001650729-22-000010", report.SourceFilingUrl);

        FinancialStatements statements = report.Financials;
        Assert.NotNull(statements.BalanceSheet);
        Assert.NotNull(statements.CashFlowStatement);
        Assert.NotNull(statements.ComprehensiveIncome);
        Assert.NotNull(statements.IncomeStatement);
        Assert.Equal(10, statements.BalanceSheet.Count);
        Assert.Equal(9, statements.CashFlowStatement.Count);
        Assert.Equal(5, statements.ComprehensiveIncome.Count);
        Assert.Equal(19, statements.IncomeStatement.Count);

        FinancialDataPoint assets = statements.BalanceSheet["assets"];
        Assert.Equal("Assets", assets.Label);
        Assert.Equal(2407400000d, assets.Value);
        Assert.Equal("USD", assets.Unit);
        Assert.Equal(100, assets.Order);
        Assert.Null(assets.Source);
        Assert.Null(assets.Formula);
        Assert.Null(assets.XPath);
        Assert.Null(assets.DerivedFrom);

        Assert.Equal(0.72, statements.IncomeStatement["basic_earnings_per_share"].Value);
        Assert.Equal("USD / shares", statements.IncomeStatement["basic_earnings_per_share"].Unit);
        Assert.Equal(-8600000d, statements.CashFlowStatement["net_cash_flow"].Value);
        Assert.Equal(40500000d, statements.ComprehensiveIncome["comprehensive_income_loss"].Value);
    }

    [Fact]
    public async Task EnumerateFollowsTheSampleCursorThenStops()
    {
        // The sample's cursor is the route with an empty query, which is still a same-origin URI
        // the traversal follows verbatim (D14); the stub serves the same page again, so the test
        // stops the traversal itself after the seam it set out to cross.
        PagingStubHandler handler = new(Fixtures.ReferenceFinancials);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<string> periods = [];

        using (client)
        using (transport)
        {
            await foreach (FinancialReport report in client.Reference.EnumerateFinancialsAsync(ticker: "SITE", limit: 1, cancellationToken: Ct))
            {
                periods.Add(report.FiscalPeriod);

                if (periods.Count == 2)
                {
                    break;
                }
            }
        }

        Assert.Equal(["Q1", "Q1"], periods);
        Assert.Equal(2, handler.Requests.Count);
        Assert.StartsWith("https://api.massive.com/vX/reference/financials", handler.Requests[1].ToString(), StringComparison.Ordinal);
    }
}
```

In `tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs`, raise the baseline:

```csharp
    private const int CoverageBaseline = 56;
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~ReferenceFinancials"`
Expected: the build fails with CS1061 for the methods and CS0246 for the three models.

- [ ] **Step 5: Add the model rows**

In `specs/endpoints.map.json`, append inside `models` after the `RiskFactorTaxonomyEntry` row (add a comma after its closing brace):

```json
    "FinancialReport": {
      "summary": "One set of financial statements for a company and period, derived from an SEC filing: the company, the fiscal period, the dates, the source filing, and the four statements.",
      "remarks": "Reference data, so a class (decision D4). The three period dates are bare strings whose prose claims a compact <c>YYYYMMDD</c> form, while both the published example and the live wire carry ISO calendar dates; the map follows the example and the wire, which agree against the prose, and binds <see cref=\"NodaTime.LocalDate\"/> (D-R9). This is the opposite reading from the SEC v1 filings, whose wire really is compact, so the two families differ on purpose. The published example omits the required <c>timeframe</c> and the optional <c>tickers</c>, both of which the live wire sends.",
      "schema": { "operationId": "ListFinancials", "pointer": "results/items" },
      "properties": {
        "cik":                    { "name": "Cik" },
        "company_name":           { "name": "CompanyName" },
        "tickers":                { "name": "Tickers" },
        "sic":                    { "name": "Sic" },
        "fiscal_year":            { "name": "FiscalYear" },
        "fiscal_period":          { "name": "FiscalPeriod" },
        "timeframe":              { "name": "Timeframe" },
        "start_date":             { "name": "StartDate", "type": "LocalDate?", "summary": "The first day of the period the statements cover. The description types this a bare string; the wire carries an ISO calendar date (D-R9)." },
        "end_date":               { "name": "EndDate", "type": "LocalDate?", "summary": "The last day of the period the statements cover. The description types this a bare string; the wire carries an ISO calendar date (D-R9)." },
        "filing_date":            { "name": "FilingDate", "type": "LocalDate?", "summary": "The date the source filing was made available, which is not necessarily the date its contents became public. The description types this a bare string; the wire carries an ISO calendar date (D-R9)." },
        "acceptance_datetime":    { "name": "AcceptanceTimestamp", "summary": "When EDGAR accepted the source filing, as the wire sends it. The description says the compact Eastern-time form, <c>yyyyMMddHHmmss</c>; the live wire sent RFC 3339 on 2026-09-03. Kept a string so either form deserializes." },
        "source_filing_url":      { "name": "SourceFilingUrl" },
        "source_filing_file_url": { "name": "SourceFilingFileUrl" },
        "financials":             { "name": "Financials", "model": "FinancialStatements" }
      }
    },

    "FinancialStatements": {
      "summary": "The four statements of a financial report, each a dictionary of data points keyed by the platform's field name.",
      "remarks": "The description documents the balance sheet's shape under a property literally named <c>*</c> and leaves the other three free-form; all four are one dictionary type over <see cref=\"FinancialDataPoint\"/>, and a statement the report lacks is null (D-R11). The keys are the field names Massive documents for each statement, such as <c>assets</c> or <c>revenues</c>.",
      "schema": { "operationId": "ListFinancials", "pointer": "results/items/financials" },
      "properties": {
        "balance_sheet":        { "name": "BalanceSheet", "type": "Dictionary<string, FinancialDataPoint>?", "summary": "The balance sheet: one data point per line item, keyed by the platform's field name, such as <c>assets</c> (D-R11)." },
        "cash_flow_statement":  { "name": "CashFlowStatement", "type": "Dictionary<string, FinancialDataPoint>?", "summary": "The cash flow statement: one data point per line item, keyed by the platform's field name, such as <c>net_cash_flow</c> (D-R11)." },
        "comprehensive_income": { "name": "ComprehensiveIncome", "type": "Dictionary<string, FinancialDataPoint>?", "summary": "The statement of comprehensive income: one data point per line item, keyed by the platform's field name, such as <c>comprehensive_income_loss</c> (D-R11)." },
        "income_statement":     { "name": "IncomeStatement", "type": "Dictionary<string, FinancialDataPoint>?", "summary": "The income statement: one data point per line item, keyed by the platform's field name, such as <c>revenues</c> (D-R11)." }
      }
    },

    "FinancialDataPoint": {
      "summary": "One line item of a financial statement: its label, value, and unit, its place in the statement, and, when sources are requested, where it came from.",
      "remarks": "Generated from the shape the description documents under the balance sheet's <c>*</c> property and used for every statement (D-R11). <see cref=\"Source\"/>, <see cref=\"Formula\"/>, <see cref=\"XPath\"/>, and <see cref=\"DerivedFrom\"/> arrive only when the request asked for sources.",
      "schema": { "operationId": "ListFinancials", "pointer": "results/items/financials/balance_sheet/*" },
      "properties": {
        "label":        { "name": "Label" },
        "value":        { "name": "Value" },
        "unit":         { "name": "Unit" },
        "order":        { "name": "Order" },
        "source":       { "name": "Source" },
        "derived_from": { "name": "DerivedFrom" },
        "formula":      { "name": "Formula" },
        "xpath":        { "name": "XPath" }
      }
    }
```

- [ ] **Step 6: Add the endpoint row**

In `specs/endpoints.map.json`, append inside `endpoints` after the `get_stocks_taxonomies_vX_risk-factors` row (add a comma after its closing brace):

```json
    {
      "operationId": "ListFinancials",
      "group": "Reference",
      "method": "ListFinancials",
      "summary": "Retrieves financial statements derived from SEC filings, filtered by company, by filing and report dates, and by timeframe.",
      "remarks": "The route's <c>vX</c> segment marks it experimental (decision D18); opt in with <c>MASSIVE0001</c>. Every filter is optional and defaults to no constraint. <paramref name=\"timeframe\"/> is <c>annual</c>, <c>quarterly</c>, or <c>ttm</c>. <paramref name=\"includeSources\"/> asks for the <see cref=\"FinancialDataPoint.XPath\"/> and <see cref=\"FinancialDataPoint.Formula\"/> of every data point. <paramref name=\"companyNameSearch\"/> is a text search where <paramref name=\"companyName\"/> is an exact match; both render under the description's wire names.",
      "result": { "kind": "array", "model": "FinancialReport", "property": "results" },
      "parameters": {
        "ticker":                { "name": "ticker" },
        "cik":                   { "name": "cik" },
        "company_name":          { "name": "companyName" },
        "sic":                   { "name": "sic" },
        "filing_date":           { "name": "filingDate" },
        "period_of_report_date": { "name": "periodOfReportDate" },
        "timeframe":             { "name": "timeframe" },
        "include_sources":       { "name": "includeSources" },
        "company_name.search":   { "name": "companyNameSearch" },
        "order":                 { "name": "order", "type": "SortOrder" },
        "limit":                 { "name": "limit" },
        "sort":                  { "name": "sort" }
      }
    }
```

The `filing_date` and `period_of_report_date` parameters carry `format: date` on this route, so their filters bind `RangeFilter<LocalDate>` with no `type` on the row.

- [ ] **Step 7: Regenerate**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen`
Expected: `FinancialReport.g.cs` with `using NodaTime;`, `FinancialStatements.g.cs` with four `Dictionary<string, FinancialDataPoint>?` properties, `FinancialDataPoint.g.cs` with `required string Label`, `double Value`, `required string Unit`, `int Order`; `ListFinancialsAsync` with two `RangeFilter<LocalDate>?` parameters, `bool? includeSources`, and `builder.AppendQuery("company_name.search", companyNameSearch);`; `EnumerateFinancialsAsync`; `[Experimental]` on both.

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS; `EndpointCoverageTests` reports 56 mapped, the number D-R1 ends at.

- [ ] **Step 9: Prove the generator is deterministic**

Run: `dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/`
Expected: no diff.

- [ ] **Step 10: Record the second fixture departure in the spec**

In `docs/superpowers/specs/2026-09-03-reference-group-design.md`, in section `### D-R12 · Fixtures: published examples, captured where none exists`, append this sentence after the one Task 8 added:

```markdown
The financials example omits `timeframe`, which the description marks required and the live wire
sends, so the fixture supplies it: a required property that never arrives fails deserialization
outright.
```

- [ ] **Step 11: Commit**

```bash
git add tests/MassiveDotNet.CodeGen.Tests/ModelBindingTests.cs specs/endpoints.map.json src/MassiveDotNet.Rest/Generated tests/MassiveDotNet.Rest.Tests/Fixtures.cs tests/MassiveDotNet.Rest.Tests/ReferenceFinancialsTests.cs tests/MassiveDotNet.Rest.Tests/EndpointCoverageTests.cs docs/superpowers/specs/2026-09-03-reference-group-design.md
git commit -m "feat: map financials under Reference as four dictionaries of one data point

The balance sheet's shape, documented under a property named '*', is
generated once and used for every statement through a verbatim
dictionary type on each row; a generator test pins that a typed row on
an object with declared properties is taken as written (D-R11).
Coverage reaches 56, the number D-R1 ends at.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 10: Root the new instantiations in the AOT smoke test and record D25

**Files:**
- Modify: `samples/MassiveDotNet.AotSmokeTest/MassiveDotNet.AotSmokeTest.csproj`
- Modify: `samples/MassiveDotNet.AotSmokeTest/Program.cs`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: `ListFinancialsAsync` (Task 9), `DownloadFilingFileAsync` (Task 4). The financials route is experimental, so the sample project needs its own `NoWarn`.
- Produces: nothing new. The publish is the proof for rules 3 and 4.

The AOT smoke test is the enforcement mechanism for rules 3 and 4, and an unreferenced generic instantiation is simply trimmed away. `Dictionary<string, FinancialDataPoint>` is the first dictionary-of-models this SDK deserializes, and its converter is reached only through the generated context; the download is the first response body this SDK reads without deserializing at all. Neither is covered by anything already in the sample.

- [ ] **Step 1: Let the sample opt into the experimental route**

In `samples/MassiveDotNet.AotSmokeTest/MassiveDotNet.AotSmokeTest.csproj`, add a property group after the existing one:

```xml
  <PropertyGroup>
    <!--
      The financials route carries a vX segment, so it ships [Experimental] (D18). It is called
      here on purpose: its payload is the SDK's only dictionary of nested models, and an
      instantiation nothing reaches is trimmed away, which would make a clean publish prove
      nothing about it. The suppression is scoped to this project, as the constitution's Stability
      convention allows; nothing is ever suppressed inside generated code.
    -->
    <NoWarn>$(NoWarn);MASSIVE0001</NoWarn>
  </PropertyGroup>
```

- [ ] **Step 2: Add the two calls to the sample**

In `samples/MassiveDotNet.AotSmokeTest/Program.cs`, insert the following after the options-contracts block (after the `if (contracts.Results is not [{ AdditionalUnderlyings: null }, ...]) { ... }` statement) and before `Console.WriteLine($"\nrequests: {handler.Requests}");`:

```csharp
// The financials page is the SDK's only dictionary of nested models, so its converter and the
// Dictionary<string, FinancialDataPoint> instantiation are reachable only through this call; the
// download is the only response body read without deserializing, so it is the only thing that
// roots the transport's copy path. A clean publish says nothing about either otherwise.
Console.WriteLine("\nfinancials, one quarterly report:");

MassivePage<FinancialReport> financials = await client.Reference.ListFinancialsAsync(
    ticker: "SITE",
    timeframe: "quarterly",
    includeSources: false,
    limit: 1);

Console.WriteLine($"request : {handler.LastRequestUri}");

foreach (FinancialReport report in financials.Results)
{
    Console.WriteLine(
        $"  {report.CompanyName}  {report.FiscalPeriod} {report.FiscalYear} ({report.Timeframe})"
        + $"  assets {report.Financials.BalanceSheet?["assets"].Value,18:N0}"
        + $"  revenues {report.Financials.IncomeStatement?["revenues"].Value,18:N0}");
}

const string ExpectedFinancialsQuery = "?ticker=SITE&timeframe=quarterly&include_sources=false&limit=1";

if (handler.LastRequestUri?.Query != ExpectedFinancialsQuery)
{
    Console.Error.WriteLine($"FAIL: expected the financials query {ExpectedFinancialsQuery}; got {handler.LastRequestUri?.Query}.");
    return 1;
}

if (financials.Results is not [{ FiscalPeriod: "Q1", Timeframe: "quarterly" } report0]
    || report0.StartDate != new LocalDate(2022, 1, 3)
    || report0.Financials.BalanceSheet is not { Count: 2 } balanceSheet
    || report0.Financials.IncomeStatement is not { Count: 1 } incomeStatement
    || balanceSheet["assets"] is not { Label: "Assets", Unit: "USD", Value: 2407400000d }
    || incomeStatement["revenues"].Value != 805300000d)
{
    Console.Error.WriteLine("FAIL: expected one quarterly Q1 report whose balance sheet holds two data points, assets at 2,407,400,000.");
    return 1;
}

Console.WriteLine("\ndownloading one filing file:");

using MemoryStream file = new();

await client.Reference.DownloadFilingFileAsync("0001683168-26-006873", "myx_i10k-053126.htm", file);

Console.WriteLine($"request : {handler.LastRequestUri}");
Console.WriteLine($"  {file.Length} bytes, starting {Encoding.UTF8.GetString(file.ToArray(), 0, 14)}");

if (file.Length == 0 || !Encoding.UTF8.GetString(file.ToArray()).StartsWith("<html", StringComparison.Ordinal))
{
    Console.Error.WriteLine("FAIL: expected the filing file body to be copied to the stream, starting with <html.");
    return 1;
}
```

Then update the request-count check at the end of the top-level statements. Replace:

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

with:

```csharp
// Two pages of the aggregates enumeration, the single-page aggregates call, the dividends call,
// the news call, one SMA page, two SMA pages enumerated, the last trade, the open/close day, the
// holidays, the trades page, the snapshots, the market status, the tickers page, the contracts
// page, the financials page, and the filing file download.
if (enumerated != 3 || handler.Requests != 18)
{
    Console.Error.WriteLine(
        $"FAIL: expected 3 enumerated bars over 18 requests; got {enumerated} over {handler.Requests}.");
    return 1;
}
```

- [ ] **Step 3: Add the two stub responses**

In the `StubHandler` class at the bottom of `samples/MassiveDotNet.AotSmokeTest/Program.cs`, add two constants after the `Contracts` constant:

```csharp
    // Trimmed to two balance-sheet points and one income-statement point: the sample proves the
    // dictionary deserializes and is reachable by key, not that the statement is complete.
    private const string Financials = """
        {
          "count": 1,
          "next_url": "https://api.massive.com/vX/reference/financials?cursor=next",
          "request_id": "55eb92ed43b25568ab0cce159830ea34",
          "results": [
            {
              "cik": "0001650729",
              "company_name": "SiteOne Landscape Supply, Inc.",
              "end_date": "2022-04-03",
              "filing_date": "2022-05-04",
              "financials": {
                "balance_sheet": {
                  "assets": { "label": "Assets", "order": 100, "unit": "USD", "value": 2407400000 },
                  "equity": { "label": "Equity", "order": 1400, "unit": "USD", "value": 1099200000 }
                },
                "income_statement": {
                  "revenues": { "label": "Revenues", "order": 100, "unit": "USD", "value": 805300000 }
                }
              },
              "fiscal_period": "Q1",
              "fiscal_year": "2022",
              "source_filing_url": "https://api.massive.com/v1/reference/sec/filings/0001650729-22-000010",
              "start_date": "2022-01-03",
              "tickers": [ "SITE" ],
              "timeframe": "quarterly"
            }
          ],
          "status": "OK"
        }
        """;

    private const string FilingFileBody = "<html><body><p>Item 1A. Risk Factors</p></body></html>";
```

Add two arms to the `switch` in its `SendAsync`, before the `_ =>` arm:

```csharp
            "/vX/reference/financials" => Financials,
            "/v1/reference/sec/filings/0001683168-26-006873/files/myx_i10k-053126.htm" => FilingFileBody,
```

The handler serves every body as `application/json`, which the download does not mind: `DownloadAsync` inspects no content type (D-R4), and the assertion is on the bytes.

- [ ] **Step 4: Run the sample and publish it**

Run: `dotnet run --project samples/MassiveDotNet.AotSmokeTest`
Expected: the two new sections print, then `AOT smoke test passed.` with exit code 0.

Run: `dotnet publish samples/MassiveDotNet.AotSmokeTest -r osx-arm64 -c Release 2>&1 | grep -c "IL[0-9]"` (on the machine's own RID)
Expected: `0`. Then run the published binary from `samples/MassiveDotNet.AotSmokeTest/bin/Release/net10.0/osx-arm64/publish/` and confirm it also prints `AOT smoke test passed.` — the dictionary converter is exactly the kind of thing that works under the JIT and fails after trimming, so the published run is the assertion that matters.

- [ ] **Step 5: Record D25 in the constitution**

In `CLAUDE.md`, append one row to the Architecture decisions table, after the `D26` row (the table is ordered by when each decision was taken, and D25 was reserved for this plan):

```markdown
| D25 | A route that serves a document where the description declares JSON ships **as declared**, and a hand-written download sits beside it: `GetFilingFileAsync` returns the declared `FilingFile` and throws on the HTML the service sends, while `DownloadFilingFileAsync` copies the bytes to a caller's stream through the same generated URI builder. `MassiveHttpTransport.DownloadAsync` inspects no content type. | The SEC filing file route declares a metadata object and serves `text/html` with either `Accept` header, observed 2026-09-03. A map-level `kind: document` would have the map overriding the description on a live observation, which is the second stability source D18 and D21 forbid, so the generated method stays as declared and its pinned live test flips the day either side moves. The download is hand-written because nothing in the description says it exists. Returning a `Stream` was rejected because it ties the response's lifetime to a value the caller may forget to dispose, and returning a `string` is wrong for the graphics and PDFs a filing carries. The content type is not inspected because the caller asked for the bytes and the files listing already names each file's type, name, and size. |
```

Then, in the "Constraints the generator must respect" section, append a bullet after the one about `.g.cs` files suppressing the nullable context:

```markdown
- A **path parameter is required whether or not the description flags it**. OpenAPI mandates the
  flag; the SEC v1 description omits it on `filing_id` and `file_id`, and reading it literally
  would default those to `null` and append an empty segment. `Spec.Parameters` therefore reads
  requiredness as `in == "path" || required` (D-R3).
```

Then, in the Conventions section, extend the **Naming** bullet by appending this sentence to it:

```markdown
  A model or method named after an SEC form spells a leading form number, because C# forbids a
  leading digit — `TenKSection`, `EightKDisclosure`, `ThirteenFHolding` — and keeps a trailing
  one: `Form3Filing`, `Form4Filing`, `List10KSectionsAsync`, `List13FHoldingsAsync`.
```

- [ ] **Step 6: Run the full offline verification**

```bash
dotnet build MassiveDotNet.slnx
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/
```

Expected: warning-free build, every test passing, no diff.

- [ ] **Step 7: Commit**

```bash
git add samples/MassiveDotNet.AotSmokeTest CLAUDE.md
git commit -m "chore: root the financials dictionary and the download in the AOT sample; record D25

The dictionary of nested models and the body copy are each reachable
only through these calls, so a clean publish said nothing about them
before. The constitution gains the document-route decision, the
path-parameter rule, and the SEC form-name convention.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 11: The live tier: the D-R13 pins and one call per operation

**Files:**
- Create: `tests/MassiveDotNet.IntegrationTests/ReferenceSecFilingsLiveTests.cs`
- Create: `tests/MassiveDotNet.IntegrationTests/ReferenceFilingSectionsLiveTests.cs`
- Create: `tests/MassiveDotNet.IntegrationTests/ReferenceOwnershipFilingsLiveTests.cs`
- Create: `tests/MassiveDotNet.IntegrationTests/ReferenceFilingTaxonomiesLiveTests.cs`
- Create: `tests/MassiveDotNet.IntegrationTests/ReferenceFinancialsLiveTests.cs`

**Interfaces:**
- Consumes: every method from Tasks 3–9 plus `DownloadFilingFileAsync` (Task 4); `LiveApiTest` (`Client`, `Ct`), which skips when no key is present and carries the `Integration` trait; the integration project's existing `<NoWarn>$(NoWarn);MASSIVE0002;MASSIVE0001</NoWarn>`, which covers the fifteen experimental routes.
- Produces: nothing new. These never run in CI (rule 13); they compile there.

These tests call the real service. `LiveCredentials` finds the key in the gitignored `.env` at the repository root on its own; do not open, print, or echo that file, and do not put the key on a command line. A `403` means the account's plan lacks an entitlement the 2026-09-03 sweep found present: report it as a finding, do not retry with diagnostics that could print a URL or a header.

Live tests are not test-first in the red-green sense: there is no implementation to write, and the service is the oracle. Write each class, run it, and fix an assertion only when the service's answer shows the assumption was wrong, saying so in a comment. Every value asserted below was observed on 2026-09-03; assertions are on shape, not on values that move, except where a date is pinned deliberately.

- [ ] **Step 1: Write the SEC v1 class, with the download and the compact-date pins**

Create `tests/MassiveDotNet.IntegrationTests/ReferenceSecFilingsLiveTests.cs`:

```csharp
using System.Text;
using System.Text.Json;
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// The SEC v1 surface against the real service: a page boundary at a small limit, the compact
/// date filter D-R9 exists for, and the two halves of the filing file route — the declared method
/// throwing on the document the service sends, and the download copying it (D-R4, D-R13).
/// </summary>
public sealed class ReferenceSecFilingsLiveTests : LiveApiTest
{
    // The date the compact filter is exercised from. Fixed rather than computed from a clock so
    // the window cannot drift under the assertion.
    private const string WindowStart = "20260101";

    [Fact]
    public async Task FilingsCrossAPageBoundary()
    {
        // limit is per page, so five filings at two per page is three requests. A repeated or
        // skipped accession number across the seam is what an incorrectly rebuilt cursor looks
        // like; the SEC cursor carries its position in the query string (D14).
        List<string> ids = [];

        await foreach (Filing filing in Client.Reference.EnumerateFilingsAsync(
            type: "10-K",
            order: SortOrder.Descending,
            sort: "filing_date",
            limit: 2,
            cancellationToken: Ct))
        {
            ids.Add(filing.Id);

            if (ids.Count == 5)
            {
                break;
            }
        }

        Assert.Equal(5, ids.Count);
        Assert.Equal(5, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task TheCompactDateFilterSelectsOnOrAfterTheDateGiven()
    {
        // The route reads yyyyMMdd. This is the assertion D-R9 rests on: the filter the SDK can
        // express selects the window it names. A LocalDate would have rendered 2026-01-01 here,
        // which this route accepts and does not read as this date.
        MassivePage<Filing> page = await Client.Reference.ListFilingsAsync(
            type: "10-K",
            filingDate: RangeFilter.Gte(WindowStart),
            order: SortOrder.Ascending,
            sort: "filing_date",
            limit: 5,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (Filing filing in page.Results)
        {
            // Compact dates sort lexically, so an ordinal comparison is a date comparison.
            Assert.True(
                string.CompareOrdinal(filing.FilingDate, WindowStart) >= 0,
                $"Expected a filing on or after {WindowStart}; got {filing.FilingDate}.");

            Assert.Equal("10-K", filing.Type);
            Assert.Equal(8, filing.FilingDate.Length);
            Assert.NotEmpty(filing.Entities);
        }
    }

    [Fact]
    public async Task AFilingFromTheListRoundTripsThroughTheGet()
    {
        // The identifier comes from the list rather than being hard-coded: a filing is a permanent
        // record, but which one is newest is not, and the get is what proves the identifier the
        // list reports is the one the get accepts.
        MassivePage<Filing> page = await Client.Reference.ListFilingsAsync(
            type: "10-K",
            order: SortOrder.Descending,
            sort: "filing_date",
            limit: 1,
            cancellationToken: Ct);

        Filing listed = Assert.Single(page.Results);

        Filing fetched = await Client.Reference.GetFilingAsync(listed.Id, Ct);

        Assert.Equal(listed.Id, fetched.Id);
        Assert.Equal(listed.AccessionNumber, fetched.AccessionNumber);
        Assert.Equal(listed.FilingDate, fetched.FilingDate);
        Assert.Equal(listed.FilesCount, fetched.FilesCount);
        Assert.Equal(14, fetched.AcceptanceTimestamp?.Length);

        FilingEntity entity = Assert.Single(fetched.Entities);
        Assert.Equal("filer", entity.Relation);
        Assert.NotNull(entity.CompanyData);
        Assert.False(string.IsNullOrEmpty(entity.CompanyData.Name));
        Assert.False(string.IsNullOrEmpty(entity.CompanyData.Cik));
    }

    [Fact]
    public async Task TheFilingFileRouteServesTheDocumentBothWays()
    {
        // One test, because both halves need the same file and a second lookup would cost another
        // two calls. The declared method throws on the HTML the service sends, which is D21
        // applied to a route that drifted rather than retired; the download is the method that
        // works, and the pin flips the day the service serves the declared object (D-R4, D25).
        MassivePage<Filing> filings = await Client.Reference.ListFilingsAsync(
            type: "10-K",
            order: SortOrder.Descending,
            sort: "filing_date",
            limit: 1,
            cancellationToken: Ct);

        string filingId = Assert.Single(filings.Results).Id;

        MassivePage<FilingFile> files = await Client.Reference.ListFilingFilesAsync(
            filingId,
            order: SortOrder.Ascending,
            sort: "sequence",
            limit: 1,
            cancellationToken: Ct);

        FilingFile file = Assert.Single(files.Results);
        Assert.Equal(1, file.Sequence);
        Assert.True(file.SizeBytes > 0);
        Assert.False(string.IsNullOrEmpty(file.Type));
        Assert.False(string.IsNullOrEmpty(file.Description));

        // Observed 2026-09-03: text/html, so the declared FilingFile cannot be read from it.
        MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(() =>
            Client.Reference.GetFilingFileAsync(filingId, file.Id, Ct));

        Assert.IsAssignableFrom<JsonException>(exception.InnerException);

        using MemoryStream destination = new();
        await Client.Reference.DownloadFilingFileAsync(filingId, file.Id, destination, Ct);

        Assert.Equal(file.SizeBytes, destination.Length);
        Assert.StartsWith("<", Encoding.UTF8.GetString(destination.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FilingFilesEnumerateBeyondTheFirstPage()
    {
        MassivePage<Filing> filings = await Client.Reference.ListFilingsAsync(
            type: "10-K",
            order: SortOrder.Descending,
            sort: "filing_date",
            limit: 1,
            cancellationToken: Ct);

        string filingId = Assert.Single(filings.Results).Id;
        List<long> sequences = [];

        await foreach (FilingFile file in Client.Reference.EnumerateFilingFilesAsync(
            filingId,
            order: SortOrder.Ascending,
            sort: "sequence",
            limit: 2,
            cancellationToken: Ct))
        {
            sequences.Add(file.Sequence);

            if (sequences.Count == 5)
            {
                break;
            }
        }

        Assert.Equal(5, sequences.Count);
        Assert.Equal(sequences.Order(), sequences);
    }
}
```

- [ ] **Step 2: Write the sections class, with the `vX_0` 404 pin**

Create `tests/MassiveDotNet.IntegrationTests/ReferenceFilingSectionsLiveTests.cs`:

```csharp
using System.Net;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// The 10-K sections and the two 8-K routes against the real service: one shape call each on the
/// served revisions, and the pin that the declared <c>vX_0</c> sections revision answers 404
/// (D21, D-R13).
/// </summary>
public sealed class ReferenceFilingSectionsLiveTests : LiveApiTest
{
    [Fact]
    public async Task TenKSectionsReturnTheRequestedSection()
    {
        MassivePage<TenKSection> page = await Client.Reference.List10KSectionsAsync(
            ticker: "AAPL",
            section: "risk_factors",
            limit: 1,
            cancellationToken: Ct);

        TenKSection section = Assert.Single(page.Results);
        Assert.Equal("AAPL", section.Ticker);
        Assert.Equal("risk_factors", section.Section);
        Assert.NotNull(section.PeriodEnd);
        Assert.NotNull(section.FilingDate);
        Assert.False(string.IsNullOrEmpty(section.Text));
        Assert.False(string.IsNullOrEmpty(section.FilingUrl));
    }

    [Fact]
    public async Task TenKSectionsHonourASetOfSections()
    {
        // The section filter is the only SetFilter on this route, and the any_of form is what a
        // fixture cannot prove renders acceptably to the server.
        MassivePage<TenKSection> page = await Client.Reference.List10KSectionsAsync(
            ticker: "AAPL",
            section: SetFilter.AnyOf("business", "risk_factors"),
            limit: 2,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);
        Assert.All(page.Results, section => Assert.True(section.Section is "business" or "risk_factors"));
    }

    [Fact]
    public async Task TheVx0SectionsRevisionAnswersNotFound()
    {
        // The description declares the vX_0 revision beside the vX one, so rule 1 keeps it mapped
        // and D21 keeps it that way until the description drops it: the service answered a
        // plain-text 404 on 2026-09-03. This pins the drift so it flips the day the route is
        // served, at which point D26 says the plain name moves here.
        MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(() =>
            Client.Reference.List10KSectionsVx0Async(ticker: "AAPL", limit: 1, cancellationToken: Ct));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task EightKDisclosuresHonourAnArrayFilterOnTickers()
    {
        // tickers is an array field, so the plain form asks for rows containing the value. The
        // array filter's wire form is the thing a fixture cannot check against the server.
        MassivePage<EightKDisclosure> page = await Client.Reference.List8KDisclosuresAsync(
            tickers: ArrayFilter.Contains("AAPL"),
            limit: 2,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (EightKDisclosure disclosure in page.Results)
        {
            Assert.Contains("AAPL", disclosure.Tickers ?? []);
            Assert.NotNull(disclosure.FilingDate);
            Assert.False(string.IsNullOrEmpty(disclosure.PrimaryCategory));
            Assert.False(string.IsNullOrEmpty(disclosure.TertiaryCategory));
        }
    }

    [Fact]
    public async Task EightKTextReturnsTheItemText()
    {
        MassivePage<EightKText> page = await Client.Reference.List8KTextAsync(
            ticker: "AAPL",
            filingDate: RangeFilter.Gte(new LocalDate(2025, 1, 1)),
            limit: 1,
            cancellationToken: Ct);

        EightKText text = Assert.Single(page.Results);
        Assert.Equal("AAPL", text.Ticker);
        Assert.StartsWith("8-K", text.FormType, StringComparison.Ordinal);
        Assert.NotNull(text.FilingDate);
        Assert.True(text.FilingDate >= new LocalDate(2025, 1, 1));
        Assert.False(string.IsNullOrEmpty(text.ItemsText));
    }
}
```

- [ ] **Step 3: Write the ownership class**

Create `tests/MassiveDotNet.IntegrationTests/ReferenceOwnershipFilingsLiveTests.cs`:

```csharp
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// 13-F holdings and forms 3 and 4 against the real service: one shape call each, asserting the
/// filters select and the rows deserialize, including the footnotes both forms share (D-R13).
/// </summary>
public sealed class ReferenceOwnershipFilingsLiveTests : LiveApiTest
{
    // Berkshire Hathaway's CIK. A 13-F filer whose filings are a matter of permanent record.
    private const string BerkshireCik = "0001067983";

    [Fact]
    public async Task ThirteenFHoldingsHonourTheFilerCik()
    {
        MassivePage<ThirteenFHolding> page = await Client.Reference.List13FHoldingsAsync(
            filerCik: BerkshireCik,
            limit: 2,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (ThirteenFHolding holding in page.Results)
        {
            Assert.Equal(BerkshireCik, holding.FilerCik);
            Assert.StartsWith("13F", holding.FormType, StringComparison.Ordinal);
            Assert.NotNull(holding.FilingDate);
            Assert.NotNull(holding.Period);
            Assert.False(string.IsNullOrEmpty(holding.IssuerName));
            Assert.False(string.IsNullOrEmpty(holding.Cusip));
            Assert.True(holding.MarketValue > 0);
        }
    }

    [Fact]
    public async Task Form3FilingsCarryTheOwnerAndTheIssuer()
    {
        MassivePage<Form3Filing> page = await Client.Reference.ListForm3FilingsAsync(
            tickers: ArrayFilter.Contains("AAPL"),
            limit: 1,
            cancellationToken: Ct);

        Form3Filing filing = Assert.Single(page.Results);
        Assert.Contains("AAPL", filing.Tickers ?? []);
        Assert.Equal("Apple Inc.", filing.IssuerName);
        Assert.StartsWith("3", filing.FormType, StringComparison.Ordinal);
        Assert.NotNull(filing.FilingDate);
        Assert.False(string.IsNullOrEmpty(filing.OwnerName));
        Assert.False(string.IsNullOrEmpty(filing.OwnerCik));
        Assert.False(string.IsNullOrEmpty(filing.IssuerCik));
    }

    [Fact]
    public async Task Form4FilingsCarryTheTransactionAndItsFootnotes()
    {
        MassivePage<Form4Filing> page = await Client.Reference.ListForm4FilingsAsync(
            tickers: ArrayFilter.Contains("AAPL"),
            limit: 5,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (Form4Filing filing in page.Results)
        {
            Assert.Contains("AAPL", filing.Tickers ?? []);
            Assert.Equal("Apple Inc.", filing.IssuerName);
            Assert.StartsWith("4", filing.FormType, StringComparison.Ordinal);
            Assert.NotNull(filing.FilingDate);
            Assert.False(string.IsNullOrEmpty(filing.OwnerName));
        }

        // The footnote model is generated from form 3 and reused here, so a live row carrying one
        // is what proves the reuse against the wire rather than against the description (D16).
        // Whether any given page carries one is not ours to choose, so a page without becomes a
        // skip with a reason rather than a failure; the fixture test covers the reuse either way.
        if (page.Results.FirstOrDefault(filing => filing.Footnotes is { Length: > 0 })
            is not { Footnotes: [FilingFootnote footnote, ..] })
        {
            Assert.Skip("No form 4 in this page carried a footnote; the reuse is covered by the fixture test.");
            return;
        }

        Assert.False(string.IsNullOrEmpty(footnote.Id));
        Assert.False(string.IsNullOrEmpty(footnote.Description));
    }

    [Fact]
    public async Task Form4FilingsHonourATransactionCode()
    {
        MassivePage<Form4Filing> page = await Client.Reference.ListForm4FilingsAsync(
            tickers: ArrayFilter.Contains("AAPL"),
            transactionCode: "S",
            limit: 2,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);
        Assert.All(page.Results, filing => Assert.Equal("S", filing.TransactionCode));
    }
}
```

- [ ] **Step 4: Write the index and taxonomies class**

Create `tests/MassiveDotNet.IntegrationTests/ReferenceFilingTaxonomiesLiveTests.cs`:

```csharp
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// The filing index, the risk factors, and both taxonomies against the real service: one shape
/// call each, and the pair of taxonomy calls that prove the same wire name really is a string on
/// one route and a number on the other (D-R13).
/// </summary>
public sealed class ReferenceFilingTaxonomiesLiveTests : LiveApiTest
{
    [Fact]
    public async Task TheFilingIndexHonoursATickerAndAFormType()
    {
        MassivePage<FilingIndexEntry> page = await Client.Reference.ListFilingIndexAsync(
            ticker: "AAPL",
            formType: "10-K",
            limit: 2,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (FilingIndexEntry entry in page.Results)
        {
            Assert.Equal("AAPL", entry.Ticker);
            Assert.Equal("10-K", entry.FormType);
            Assert.Equal("0000320193", entry.Cik);
            Assert.NotNull(entry.FilingDate);
            Assert.False(string.IsNullOrEmpty(entry.AccessionNumber));
            Assert.False(string.IsNullOrEmpty(entry.IssuerName));
        }
    }

    [Fact]
    public async Task RiskFactorsCarryTheThreeLevelCategory()
    {
        MassivePage<RiskFactor> page = await Client.Reference.ListRiskFactorsAsync(
            ticker: "AAPL",
            limit: 2,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (RiskFactor factor in page.Results)
        {
            Assert.Equal("AAPL", factor.Ticker);
            Assert.NotNull(factor.FilingDate);
            Assert.False(string.IsNullOrEmpty(factor.PrimaryCategory));
            Assert.False(string.IsNullOrEmpty(factor.SecondaryCategory));
            Assert.False(string.IsNullOrEmpty(factor.TertiaryCategory));
            Assert.False(string.IsNullOrEmpty(factor.SupportingText));
        }
    }

    [Fact]
    public async Task TheDisclosureTaxonomyVersionIsAString()
    {
        MassivePage<DisclosureTaxonomyEntry> page = await Client.Reference.ListDisclosureTaxonomyAsync(
            limit: 5,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (DisclosureTaxonomyEntry entry in page.Results)
        {
            Assert.False(string.IsNullOrEmpty(entry.Taxonomy));
            Assert.False(string.IsNullOrEmpty(entry.PrimaryCategory));
            Assert.False(string.IsNullOrEmpty(entry.Description));
        }
    }

    [Fact]
    public async Task TheRiskFactorTaxonomyVersionIsANumber()
    {
        // The published example spells this version as a string where the schema declares a
        // number; the wire sends the number, which is why the fixture departs from the example
        // (the Task 8 ruling, recorded in D-R12). The gte filter is a double on this route.
        MassivePage<RiskFactorTaxonomyEntry> page = await Client.Reference.ListRiskFactorTaxonomyAsync(
            taxonomy: RangeFilter.Gte(1d),
            limit: 5,
            cancellationToken: Ct);

        Assert.NotEmpty(page.Results);

        foreach (RiskFactorTaxonomyEntry entry in page.Results)
        {
            Assert.True(entry.Taxonomy >= 1d);
            Assert.False(string.IsNullOrEmpty(entry.PrimaryCategory));
            Assert.False(string.IsNullOrEmpty(entry.Description));
        }
    }
}
```

- [ ] **Step 5: Write the financials class**

Create `tests/MassiveDotNet.IntegrationTests/ReferenceFinancialsLiveTests.cs`:

```csharp
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Financials against the real service: the four dictionaries deserialize with their line items
/// reachable by key, the required fields the published sample omits do arrive, and asking for
/// sources adds the attributes it promises (D-R11, D-R13).
/// </summary>
public sealed class ReferenceFinancialsLiveTests : LiveApiTest
{
    [Fact]
    public async Task AnAnnualReportCarriesFourStatementsReachableByKey()
    {
        MassivePage<FinancialReport> page = await Client.Reference.ListFinancialsAsync(
            ticker: "AAPL",
            timeframe: "annual",
            limit: 1,
            cancellationToken: Ct);

        FinancialReport report = Assert.Single(page.Results);

        Assert.Equal("0000320193", report.Cik);
        Assert.Contains("Apple", report.CompanyName, StringComparison.Ordinal);
        Assert.Equal("annual", report.Timeframe);
        Assert.False(string.IsNullOrEmpty(report.FiscalPeriod));
        Assert.NotNull(report.StartDate);
        Assert.NotNull(report.EndDate);
        Assert.NotNull(report.FilingDate);
        Assert.True(report.StartDate < report.EndDate);

        // The published sample omits tickers, so the fixture test asserts it null and this one
        // asserts it arrives; the acceptance timestamp is absent from the sample too, and the
        // wire sent RFC 3339 here where the SEC v1 filings send the compact form, which is why
        // the property is a string on both (D-R9).
        Assert.Contains("AAPL", report.Tickers ?? []);
        Assert.NotNull(report.AcceptanceTimestamp);

        FinancialStatements statements = report.Financials;
        Assert.NotNull(statements.BalanceSheet);
        Assert.NotNull(statements.CashFlowStatement);
        Assert.NotNull(statements.ComprehensiveIncome);
        Assert.NotNull(statements.IncomeStatement);

        FinancialDataPoint assets = statements.BalanceSheet["assets"];
        Assert.Equal("Assets", assets.Label);
        Assert.Equal("USD", assets.Unit);
        Assert.True(assets.Value > 0);
        Assert.True(assets.Order > 0);

        Assert.True(statements.IncomeStatement["revenues"].Value > 0);
    }

    [Fact]
    public async Task AskingForSourcesAddsTheXPathAndFormula()
    {
        // include_sources is the only parameter whose effect is visible in the payload, so it is
        // the one thing here a fixture genuinely cannot check.
        MassivePage<FinancialReport> page = await Client.Reference.ListFinancialsAsync(
            ticker: "AAPL",
            timeframe: "annual",
            includeSources: true,
            limit: 1,
            cancellationToken: Ct);

        FinancialReport report = Assert.Single(page.Results);
        Assert.NotNull(report.Financials.BalanceSheet);

        Assert.Contains(
            report.Financials.BalanceSheet.Values,
            point => !string.IsNullOrEmpty(point.XPath) || !string.IsNullOrEmpty(point.Formula));

        Assert.Contains(report.Financials.BalanceSheet.Values, point => !string.IsNullOrEmpty(point.Source));
    }

    [Fact]
    public async Task FinancialsCrossAPageBoundary()
    {
        List<string> periods = [];

        await foreach (FinancialReport report in Client.Reference.EnumerateFinancialsAsync(
            ticker: "AAPL",
            timeframe: "quarterly",
            order: SortOrder.Descending,
            sort: "period_of_report_date",
            limit: 2,
            cancellationToken: Ct))
        {
            periods.Add($"{report.FiscalYear} {report.FiscalPeriod}");

            if (periods.Count == 5)
            {
                break;
            }
        }

        Assert.Equal(5, periods.Count);
        Assert.Equal(5, periods.Distinct(StringComparer.Ordinal).Count());
    }
}
```

- [ ] **Step 6: Compile the live project the way CI does**

Run: `dotnet build tests/MassiveDotNet.IntegrationTests`
Expected: warning-free. (CI compiles this project and never runs it, rule 13.)

- [ ] **Step 7: Run the live tier**

Run: `dotnet test tests/MassiveDotNet.IntegrationTests --filter "FullyQualifiedName~ReferenceSecFilings|FullyQualifiedName~ReferenceFilingSections|FullyQualifiedName~ReferenceOwnershipFilings|FullyQualifiedName~ReferenceFilingTaxonomies|FullyQualifiedName~ReferenceFinancials"`
Expected: every test passes, none skipped except possibly the form 4 footnote branch, which skips with its reason when the page it drew carried none. A skip on any other test means `LiveCredentials` found no key, which means `.env` is missing; report that rather than creating one. If an assertion fails because the service's answer differs from the assumption, adjust the assertion to what the service actually does and say why in a comment. If a test fails with `403`, report the entitlement finding and stop.

Then run the whole live suite once, so the Plan A and Stocks classes still pass on today's service:

Run: `dotnet test MassiveDotNet.slnx --filter "Category=Integration"`
Expected: every test passes.

- [ ] **Step 8: Commit**

```bash
git add tests/MassiveDotNet.IntegrationTests/ReferenceSecFilingsLiveTests.cs tests/MassiveDotNet.IntegrationTests/ReferenceFilingSectionsLiveTests.cs tests/MassiveDotNet.IntegrationTests/ReferenceOwnershipFilingsLiveTests.cs tests/MassiveDotNet.IntegrationTests/ReferenceFilingTaxonomiesLiveTests.cs tests/MassiveDotNet.IntegrationTests/ReferenceFinancialsLiveTests.cs
git commit -m "test: add the reference SEC and financials live tier

Filings cross a page boundary at limit 2 and the compact date filter
selects its window; the filing file route's drift and the vX_0 sections
404 are pinned, dated; the download copies the document; financials
deserialize four dictionaries and gain their sources on request
(D-R13).

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 12: Issue bookkeeping (controller only, after the branch lands, with the user's go-ahead)

**Files:** none in the repository.

This task posts to GitHub, which is a side effect outside the working tree. Do not dispatch a subagent for it, and do not run it until the user has chosen how the branch lands (merge or pull request) and has said to post. Present the comment text below and ask once.

- [ ] **Step 1: Comment on #9 and close it**

Plan B completes issue #9. Post this as a comment, then close the issue:

```
Plan B of the Reference group design landed: the SEC filings surface and financials, sixteen operations under `client.Reference`, `CoverageBaseline` 40 → 56. With Plan A's seventeen and the two that were already mapped, this closes #9's thirty-five.

- `ListFilings` → `ListFilingsAsync` / `EnumerateFilingsAsync`
- `GetFiling` → `GetFilingAsync`
- `ListFilingFiles` → `ListFilingFilesAsync` / `EnumerateFilingFilesAsync`
- `GetFilingFile` → `GetFilingFileAsync`, beside the hand-written `DownloadFilingFileAsync`
- `get_stocks_filings_10-K_vX_sections` → `List10KSectionsAsync` / `Enumerate10KSectionsAsync` (`[Experimental]`)
- `get_stocks_filings_10-K_vX_0_sections` → `List10KSectionsVx0Async` / `Enumerate10KSectionsVx0Async` (404 today, pinned; D21, D26)
- `get_stocks_filings_8-K_vX_disclosures` → `List8KDisclosuresAsync` / `Enumerate8KDisclosuresAsync` (`[Experimental]`)
- `get_stocks_filings_8-K_vX_text` → `List8KTextAsync` / `Enumerate8KTextAsync` (`[Experimental]`)
- `get_stocks_filings_vX_13-F` → `List13FHoldingsAsync` / `Enumerate13FHoldingsAsync` (`[Experimental]`)
- `get_stocks_filings_vX_form-3` → `ListForm3FilingsAsync` / `EnumerateForm3FilingsAsync` (`[Experimental]`)
- `get_stocks_filings_vX_form-4` → `ListForm4FilingsAsync` / `EnumerateForm4FilingsAsync` (`[Experimental]`)
- `get_stocks_filings_vX_index` → `ListFilingIndexAsync` / `EnumerateFilingIndexAsync` (`[Experimental]`)
- `get_stocks_filings_vX_risk-factors` → `ListRiskFactorsAsync` / `EnumerateRiskFactorsAsync` (`[Experimental]`)
- `get_stocks_taxonomies_vX_disclosures` → `ListDisclosureTaxonomyAsync` / `EnumerateDisclosureTaxonomyAsync` (`[Experimental]`)
- `get_stocks_taxonomies_vX_risk-factors` → `ListRiskFactorTaxonomyAsync` / `EnumerateRiskFactorTaxonomyAsync` (`[Experimental]`)
- `ListFinancials` → `ListFinancialsAsync` / `EnumerateFinancialsAsync` (`[Experimental]`)

Core gains `MassiveHttpTransport.DownloadAsync`, and the generator now reads every path parameter as required whether or not the description flags it. Three things the description gets wrong are handled rather than reproduced: the SEC v1 dates bind `string` because the route reads `yyyyMMdd` and silently misreads the ISO form; the filing file route ships as declared and throws on the HTML it actually serves, with `DownloadFilingFileAsync` beside it (D25); and the financials statements bind one `Dictionary<string, FinancialDataPoint>` each, from the shape the description documents under a property named `*`.

Live pins, all dated 2026-09-03: the `vX_0` sections revision answers 404, and the filing file route serves `text/html`. Design: `docs/superpowers/specs/2026-09-03-reference-group-design.md`.
```

- [ ] **Step 2: Check whether #15 is affected**

`/stocks/financials/v1/*` is a stated non-goal of this spec, and #15 owns it. The financials route this plan mapped is `/vX/reference/financials`, a different route with a different payload. Post nothing on #15 unless the user asks; if they do, the accurate note is that `Reference.ListFinancialsAsync` now covers the `vX` reference route, and #15's `stocks/v1` group is still unmapped.

- [ ] **Step 3: Report**

Confirm the comment posted and the issue closed, and relay both URLs.

---

## Notes for the executor

**Where the numbers come from.** Every fixture in this plan is either the published example from `specs/openapi.json` (Tasks 5–9) or a live capture reviewed on 2026-09-03 (Task 3). The captures were taken, reviewed for account identifiers and embedded keys, and pasted into this plan before it was written; no task in this plan calls the live service except Task 11, which asserts rather than captures.

**Three departures from published examples**, each commented in the fixture and each narrower than it looks:

| Fixture | Departure | Why |
|---|---|---|
| `ReferenceRiskFactorTaxonomy` | `"taxonomy": "1.0"` becomes `1.0` | The schema declares a required number and the wire sends one; the string would not deserialize (Task 8 ruling, D-R12). |
| `ReferenceFinancials` | `"timeframe": "quarterly"` is added | The description marks it required and the wire sends it; the example omits it, and a required property that never arrives fails deserialization outright (Task 9). |
| `ReferenceSecFilingsLastPage` | The captured second page, trimmed to one filing with its cursor removed | A traversal test needs a terminal page, and the service cannot be asked for "the page after this one, without a cursor". |

**What the offline suite cannot see, and Task 11 therefore must.** The compact date filter's behaviour, the filing file route's content type, the `vX_0` revision's 404, `include_sources` changing the payload, and every `any_of` / `all_of` wire form's acceptability to the server. Everything else is a fixture's job; do not port a fixture assertion into the live tier.

**Two fields the wire sends that no model declares.** The SEC filings rows carry `main_file_url` and `xbrl_instance_url`, and the company object carries a `tickers` array, none of which the description declares. The models omit them deliberately: the description is the contract for what ships (D21), and adding a property the description lacks is the second source D18 forbids. The captured fixtures keep them so the omission is visible in review, and `System.Text.Json` ignores an unmapped property by default.

**If `EndpointCoverageTests` reports more than the baseline**, something outside this plan was mapped; stop and report it rather than raising the number.
