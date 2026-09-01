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
    public async Task TraversesThroughAPageWithNoResults()
    {
        // A page with no results but a further cursor is ordinary service behaviour -- for
        // example a time window that matched nothing -- and is not the end of the traversal;
        // only a null next_url is. The first page here omits "results" entirely, exercising the
        // envelope.Results ?? [] fallback the same way an empty array would.
        PagingStubHandler handler = new(
            $$"""{"next_url":"{{Cursor("a")}}","status":"OK"}""",
            Page(1, nextUrl: null));

        using MassiveHttpTransport transport = Create(handler);

        List<int> seen = [];
        await foreach (int value in transport.EnumerateAsync<FakePage, int>(
            StartUri, TestJsonContext.Default.FakePage, Ct))
        {
            seen.Add(value);
        }

        Assert.Equal([1, 2], seen);
        Assert.Equal(2, handler.Requests.Count);
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
    public async Task RefusesToFollowAProtocolRelativeCursorPointingAtAnotherHost()
    {
        // A network-path reference ("//host/path") reports IsAbsoluteUri == false, so a check
        // gated on that flag -- rather than on the URI actually resolved against the base --
        // would never reach the comparison below for exactly this shape, even though resolving
        // it still hands the request to the host it names.
        PagingStubHandler handler = new(
            Page(1, "//evil.example/v3/reference/things?cursor=a"),
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

        // As with the absolute-URL case above: the key must never have travelled to the
        // foreign host, so only the first, same-origin request was ever issued.
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RefusesAMalformedRelativeCursorRatherThanThrowingUriFormatException()
    {
        // "///evil.example/x" parses as a relative URI on its own -- Uri.TryCreate with
        // RelativeOrAbsolute accepts it -- but combining it with the base address is what fails.
        // EnumerateAsync's documented contract is MassiveApiException for a bad cursor, so that
        // failure must surface the same way rather than as an unhandled UriFormatException from
        // deep inside Uri's own constructor.
        PagingStubHandler handler = new(Page(1, "///evil.example/x"), Page(3, nextUrl: null));

        using MassiveHttpTransport transport = Create(handler);

        await Assert.ThrowsAsync<MassiveApiException>(async () =>
        {
            await foreach (int _ in transport.EnumerateAsync<FakePage, int>(
                StartUri, TestJsonContext.Default.FakePage, Ct))
            {
            }
        });

        Assert.Single(handler.Requests);
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
            string body = string.Join(',', Enumerable.Range(i * 10_000, 10_000));
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

        // 500 pages x 10,000 ints is ~20 MB of int arrays alone if pages accumulate (plus
        // ~500 small envelopes and cursor strings on top) -- comfortably above the 8 MB bound.
        // The bound is deliberately loose: this is asserting the absence of accumulation, not a
        // precise figure.
        //
        // The bound is 8 MB, not the originally planned 512 KB, because of GC measurement
        // noise: run alongside certain sibling tests in this class, GC.GetTotalMemory reports a
        // fixed, order-dependent delta up to ~2.77 MB that does not scale with items consumed
        // (confirmed by sampling total memory at intervals through the traversal, which shows
        // the delta oscillating rather than climbing) and disappears when this test runs alone
        // or as part of the full unfiltered suite. That is a GC heap-segment sizing artifact
        // from neighboring tests, not accumulation in EnumerateAsync -- which yields one item at
        // a time and lets each page's envelope become garbage once its results are exhausted
        // (see EnumerateCoreAsync). At 1,000 ints per page the ~2 MB of true accumulation signal
        // was smaller than that ~2.77 MB noise ceiling, so no bound could be both stable and
        // meaningful -- an 8 MB bound would have been vacuous, passing even if every page were
        // retained. Raising the per-page item count to 10,000 raises the signal to ~20 MB
        // (about 2.5x above this 8 MB bound) while the noise ceiling stays about 3x below it, so
        // the bound now sits in the gap between "genuine accumulation" and "measurement noise."
        Assert.True(
            after - before < 8 * 1024 * 1024,
            $"Retained {after - before:N0} bytes after {Pages} pages; pages appear to accumulate.");

        Assert.True(total > 0);
    }
}
