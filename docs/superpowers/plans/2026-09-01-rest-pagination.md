# REST Pagination Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let callers walk every page of a paginated Massive endpoint with `await foreach`, without ever rebuilding a cursor or sending the API key off-origin.

**Architecture:** One cursor-following method on `MassiveHttpTransport` in core, driven by a tiny `IPagedEnvelope<TItem>` interface that generated response envelopes implement. The code generator detects pagination from the OpenAPI success schema and emits two public methods per paginated endpoint — `EnumerateXxxAsync` returning `IAsyncEnumerable<T>`, and `ListXxxAsync` returning `MassivePage<T>`. Endpoints without a cursor are untouched.

**Tech Stack:** .NET 10, C# latest, xUnit v3, System.Text.Json source generation, NodaTime. No new package dependencies.

**Spec:** `docs/superpowers/specs/2026-09-01-rest-pagination-design.md`

## Global Constraints

Copied from `CLAUDE.md`. Every task inherits these.

- **Rule 3** — No reflection-based serialization in shipped code. `System.Text.Json` source generation only. Test projects may define their own `JsonSerializerContext`; they must not rely on reflection.
- **Rule 5** — `*.g.cs` files are never hand-edited. Change `tools/MassiveDotNet.CodeGen` and regenerate.
- **Rule 6** — The generator is deterministic: the same inputs produce byte-identical output.
- **Rule 7** — `MassiveDotNet` (core) references no external package other than NodaTime.
- **Rule 9** — `TreatWarningsAsErrors` is on. `EnforceCodeStyleInBuild` is on, so an **unused `using` fails the build** (IDE0005). Emit usings conditionally.
- **Rule 10** — Every public member carries XML documentation, or CS1591 fails the build.
- **Rule 12** — NodaTime only. No BCL `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, or `TimeSpan` may be *named* anywhere in `src`, `tests`, `samples`, or `tools`. Nothing in this plan is temporal; if you reach for one, you have gone wrong.
- **Rule 13** — CI runs offline only. Live tests carry `[Trait("Category", "Integration")]` and live in `tests/MassiveDotNet.IntegrationTests`.
- **Convention** — Do not commit or push unless asked. Steps below include commits; confirm with the user before the first one.

**Verification commands** (from CLAUDE.md, "Before opening a PR"):

```bash
dotnet build MassiveDotNet.slnx                                    # must be warning-free
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/
dotnet publish samples/MassiveDotNet.AotSmokeTest -r osx-arm64 -c Release   # zero IL warnings
```

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `src/MassiveDotNet/MassivePage.cs` | **Create.** The single-page result type. | 1 |
| `src/MassiveDotNet/Http/IPagedEnvelope.cs` | **Create.** The contract generated envelopes implement. | 2 |
| `src/MassiveDotNet/Http/MassiveHttpTransport.cs` | **Modify.** Add `EnumerateAsync` and `ResolveCursor`. | 2 |
| `tests/MassiveDotNet.Rest.Tests/PagingStubHandler.cs` | **Create.** Multi-response stub recording every request. | 2 |
| `tests/MassiveDotNet.Rest.Tests/TestEnvelopes.cs` | **Create.** `FakePage` + source-gen context for transport tests. | 2 |
| `tests/MassiveDotNet.Rest.Tests/CursorTraversalTests.cs` | **Create.** Transport-level pagination behaviour. | 2 |
| `tools/MassiveDotNet.CodeGen/Spec.cs` | **Modify.** Add `IsPaginated`. | 3 |
| `tools/MassiveDotNet.CodeGen/Naming.cs` | **Modify.** Add `Enumerate`. | 3 |
| `tools/MassiveDotNet.CodeGen/Emitter.cs` | **Modify.** Emit the interface, `Enumerate`, page-aware `List`. | 3 |
| `src/MassiveDotNet.Rest/Generated/**` | **Regenerate.** Never hand-edit. | 3 |
| `tests/MassiveDotNet.Rest.Tests/StocksAggregatesTests.cs` | **Modify.** 4 call sites move to `MassivePage<Agg>`. | 3 |
| `tests/MassiveDotNet.IntegrationTests/StocksAggregatesLiveTests.cs` | **Modify.** 3 call sites. | 3 |
| `tests/MassiveDotNet.Rest.Tests/StocksPaginationTests.cs` | **Create.** The generated surface, end to end. | 4 |
| `tests/MassiveDotNet.IntegrationTests/PaginationLiveTests.cs` | **Create.** One real multi-page traversal. | 5 |
| `CLAUDE.md` | **Modify.** Add decision D14. | 5 |

---

### Task 1: `MassivePage<T>`

**Files:**
- Create: `src/MassiveDotNet/MassivePage.cs`
- Test: `tests/MassiveDotNet.Rest.Tests/MassivePageTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `MassiveDotNet.MassivePage<T>` with constructor `MassivePage(T[]? results, bool hasMore, string? requestId)` and members `T[] Results`, `bool HasMore`, `string? RequestId`.

Why a `readonly record struct`: one is produced per call, there is no meaningful "null page", and it costs no allocation. The private nullable backing field is what makes `Results` non-null even for `default(MassivePage<T>)` — a plain `{ get; init; }` auto-property would hand back `null` there and every caller would need a guard.

- [ ] **Step 1: Write the failing test**

Create `tests/MassiveDotNet.Rest.Tests/MassivePageTests.cs`:

```csharp
using Xunit;

namespace MassiveDotNet.Rest.Tests;

public sealed class MassivePageTests
{
    [Fact]
    public void ExposesTheResultsItWasGiven()
    {
        MassivePage<int> page = new([1, 2, 3], hasMore: true, requestId: "abc");

        Assert.Equal([1, 2, 3], page.Results);
        Assert.True(page.HasMore);
        Assert.Equal("abc", page.RequestId);
    }

    [Fact]
    public void ReportsAnEmptyArrayRatherThanNullWhenTheServerSentNoResults()
    {
        MassivePage<int> page = new(null, hasMore: false, requestId: null);

        Assert.Empty(page.Results);
        Assert.False(page.HasMore);
        Assert.Null(page.RequestId);
    }

    [Fact]
    public void ReportsAnEmptyArrayForADefaultInstance()
    {
        // A struct can always be default-constructed, so the non-null guarantee
        // has to survive that path too.
        MassivePage<int> page = default;

        Assert.Empty(page.Results);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~MassivePageTests"
```

Expected: FAIL to compile — `CS0246: The type or namespace name 'MassivePage<>' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `src/MassiveDotNet/MassivePage.cs`:

```csharp
namespace MassiveDotNet;

/// <summary>
/// One page of results from a paginated Massive endpoint.
/// </summary>
/// <typeparam name="T">The result item type.</typeparam>
/// <remarks>
/// <para>
/// Returned by the <c>List</c> methods of endpoints that paginate. To walk every page instead of
/// handling cursors yourself, use the matching <c>Enumerate</c> method, which returns an
/// <see cref="IAsyncEnumerable{T}"/>.
/// </para>
/// <para>
/// The cursor itself is deliberately not exposed. It is a server-supplied absolute URL that the
/// SDK validates and follows internally; there is no public API that accepts one back, so
/// surfacing it would offer callers a value they could not use.
/// </para>
/// </remarks>
public readonly record struct MassivePage<T>
{
    private readonly T[]? _results;

    /// <summary>Initializes a new instance of the <see cref="MassivePage{T}"/> struct.</summary>
    /// <param name="results">The page's results, or <see langword="null"/> when the server sent none.</param>
    /// <param name="hasMore">Whether the server offered a cursor to a further page.</param>
    /// <param name="requestId">The server-assigned request identifier, when present.</param>
    public MassivePage(T[]? results, bool hasMore, string? requestId)
    {
        _results = results;
        HasMore = hasMore;
        RequestId = requestId;
    }

    /// <summary>
    /// The results in this page. Never <see langword="null"/>; empty when the server returned none.
    /// </summary>
    public T[] Results => _results ?? [];

    /// <summary>
    /// Whether more pages exist. When <see langword="true"/>, the matching <c>Enumerate</c> method
    /// will retrieve the remainder.
    /// </summary>
    public bool HasMore { get; }

    /// <summary>
    /// The server-assigned request identifier. Include this when contacting Massive support.
    /// </summary>
    public string? RequestId { get; }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~MassivePageTests"
```

Expected: PASS, 3 tests.

- [ ] **Step 5: Verify the whole build is still warning-free**

```bash
dotnet build MassiveDotNet.slnx
```

Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add src/MassiveDotNet/MassivePage.cs tests/MassiveDotNet.Rest.Tests/MassivePageTests.cs
git commit -m "feat: add MassivePage<T> for single-page results"
```

---

### Task 2: Cursor traversal in the transport

**Files:**
- Create: `src/MassiveDotNet/Http/IPagedEnvelope.cs`
- Modify: `src/MassiveDotNet/Http/MassiveHttpTransport.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/PagingStubHandler.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/TestEnvelopes.cs`
- Create: `tests/MassiveDotNet.Rest.Tests/CursorTraversalTests.cs`

**Interfaces:**
- Consumes: `MassiveDotNet.MassivePage<T>` (Task 1) — not directly used here, but must compile alongside.
- Produces:
  - `MassiveDotNet.Http.IPagedEnvelope<TItem>` with `TItem[]? Results { get; }` and `string? NextUrl { get; }`
  - `MassiveHttpTransport.EnumerateAsync<TEnvelope, TItem>(string requestUri, JsonTypeInfo<TEnvelope> typeInfo, CancellationToken cancellationToken = default)` returning `IAsyncEnumerable<TItem>`, constrained `where TEnvelope : class, IPagedEnvelope<TItem>`. Task 3 generates calls to exactly this signature.

- [ ] **Step 1: Write the test scaffolding**

Create `tests/MassiveDotNet.Rest.Tests/PagingStubHandler.cs`:

```csharp
using System.Net;
using System.Text;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Serves a scripted sequence of response bodies, recording every request. Unlike
/// <see cref="StubHandler"/> this keeps the full request history, which is what pagination
/// assertions are made against.
/// </summary>
internal sealed class PagingStubHandler : HttpMessageHandler
{
    private readonly string[] _pages;

    public PagingStubHandler(params string[] pages) => _pages = pages;

    public List<Uri> Requests { get; } = [];

    public List<string?> Authorizations { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        Authorizations.Add(request.Headers.Authorization?.ToString());

        // Past the end of the script, keep serving the last page so a runaway
        // traversal shows up as a failed count assertion rather than an IndexOutOfRange.
        string body = _pages[Math.Min(Requests.Count - 1, _pages.Length - 1)];

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}
```

Create `tests/MassiveDotNet.Rest.Tests/TestEnvelopes.cs`:

```csharp
using System.Text.Json.Serialization;
using MassiveDotNet.Http;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// A minimal paged envelope, so transport-level traversal can be tested without depending on
/// any particular generated endpoint.
/// </summary>
internal sealed class FakePage : IPagedEnvelope<int>
{
    [JsonPropertyName("results")]
    public int[]? Results { get; init; }

    [JsonPropertyName("next_url")]
    public string? NextUrl { get; init; }
}

/// <summary>Source-generated metadata for the test envelopes, so no test relies on reflection.</summary>
[JsonSerializable(typeof(FakePage))]
internal sealed partial class TestJsonContext : JsonSerializerContext;
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/CursorTraversalTests.cs`:

```csharp
using MassiveDotNet.Http;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

public sealed class CursorTraversalTests
{
    private const string StartUri = "/v3/reference/things?limit=2";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A page of two integers, optionally advertising a cursor to another page.</summary>
    private static string Page(int first, string? nextUrl) =>
        nextUrl is null
            ? $$"""{"results":[{{first}},{{first + 1}}],"status":"OK"}"""
            : $$"""{"results":[{{first}},{{first + 1}}],"next_url":"{{nextUrl}}","status":"OK"}""";

    private static string Cursor(string cursor) =>
        $"https://api.massive.com/v3/reference/things?cursor={cursor}";

    private static MassiveHttpTransport Create(PagingStubHandler handler)
    {
        // The authentication handler is deliberately in the chain: several assertions below
        // are about where the API key does and does not travel.
        MassiveAuthenticationHandler authentication =
            new("test-key", MassiveAuthenticationScheme.BearerToken) { InnerHandler = handler };

        HttpClient httpClient = new(authentication) { BaseAddress = MassiveEndpoints.Production };
        return new MassiveHttpTransport(httpClient);
    }

    [Fact]
    public async Task YieldsEveryItemAcrossEveryPage()
    {
        PagingStubHandler handler = new(
            Page(1, Cursor("a")),
            Page(3, Cursor("b")),
            Page(5, nextUrl: null));

        using MassiveHttpTransport transport = Create(handler);

        List<int> seen = [];
        await foreach (int value in transport.EnumerateAsync<FakePage, int>(
            StartUri, TestJsonContext.Default.FakePage, Ct))
        {
            seen.Add(value);
        }

        Assert.Equal([1, 2, 3, 4, 5, 6], seen);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task FollowsTheCursorVerbatimRatherThanRebuildingIt()
    {
        // The aggregates cursor rewrites a path segment, so any reconstruction from the
        // original arguments silently restarts the traversal.
        const string Rewritten =
            "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/1704776400000/2024-06-30?cursor=xyz";

        PagingStubHandler handler = new(Page(1, Rewritten), Page(3, nextUrl: null));
        using MassiveHttpTransport transport = Create(handler);

        await foreach (int _ in transport.EnumerateAsync<FakePage, int>(
            StartUri, TestJsonContext.Default.FakePage, Ct))
        {
        }

        Assert.Equal(Rewritten, handler.Requests[1].ToString());
    }

    [Fact]
    public async Task DoesNotFetchAPageBeforeItsPredecessorIsFullyConsumed()
    {
        PagingStubHandler handler = new(
            Page(1, Cursor("a")),
            Page(3, Cursor("b")),
            Page(5, nextUrl: null));

        using MassiveHttpTransport transport = Create(handler);

        int consumed = 0;
        await foreach (int _ in transport.EnumerateAsync<FakePage, int>(
            StartUri, TestJsonContext.Default.FakePage, Ct))
        {
            consumed++;

            // Two items per page, so after consuming n items exactly ceil(n/2)
            // requests should have been issued -- never more.
            Assert.Equal((consumed + 1) / 2, handler.Requests.Count);
        }

        Assert.Equal(6, consumed);
    }

    [Fact]
    public async Task StopsWithoutAFurtherRequestWhenTheCallerBreaksOut()
    {
        PagingStubHandler handler = new(Page(1, Cursor("a")), Page(3, nextUrl: null));
        using MassiveHttpTransport transport = Create(handler);

        await foreach (int _ in transport.EnumerateAsync<FakePage, int>(
            StartUri, TestJsonContext.Default.FakePage, Ct))
        {
            break;
        }

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task StopsWithoutAFurtherRequestWhenTheTokenIsCancelled()
    {
        PagingStubHandler handler = new(Page(1, Cursor("a")), Page(3, nextUrl: null));
        using MassiveHttpTransport transport = Create(handler);

        using CancellationTokenSource cts = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (int _ in transport.EnumerateAsync<FakePage, int>(
                StartUri, TestJsonContext.Default.FakePage, cts.Token))
            {
                await cts.CancelAsync();
            }
        });

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RefusesToFollowACursorPointingAtAnotherHost()
    {
        PagingStubHandler handler = new(
            Page(1, "https://evil.example/v3/reference/things?cursor=a"),
            Page(3, nextUrl: null));

        using MassiveHttpTransport transport = Create(handler);

        MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(async () =>
        {
            await foreach (int _ in transport.EnumerateAsync<FakePage, int>(
                StartUri, TestJsonContext.Default.FakePage, Ct))
            {
            }
        });

        Assert.Contains("evil.example", exception.Message, StringComparison.Ordinal);

        // The key must not have travelled to the foreign host: only the first,
        // same-origin request was ever issued.
        Assert.Single(handler.Requests);
        Assert.Equal("api.massive.com", handler.Requests[0].Host);
        Assert.Equal("Bearer test-key", handler.Authorizations[0]);
    }

    [Fact]
    public async Task ResolvesARelativeCursorAgainstTheBaseAddress()
    {
        PagingStubHandler handler = new(
            Page(1, "/v3/reference/things?cursor=a"),
            Page(3, nextUrl: null));

        using MassiveHttpTransport transport = Create(handler);

        await foreach (int _ in transport.EnumerateAsync<FakePage, int>(
            StartUri, TestJsonContext.Default.FakePage, Ct))
        {
        }

        Assert.Equal(
            "https://api.massive.com/v3/reference/things?cursor=a",
            handler.Requests[1].ToString());
    }

    [Fact]
    public void ValidatesArgumentsEagerlyRatherThanAtFirstIteration()
    {
        PagingStubHandler handler = new(Page(1, nextUrl: null));
        using MassiveHttpTransport transport = Create(handler);

        // The throw must happen on the call itself, not when someone starts iterating,
        // which is why EnumerateAsync is not itself an iterator method.
        Assert.Throws<ArgumentException>(() =>
            transport.EnumerateAsync<FakePage, int>("  ", TestJsonContext.Default.FakePage, Ct));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RetainsNoMemoryProportionalToThePagesTraversed()
    {
        // Secondary evidence only. DoesNotFetchAPageBeforeItsPredecessorIsFullyConsumed
        // is what actually proves one page is in flight; this catches accumulation.
        const int Pages = 500;

        string[] script = new string[Pages];
        for (int i = 0; i < Pages; i++)
        {
            string body = string.Join(',', Enumerable.Range(i * 1000, 1000));
            script[i] = i == Pages - 1
                ? $$"""{"results":[{{body}}],"status":"OK"}"""
                : $$"""{"results":[{{body}}],"next_url":"{{Cursor(i.ToString())}}","status":"OK"}""";
        }

        PagingStubHandler handler = new(script);
        using MassiveHttpTransport transport = Create(handler);

        long before = GC.GetTotalMemory(forceFullCollection: true);

        long total = 0;
        await foreach (int value in transport.EnumerateAsync<FakePage, int>(
            StartUri, TestJsonContext.Default.FakePage, Ct))
        {
            total += value;
        }

        long after = GC.GetTotalMemory(forceFullCollection: true);

        Assert.Equal(Pages, handler.Requests.Count);

        // 500 pages x 1000 ints is ~2 MB if pages accumulate. The bound is deliberately
        // loose: this is asserting the absence of accumulation, not a precise figure.
        Assert.True(
            after - before < 512 * 1024,
            $"Retained {after - before:N0} bytes after {Pages} pages; pages appear to accumulate.");

        Assert.True(total > 0);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~CursorTraversalTests"
```

Expected: FAIL to compile — `IPagedEnvelope<>` and `EnumerateAsync` do not exist yet.

- [ ] **Step 4: Create the envelope interface**

Create `src/MassiveDotNet/Http/IPagedEnvelope.cs`:

```csharp
namespace MassiveDotNet.Http;

/// <summary>
/// A response envelope that carries one page of results and, when more exist, a cursor to the
/// next page.
/// </summary>
/// <typeparam name="TItem">The result item type.</typeparam>
/// <remarks>
/// Implemented by generated response envelopes for the operations whose OpenAPI success schema
/// declares <c>next_url</c>. It exists so that
/// <see cref="MassiveHttpTransport.EnumerateAsync{TEnvelope, TItem}"/> can traverse any of them
/// with a single implementation, rather than the generator emitting one traversal loop per
/// endpoint.
/// </remarks>
public interface IPagedEnvelope<TItem>
{
    /// <summary>The results in this page, or <see langword="null"/> when the server sent none.</summary>
    TItem[]? Results { get; }

    /// <summary>The absolute URL of the next page, or <see langword="null"/> on the final page.</summary>
    string? NextUrl { get; }
}
```

- [ ] **Step 5: Add traversal and cursor validation to the transport**

In `src/MassiveDotNet/Http/MassiveHttpTransport.cs`, add to the top of the file:

```csharp
using System.Runtime.CompilerServices;
```

Then insert these three members immediately after the existing `GetAsync<T>` method:

```csharp
    /// <summary>
    /// Issues a GET request and then follows the response's <c>next_url</c> cursor, yielding every
    /// item from every page.
    /// </summary>
    /// <typeparam name="TEnvelope">The paged response envelope type.</typeparam>
    /// <typeparam name="TItem">The result item type.</typeparam>
    /// <param name="requestUri">The first page's URI, relative to the configured base address.</param>
    /// <param name="typeInfo">Source-generated metadata describing <typeparamref name="TEnvelope"/>.</param>
    /// <param name="cancellationToken">A token to cancel the traversal.</param>
    /// <returns>Every item across every page, in the order the server returned them.</returns>
    /// <remarks>
    /// Exactly one page is in flight at a time: the next request is issued only once the previous
    /// page has been fully consumed, so a caller who stops early stops the traffic too.
    /// </remarks>
    /// <exception cref="MassiveApiException">
    /// The server responded with an error status, or returned a cursor pointing outside the
    /// configured base address.
    /// </exception>
    public IAsyncEnumerable<TItem> EnumerateAsync<TEnvelope, TItem>(
        string requestUri,
        JsonTypeInfo<TEnvelope> typeInfo,
        CancellationToken cancellationToken = default)
        where TEnvelope : class, IPagedEnvelope<TItem>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Validation happens here rather than in the iterator below, so a bad argument throws at
        // the call site instead of being deferred until someone starts enumerating.
        return EnumerateCoreAsync<TEnvelope, TItem>(requestUri, typeInfo, cancellationToken);
    }

    private async IAsyncEnumerable<TItem> EnumerateCoreAsync<TEnvelope, TItem>(
        string requestUri,
        JsonTypeInfo<TEnvelope> typeInfo,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TEnvelope : class, IPagedEnvelope<TItem>
    {
        string? next = requestUri;

        while (next is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TEnvelope? envelope = await GetAsync(next, typeInfo, cancellationToken).ConfigureAwait(false);

            if (envelope is null)
            {
                yield break;
            }

            foreach (TItem item in envelope.Results ?? [])
            {
                yield return item;
            }

            // The previous page becomes garbage here: nothing accumulates across the traversal.
            next = envelope.NextUrl is { } nextUrl ? ResolveCursor(nextUrl).ToString() : null;
        }
    }

    /// <summary>
    /// Validates a server-supplied cursor and resolves it to an absolute URI.
    /// </summary>
    /// <remarks>
    /// The cursor is compared, never rebuilt: some endpoints move state into the path rather than
    /// the query string, so reconstructing it from the original arguments silently restarts the
    /// traversal. The origin check exists because the SDK re-attaches the API key to every page,
    /// and <c>next_url</c> is a URL chosen by the response body.
    /// </remarks>
    private Uri ResolveCursor(string nextUrl)
    {
        if (!Uri.TryCreate(nextUrl, UriKind.RelativeOrAbsolute, out Uri? cursor))
        {
            throw new MassiveApiException(
                HttpStatusCode.OK,
                "The server returned a 'next_url' value that is not a valid URI.");
        }

        if (_httpClient.BaseAddress is not { } baseAddress)
        {
            throw new InvalidOperationException(
                "Following a pagination cursor requires HttpClient.BaseAddress to be set, because "
                + "the cursor's origin is checked against it before the API key is sent.");
        }

        if (!cursor.IsAbsoluteUri)
        {
            return new Uri(baseAddress, cursor);
        }

        bool sameOrigin = Uri.Compare(
            baseAddress,
            cursor,
            UriComponents.SchemeAndServer,
            UriFormat.UriEscaped,
            StringComparison.OrdinalIgnoreCase) == 0;

        if (!sameOrigin)
        {
            throw new MassiveApiException(
                HttpStatusCode.OK,
                $"The server returned a 'next_url' pointing at "
                + $"'{cursor.GetLeftPart(UriPartial.Authority)}', which is not the configured base "
                + $"address '{baseAddress.GetLeftPart(UriPartial.Authority)}'. The cursor was not "
                + "followed, so the API key was not sent to that host.");
        }

        return cursor;
    }
```

Note on the status code: `HttpStatusCode.OK` is correct rather than odd. The response *was* successful; what failed is the SDK's trust check on its body. The existing `JsonException` handler in `GetAsync` already throws `MassiveApiException` carrying a success status for the same reason.

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~CursorTraversalTests"
```

Expected: PASS, 9 tests.

If `RetainsNoMemoryProportionalToThePagesTraversed` fails, check for accumulation first (a `List<TItem>` collecting results, or holding `envelope` past the loop) before loosening the bound. Only relax the number if the implementation is provably non-accumulating.

- [ ] **Step 7: Verify the build and the full offline suite**

```bash
dotnet build MassiveDotNet.slnx
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
```

Expected: `0 Warning(s)`; all tests pass (21 pre-existing + 3 from Task 1 + 9 here = 33).

- [ ] **Step 8: Commit**

```bash
git add src/MassiveDotNet/Http/IPagedEnvelope.cs \
        src/MassiveDotNet/Http/MassiveHttpTransport.cs \
        tests/MassiveDotNet.Rest.Tests/PagingStubHandler.cs \
        tests/MassiveDotNet.Rest.Tests/TestEnvelopes.cs \
        tests/MassiveDotNet.Rest.Tests/CursorTraversalTests.cs
git commit -m "feat: follow next_url cursors from the transport, within the configured origin"
```

---

### Task 3: Generate the pagination surface

**Files:**
- Modify: `tools/MassiveDotNet.CodeGen/Spec.cs`
- Modify: `tools/MassiveDotNet.CodeGen/Naming.cs`
- Modify: `tools/MassiveDotNet.CodeGen/Emitter.cs`
- Regenerate: `src/MassiveDotNet.Rest/Generated/**`
- Modify: `tests/MassiveDotNet.Rest.Tests/StocksAggregatesTests.cs`
- Modify: `tests/MassiveDotNet.IntegrationTests/StocksAggregatesLiveTests.cs`

**Interfaces:**
- Consumes: `MassivePage<T>` (Task 1), `IPagedEnvelope<TItem>` and `EnumerateAsync` (Task 2).
- Produces, on `StocksGroup`:
  - `IAsyncEnumerable<Agg> EnumerateAggregatesAsync(string ticker, int multiplier, AggregateTimespan timespan, DateOrTimestamp from, DateOrTimestamp to, bool? adjusted = null, SortOrder? sort = null, int? limit = null, CancellationToken cancellationToken = default)`
  - `Task<MassivePage<Agg>> ListAggregatesAsync(…same parameters…)` — **return type changed** from `Task<Agg[]>`.

This task must be atomic. Regenerating changes `ListAggregatesAsync`'s return type, which breaks seven existing call sites; the build is red until they are updated in the same commit.

- [ ] **Step 1: Add pagination detection to the spec reader**

In `tools/MassiveDotNet.CodeGen/Spec.cs`, add this method to the `Spec` class, immediately after `SuccessSchema`:

```csharp
    /// <summary>
    /// Whether an operation's success envelope carries a <c>next_url</c> cursor.
    /// </summary>
    /// <remarks>
    /// Read from the description rather than declared in the map, so it cannot drift: if Massive
    /// starts paginating an endpoint that previously did not, the next spec sync grows the
    /// streaming surface without anyone having to notice.
    /// </remarks>
    public static bool IsPaginated(SpecOperation operation) =>
        Properties(SuccessSchema(operation)).Exists(p => p.Name == "next_url");
```

- [ ] **Step 2: Add the Enumerate naming rule**

In `tools/MassiveDotNet.CodeGen/Naming.cs`, add:

```csharp
    /// <summary>
    /// The streaming counterpart of a method name: <c>ListAggregates</c> becomes
    /// <c>EnumerateAggregates</c>.
    /// </summary>
    /// <remarks>
    /// Mirrors the BCL's own distinction between <c>Directory.GetFiles</c> and
    /// <c>Directory.EnumerateFiles</c> -- a materialized result versus a lazy sequence.
    /// </remarks>
    public static string Enumerate(string method) =>
        method.StartsWith("List", StringComparison.Ordinal)
            ? $"Enumerate{method["List".Length..]}"
            : $"Enumerate{method}";
```

- [ ] **Step 3: Emit the interface on paginated envelopes**

In `tools/MassiveDotNet.CodeGen/Emitter.cs`, replace the `EmitEnvelopes` method with:

```csharp
    private string EmitEnvelopes()
    {
        bool anyPaginated = map.Endpoints.Any(e => Spec.IsPaginated(spec.Operation(e.OperationId)));

        CodeWriter writer = new();
        writer.Line(Header);
        writer.Line();
        writer.Line("using System.Text.Json.Serialization;");

        // Emitted conditionally: an unused using fails the build under EnforceCodeStyleInBuild.
        if (anyPaginated)
        {
            writer.Line("using MassiveDotNet.Http;");
        }

        writer.Line("using MassiveDotNet.Rest.Models;");
        writer.Line();
        writer.Line("namespace MassiveDotNet.Rest.Serialization;");

        foreach (MapEndpoint endpoint in map.Endpoints)
        {
            SpecOperation operation = spec.Operation(endpoint.OperationId);
            List<SpecProperty> properties = Spec.Properties(Spec.SuccessSchema(operation));
            bool paginated = Spec.IsPaginated(operation);

            writer.Line();
            writer.Doc("summary", $"The response envelope returned by {operation.Path}.");

            string declaration = paginated
                ? $"internal sealed class {EnvelopeName(endpoint)} : IPagedEnvelope<{endpoint.Result.Model}>"
                : $"internal sealed class {EnvelopeName(endpoint)}";

            using (writer.Block(declaration))
            {
                bool first = true;

                foreach (SpecProperty property in properties)
                {
                    if (!first)
                    {
                        writer.Line();
                    }

                    first = false;

                    string type = property.Name == endpoint.Result.Property
                        ? $"{endpoint.Result.Model}[]?"
                        : NullableEnvelopeType(property);

                    writer.Doc("summary", Prose.Clean(property.Description));
                    writer.Line($"[JsonPropertyName(\"{property.Name}\")]");
                    writer.Line($"public {type} {Naming.Pascal(property.Name)} {{ get; init; }}");
                }

                // The interface names the results property `Results`. When the endpoint's result
                // property maps to some other name, satisfy it explicitly rather than renaming
                // the public property away from the wire shape.
                string resultsProperty = Naming.Pascal(endpoint.Result.Property);

                if (paginated && resultsProperty != "Results")
                {
                    writer.Line();
                    writer.Line(
                        $"{endpoint.Result.Model}[]? IPagedEnvelope<{endpoint.Result.Model}>.Results => {resultsProperty};");
                }
            }
        }

        return writer.ToString();
    }
```

- [ ] **Step 4: Emit the Enumerate method and the page-aware List method**

In `tools/MassiveDotNet.CodeGen/Emitter.cs`, replace the `EmitEndpoint` method with:

```csharp
    private void EmitEndpoint(CodeWriter writer, MapEndpoint endpoint)
    {
        SpecOperation operation = spec.Operation(endpoint.OperationId);
        List<SpecParameter> parameters = spec.Parameters(operation);
        bool paginated = Spec.IsPaginated(operation);

        List<Argument> arguments = [.. parameters
            .Select(p => Argument.Create(p, endpoint.Parameters.GetValueOrDefault(p.Name)))
            .OrderByDescending(a => a.Required)];

        string model = endpoint.Result.Model;
        string returnType = paginated ? $"MassivePage<{model}>" : $"{model}[]";
        string callArguments = string.Join(", ", arguments.Select(a => a.Identifier));
        string summary = endpoint.Summary ?? Prose.Clean(Summary(operation));
        bool preserve = endpoint.Summary is not null;

        if (paginated)
        {
            writer.Doc("summary", summary, preserveMarkup: preserve);
            writer.Doc(
                "remarks",
                "Walks every page, requesting the next only once the previous one has been consumed. "
                + "Use <see cref=\"" + endpoint.Method + "Async\"/> to retrieve a single page instead.",
                preserveMarkup: true);

            EmitParameterDocs(writer, arguments, "A token to cancel the traversal.");
            writer.Doc(
                "returns",
                $"Every <c>{endpoint.Result.Property}</c> item across every page.",
                preserveMarkup: true);
            writer.Doc("exception", "The server responded with an error status.", "cref=\"MassiveApiException\"");

            List<string> enumerateSignature = Signature(
                $"public IAsyncEnumerable<{model}> {Naming.Enumerate(endpoint.Method)}Async",
                [.. arguments.Select(a => a.Declaration), "CancellationToken cancellationToken = default"]);

            using (writer.Block(enumerateSignature))
            {
                EmitGuards(writer, arguments);
                writer.Line($"string requestUri = Build{endpoint.Method}Uri({callArguments});");
                writer.Line($"return _transport.EnumerateAsync<{EnvelopeName(endpoint)}, {model}>(");
                writer.Line($"    requestUri, MassiveRestJsonContext.Default.{EnvelopeName(endpoint)}, cancellationToken);");
            }

            writer.Line();
        }

        writer.Doc("summary", summary, preserveMarkup: preserve);
        writer.Doc("remarks", endpoint.Remarks, preserveMarkup: true);

        EmitParameterDocs(writer, arguments, "A token to cancel the request.");

        writer.Doc(
            "returns",
            paginated
                ? $"A single page of <c>{endpoint.Result.Property}</c>, reporting whether more exist."
                : $"The <c>{endpoint.Result.Property}</c> array from the response, empty when the server returned none.",
            preserveMarkup: true);
        writer.Doc("exception", "The server responded with an error status.", "cref=\"MassiveApiException\"");

        List<string> signature = Signature(
            $"public Task<{returnType}> {endpoint.Method}Async",
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

        using (writer.Block($"private async Task<{returnType}> Send{endpoint.Method}Async(string requestUri, CancellationToken cancellationToken)"))
        {
            writer.Line($"{EnvelopeName(endpoint)}? response = await _transport");
            writer.Line($"    .GetAsync(requestUri, MassiveRestJsonContext.Default.{EnvelopeName(endpoint)}, cancellationToken)");
            writer.Line("    .ConfigureAwait(false);");
            writer.Line();

            if (paginated)
            {
                writer.Line($"return new MassivePage<{model}>(");
                writer.Line($"    response?.{Naming.Pascal(endpoint.Result.Property)},");
                writer.Line("    response?.NextUrl is not null,");
                writer.Line("    response?.RequestId);");
            }
            else
            {
                writer.Line($"return response?.{Naming.Pascal(endpoint.Result.Property)} ?? [];");
            }
        }
    }

    private static void EmitGuards(CodeWriter writer, List<Argument> arguments)
    {
        foreach (Argument argument in arguments.Where(a => a is { Required: true, CSharpType: "string" }))
        {
            writer.Line($"ArgumentException.ThrowIfNullOrWhiteSpace({argument.Identifier});");
        }
    }

    private static void EmitParameterDocs(CodeWriter writer, List<Argument> arguments, string cancellationDescription)
    {
        foreach (Argument argument in arguments)
        {
            writer.Doc("param", argument.Description, $"name=\"{argument.Identifier}\"");
        }

        writer.Doc("param", cancellationDescription, "name=\"cancellationToken\"");
    }
```

Note: `Send{Method}Async` reads `response?.RequestId`. Every paginated envelope has a `request_id` property in the spec, so `RequestId` is always generated for them. If a future paginated operation lacks it, this will fail to compile loudly rather than silently drop the value — which is the right failure.

- [ ] **Step 5: Regenerate and inspect the output**

```bash
dotnet run --project tools/MassiveDotNet.CodeGen
git diff --stat src/MassiveDotNet.Rest/Generated/
cat src/MassiveDotNet.Rest/Generated/StocksGroup.g.cs
```

Expected: `StocksGroup.g.cs` now has `EnumerateAggregatesAsync` returning `IAsyncEnumerable<Agg>`, `ListAggregatesAsync` returning `Task<MassivePage<Agg>>`, and `Envelopes.g.cs` declares `: IPagedEnvelope<Agg>` plus a `using MassiveDotNet.Http;`.

- [ ] **Step 6: Confirm the generator is deterministic**

```bash
dotnet run --project tools/MassiveDotNet.CodeGen
git diff --exit-code src/MassiveDotNet.Rest/Generated/
```

Expected: exit code 0 — running twice produces byte-identical output (rule 6).

- [ ] **Step 7: Update the four offline call sites that use the return value**

In `tests/MassiveDotNet.Rest.Tests/StocksAggregatesTests.cs`, four tests bind the result. Change each `Agg[] bars` declaration to a page and read `.Results`:

```csharp
// DeserializesTheDocumentedSampleResponse
MassivePage<Agg> page;
// ...
    page = await client.Stocks.ListAggregatesAsync(
        "AAPL", 1, AggregateTimespan.Day, "2020-01-01", "2020-01-10", cancellationToken: Ct);
}

Agg[] bars = page.Results;
Assert.Equal(2, bars.Length);
```

Apply the same `MassivePage<Agg> page` / `Agg[] bars = page.Results;` shape to:
- `LeavesOtcFalseWhenTheServerOmitsTheField`
- `ComputesTimestampFromTheRawMilliseconds`
- `ReturnsAnEmptyArrayWhenTheServerSendsNoResults` — its assertion becomes `Assert.Empty(page.Results);`

The other tests discard the result and need no change. `RejectsABlankTickerBeforeIssuingARequest` still compiles: `Assert.ThrowsAsync<ArgumentException>` accepts the new `Task<MassivePage<Agg>>` unchanged.

- [ ] **Step 8: Add a test that the documented sample's cursor is now reported**

Append to `tests/MassiveDotNet.Rest.Tests/StocksAggregatesTests.cs`:

```csharp
    [Fact]
    public async Task ReportsThatMorePagesExistWhenTheSampleCarriesACursor()
    {
        // The published sample carries a next_url. Before pagination the SDK deserialized
        // that field and threw it away, so a caller could not tell a complete result from
        // a truncated one.
        StubHandler handler = new(Fixtures.StocksAggregates);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Agg> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListAggregatesAsync(
                "AAPL", 1, AggregateTimespan.Day, "2020-01-01", "2020-01-10", cancellationToken: Ct);
        }

        Assert.True(page.HasMore);
        Assert.Equal("6a7e466379af0a71039d60cc78e72282", page.RequestId);
    }
```

- [ ] **Step 9: Update the three live call sites**

In `tests/MassiveDotNet.IntegrationTests/StocksAggregatesLiveTests.cs`, change each of the three `Agg[] bars = await Client.Stocks.ListAggregatesAsync(…);` to:

```csharp
        Agg[] bars = (await Client.Stocks.ListAggregatesAsync(
            /* …existing arguments, unchanged… */)).Results;
