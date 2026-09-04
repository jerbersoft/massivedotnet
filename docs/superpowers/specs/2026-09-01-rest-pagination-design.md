# REST pagination: cursor traversal as `IAsyncEnumerable<T>`

Issue: [#1](https://github.com/jerbersoft/massivedotnet/issues/1) · Milestone: v0.1 REST · Date: 2026-09-01

## Why

100 of the 147 REST operations return a `next_url` cursor. Today the SDK deserializes that field
into the response envelope and then throws it away: `ListAggregatesAsync` returns `Agg[]` and the
caller has no way to learn that more data exists.

That is the single largest structural gap left by the vertical slice, and issues #8–#17 — the ten
endpoint groups that make up the rest of v0.1 — all inherit whatever shape is chosen here.

## What the wire actually does

Verified against the live API on 2026-09-01 rather than inferred from the OpenAPI description,
which says only *"If present, this value can be used to fetch the next page of data."*

| Question | Finding |
|---|---|
| Is `next_url` absolute? | Yes — `https://api.massive.com/v3/reference/splits?cursor=…` |
| Does it carry the API key? | **No.** Credentials must be re-applied to every page. |
| Which host does it name? | It echoes the host that was called: `api.polygon.io` in, `api.polygon.io` out. |
| How does traversal terminate? | `next_url` is simply absent on the final page. |
| Does an exactly-full final page still emit one? | **No.** `AAPL` has 5 splits; `limit=5` returned 5 results and no cursor. There is no trailing empty page. |
| What is in the cursor? | base64 of the original query: `ap=2&as=&limit=2&order=desc&sort=execution_date&ticker=AAPL` |

One finding changes the design more than the others. The aggregates cursor **moves state into the
path**:

```
request   /v2/aggs/ticker/AAPL/range/1/day/2024-01-01/2024-06-30?limit=5
next_url  /v2/aggs/ticker/AAPL/range/1/day/1704776400000/2024-06-30?cursor=bGltaXQ9NSZzb3J0PWFzYw
                                            ^^^^^^^^^^^^^ the `from` segment was rewritten
```

A cursor is therefore not "the original request plus a `cursor` parameter". It must be followed
verbatim; any attempt to rebuild it from the caller's arguments loses the rewritten path segment
and silently restarts the traversal.

None of the 100 paginated operations are deprecated, so all 100 need this surface.

## Decisions

### D-P1 · The page loop lives in the transport, not in generated code

Core gains one interface and one method. Generated envelopes implement the interface; generated
endpoint methods delegate to the transport.

```csharp
namespace MassiveDotNet.Http;

/// <summary>A response envelope that carries a page of results and an optional cursor.</summary>
public interface IPagedEnvelope<TItem>
{
    TItem[]? Results { get; }
    string? NextUrl { get; }
}

public async IAsyncEnumerable<TItem> EnumerateAsync<TEnvelope, TItem>(
    string requestUri,
    JsonTypeInfo<TEnvelope> typeInfo,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
    where TEnvelope : class, IPagedEnvelope<TItem>
```

The alternative was emitting the traversal loop into each endpoint. That means 100 copies of the
same cancellation, termination, and host-validation logic — tested once and trusted ninety-nine
times. One implementation has one set of tests.

The interface is preferred over passing `Func<TEnvelope, TItem[]?>` and `Func<TEnvelope, string?>`
selectors: no delegate allocation per call, and a constraint the AOT compiler resolves statically.

Generated envelopes are `internal`; implementing a public interface from an internal class is
legal C# and keeps the envelope types out of the public surface, where they do not belong.

### D-P2 · Two methods per paginated endpoint, and neither of them lies

```csharp
// the common case
await foreach (Agg bar in client.Stocks.EnumerateAggregatesAsync(…, ct)) { … }

// one page, and the caller can see that more exist
MassivePage<Agg> page = await client.Stocks.ListAggregatesAsync(…, ct);
```

`Enumerate` / `List` follows the BCL's own established distinction — `Directory.EnumerateFiles`
returns a lazy sequence, `Directory.GetFiles` returns a materialized array. It is a convention
.NET developers already know.

The name `Stream` was considered and rejected. Three unrelated things in this SDK would answer to
it: the deferred zero-copy work, the v0.2 WebSocket feeds, and this. Overloading the term across a
memory optimization, a real-time protocol, and multi-request iteration guarantees confusion.

### D-P3 · `EnumerateXxxAsync` is not an async iterator

`RequestUriBuilder` is a `ref struct` and cannot appear in an async method. That constraint already
forces the three-method split documented in CLAUDE.md. It resolves unusually cleanly here:

```csharp
public IAsyncEnumerable<Agg> EnumerateAggregatesAsync(
    string ticker, /* … */, CancellationToken cancellationToken = default)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(ticker);
    string requestUri = BuildListAggregatesUri(/* … */);
    return _transport.EnumerateAsync<GetStocksAggregatesResponse, Agg>(
        requestUri, MassiveRestJsonContext.Default.GetStocksAggregatesResponse, cancellationToken);
}
```

Because the method merely *returns* an `IAsyncEnumerable` rather than being one, the ref struct is
legal inside it, and argument validation throws at the call site instead of being deferred to the
first `MoveNextAsync`. Deferred validation is a known trap in iterator APIs; `Directory.Enumerate*`
avoids it the same way.

Four generated methods per paginated endpoint: `EnumerateXxxAsync`, `ListXxxAsync`, `BuildXxxUri`,
`SendXxxAsync`.

### D-P4 · `MassivePage<T>` carries no cursor

```csharp
namespace MassiveDotNet;

public readonly record struct MassivePage<T>
{
    public T[] Results { get; init; }    // never null; empty when the server returned none
    public bool HasMore { get; init; }   // next_url was present
    public string? RequestId { get; init; }
}
```

A `readonly record struct` because one is produced per call, there is no meaningful null page, and
it costs no allocation — consistent with decision D4's reasoning even though a page is neither a
tick type nor reference data.

The raw `next_url` is deliberately absent. Exposing a cursor with no API that accepts one back is a
tease: callers would read the property and find nothing to do with it. Resume-from-cursor is a real
feature and gets its own issue, because it reopens host validation for a *caller*-supplied URL,
which is a different threat model from a *server*-supplied one.

Adding a property to this struct later is not a breaking change, so nothing is foreclosed.

### D-P5 · Cursors are followed verbatim, but only within the configured origin

`next_url` is server-supplied, absolute, and carries no key — so the SDK re-attaches
`Authorization: Bearer <key>` to it. Following it unconditionally means sending the user's
credential to whatever host a response body names. That is a credential-exfiltration path adjacent
to constitution rule 11.

| `next_url` | Behaviour |
|---|---|
| Absolute, same scheme + host + port as `BaseAddress` | Follow verbatim |
| Absolute, different origin | Throw `MassiveApiException`; the key is never sent |
| Relative | Resolve against `BaseAddress`, then apply the **same origin check to the resolved URI**; follow only if it passes |
| Relative but uncombinable with the base (`///evil.example/x`) | Throw `MassiveApiException` — the documented failure for a bad cursor, not a raw `UriFormatException` |
| Blank (`""` or whitespace) | Not a cursor. End the traversal. |
| Any form, but `BaseAddress` is `null` | Throw `InvalidOperationException` — validation is impossible, so do not guess |

**Resolution precedes validation, and the order is load-bearing.** A network-path reference such as
`//evil.example/x` is *relative* per RFC 3986 — `Uri.IsAbsoluteUri` reports `false` for it — yet it
supplies its own authority, so resolving it against `https://api.massive.com/` yields
`https://evil.example/x`. A check gated on "is this cursor absolute?" therefore never runs for
exactly the shape it most needs to catch, and the API key travels to a host the response body
named. Resolving first collapses every shape to one absolute URI, and the single origin comparison
then always sees the authority the request will actually be sent to.

The blank cursor is the mirror-image trap. `""` also resolves — to the base address itself — so it
*passes* the origin check and re-requests the first page indefinitely, yielding duplicates and
burning quota. An empty string is not a URL, and `"next_url": ""` is a common way to say "no next
page", so it ends the traversal rather than being followed. `MassivePage<T>.HasMore` uses the same
test, so the single-page and traversing surfaces never disagree about one response.

This is a comparison, not a reconstruction. The path and query are copied untouched, which the
rewritten `from` segment in "What the wire actually does" makes mandatory.

The cost of being wrong in the other direction is understood: if Massive ever legitimately shards
pagination onto a second hostname, this throws where a naive client would keep working. That is the
correct failure — loud, and not a leaked key. An opt-out was considered and rejected as an option
nobody would find before filing a bug, and everybody would find after reading a workaround online.

Recorded in CLAUDE.md as **decision D14**.

### D-P6 · Pagination is detected from the spec, never declared in the map

The success schema either has a `next_url` property or it does not. The generator reads it.

Nothing is hand-maintained, so nothing can drift; and if Massive paginates an endpoint that
previously did not, the next spec sync regenerates the streaming surface without anyone noticing it
needed to. This follows the map's stated principle: it carries only what the OpenAPI description
*cannot* express.

### D-P7 · `ListXxxAsync` returns two different shapes

- Paginated (100 operations): `Task<MassivePage<T>>`
- Not paginated (47 operations): `Task<T[]>`, unchanged

A uniform `MassivePage<T>` everywhere would mean 47 endpoints returning a page whose `HasMore` is
permanently `false` — uniform, and dishonest about what the endpoint can do. The asymmetry is the
honest encoding: a page type exists where paging exists.

## Scope

This PR lands the mechanism and proves it on the one endpoint already shipped.

```
src/MassiveDotNet/MassivePage.cs                 new
src/MassiveDotNet/Http/IPagedEnvelope.cs         new
src/MassiveDotNet/Http/MassiveHttpTransport.cs   + EnumerateAsync, + cursor validation
tools/MassiveDotNet.CodeGen/Spec.cs              + paginated-operation detection
tools/MassiveDotNet.CodeGen/Emitter.cs           + Enumerate emission, page-aware List
src/MassiveDotNet.Rest/Generated/                regenerated
tests/MassiveDotNet.Rest.Tests/                  pagination tests
CLAUDE.md                                        + D14
```

`CoverageBaseline` stays at 1. Adding endpoint groups is #8–#17 and depends on this landing first.

## Testing

Four structural tests, all driven through the public API against a stubbed handler, per the
constitution's testing convention.

1. **Multi-page traversal.** Stub serves three pages, the last without `next_url`. Assert every item
   arrives in order and that exactly three requests were issued.
2. **No prefetch.** The stub records request order interleaved with item consumption; assert page
   *N+1* is requested only after page *N* is fully consumed. This is the acceptance criterion
   stated directly, and it is the strongest guarantee in the set.
3. **Cancellation and early exit.** Cancelling the token mid-stream, and `break`ing out of the
   `await foreach`, each stop without issuing a further request.
4. **Host mismatch.** A `next_url` pointing off-origin throws `MassiveApiException`, exactly one
   request was made, and no request ever carried the key to the foreign host.

A fifth criterion — *"allocation per page is bounded and does not grow with total pages traversed"* —
is asserted on **retained** memory, not allocated. Total allocated bytes necessarily grow with page
count, since each page deserializes a new array; what must stay flat is what is still reachable
after the traversal. The assertion uses generous bounds and is treated as secondary evidence.
Test 2 is what actually proves the property, by demonstrating that no page is fetched before its
predecessor is consumed.

The live tier gains one test: a real multi-page traversal, which is precisely what a single-page
fixture cannot verify. It goes in `MassiveDotNet.IntegrationTests`, runs locally, and is excluded
from CI by category (rule 13).

## Non-goals

- **Resume from a cursor.** Separate issue; needs its own threat model for caller-supplied URLs.
- **A page-count cap.** A server returning a self-referential `next_url` would loop forever. Any
  cap is arbitrary policy, and rate limiting (#5) surfaces the condition as a 429 regardless.
  Worth an issue; not worth inventing a number here.
  *Settled by D29 (#30): still no cap, but a cursor identical to the one just followed now throws,
  which bounds the self-referential case without inventing a number.*
- **Auto-raising `limit` to minimise round trips.** `limit` stays whatever the caller passed, and is
  documented as per-page rather than total.
- **`IAsyncEnumerable<MassivePage<T>>`.** Page-level iteration is a third surface serving a use case
  nobody has asked for yet.