```

Nothing else in that file changes; the assertions all operate on `bars`.

- [ ] **Step 10: Build and run the offline suite**

```bash
dotnet build MassiveDotNet.slnx
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
```

Expected: `0 Warning(s)`; 34 tests pass (33 from Task 2 + the new cursor-reporting test).

- [ ] **Step 11: Verify Native AOT is still clean**

```bash
dotnet publish samples/MassiveDotNet.AotSmokeTest -r osx-arm64 -c Release 2>&1 \
  | grep -E ': (warning|error) (IL|AOT|Trim)?[0-9]{4}' && echo "AOT WARNINGS FOUND" || echo "AOT clean"
```

Expected: `AOT clean`. The generic async iterator and the interface constraint are both statically resolvable, so no IL warning should appear.

- [ ] **Step 12: Commit**

```bash
git add tools/MassiveDotNet.CodeGen/ src/MassiveDotNet.Rest/Generated/ \
        tests/MassiveDotNet.Rest.Tests/StocksAggregatesTests.cs \
        tests/MassiveDotNet.IntegrationTests/StocksAggregatesLiveTests.cs
git commit -m "feat: generate Enumerate and page-aware List for paginated endpoints"
```

---

### Task 4: Prove the generated surface end to end

**Files:**
- Create: `tests/MassiveDotNet.Rest.Tests/StocksPaginationTests.cs`

**Interfaces:**
- Consumes: `StocksGroup.EnumerateAggregatesAsync` and `StocksGroup.ListAggregatesAsync` (Task 3).
- Produces: nothing consumed by later tasks.

Task 2 proved the transport traverses cursors. This proves the *generated* method is wired to it correctly — the right envelope type, the right `JsonTypeInfo`, the right base URI — which is exactly what #8–#17 will replicate 99 more times.

- [ ] **Step 1: Write the failing tests**

Create `tests/MassiveDotNet.Rest.Tests/StocksPaginationTests.cs`:

```csharp
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

public sealed class StocksPaginationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>One aggregate bar, with a distinguishing close price.</summary>
    private static string Bar(double close) =>
        $$"""{"c":{{close}},"h":1,"l":1,"o":1,"t":1577941200000,"v":1}""";

    private static string Page(double close, string? nextUrl) =>
        nextUrl is null
            ? $$"""{"ticker":"AAPL","results":[{{Bar(close)}}],"status":"OK"}"""
            : $$"""{"ticker":"AAPL","next_url":"{{nextUrl}}","results":[{{Bar(close)}}],"status":"OK"}""";

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(PagingStubHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task EnumerateWalksEveryPageOfBars()
    {
        PagingStubHandler handler = new(
            Page(1.0, "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/1/2?cursor=a"),
            Page(2.0, "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/1/2?cursor=b"),
            Page(3.0, nextUrl: null));

        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        List<double> closes = [];

        using (client)
        using (transport)
        {
            await foreach (Agg bar in client.Stocks.EnumerateAggregatesAsync(
                "AAPL", 1, AggregateTimespan.Day, "2020-01-01", "2020-01-10", cancellationToken: Ct))
            {
                closes.Add(bar.Close);
            }
        }

        Assert.Equal([1.0, 2.0, 3.0], closes);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task EnumerateStartsFromTheSameUriThatListWouldRequest()
    {
        PagingStubHandler handler = new(Page(1.0, nextUrl: null));
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await foreach (Agg _ in client.Stocks.EnumerateAggregatesAsync(
                "AAPL", 1, AggregateTimespan.Day, "2020-01-01", "2020-01-10",
                sort: SortOrder.Ascending, limit: 5, cancellationToken: Ct))
            {
            }
        }

        Assert.Equal(
            "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2020-01-01/2020-01-10?sort=asc&limit=5",
            handler.Requests[0].ToString());
    }

    [Fact]
    public void EnumerateRejectsABlankTickerBeforeIssuingARequest()
    {
        PagingStubHandler handler = new(Page(1.0, nextUrl: null));
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            // Not awaited: the guard must fire on the call, not on the first iteration.
            Assert.Throws<ArgumentException>(() => client.Stocks.EnumerateAggregatesAsync(
                "  ", 1, AggregateTimespan.Day, "2020-01-01", "2020-01-10"));
        }

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ListReportsNoFurtherPagesWhenTheServerOmitsTheCursor()
    {
        PagingStubHandler handler = new(Page(1.0, nextUrl: null));
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<Agg> page;

        using (client)
        using (transport)
        {
            page = await client.Stocks.ListAggregatesAsync(
                "AAPL", 1, AggregateTimespan.Day, "2020-01-01", "2020-01-10", cancellationToken: Ct);
        }

        Assert.False(page.HasMore);
        Assert.Single(page.Results);
        Assert.Single(handler.Requests);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail, then pass**

```bash
dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~StocksPaginationTests"
```

If Task 3 is complete these pass immediately. That is expected and fine — they are regression tests for the generated shape, and their value is in failing when a later generator change breaks the wiring. If any fails, the fault is in Task 3's emitter, not here.

Expected: PASS, 4 tests.

- [ ] **Step 3: Run the full offline suite**

```bash
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
```

Expected: 38 tests pass.

- [ ] **Step 4: Commit**

```bash
git add tests/MassiveDotNet.Rest.Tests/StocksPaginationTests.cs
git commit -m "test: cover the generated pagination surface end to end"
```

---

### Task 5: Live traversal, and record the decision

**Files:**
- Create: `tests/MassiveDotNet.IntegrationTests/PaginationLiveTests.cs`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: `StocksGroup.EnumerateAggregatesAsync` (Task 3), `LiveApiTest` base class (existing).
- Produces: nothing.

A fixture asserts the SDK agrees with a recording of the past. Only a live call asserts it agrees with the service today — and multi-page traversal is precisely what a single-page fixture cannot exercise.

- [ ] **Step 1: Write the live test**

Create `tests/MassiveDotNet.IntegrationTests/PaginationLiveTests.cs`:

```csharp
using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Verifies cursor traversal against the real service. Fixtures cannot cover this: they assert
/// the SDK agrees with a recording, whereas crossing a real page boundary asserts the cursor
/// contract still holds today.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PaginationLiveTests : LiveApiTest
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CrossesRealPageBoundaries()
    {
        // limit is per page, so a small limit over a wide window forces several round trips.
        List<Agg> bars = [];

        await foreach (Agg bar in Client.Stocks.EnumerateAggregatesAsync(
            "AAPL",
            1,
            AggregateTimespan.Day,
            new LocalDate(2024, 1, 1),
            new LocalDate(2024, 6, 30),
            limit: 5,
            cancellationToken: Ct))
        {
            bars.Add(bar);

            // Enough to prove several pages were crossed without walking the whole window.
            if (bars.Count >= 40)
            {
                break;
            }
        }

        Assert.Equal(40, bars.Count);

        // Bars must be strictly ordered across the page seam, which is where an incorrectly
        // rebuilt cursor would show up as repeated or skipped windows.
        Assert.Equal(
            bars.Select(b => b.TimestampMilliseconds).Order(),
            bars.Select(b => b.TimestampMilliseconds));

        Assert.Equal(
            bars.Select(b => b.TimestampMilliseconds).Distinct().Count(),
            bars.Count);
    }

    [Fact]
    public async Task ListReportsThatMorePagesExist()
    {
        MassivePage<Agg> page = await Client.Stocks.ListAggregatesAsync(
            "AAPL",
            1,
            AggregateTimespan.Day,
            new LocalDate(2024, 1, 1),
            new LocalDate(2024, 6, 30),
            limit: 5,
            cancellationToken: Ct);

        Assert.Equal(5, page.Results.Length);
        Assert.True(page.HasMore);
        Assert.NotNull(page.RequestId);
    }
}
```

Add `using NodaTime;` at the top if `LocalDate` is not already in scope through the project's implicit usings — check `StocksAggregatesLiveTests.cs` for the existing pattern and match it.

- [ ] **Step 2: Confirm CI still excludes the live tier**

```bash
dotnet test MassiveDotNet.slnx --filter "Category!=Integration" 2>&1 | grep -i "integration"
```

Expected: a line reading `No test matches the given testcase filter 'Category!=Integration' in …MassiveDotNet.IntegrationTests.dll` — compiled, never executed. This is rule 13.

- [ ] **Step 3: Run the live tier locally**

```bash
dotnet test MassiveDotNet.slnx --filter "Category=Integration"
```

Expected: 6 tests pass (4 pre-existing + 2 new). Requires `MASSIVE_API_KEY` in the gitignored `.env`. If it skips, the skip reason names the variable to set.

- [ ] **Step 4: Record decision D14 in the constitution**

In `CLAUDE.md`, add this row to the end of the "Architecture decisions" table:

```markdown
| D14 | Pagination cursors are followed **verbatim**, but only when `next_url` names the same origin as the configured `BaseAddress`. A mismatch throws rather than following. | `next_url` is absolute, carries no key, and is chosen by the response body — so following it unconditionally sends the caller's API key to whatever host a server names, which is rule 11's concern arriving by another route. Verbatim matters independently: the aggregates cursor rewrites a path segment (`2024-01-01` becomes `1704776400000`), so rebuilding a cursor from the original arguments silently restarts the traversal. An opt-out was rejected — it is an option nobody finds before filing a bug, and everybody finds after reading a workaround online. The cost is understood: if Massive ever shards pagination onto a second hostname this throws where a naive client would keep working, which is the correct failure. |
```

- [ ] **Step 5: Document the pagination surface in the conventions section**

In `CLAUDE.md`, under **Conventions**, add after the **Naming** bullet:

```markdown
- **Pagination**: the 100 operations whose success schema declares `next_url` get two methods —
  `ListXxxAsync` returning `MassivePage<T>` (one page, reporting whether more exist) and
  `EnumerateXxxAsync` returning `IAsyncEnumerable<T>` (every page, one in flight at a time).
  The 47 that do not paginate keep returning `T[]`. Pagination is detected from the spec, never
  declared in the map. `Enumerate`/`List` follows the BCL's `Directory.EnumerateFiles` /
  `Directory.GetFiles` distinction; avoid "Stream", which in this SDK means WebSockets.
```

- [ ] **Step 6: Full verification**

```bash
dotnet build MassiveDotNet.slnx
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/
dotnet publish samples/MassiveDotNet.AotSmokeTest -r osx-arm64 -c Release 2>&1 \
  | grep -E ': (warning|error) (IL|AOT|Trim)?[0-9]{4}' && echo "AOT WARNINGS" || echo "AOT clean"
```

Expected: warning-free build, 38 offline tests passing, no generated diff, `AOT clean`.

- [ ] **Step 7: Commit**

```bash
git add tests/MassiveDotNet.IntegrationTests/PaginationLiveTests.cs CLAUDE.md
git commit -m "feat: add live multi-page traversal test and record decision D14"
```

- [ ] **Step 8: Open the follow-up issue for the deferred non-goal**

```bash
gh issue create \
  --title "Pagination: decide whether a runaway cursor needs a page cap" \
  --label "area:rest,type:question" \
  --milestone "v0.1 REST" \
  --body "$(cat <<'EOF'
A server returning a self-referential `next_url` would make `EnumerateXxxAsync` loop forever,
burning quota until rate limiting (#5) surfaces it as a 429.

Deliberately not addressed in the pagination PR: any cap is an arbitrary number, and picking one
without evidence trades a rare hang for a common truncation. Options if this proves real:

- a `MaxPages` option on `MassiveClientOptions`, defaulting to unlimited
- detecting a repeated cursor and throwing
- doing nothing, and letting #5's rate limiting bound it

See `docs/superpowers/specs/2026-09-01-rest-pagination-design.md`, "Non-goals".
EOF
)"
```

---

## Self-Review

**Spec coverage.** Every section of the design maps to a task:

| Spec section | Task |
|---|---|
| D-P1 · loop in the transport, `IPagedEnvelope<TItem>` | 2 |
| D-P2 · `Enumerate` / `List` naming | 3 (`Naming.Enumerate`) |
| D-P3 · `EnumerateXxxAsync` is not an async iterator | 2 (transport split), 3 (generated method), tested in 2 and 4 |
| D-P4 · `MassivePage<T>` carries no cursor | 1 |
| D-P5 · verbatim-within-origin | 2 (`ResolveCursor`), 5 (D14) |
| D-P6 · pagination detected from the spec | 3 (`Spec.IsPaginated`) |
| D-P7 · `List` returns two shapes | 3 (emitter branch) |
| Testing · multi-page | 2, 4, 5 |
| Testing · no prefetch | 2 |
| Testing · cancellation and early exit | 2 |
| Testing · host mismatch | 2 |
| Testing · retained memory | 2 |
| Testing · live tier | 5 |
| Non-goals · page cap | 5 (issue filed) |

**Placeholder scan.** No TBD/TODO. Every code step carries the actual code. The only judgement call left to the implementer is Task 5 Step 1's `using NodaTime;`, which names the file to copy the pattern from.

**Type consistency.** Checked across tasks:
- `MassivePage<T>(T[]?, bool, string?)` — defined Task 1, called by generated code in Task 3 Step 4, asserted in Tasks 3, 4, 5.
- `IPagedEnvelope<TItem>.Results` / `.NextUrl` — defined Task 2, implemented by `FakePage` in Task 2, emitted in Task 3 Step 3.
- `EnumerateAsync<TEnvelope, TItem>(string, JsonTypeInfo<TEnvelope>, CancellationToken)` — defined Task 2 Step 5, called verbatim by the emitter in Task 3 Step 4.
- `Naming.Enumerate("ListAggregates")` → `"EnumerateAggregates"`, so the generated method is `EnumerateAggregatesAsync` — the name used in Tasks 4 and 5.
- `PagingStubHandler.Requests` / `.Authorizations` — defined Task 2, used in Tasks 2 and 4.

**Known risk.** Task 3 Step 4 emits an XML `<see cref="…Async"/>` for the `List` counterpart from inside the `Enumerate` doc block. If `cref` resolution fails, CS1574 becomes an error under `TreatWarningsAsErrors`. Task 3 Step 10's build catches it immediately; the fix is to drop the `cref` to plain `<c>` text.
