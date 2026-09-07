# WebSocket Transport Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `MassiveDotNet.WebSocket` — the fourth package — with connection, message authentication, subscribe and unsubscribe with acknowledgement counting, transparent reconnect with backoff and replay, and bounded per-topic sequences that drop the oldest event and count it, proven end to end on stock trades and quotes against a fake socket, with allocation gates and a live tier pinning what this key can reach.

**Architecture:** An internal `MassiveStreamConnection` owns one `IMassiveWebSocket`, the handshake, the read loop, and the reconnect state machine. Per-market façades (`MassiveStockStream`) expose typed topics; each subscribed topic owns one bounded `Channel<T>` whose `itemDropped` callback increments a counter, and the read loop never blocks on a writer. Events parse straight off `Utf8JsonReader` through core's `JsonValueReader` (D32's primitive), with tickers interned through a capped pool and condition codes held in an inline array, so the steady state allocates nothing per event. `ClientWebSocket` is in-box, so no package is added.

**Tech Stack:** .NET 10, C# latest, xUnit v3, `System.Text.Json` with hand-written `JsonConverter<T>` (there is no spec to generate from), `System.Threading.Channels`, NodaTime 3.3.3. No new package dependencies.

**Spec:** `docs/superpowers/specs/2026-09-07-websocket-transport-design.md` (D-W1 through D-W11).

## Global Constraints

Copied from `CLAUDE.md` and the spec. Every task inherits these.

- **Rule 3** — No reflection-based serialization. Every event type carries a hand-written
  `JsonConverter<T>` reading tokens directly; nothing uses `JsonSerializer.Deserialize` without a
  `JsonTypeInfo`. The AOT smoke publish must stay at zero IL warnings.
- **Rule 4** — `src/Directory.Build.props` already sets `IsAotCompatible`, which implies
  `IsTrimmable`, `EnableAotAnalyzer`, and `EnableTrimAnalyzer`. The new project inherits it by
  living under `src/`; no property is set in its `.csproj`.
- **Rule 5** — There are no `.g.cs` files in this package. `Epoch` moving in Task 2 *does* change
  generated output, so that task regenerates and commits the regenerated files with the change.
- **Rule 6** — After Task 2, `dotnet run --project tools/MassiveDotNet.CodeGen` twice must produce
  byte-identical output; `git diff --exit-code src/` after a regeneration must be clean.
- **Rule 7** — Core takes no new package. `.WebSocket` references **core only**:
  `System.Net.WebSockets.ClientWebSocket` and `System.Threading.Channels` are both in-box on
  net10.0, so the restore graph gains nothing.
- **Rule 8** — `Microsoft.Extensions.*` appears only in
  `MassiveDotNet.Extensions.DependencyInjection`. The `ILogger` bridge of D-W5 lives there and
  nowhere else; `.WebSocket` must never reference it.
- **Rule 9** — `TreatWarningsAsErrors` is on. An **unused `using` fails the build** (IDE0005), as do
  IDE0035, IDE0051, IDE0052. CA1305 (format provider), CA1307/CA1310 (`StringComparison`), and
  CA1861 (hoist constant arrays) apply to test code too.
- **Rule 10** — Every public member of the new library carries XML documentation, or CS1591 fails
  the build.
- **Rule 11** — API keys are never logged, echoed in exception messages, or written to disk. This
  package is where that rule gets hardest, because the key travels in a **frame body** (D-W9): the
  auth frame is never rendered into an exception, never traced, and its buffer is cleared after the
  send. Task 6 proves it with a test. Live tests read the key from the gitignored `.env`; never
  open, print, or echo that file.
- **Rule 12** — NodaTime only. No BCL `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, or
  `TimeSpan` may be *named* anywhere under `src`, `tests`, `samples`, `tools`, or `benchmarks`.
  `src` is already scanned recursively, so the new project is covered the moment it exists. Two
  boundary crossings arrive with this work and are converted inline:
  `ClientWebSocketOptions.KeepAliveInterval` ← `keepAlive.ToTimeSpan()`, and
  `CancellationTokenSource.CancelAfter` ← `cts.CancelAfter(timeout.ToTimeSpan())`. `Task.Delay` for
  backoff uses the existing pattern `Task.Delay(delay.ToTimeSpan(), cancellationToken)`.
- **Rule 13** — CI runs offline only. Every live test derives from `LiveApiTest`
  (`[Trait("Category", "Integration")]`) and lives in `tests/MassiveDotNet.IntegrationTests`. No
  offline test class may carry `LiveTests` in its name.

### Fixed values

These are the plan's, not the spec's — the spec set the shapes and left the numbers. Use them
verbatim; do not invent alternatives.

| Setting | Value | Why this number |
|---|---|---|
| `TopicBufferCapacity` | `1024` | About 96 KB per topic at ~96 bytes an event. Large enough to ride a burst, small enough that eight topics is under a megabyte. |
| `TickerPoolCapacity` | `16384` | Above the ~11,000 US equity universe, so a `T.*` subscription interns every ticker it will ever see and still has headroom. |
| `MaxMessageBytes` | `4194304` (4 MiB) | A hard ceiling on frame reassembly. Nothing observed came close; this exists so a server cannot make the client allocate without limit. |
| `HandshakeTimeout` | `Duration.FromSeconds(10)` | Covers connect, auth, and the subscribe acknowledgement window. |
| `KeepAliveInterval` | `Duration.FromSeconds(20)` | Below any reasonable idle timeout; no heartbeat was observed from the server, so the client drives it. |
| `InitialBackoff` | `Duration.FromMilliseconds(500)` | |
| `MaxBackoff` | `Duration.FromSeconds(30)` | |
| `BackoffMultiplier` | `2.0` | |
| `Jitter` | `0.2` | ±20%, so a fleet of reconnecting clients does not synchronise. |
| Inline condition capacity | `8` | Covers every observed condition set; more than 8 falls back to a heap array rather than truncating, because silent truncation is the data loss this SDK refuses. |

### Observed wire facts

Established 2026-09-07 by probing the live service. Do not re-derive these; do not contradict them.

```
connect   wss://socket.massive.com/stocks
<--       [{"ev":"status","status":"connected","message":"Connected Successfully"}]
-->       {"action":"auth","params":"<key>"}
<--       [{"ev":"status","status":"auth_success","message":"authenticated"}]
-->       {"action":"subscribe","params":"T.AAPL,A.AAPL"}
<--       [{"ev":"status","status":"success","message":"subscribed to: T.AAPL"},
           {"ev":"status","status":"success","message":"subscribed to: A.AAPL"}]
```

- Everything inbound is a JSON **array**, including control responses. Outbound control messages are
  single objects.
- One frame can carry several events. The parser must never assume one event per frame.
- A bad key answers `auth_failed` / `"authentication failed"`. An **unentitled market** answers
  `auth_failed` with `"Your plan doesn't include websocket access. Visit
  https://massive.com/pricing to upgrade."` — same status, different prose.
- After `auth_failed` the server **closes without a close handshake**, so `ReceiveAsync` throws
  rather than reporting a Close frame.
- An unknown **ticker** on a valid topic (`T.NOTATICKER`) is acknowledged and yields no data.
- An unknown **topic** (`ZZ.AAPL`, `QQ.AAPL`) is **silently dropped**: no acknowledgement, no error.
- `T.*` wildcards are accepted and acknowledged like any other subscription.
- Eight feed stems are provisioned on each of `massive.com` and `polygon.io`: `socket`, `delayed`,
  `business`, `polyfeed`, `polyfeedplus`, `nasdaqfeed`, `starterfeed`, `delayed-business`.
  `launchpad` is **not** provisioned on either domain.
- On the key in local `.env`, only `stocks` authenticates. The other five markets answer
  `auth_failed` with the entitlement message.

### Style

Explicit types, never `var`. Collection expressions (`[]`, `[.. x]`). `is not { } x` null patterns.
File-scoped namespaces. Raw string literals for JSON. Comments explain *why*, never *what*. Match
the surrounding code.

### Working tree and commits

Work happens in place on the existing branch **`feat/websocket-transport`** (created from master,
already holding the design spec at `e7b83a9`), not in a worktree, because Task 14's live tier needs
the repository's gitignored `.env`.

`CLAUDE.md` says do not commit or push unless asked. The steps below include commits: the user chose
the brainstorm-to-plan workflow, which authorizes commits **on this feature branch only**. Nothing
is pushed and nothing is merged without a separate request. Commit messages end with the trailer:

```
Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB
```

## File Structure

```
src/MassiveDotNet/
  Epoch.cs                              MOVED from .Rest in Task 2, now public

src/MassiveDotNet.WebSocket/
  MassiveDotNet.WebSocket.csproj        References core only
  MassiveFeeds.cs                       8 hosts + Legacy nested class
  MassiveMarket.cs                      6 markets, rendered as a path segment
  MassiveStreamOptions.cs               Duration throughout; Validate()
  MassiveStreamRetryOptions.cs          Backoff triple + jitter
  MassiveStreamClient.cs                Entry point; owns the connection
  MassiveStockStream.cs                 Stocks façade: typed topic subscriptions
  StockTopic.cs                         Trades, Quotes (enum; #21 adds six)
  MassiveTopicSubscription.cs           IAsyncEnumerable<T> + DroppedCount
  MassiveStreamException.cs             Base
  MassiveStreamAuthenticationException.cs
  MassiveStreamSubscriptionException.cs Unacknowledged subscriptions
  Events/
    StockTrade.cs                       From the published sample
    StockQuote.cs                       From the published sample
    ConditionSet.cs                     Inline array of 8, heap overflow beyond
  Internal/
    IMassiveWebSocket.cs                The seam ClientWebSocket cannot provide
    ClientWebSocketAdapter.cs           The real implementation
    MassiveStreamConnection.cs          Handshake, read loop, reconnect, dispatch
    SubscriptionRegistry.cs             What reconnect replays
    TickerPool.cs                       Capped intern table
    FrameReader.cs                      Reassembly to MaxMessageBytes
    StatusMessage.cs                    Parsed control frame
    StockTradeConverter.cs              JsonConverter<StockTrade>
    StockQuoteConverter.cs              JsonConverter<StockQuote>

src/MassiveDotNet.Extensions.DependencyInjection/
  MassiveStreamServiceCollectionExtensions.cs   AddMassiveStream + the ILogger bridge

tests/MassiveDotNet.WebSocket.Tests/           New offline project
  FakeWebSocket.cs                      Scripted IMassiveWebSocket double
  HandshakeTests.cs
  SubscriptionTests.cs
  ReadLoopTests.cs
  BackpressureTests.cs
  ReconnectTests.cs
  EventParsingTests.cs
  TickerPoolTests.cs
  AllocationTests.cs
  Fixtures/
    stock-trade.json                    Verbatim published sample
    stock-quote.json                    Verbatim published sample

tests/MassiveDotNet.IntegrationTests/
  StreamHandshakeLiveTests.cs           Stocks; the five unentitled markets; launchpad
```

One file per responsibility, so a task's diff is reviewable on its own. `MassiveStreamConnection` is
the one file that will grow; it is split from the façades deliberately so the transport can be
tested without any event type existing.

---

### Task 1: The package, the feed hosts, and the enforcement that notices it

**Files:**
- Create: `src/MassiveDotNet.WebSocket/MassiveDotNet.WebSocket.csproj`
- Create: `src/MassiveDotNet.WebSocket/MassiveFeeds.cs`
- Create: `src/MassiveDotNet.WebSocket/MassiveMarket.cs`
- Create: `tests/MassiveDotNet.WebSocket.Tests/MassiveDotNet.WebSocket.Tests.csproj`
- Create: `tests/MassiveDotNet.WebSocket.Tests/FeedTests.cs`
- Modify: `MassiveDotNet.slnx`
- Modify: `tests/MassiveDotNet.Rest.Tests/MassiveDotNet.Rest.Tests.csproj` (reference the new package)
- Modify: `tests/MassiveDotNet.Rest.Tests/TemporalTypeTests.cs:48-60`
- Modify: `CLAUDE.md:244`

**Interfaces:**
- Consumes: nothing.
- Produces: `MassiveDotNet.WebSocket.MassiveFeeds` (static `Uri` properties `RealTime`, `Delayed`,
  `Business`, `DelayedBusiness`, `PolyFeed`, `PolyFeedPlus`, `NasdaqFeed`, `StarterFeed`, plus
  nested `MassiveFeeds.Legacy` with the same eight on `polygon.io`);
  `MassiveDotNet.WebSocket.MassiveMarket` (enum: `Stocks`, `Options`, `Indices`, `Forex`, `Crypto`,
  `Futures`) and `MassiveMarket.ToPathSegment()` returning the lowercase segment.

Start here because `EveryShippedLibraryIsInspected` fails the moment the project directory exists —
the enforcement and the package have to land together or the tree is red.

- [ ] **Step 1: Write the failing test**

`tests/MassiveDotNet.WebSocket.Tests/FeedTests.cs`:

```csharp
using MassiveDotNet.WebSocket;

namespace MassiveDotNet.WebSocket.Tests;

public class FeedTests
{
    public static TheoryData<Uri, string> ProductionFeeds() => new()
    {
        { MassiveFeeds.RealTime, "wss://socket.massive.com/" },
        { MassiveFeeds.Delayed, "wss://delayed.massive.com/" },
        { MassiveFeeds.Business, "wss://business.massive.com/" },
        { MassiveFeeds.DelayedBusiness, "wss://delayed-business.massive.com/" },
        { MassiveFeeds.PolyFeed, "wss://polyfeed.massive.com/" },
        { MassiveFeeds.PolyFeedPlus, "wss://polyfeedplus.massive.com/" },
        { MassiveFeeds.NasdaqFeed, "wss://nasdaqfeed.massive.com/" },
        { MassiveFeeds.StarterFeed, "wss://starterfeed.massive.com/" },
    };

    [Theory]
    [MemberData(nameof(ProductionFeeds))]
    public void ProductionFeedsAreAbsoluteWebSocketUris(Uri feed, string expected)
    {
        Assert.True(feed.IsAbsoluteUri);
        Assert.Equal("wss", feed.Scheme);
        Assert.Equal(expected, feed.ToString());
    }

    [Fact]
    public void LegacyFeedsMirrorProductionOnThePolygonDomain()
    {
        Assert.Equal("wss://socket.polygon.io/", MassiveFeeds.Legacy.RealTime.ToString());
        Assert.Equal("wss://delayed-business.polygon.io/", MassiveFeeds.Legacy.DelayedBusiness.ToString());
    }

    // launchpad is named in issue #20 but presents the ingress default certificate on both
    // domains (2026-09-07), so shipping a property for it would hand a caller a TLS failure
    // from a name the SDK told them was real. This test is the reminder, not a formality.
    [Fact]
    public void NoLaunchpadFeedIsExposed()
    {
        Assert.DoesNotContain(
            typeof(MassiveFeeds).GetProperties(),
            property => property.Name.Contains("Launchpad", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(MassiveMarket.Stocks, "stocks")]
    [InlineData(MassiveMarket.Options, "options")]
    [InlineData(MassiveMarket.Indices, "indices")]
    [InlineData(MassiveMarket.Forex, "forex")]
    [InlineData(MassiveMarket.Crypto, "crypto")]
    [InlineData(MassiveMarket.Futures, "futures")]
    public void MarketsRenderAsLowercasePathSegments(MassiveMarket market, string expected) =>
        Assert.Equal(expected, market.ToPathSegment());
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests`
Expected: FAIL — the project does not exist yet.

- [ ] **Step 3: Create the package**

`src/MassiveDotNet.WebSocket/MassiveDotNet.WebSocket.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <RootNamespace>MassiveDotNet.WebSocket</RootNamespace>
    <PackageId>MassiveDotNet.WebSocket</PackageId>
    <Description>WebSocket streaming client for the Massive (formerly Polygon.io) market data platform: real-time trades, quotes, and aggregates over the platform's feed hosts.</Description>
  </PropertyGroup>

  <ItemGroup>
    <!-- Core only. ClientWebSocket and System.Threading.Channels are both in-box on net10.0,
         so this package adds nothing to a consumer's restore graph (rules 7 and 8). -->
    <ProjectReference Include="../MassiveDotNet/MassiveDotNet.csproj" />
  </ItemGroup>

</Project>
```

`src/MassiveDotNet.WebSocket/MassiveFeeds.cs`:

```csharp
namespace MassiveDotNet.WebSocket;

/// <summary>
/// Well-known feed hosts for the Massive streaming platform.
/// </summary>
/// <remarks>
/// Eight hosts, established by certificate on 2026-09-07: both <c>massive.com</c> and
/// <c>polygon.io</c> answer DNS with wildcard records, so a name resolving proves nothing, while a
/// provisioned host presents a certificate naming itself and an unprovisioned one falls through to
/// the ingress default. A <c>launchpad</c> host is named in issue #20 but is not provisioned on
/// either domain, so no property exposes it.
/// </remarks>
public static class MassiveFeeds
{
    /// <summary>The real-time feed, <c>wss://socket.massive.com</c>.</summary>
    public static Uri RealTime { get; } = new("wss://socket.massive.com", UriKind.Absolute);

    /// <summary>The 15-minute delayed feed, <c>wss://delayed.massive.com</c>.</summary>
    public static Uri Delayed { get; } = new("wss://delayed.massive.com", UriKind.Absolute);

    /// <summary>The business-plan feed, <c>wss://business.massive.com</c>.</summary>
    public static Uri Business { get; } = new("wss://business.massive.com", UriKind.Absolute);

    /// <summary>The delayed business-plan feed, <c>wss://delayed-business.massive.com</c>.</summary>
    public static Uri DelayedBusiness { get; } = new("wss://delayed-business.massive.com", UriKind.Absolute);

    /// <summary>The PolyFeed host, <c>wss://polyfeed.massive.com</c>.</summary>
    public static Uri PolyFeed { get; } = new("wss://polyfeed.massive.com", UriKind.Absolute);

    /// <summary>The PolyFeed Plus host, <c>wss://polyfeedplus.massive.com</c>.</summary>
    public static Uri PolyFeedPlus { get; } = new("wss://polyfeedplus.massive.com", UriKind.Absolute);

    /// <summary>The Nasdaq Basic host, <c>wss://nasdaqfeed.massive.com</c>.</summary>
    public static Uri NasdaqFeed { get; } = new("wss://nasdaqfeed.massive.com", UriKind.Absolute);

    /// <summary>The starter-plan host, <c>wss://starterfeed.massive.com</c>.</summary>
    public static Uri StarterFeed { get; } = new("wss://starterfeed.massive.com", UriKind.Absolute);

    /// <summary>
    /// The same eight hosts on the legacy <c>polygon.io</c> domain, which Massive operates in
    /// parallel exactly as <see cref="MassiveEndpoints.Legacy"/> describes for REST.
    /// </summary>
    public static class Legacy
    {
        /// <summary>The real-time feed, <c>wss://socket.polygon.io</c>.</summary>
        public static Uri RealTime { get; } = new("wss://socket.polygon.io", UriKind.Absolute);

        /// <summary>The 15-minute delayed feed, <c>wss://delayed.polygon.io</c>.</summary>
        public static Uri Delayed { get; } = new("wss://delayed.polygon.io", UriKind.Absolute);

        /// <summary>The business-plan feed, <c>wss://business.polygon.io</c>.</summary>
        public static Uri Business { get; } = new("wss://business.polygon.io", UriKind.Absolute);

        /// <summary>The delayed business-plan feed, <c>wss://delayed-business.polygon.io</c>.</summary>
        public static Uri DelayedBusiness { get; } = new("wss://delayed-business.polygon.io", UriKind.Absolute);

        /// <summary>The PolyFeed host, <c>wss://polyfeed.polygon.io</c>.</summary>
        public static Uri PolyFeed { get; } = new("wss://polyfeed.polygon.io", UriKind.Absolute);

        /// <summary>The PolyFeed Plus host, <c>wss://polyfeedplus.polygon.io</c>.</summary>
        public static Uri PolyFeedPlus { get; } = new("wss://polyfeedplus.polygon.io", UriKind.Absolute);

        /// <summary>The Nasdaq Basic host, <c>wss://nasdaqfeed.polygon.io</c>.</summary>
        public static Uri NasdaqFeed { get; } = new("wss://nasdaqfeed.polygon.io", UriKind.Absolute);

        /// <summary>The starter-plan host, <c>wss://starterfeed.polygon.io</c>.</summary>
        public static Uri StarterFeed { get; } = new("wss://starterfeed.polygon.io", UriKind.Absolute);
    }
}
```

`src/MassiveDotNet.WebSocket/MassiveMarket.cs`:

```csharp
namespace MassiveDotNet.WebSocket;

/// <summary>The market a stream connects to, which is the path segment on a feed host.</summary>
public enum MassiveMarket
{
    /// <summary>US equities.</summary>
    Stocks,

    /// <summary>US options contracts.</summary>
    Options,

    /// <summary>Index values and aggregates.</summary>
    Indices,

    /// <summary>Foreign exchange pairs.</summary>
    Forex,

    /// <summary>Cryptocurrency pairs.</summary>
    Crypto,

    /// <summary>Futures contracts.</summary>
    Futures,
}

/// <summary>Extensions for <see cref="MassiveMarket"/>.</summary>
public static class MassiveMarketExtensions
{
    /// <summary>Renders the market as the lowercase path segment a feed host expects.</summary>
    /// <param name="market">The market.</param>
    /// <returns>The path segment, such as <c>stocks</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The market is not a declared member.</exception>
    public static string ToPathSegment(this MassiveMarket market) => market switch
    {
        MassiveMarket.Stocks => "stocks",
        MassiveMarket.Options => "options",
        MassiveMarket.Indices => "indices",
        MassiveMarket.Forex => "forex",
        MassiveMarket.Crypto => "crypto",
        MassiveMarket.Futures => "futures",
        _ => throw new ArgumentOutOfRangeException(nameof(market), market, null),
    };
}
```

- [ ] **Step 4: Create the test project and register both in the solution**

`tests/MassiveDotNet.WebSocket.Tests/MassiveDotNet.WebSocket.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <!-- AllocationTests in Task 13 asserts byte counts, and tiered compilation makes them vary
         between runs of the same binary. Same reasoning as the Rest test project (D31). -->
    <TieredCompilation>false</TieredCompilation>
    <OutputType>Exe</OutputType>
    <RootNamespace>MassiveDotNet.WebSocket.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/MassiveDotNet.WebSocket/MassiveDotNet.WebSocket.csproj" />
  </ItemGroup>

</Project>
```

Add both to `MassiveDotNet.slnx`, in the existing folders, keeping each list alphabetical:

```xml
  <Folder Name="/src/">
    <Project Path="src/MassiveDotNet.Extensions.DependencyInjection/MassiveDotNet.Extensions.DependencyInjection.csproj" />
    <Project Path="src/MassiveDotNet.Rest/MassiveDotNet.Rest.csproj" />
    <Project Path="src/MassiveDotNet.WebSocket/MassiveDotNet.WebSocket.csproj" />
    <Project Path="src/MassiveDotNet/MassiveDotNet.csproj" />
  </Folder>
```

```xml
    <Project Path="tests/MassiveDotNet.Rest.Tests/MassiveDotNet.Rest.Tests.csproj" />
    <Project Path="tests/MassiveDotNet.WebSocket.Tests/MassiveDotNet.WebSocket.Tests.csproj" />
```

`BuildGateTests` asserts every project on disk is in the solution; both additions are what keeps it
green.

- [ ] **Step 5: Extend rule 12's reflection layer to the fourth assembly**

`tests/MassiveDotNet.Rest.Tests/MassiveDotNet.Rest.Tests.csproj` gains the reference:

```xml
    <ProjectReference Include="../../src/MassiveDotNet.WebSocket/MassiveDotNet.WebSocket.csproj" />
```

`TemporalTypeTests.cs` — add the assembly and tighten the guard from a count to an identity
comparison, because listing an existing assembly twice would satisfy the count:

```csharp
    private static readonly Assembly[] ShippedAssemblies =
    [
        typeof(MassiveClientOptions).Assembly,
        typeof(MassiveRestClient).Assembly,
        typeof(MassiveServiceCollectionExtensions).Assembly,
        typeof(MassiveDotNet.WebSocket.MassiveFeeds).Assembly,
    ];

    [Fact]
    public void EveryShippedLibraryIsInspected()
    {
        // Compared by name rather than by count: a count is satisfied by listing one assembly
        // twice, which is exactly the mistake this guard exists to catch.
        string[] projects =
        [
            .. Directory
                .GetDirectories(Path.Combine(RepositoryRoot, "src"))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
        ];

        string[] inspected =
        [
            .. ShippedAssemblies
                .Select(assembly => assembly.GetName().Name)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
        ];

        Assert.Equal(projects, inspected);
    }
```

- [ ] **Step 6: Correct the stale count in CLAUDE.md**

Line 244 reads "the exported surface of both shipped assemblies". There are four now:

```
1. **Reflection** over the exported surface of all four shipped assemblies — properties, fields,
```

- [ ] **Step 7: Run the tests**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS, including `EveryShippedLibraryIsInspected`, `EveryDirectoryHoldingSourceIsScanned`,
and `BuildGateTests`. Build must be warning-free.

- [ ] **Step 8: Commit**

```bash
git add src/MassiveDotNet.WebSocket tests/MassiveDotNet.WebSocket.Tests MassiveDotNet.slnx \
        tests/MassiveDotNet.Rest.Tests CLAUDE.md
git commit -m "feat: add the WebSocket package with its eight provisioned feed hosts

Eight hosts, not the eighteen issue #20 named. Both domains answer DNS with
wildcard records, so a name resolving proves nothing; a provisioned host presents
a certificate naming itself, and launchpad presents the ingress default on both
domains, so no property exposes it.

Rule 12's reflection layer gains the fourth assembly, and its guard moves from
comparing counts to comparing names -- a count is satisfied by listing one
assembly twice.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 2: `Epoch` moves to core

**Files:**
- Delete: `src/MassiveDotNet.Rest/Models/Epoch.cs`
- Create: `src/MassiveDotNet/Epoch.cs`
- Modify: `tools/MassiveDotNet.CodeGen/Emitter.cs` (the `using` emitted into model files)
- Modify: every `src/MassiveDotNet.Rest/Generated/Models/*.g.cs` — **by regenerating, never by hand**
- Create: `tests/MassiveDotNet.Rest.Tests/EpochTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `MassiveDotNet.Epoch` — `public static Instant FromMilliseconds(long milliseconds)` and
  `public static Instant FromNanoseconds(long nanoseconds)`.

`Epoch` is `internal` to `.Rest`, and its own comment gives the reason: *"core has no reason to know
about wire epochs."* A second package needing the same conversion is what expires that premise
(D-W11). Duplicating it puts two copies of a subtle nanosecond argument in the tree;
`InternalsVisibleTo` has no precedent here and would be a strange first one to set between two
shipped packages.

- [ ] **Step 1: Write the failing test**

`tests/MassiveDotNet.Rest.Tests/EpochTests.cs`:

```csharp
using MassiveDotNet;
using NodaTime;

namespace MassiveDotNet.Rest.Tests;

public class EpochTests
{
    [Fact]
    public void MillisecondsConvertToTheSameInstantNodaTimeWould() =>
        Assert.Equal(
            Instant.FromUnixTimeMilliseconds(1536036818784),
            Epoch.FromMilliseconds(1536036818784));

    // The whole reason this helper exists rather than Instant.FromUnixTimeTicks: ticks are 100ns,
    // so a nanosecond value that is not a multiple of 100 would be silently truncated.
    [Fact]
    public void NanosecondsKeepPrecisionBelowTheTickBoundary()
    {
        Instant instant = Epoch.FromNanoseconds(1536036818784123456);

        Assert.Equal(
            1536036818784123456L,
            (instant - NodaConstants.UnixEpoch).ToInt64Nanoseconds());
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/MassiveDotNet.Rest.Tests --filter "FullyQualifiedName~EpochTests"`
Expected: FAIL — `Epoch` is not accessible from the `MassiveDotNet` namespace.

- [ ] **Step 3: Move the type**

Delete `src/MassiveDotNet.Rest/Models/Epoch.cs`. Create `src/MassiveDotNet/Epoch.cs` with the same
two methods, made public, in namespace `MassiveDotNet`, and with the remark updated — the old one
argues for a placement that no longer holds:

```csharp
using NodaTime;

namespace MassiveDotNet;

/// <summary>
/// Converts the raw epoch values wire DTOs store into instants, at the precision each wire field
/// carries.
/// </summary>
/// <remarks>
/// Models store the epoch value as a <see cref="long"/> and compute the <see cref="Instant"/> only
/// when read (decision D5), so this is called from computed properties, never at deserialization.
/// Nanosecond precision is kept: <see cref="Instant"/> resolves to the nanosecond and a
/// <see cref="Duration"/> built from nanoseconds loses nothing, whereas
/// <see cref="Instant.FromUnixTimeTicks"/> would truncate to 100 ns. It lives in core because both
/// the REST and WebSocket packages read wire epochs, and the two units do not agree: REST v3 sends
/// nanoseconds where the streaming wire sends milliseconds for the same conceptual field (D-W11).
/// </remarks>
public static class Epoch
{
    /// <summary>Converts Unix milliseconds, the unit of aggregate, indicator, and streaming timestamps.</summary>
    /// <param name="milliseconds">Milliseconds since the Unix epoch.</param>
    /// <returns>The instant.</returns>
    public static Instant FromMilliseconds(long milliseconds) => Instant.FromUnixTimeMilliseconds(milliseconds);

    /// <summary>Converts Unix nanoseconds, the unit of every REST tick-level timestamp.</summary>
    /// <param name="nanoseconds">Nanoseconds since the Unix epoch.</param>
    /// <returns>The instant, to the nanosecond.</returns>
    public static Instant FromNanoseconds(long nanoseconds) => NodaConstants.UnixEpoch + Duration.FromNanoseconds(nanoseconds);
}
```

- [ ] **Step 4: Point the generator at the new namespace**

Generated models reference `Epoch` from `MassiveDotNet.Rest.Models`, their own namespace, so they
carry no `using` for it today. In core it needs one. Find where `Emitter.cs` writes the model file
header and add `using MassiveDotNet;` to the set it emits for model files.

Rule 5: do not hand-edit a single `.g.cs`. Change the emitter, then regenerate.

- [ ] **Step 5: Regenerate and prove determinism**

```bash
dotnet run --project tools/MassiveDotNet.CodeGen
git add src/
dotnet run --project tools/MassiveDotNet.CodeGen
git diff --exit-code --quiet -- src/    # rule 6: two runs, identical bytes
dotnet build MassiveDotNet.slnx          # must be warning-free
```

Expected: the second run produces no diff. The staged diff from the first run touches only the
`using` block of the model files that call `Epoch`.

- [ ] **Step 6: Run the tests**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS. `EpochTests` is green and no existing deserialization test has changed behaviour —
this is a move, not a rewrite.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor: promote Epoch to core for the streaming package

Its own comment argued it belonged in .Rest because 'core has no reason to know
about wire epochs'. A second package needing the same conversion is what expires
that premise. Duplicating it would put two copies of a subtle nanosecond argument
in the tree, and InternalsVisibleTo has no precedent here.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 3: The capped ticker pool

**Files:**
- Create: `src/MassiveDotNet.WebSocket/Internal/TickerPool.cs`
- Create: `tests/MassiveDotNet.WebSocket.Tests/TickerPoolTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `internal sealed class TickerPool` — `TickerPool(int capacity)`,
  `string Intern(ReadOnlySpan<char> ticker)`, `string Intern(ref Utf8JsonReader reader)`,
  `int Count { get; }`.

Interning the ticker is what takes per-event allocation to zero: `reader.GetString()` mints a fresh
string on every event, and a busy symbol produces tens of thousands of identical ones a minute.

- [ ] **Step 1: Write the failing test**

`tests/MassiveDotNet.WebSocket.Tests/TickerPoolTests.cs`:

```csharp
using System.Text;
using System.Text.Json;
using MassiveDotNet.WebSocket.Internal;

namespace MassiveDotNet.WebSocket.Tests;

public class TickerPoolTests
{
    [Fact]
    public void TheSameTickerInternsToTheSameInstance()
    {
        TickerPool pool = new(capacity: 16);

        string first = pool.Intern("AAPL");
        string second = pool.Intern("AAPL");

        Assert.Equal("AAPL", first);
        Assert.Same(first, second);
    }

    [Fact]
    public void DifferentTickersAreDistinct()
    {
        TickerPool pool = new(capacity: 16);

        Assert.NotSame(pool.Intern("AAPL"), pool.Intern("MSFT"));
        Assert.Equal(2, pool.Count);
    }

    // An uncapped intern table is an unbounded cache wearing a helpful hat. Past the cap the pool
    // degrades to allocating, which is bounded and correct, rather than growing forever.
    [Fact]
    public void PastTheCapItStopsInterningButKeepsReturningCorrectValues()
    {
        TickerPool pool = new(capacity: 2);

        pool.Intern("AAA");
        pool.Intern("BBB");

        string first = pool.Intern("CCC");
        string second = pool.Intern("CCC");

        Assert.Equal("CCC", first);
        Assert.Equal("CCC", second);
        Assert.NotSame(first, second);
        Assert.Equal(2, pool.Count);
    }

    [Fact]
    public void ItInternsStraightOffTheReaderWithoutAProbeString()
    {
        TickerPool pool = new(capacity: 16);
        byte[] json = Encoding.UTF8.GetBytes("""{"sym":"AAPL"}""");

        Utf8JsonReader reader = new(json);
        reader.Read();              // {
        reader.Read();              // "sym"
        reader.Read();              // "AAPL"

        Assert.Equal("AAPL", pool.Intern(ref reader));
        Assert.Same(pool.Intern("AAPL"), pool.Intern("AAPL"));
    }

    // A value longer than the stack buffer must still be correct; it simply does not intern.
    [Fact]
    public void AnOverlongValueFallsBackToAllocating()
    {
        TickerPool pool = new(capacity: 16);
        string overlong = new('A', 64);
        byte[] json = Encoding.UTF8.GetBytes($$"""{"sym":"{{overlong}}"}""");

        Utf8JsonReader reader = new(json);
        reader.Read();
        reader.Read();
        reader.Read();

        Assert.Equal(overlong, pool.Intern(ref reader));
        Assert.Equal(0, pool.Count);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~TickerPoolTests"`
Expected: FAIL — `TickerPool` does not exist.

- [ ] **Step 3: Implement it**

`src/MassiveDotNet.WebSocket/Internal/TickerPool.cs`:

```csharp
using System.Text.Json;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>
/// A bounded intern table for ticker symbols, so a repeated symbol costs no allocation.
/// </summary>
/// <remarks>
/// <para>
/// The read loop is single-threaded by construction — one loop per connection — so nothing here
/// locks. Sharing an instance across connections would need one and is not done.
/// </para>
/// <para>
/// The cap is not a tuning knob but a correctness property: without it this is a cache that grows
/// with every distinct symbol ever seen and is never trimmed. Past the cap it stops adding and
/// returns freshly allocated strings, which is slower and bounded rather than faster and unbounded.
/// </para>
/// </remarks>
internal sealed class TickerPool
{
    // Comfortably above any real symbol, including crypto pairs such as X:BTC-USD. A value longer
    // than this is not a ticker, so it is not worth a pool slot.
    private const int MaxStackChars = 24;

    private readonly Dictionary<string, string> _pool;
    private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    private readonly int _capacity;

    public TickerPool(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _capacity = capacity;
        _pool = new Dictionary<string, string>(StringComparer.Ordinal);
        _lookup = _pool.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>How many distinct symbols are held.</summary>
    public int Count => _pool.Count;

    /// <summary>Returns the pooled instance for <paramref name="ticker"/>, adding it if there is room.</summary>
    public string Intern(ReadOnlySpan<char> ticker)
    {
        // The alternate lookup is the point: probing by span means no string is allocated to ask
        // the question, so a hit costs nothing at all.
        if (_lookup.TryGetValue(ticker, out string? existing))
        {
            return existing;
        }

        string created = new(ticker);

        if (_pool.Count < _capacity)
        {
            _pool[created] = created;
        }

        return created;
    }

    /// <summary>Interns the string the reader is positioned on, without materializing a probe.</summary>
    /// <param name="reader">A reader positioned on a string token.</param>
    /// <returns>The pooled instance, or a fresh string when the value cannot be pooled.</returns>
    public string Intern(ref Utf8JsonReader reader)
    {
        // The UTF-8 byte count is an upper bound on the char count, so it is safe to size the
        // stack buffer from it: no encoding produces more chars than bytes here.
        long byteLength = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;

        if (byteLength > MaxStackChars)
        {
            return reader.GetString() ?? string.Empty;
        }

        Span<char> buffer = stackalloc char[MaxStackChars];
        int written = reader.CopyString(buffer);

        return Intern(buffer[..written]);
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~TickerPoolTests"`
Expected: PASS, all five.

- [ ] **Step 5: Commit**

```bash
git add src/MassiveDotNet.WebSocket/Internal/TickerPool.cs \
        tests/MassiveDotNet.WebSocket.Tests/TickerPoolTests.cs
git commit -m "feat: intern ticker symbols through a capped pool

A busy symbol produces tens of thousands of identical strings a minute, and
reader.GetString() mints a new one every time. Probing through an alternate
span lookup means a hit allocates nothing at all.

The cap is a correctness property rather than a tuning knob: without it this is
a cache that grows with every distinct symbol seen and is never trimmed.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 4: Stream options

**Files:**
- Create: `src/MassiveDotNet.WebSocket/MassiveStreamOptions.cs`
- Create: `src/MassiveDotNet.WebSocket/MassiveStreamReconnectOptions.cs`
- Create: `tests/MassiveDotNet.WebSocket.Tests/StreamOptionsTests.cs`

**Interfaces:**
- Consumes: `MassiveFeeds` (Task 1).
- Produces: `MassiveStreamOptions` with `ApiKey`, `Feed`, `TopicBufferCapacity`,
  `TickerPoolCapacity`, `MaxMessageBytes`, `HandshakeTimeout`, `KeepAliveInterval`, `UserAgent`,
  `Reconnect`, and `void Validate()`; `MassiveStreamReconnectOptions` with `InitialBackoff`,
  `MaxBackoff`, `BackoffMultiplier`, `Jitter`, and `void Validate()`.

Use the values from **Fixed values** verbatim.

- [ ] **Step 1: Write the failing test**

`tests/MassiveDotNet.WebSocket.Tests/StreamOptionsTests.cs`:

```csharp
using NodaTime;

namespace MassiveDotNet.WebSocket.Tests;

public class StreamOptionsTests
{
    [Fact]
    public void DefaultsMatchThePlannedValues()
    {
        MassiveStreamOptions options = new();

        Assert.Equal(MassiveFeeds.RealTime, options.Feed);
        Assert.Equal(1024, options.TopicBufferCapacity);
        Assert.Equal(16384, options.TickerPoolCapacity);
        Assert.Equal(4 * 1024 * 1024, options.MaxMessageBytes);
        Assert.Equal(Duration.FromSeconds(10), options.HandshakeTimeout);
        Assert.Equal(Duration.FromSeconds(20), options.KeepAliveInterval);
    }

    // Unlike rate limiting and retry (D30), reconnect is on by default: a stream that gives up on
    // the first dropped connection is not a streaming client, it is a demo.
    [Fact]
    public void ReconnectIsOnByDefaultAndCanBeDisabled()
    {
        MassiveStreamOptions options = new();

        Assert.NotNull(options.Reconnect);
        Assert.Equal(Duration.FromMilliseconds(500), options.Reconnect!.InitialBackoff);
        Assert.Equal(Duration.FromSeconds(30), options.Reconnect.MaxBackoff);
        Assert.Equal(2.0, options.Reconnect.BackoffMultiplier);
        Assert.Equal(0.2, options.Reconnect.Jitter);

        options.Reconnect = null;
        Assert.Null(options.Reconnect);
    }

    [Fact]
    public void AWhitespaceKeyIsTreatedAsAbsent()
    {
        MassiveStreamOptions options = new() { ApiKey = "   " };

        Assert.Null(options.ApiKey);
    }

    [Fact]
    public void ValidateRejectsAMissingKey()
    {
        MassiveStreamOptions options = new();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(MassiveStreamOptions.ApiKey), error.Message, StringComparison.Ordinal);
    }

    // Rule 11: the key is a frame body here (D-W9), so it must not travel in a message either.
    [Fact]
    public void ValidateNeverEchoesTheKey()
    {
        MassiveStreamOptions options = new() { ApiKey = "super-secret-key", Feed = new Uri("/relative", UriKind.Relative) };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.DoesNotContain("super-secret-key", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateRejectsANonPositiveBufferCapacity(int capacity)
    {
        MassiveStreamOptions options = new() { ApiKey = "k", TopicBufferCapacity = capacity };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void ValidateRejectsBackoffThatCannotGrow()
    {
        MassiveStreamOptions options = new()
        {
            ApiKey = "k",
            Reconnect = new MassiveStreamReconnectOptions { BackoffMultiplier = 0.5 },
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~StreamOptionsTests"`
Expected: FAIL — neither options type exists.

- [ ] **Step 3: Implement both options types**

`src/MassiveDotNet.WebSocket/MassiveStreamReconnectOptions.cs`:

```csharp
using NodaTime;

namespace MassiveDotNet.WebSocket;

/// <summary>Backoff for reconnecting a dropped stream.</summary>
public sealed class MassiveStreamReconnectOptions
{
    /// <summary>How long to wait before the first reconnect attempt. Defaults to 500 ms.</summary>
    public Duration InitialBackoff { get; set; } = Duration.FromMilliseconds(500);

    /// <summary>The ceiling on the backoff delay. Defaults to 30 seconds.</summary>
    public Duration MaxBackoff { get; set; } = Duration.FromSeconds(30);

    /// <summary>The factor each successive delay is multiplied by. Defaults to 2.0.</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// The proportion of random jitter applied to each delay, so a fleet of clients reconnecting
    /// after the same outage does not synchronise. Defaults to 0.2, meaning plus or minus 20%.
    /// </summary>
    public double Jitter { get; set; } = 0.2;

    /// <summary>Throws if the options are not in a usable state.</summary>
    /// <exception cref="InvalidOperationException">A value cannot be acted on.</exception>
    public void Validate()
    {
        if (InitialBackoff <= Duration.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamReconnectOptions)}.{nameof(InitialBackoff)} must be positive.");
        }

        if (MaxBackoff < InitialBackoff)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamReconnectOptions)}.{nameof(MaxBackoff)} must be at least "
                + $"{nameof(InitialBackoff)}.");
        }

        if (BackoffMultiplier < 1.0)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamReconnectOptions)}.{nameof(BackoffMultiplier)} must be at "
                + "least 1.0, or the delay shrinks on every attempt.");
        }

        if (Jitter is < 0.0 or > 1.0)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamReconnectOptions)}.{nameof(Jitter)} must be between 0 and 1.");
        }
    }
}
```

`src/MassiveDotNet.WebSocket/MassiveStreamOptions.cs`:

```csharp
using NodaTime;

namespace MassiveDotNet.WebSocket;

/// <summary>Configuration for a Massive streaming client.</summary>
public sealed class MassiveStreamOptions
{
    private string? _apiKey;

    /// <summary>The API key used to authenticate the stream. Required.</summary>
    /// <remarks>
    /// Unlike REST, this key travels in a message rather than a header (D-W9). It is never
    /// rendered into an exception message, traced, or written to disk (rule 11).
    /// </remarks>
    public string? ApiKey
    {
        get => _apiKey;
        set => _apiKey = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>The feed host to connect to. Defaults to <see cref="MassiveFeeds.RealTime"/>.</summary>
    public Uri Feed { get; set; } = MassiveFeeds.RealTime;

    /// <summary>
    /// How many events each subscribed topic buffers before the oldest is dropped. Defaults to 1024.
    /// </summary>
    /// <remarks>
    /// This is the SDK's whole memory budget for a stream, and it is arithmetic a caller can do in
    /// advance: capacity multiplied by the event size, multiplied by the number of topics
    /// subscribed. Nothing here grows with time or with messages received.
    /// </remarks>
    public int TopicBufferCapacity { get; set; } = 1024;

    /// <summary>How many distinct ticker symbols are interned. Defaults to 16384.</summary>
    public int TickerPoolCapacity { get; set; } = 16384;

    /// <summary>The largest message accepted, in bytes. Defaults to 4 MiB.</summary>
    /// <remarks>
    /// A ceiling on frame reassembly. Without it a server could make the client allocate without
    /// limit, which is the one unbounded growth path the protocol leaves open.
    /// </remarks>
    public int MaxMessageBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>How long connect, authentication, and subscription acknowledgement may take. Defaults to 10 seconds.</summary>
    public Duration HandshakeTimeout { get; set; } = Duration.FromSeconds(10);

    /// <summary>The WebSocket keep-alive interval. Defaults to 20 seconds.</summary>
    public Duration KeepAliveInterval { get; set; } = Duration.FromSeconds(20);

    /// <summary>An optional product token appended to the <c>User-Agent</c> header.</summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Reconnect behaviour, or <see langword="null"/> to surface a dropped connection instead of
    /// re-establishing it. On by default.
    /// </summary>
    /// <remarks>
    /// This inverts D30's posture, where rate limiting and retry are off until a caller opts in.
    /// The reason they differ: a caller's request rate is theirs to choose and the SDK cannot infer
    /// a tier, whereas a stream that ends on the first dropped connection is not a streaming client
    /// at all. Authentication failure is never retried regardless of this setting (D-W6).
    /// </remarks>
    public MassiveStreamReconnectOptions? Reconnect { get; set; } = new();

    /// <summary>Throws if the options are not in a usable state.</summary>
    /// <exception cref="InvalidOperationException">
    /// The API key is missing, the feed is not an absolute URI, or a capacity or duration cannot be
    /// acted on.
    /// </exception>
    public void Validate()
    {
        // No message below names the key's value. Rule 11 is easy to honour in a getter and easy
        // to lose in an error path, which is the path that gets logged.
        if (_apiKey is null)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamOptions)}.{nameof(ApiKey)} must be set to a non-empty value.");
        }

        if (!Feed.IsAbsoluteUri)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamOptions)}.{nameof(Feed)} must be an absolute URI.");
        }

        if (TopicBufferCapacity <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamOptions)}.{nameof(TopicBufferCapacity)} must be positive.");
        }

        if (TickerPoolCapacity <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamOptions)}.{nameof(TickerPoolCapacity)} must be positive.");
        }

        if (MaxMessageBytes <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamOptions)}.{nameof(MaxMessageBytes)} must be positive.");
        }

        if (HandshakeTimeout <= Duration.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(MassiveStreamOptions)}.{nameof(HandshakeTimeout)} must be positive.");
        }

        Reconnect?.Validate();
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~StreamOptionsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/MassiveDotNet.WebSocket tests/MassiveDotNet.WebSocket.Tests/StreamOptionsTests.cs
git commit -m "feat: add stream options with NodaTime durations throughout

Reconnect is on by default, which inverts D30's posture for rate limiting and
retry. They differ for a reason: a caller's request rate is theirs to choose,
whereas a stream that ends on the first dropped connection is not a streaming
client.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 5: The socket seam and the fake that drives it

**Files:**
- Create: `src/MassiveDotNet.WebSocket/Internal/IMassiveWebSocket.cs`
- Create: `src/MassiveDotNet.WebSocket/Internal/ClientWebSocketAdapter.cs`
- Create: `tests/MassiveDotNet.WebSocket.Tests/FakeWebSocket.cs`
- Create: `tests/MassiveDotNet.WebSocket.Tests/FakeWebSocketTests.cs`
- Modify: `src/MassiveDotNet.WebSocket/MassiveDotNet.WebSocket.csproj` (InternalsVisibleTo)

**Interfaces:**
- Consumes: `MassiveStreamOptions` (Task 4).
- Produces: `internal interface IMassiveWebSocket : IAsyncDisposable` with `State`, `ConnectAsync`,
  `SendAsync`, `ReceiveAsync`, `CloseAsync`; `internal delegate IMassiveWebSocket
  MassiveWebSocketFactory()`; `internal sealed class ClientWebSocketAdapter`; and the test double
  `FakeWebSocket` with `EnqueueText`, `EnqueueFragmented`, `AbortNext`, and `Sent`.

`ClientWebSocket` is sealed, so the only way to test the handshake, the read loop, reconnect, and
backpressure without a socket is to put a seam under them. This mirrors `StubHandler` at the
`HttpMessageHandler` seam and keeps the offline tier genuinely offline.

The factory delegate matters: reconnect needs a **new** socket each attempt, because a
`ClientWebSocket` cannot be reconnected once aborted.

- [ ] **Step 1: Write the failing test**

`tests/MassiveDotNet.WebSocket.Tests/FakeWebSocketTests.cs`:

```csharp
using System.Net.WebSockets;
using System.Text;
using MassiveDotNet.WebSocket.Internal;

namespace MassiveDotNet.WebSocket.Tests;

public class FakeWebSocketTests
{
    [Fact]
    public async Task ItDeliversAWholeMessageInOneReceive()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText("""[{"ev":"status"}]""");

        byte[] buffer = new byte[128];
        ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer, TestContext.Current.CancellationToken);

        Assert.True(result.EndOfMessage);
        Assert.Equal("""[{"ev":"status"}]""", Encoding.UTF8.GetString(buffer, 0, result.Count));
    }

    // The read loop must never assume a message arrives in one frame; the fake has to be able to
    // prove that, or the reassembly path is untested.
    [Fact]
    public async Task ItCanSplitAMessageAcrossFrames()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueFragmented("""[{"ev":"status"}]""", chunkSize: 5);

        byte[] buffer = new byte[128];
        StringBuilder assembled = new();
        ValueWebSocketReceiveResult result;
        int frames = 0;

        do
        {
            result = await socket.ReceiveAsync(buffer, TestContext.Current.CancellationToken);
            assembled.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            frames++;
        }
        while (!result.EndOfMessage);

        Assert.True(frames > 1);
        Assert.Equal("""[{"ev":"status"}]""", assembled.ToString());
    }

    [Fact]
    public async Task ItRecordsWhatWasSent()
    {
        await using FakeWebSocket socket = new();

        await socket.SendAsync(Encoding.UTF8.GetBytes("hello"), TestContext.Current.CancellationToken);

        Assert.Equal(["hello"], socket.Sent);
    }

    // The server closes without a close handshake after auth_failed, so ReceiveAsync throws
    // rather than reporting a Close frame. Reconnect is written against this shape.
    [Fact]
    public async Task ItCanAbortTheWayTheRealServerDoes()
    {
        await using FakeWebSocket socket = new();
        socket.AbortNext();

        await Assert.ThrowsAsync<WebSocketException>(async () =>
            await socket.ReceiveAsync(new byte[128], TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~FakeWebSocketTests"`
Expected: FAIL — neither `IMassiveWebSocket` nor `FakeWebSocket` exists.

- [ ] **Step 3: Define the seam**

`src/MassiveDotNet.WebSocket/Internal/IMassiveWebSocket.cs`:

```csharp
using System.Net.WebSockets;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>
/// The subset of <see cref="ClientWebSocket"/> this SDK uses, behind an interface so the transport
/// can be driven without a socket.
/// </summary>
/// <remarks>
/// <see cref="ClientWebSocket"/> is sealed, so a test double is impossible without this seam. It is
/// the same shape the REST tests get from stubbing <c>HttpMessageHandler</c>, and it is what keeps
/// reconnect, backpressure, and frame reassembly testable in the offline tier (rule 13).
/// </remarks>
internal interface IMassiveWebSocket : IAsyncDisposable
{
    /// <summary>The socket's current state.</summary>
    WebSocketState State { get; }

    /// <summary>Opens the connection.</summary>
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    /// <summary>Sends one complete text message.</summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>Receives the next frame, which may be part of a larger message.</summary>
    ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>Closes the connection politely, if it is still open.</summary>
    Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken);
}

/// <summary>
/// Creates a socket. Reconnect needs a fresh one per attempt, because a <see cref="ClientWebSocket"/>
/// cannot be reopened once it has been aborted.
/// </summary>
internal delegate IMassiveWebSocket MassiveWebSocketFactory();
```

`src/MassiveDotNet.WebSocket/Internal/ClientWebSocketAdapter.cs`:

```csharp
using System.Net.WebSockets;
using MassiveDotNet.WebSocket;
using NodaTime;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>The real <see cref="IMassiveWebSocket"/>, over <see cref="ClientWebSocket"/>.</summary>
internal sealed class ClientWebSocketAdapter : IMassiveWebSocket
{
    private readonly ClientWebSocket _socket = new();

    public ClientWebSocketAdapter(MassiveStreamOptions options)
    {
        // Boundary crossing (produce): converted inline from a Duration, so the BCL type is never
        // named. See CLAUDE.md, "Temporal types" -- this is the row anticipated for #20.
        _socket.Options.KeepAliveInterval = options.KeepAliveInterval.ToTimeSpan();

        if (!string.IsNullOrWhiteSpace(options.UserAgent))
        {
            _socket.Options.SetRequestHeader("User-Agent", options.UserAgent);
        }
    }

    public WebSocketState State => _socket.State;

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
        _socket.ConnectAsync(uri, cancellationToken);

    public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) =>
        _socket.SendAsync(buffer, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    public ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        _socket.ReceiveAsync(buffer, cancellationToken);

    public Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
        _socket.State is WebSocketState.Open or WebSocketState.CloseReceived
            ? _socket.CloseAsync(closeStatus, statusDescription, cancellationToken)
            : Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

Add to `src/MassiveDotNet.WebSocket/MassiveDotNet.WebSocket.csproj` so the test project can see the
internals:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="MassiveDotNet.WebSocket.Tests" />
  </ItemGroup>
```

This is `InternalsVisibleTo` to a **test** project, which is ordinary. D-W11 rejected it between two
*shipped* packages, which is a different thing.

- [ ] **Step 4: Write the fake**

`tests/MassiveDotNet.WebSocket.Tests/FakeWebSocket.cs`:

```csharp
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using MassiveDotNet.WebSocket.Internal;

namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// A scripted <see cref="IMassiveWebSocket"/>. Inbound frames are queued by the test; outbound
/// messages are recorded verbatim so a test can assert on the exact wire text.
/// </summary>
internal sealed class FakeWebSocket : IMassiveWebSocket
{
    private readonly record struct Frame(byte[] Payload, bool EndOfMessage, bool Abort);

    private readonly Channel<Frame> _inbound = Channel.CreateUnbounded<Frame>();

    public WebSocketState State { get; private set; } = WebSocketState.None;

    /// <summary>Every message sent, decoded as UTF-8, in order.</summary>
    public List<string> Sent { get; } = [];

    /// <summary>Completes once a message has been sent, so a test need not poll.</summary>
    public TaskCompletionSource SentSignal { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int ConnectCount { get; private set; }

    /// <summary>Queues one complete message.</summary>
    public void EnqueueText(string message) =>
        _inbound.Writer.TryWrite(new Frame(Encoding.UTF8.GetBytes(message), EndOfMessage: true, Abort: false));

    /// <summary>Queues one message split across frames, to exercise reassembly.</summary>
    public void EnqueueFragmented(string message, int chunkSize)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message);

        for (int offset = 0; offset < payload.Length; offset += chunkSize)
        {
            int length = Math.Min(chunkSize, payload.Length - offset);
            bool last = offset + length >= payload.Length;

            _inbound.Writer.TryWrite(new Frame(payload[offset..(offset + length)], last, Abort: false));
        }
    }

    /// <summary>Makes the next receive throw, the way the server behaves after auth_failed.</summary>
    public void AbortNext() =>
        _inbound.Writer.TryWrite(new Frame([], EndOfMessage: true, Abort: true));

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        ConnectCount++;
        State = WebSocketState.Open;
        return Task.CompletedTask;
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        Sent.Add(Encoding.UTF8.GetString(buffer.Span));
        SentSignal.TrySetResult();
        SentSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        Frame frame = await _inbound.Reader.ReadAsync(cancellationToken);

        if (frame.Abort)
        {
            State = WebSocketState.Aborted;
            throw new WebSocketException(
                WebSocketError.ConnectionClosedPrematurely,
                "The remote party closed the WebSocket connection without completing the close handshake.");
        }

        frame.Payload.CopyTo(buffer.Span);

        return new ValueWebSocketReceiveResult(frame.Payload.Length, WebSocketMessageType.Text, frame.EndOfMessage);
    }

    public Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        State = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        State = WebSocketState.Closed;
        _inbound.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~FakeWebSocketTests"`
Expected: PASS, all four.

- [ ] **Step 6: Commit**

```bash
git add src/MassiveDotNet.WebSocket tests/MassiveDotNet.WebSocket.Tests
git commit -m "test: put a seam under ClientWebSocket so the transport can be driven

ClientWebSocket is sealed, so the handshake, read loop, reconnect, and
backpressure are untestable without this. Same shape the REST tests get from
stubbing HttpMessageHandler, and it is what keeps the offline tier offline.

The factory delegate is not ceremony: a ClientWebSocket cannot be reopened once
aborted, so every reconnect attempt needs a fresh one.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 6: Connect and authenticate

**Files:**
- Create: `src/MassiveDotNet.WebSocket/MassiveStreamException.cs`
- Create: `src/MassiveDotNet.WebSocket/MassiveStreamAuthenticationException.cs`
- Create: `src/MassiveDotNet.WebSocket/Internal/StatusMessage.cs`
- Create: `src/MassiveDotNet.WebSocket/Internal/MassiveStreamConnection.cs`
- Create: `tests/MassiveDotNet.WebSocket.Tests/HandshakeTests.cs`

**Interfaces:**
- Consumes: `IMassiveWebSocket`, `MassiveWebSocketFactory` (Task 5); `MassiveStreamOptions` (Task 4).
- Produces: `internal sealed class MassiveStreamConnection` — constructor
  `(MassiveStreamOptions options, MassiveMarket market, MassiveWebSocketFactory factory, IClock clock)`,
  `Task ConnectAsync(CancellationToken)`;
  `internal readonly record struct StatusMessage(string Status, string Message)` with
  `static int Parse(ReadOnlySpan<byte> payload, Span<StatusMessage> destination)`;
  `public class MassiveStreamException : Exception`;
  `public sealed class MassiveStreamAuthenticationException : MassiveStreamException` exposing
  `ServerMessage`.

The handshake is a strict three-step sequence and the read loop is not running yet, so this task
reads frames directly. Task 7 starts the loop.

- [ ] **Step 1: Write the failing test**

`tests/MassiveDotNet.WebSocket.Tests/HandshakeTests.cs`:

```csharp
using MassiveDotNet.WebSocket.Internal;
using NodaTime;
using NodaTime.Testing;

namespace MassiveDotNet.WebSocket.Tests;

public class HandshakeTests
{
    private const string Connected = """[{"ev":"status","status":"connected","message":"Connected Successfully"}]""";
    private const string AuthSuccess = """[{"ev":"status","status":"auth_success","message":"authenticated"}]""";

    private static MassiveStreamConnection CreateConnection(
        FakeWebSocket socket,
        string apiKey = "test-key",
        MassiveMarket market = MassiveMarket.Stocks)
    {
        MassiveStreamOptions options = new() { ApiKey = apiKey };

        return new MassiveStreamConnection(options, market, () => socket, new FakeClock(Instant.FromUnixTimeSeconds(0)));
    }

    [Fact]
    public async Task ItAuthenticatesWithTheKeyAsAMessage()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        await using MassiveStreamConnection connection = CreateConnection(socket);
        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["""{"action":"auth","params":"test-key"}"""], socket.Sent);
    }

    [Fact]
    public async Task ABadKeyThrowsCarryingTheServerMessageVerbatim()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText("""[{"ev":"status","status":"auth_failed","message":"authentication failed"}]""");

        await using MassiveStreamConnection connection = CreateConnection(socket);

        MassiveStreamAuthenticationException error =
            await Assert.ThrowsAsync<MassiveStreamAuthenticationException>(
                async () => await connection.ConnectAsync(TestContext.Current.CancellationToken));

        Assert.Equal("authentication failed", error.ServerMessage);
    }

    // auth_failed carries two unrelated failures apart from the prose, and a caller's response to
    // each differs completely. The SDK reports what the server said rather than inventing a
    // category it cannot determine (D-W6).
    [Fact]
    public async Task AnUnentitledMarketThrowsTheSameTypeWithItsOwnMessage()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(
            """[{"ev":"status","status":"auth_failed","message":"Your plan doesn't include websocket access. Visit https://massive.com/pricing to upgrade."}]""");

        await using MassiveStreamConnection connection = CreateConnection(socket, market: MassiveMarket.Crypto);

        MassiveStreamAuthenticationException error =
            await Assert.ThrowsAsync<MassiveStreamAuthenticationException>(
                async () => await connection.ConnectAsync(TestContext.Current.CancellationToken));

        Assert.Contains("doesn't include websocket access", error.ServerMessage, StringComparison.Ordinal);
        Assert.Contains("doesn't include websocket access", error.Message, StringComparison.Ordinal);
    }

    // Rule 11, and the reason D-W9 exists: under REST the key lived in a header the SDK never
    // rendered. Here it is a frame body, one ToString() away from a log file.
    [Fact]
    public async Task NoExceptionEverNamesTheKey()
    {
        const string Key = "sk-live-do-not-leak-me";

        await using FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText("""[{"ev":"status","status":"auth_failed","message":"authentication failed"}]""");

        await using MassiveStreamConnection connection = CreateConnection(socket, apiKey: Key);

        MassiveStreamAuthenticationException error =
            await Assert.ThrowsAsync<MassiveStreamAuthenticationException>(
                async () => await connection.ConnectAsync(TestContext.Current.CancellationToken));

        Assert.DoesNotContain(Key, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItConnectsToTheMarketPathOnTheConfiguredFeed()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        await using MassiveStreamConnection connection = CreateConnection(socket, market: MassiveMarket.Options);
        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("wss://socket.massive.com/options"), connection.Endpoint);
    }

    [Fact]
    public async Task AnUnexpectedFirstMessageThrowsRatherThanProceeding()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText("""[{"ev":"status","status":"disconnected","message":"go away"}]""");

        await using MassiveStreamConnection connection = CreateConnection(socket);

        await Assert.ThrowsAsync<MassiveStreamException>(
            async () => await connection.ConnectAsync(TestContext.Current.CancellationToken));
    }
}
```

`NodaTime.Testing` provides `FakeClock`; add the package to
`tests/MassiveDotNet.WebSocket.Tests` and to `Directory.Packages.props` pinned to the NodaTime
version already in use.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~HandshakeTests"`
Expected: FAIL — `MassiveStreamConnection` does not exist.

- [ ] **Step 3: Implement the exceptions and the status parser**

`src/MassiveDotNet.WebSocket/MassiveStreamException.cs`:

```csharp
namespace MassiveDotNet.WebSocket;

/// <summary>The base for every error raised by the streaming client.</summary>
public class MassiveStreamException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    public MassiveStreamException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public MassiveStreamException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
```

`src/MassiveDotNet.WebSocket/MassiveStreamAuthenticationException.cs`:

```csharp
namespace MassiveDotNet.WebSocket;

/// <summary>The server refused the stream's authentication message.</summary>
/// <remarks>
/// The server answers <c>auth_failed</c> for two unrelated reasons — a key it does not accept, and
/// a plan that does not include WebSocket access for the market — distinguished only by the prose
/// it sends. The SDK cannot tell them apart without reading that prose, so it reports the server's
/// own words in <see cref="ServerMessage"/> rather than inventing a category (D-W6).
/// A failure of this kind is terminal: the stream does not reconnect after it.
/// </remarks>
public sealed class MassiveStreamAuthenticationException : MassiveStreamException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="serverMessage">The message the server sent, verbatim.</param>
    public MassiveStreamAuthenticationException(string serverMessage)
        : base($"The Massive stream refused authentication: {serverMessage}") =>
        ServerMessage = serverMessage;

    /// <summary>The server's own message, unmodified.</summary>
    public string ServerMessage { get; }
}
```

`src/MassiveDotNet.WebSocket/Internal/StatusMessage.cs`:

```csharp
using System.Text.Json;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>One <c>ev: status</c> control message.</summary>
/// <remarks>
/// Everything inbound is a JSON array, control messages included, and one frame can carry several
/// of them — a subscribe to two topics is acknowledged in a single frame.
/// </remarks>
internal readonly record struct StatusMessage(string Status, string Message)
{
    public const string Connected = "connected";
    public const string AuthSuccess = "auth_success";
    public const string AuthFailed = "auth_failed";
    public const string Success = "success";

    /// <summary>
    /// Reads every status message in <paramref name="payload"/> into <paramref name="destination"/>.
    /// </summary>
    /// <returns>How many were written. Non-status events are skipped.</returns>
    public static int Parse(ReadOnlySpan<byte> payload, Span<StatusMessage> destination)
    {
        Utf8JsonReader reader = new(payload);
        int written = 0;

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            throw new MassiveStreamException("The stream sent a message that is not a JSON array.");
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            string? kind = null;
            string? status = null;
            string? message = null;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("ev"u8))
                {
                    reader.Read();
                    kind = reader.GetString();
                }
                else if (reader.ValueTextEquals("status"u8))
                {
                    reader.Read();
                    status = reader.GetString();
                }
                else if (reader.ValueTextEquals("message"u8))
                {
                    reader.Read();
                    message = reader.GetString();
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            if (kind == "status" && status is not null && written < destination.Length)
            {
                destination[written++] = new StatusMessage(status, message ?? string.Empty);
            }
        }

        return written;
    }
}
```

- [ ] **Step 4: Implement the handshake**

`src/MassiveDotNet.WebSocket/Internal/MassiveStreamConnection.cs` — this task's half only; Tasks 7,
8, 10, and 11 extend the same class:

```csharp
using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NodaTime;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>Owns one socket, its handshake, its read loop, and its reconnect state.</summary>
internal sealed partial class MassiveStreamConnection : IAsyncDisposable
{
    private readonly MassiveStreamOptions _options;
    private readonly MassiveWebSocketFactory _factory;
    private readonly IClock _clock;

    private IMassiveWebSocket? _socket;

    public MassiveStreamConnection(
        MassiveStreamOptions options,
        MassiveMarket market,
        MassiveWebSocketFactory factory,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(clock);
        options.Validate();

        _options = options;
        _factory = factory;
        _clock = clock;
        Endpoint = new Uri(options.Feed, market.ToPathSegment());
    }

    /// <summary>The URI this connection opens: the feed host with the market as its path.</summary>
    public Uri Endpoint { get; }

    /// <summary>Opens the socket and authenticates, returning only once the server accepts.</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Boundary crossing (produce): the domain Duration converts here and nowhere above.
        timeout.CancelAfter(_options.HandshakeTimeout.ToTimeSpan());

        _socket = _factory();
        await _socket.ConnectAsync(Endpoint, timeout.Token);

        await ExpectStatusAsync(StatusMessage.Connected, timeout.Token);
        await SendAuthenticationAsync(timeout.Token);
        await ExpectAuthenticationAsync(timeout.Token);
    }

    private async Task SendAuthenticationAsync(CancellationToken cancellationToken)
    {
        // Rule 11 (D-W9). The key is a frame body here, not a header, so it is built into a rented
        // buffer, sent, and wiped -- never interpolated into anything that could be logged, and
        // never held in a field beyond this call.
        string frame = $$"""{"action":"auth","params":"{{_options.ApiKey}}"}""";
        int byteCount = Encoding.UTF8.GetByteCount(frame);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            int written = Encoding.UTF8.GetBytes(frame, buffer);
            await _socket!.SendAsync(buffer.AsMemory(0, written), cancellationToken);
        }
        finally
        {
            Array.Clear(buffer, 0, byteCount);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task ExpectAuthenticationAsync(CancellationToken cancellationToken)
    {
        StatusMessage status = await ReadStatusAsync(cancellationToken);

        if (status.Status == StatusMessage.AuthSuccess)
        {
            return;
        }

        if (status.Status == StatusMessage.AuthFailed)
        {
            throw new MassiveStreamAuthenticationException(status.Message);
        }

        throw new MassiveStreamException(
            $"The stream answered authentication with an unexpected status '{status.Status}'.");
    }

    private async Task ExpectStatusAsync(string expected, CancellationToken cancellationToken)
    {
        StatusMessage status = await ReadStatusAsync(cancellationToken);

        if (status.Status != expected)
        {
            throw new MassiveStreamException(
                $"The stream sent status '{status.Status}' where '{expected}' was expected: {status.Message}");
        }
    }

    private async Task<StatusMessage> ReadStatusAsync(CancellationToken cancellationToken)
    {
        // The handshake is strictly sequential, so this reads directly; the continuous loop that
        // Task 7 starts takes over only once authentication has succeeded.
        using IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(_options.MaxMessageBytes);
        int length = await ReadMessageAsync(owner.Memory, cancellationToken);

        StatusMessage[] statuses = new StatusMessage[1];

        return StatusMessage.Parse(owner.Memory.Span[..length], statuses) == 1
            ? statuses[0]
            : throw new MassiveStreamException("The stream sent a message carrying no status event.");
    }
}
```

`ReadMessageAsync` is Task 7's frame reassembly. For this task, implement the minimal version that
loops `ReceiveAsync` until `EndOfMessage`; Task 7 replaces it with the bounded implementation and
its own tests.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~HandshakeTests"`
Expected: PASS, all seven — including `NoExceptionEverNamesTheKey`.

- [ ] **Step 6: Commit**

```bash
git add src/MassiveDotNet.WebSocket tests/MassiveDotNet.WebSocket.Tests/HandshakeTests.cs
git commit -m "feat: connect and authenticate the stream with the key as a message

auth_failed carries two unrelated failures -- a key the server rejects and a plan
without WebSocket access -- distinguished only by prose. The SDK cannot tell them
apart, so it reports the server's own words rather than inventing a category.

Rule 11 gets harder here than under REST, where the key lived in a header the SDK
never rendered. As a frame body it is one ToString() away from a log file, so the
auth frame is built into a rented buffer, sent, and wiped, and a test asserts no
exception ever names it.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 7: The read loop and bounded frame reassembly

**Files:**
- Create: `src/MassiveDotNet.WebSocket/Internal/FrameReader.cs`
- Modify: `src/MassiveDotNet.WebSocket/Internal/MassiveStreamConnection.cs`
- Create: `tests/MassiveDotNet.WebSocket.Tests/ReadLoopTests.cs`

**Interfaces:**
- Consumes: `IMassiveWebSocket` (Task 5), `MassiveStreamConnection` (Task 6).
- Produces: `internal sealed class FrameReader` — `FrameReader(IMassiveWebSocket socket, int maxMessageBytes)`,
  `ValueTask<int> ReadMessageAsync(Memory<byte> destination, CancellationToken)`; and on the
  connection, `void StartReading()`, `Task ReadLoopTask { get; }`, plus an internal hook
  `Func<ReadOnlyMemory<byte>, ValueTask>? OnMessage` that Tasks 8 and 10 attach to.

One message can span several frames, and the ceiling is what stops a server making the client
allocate without limit — the only unbounded growth path the protocol leaves open.

- [ ] **Step 1: Write the failing test**

`tests/MassiveDotNet.WebSocket.Tests/ReadLoopTests.cs`:

```csharp
using System.Text;
using MassiveDotNet.WebSocket.Internal;

namespace MassiveDotNet.WebSocket.Tests;

public class ReadLoopTests
{
    [Fact]
    public async Task ItReassemblesAMessageSplitAcrossFrames()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueFragmented("""[{"ev":"status","status":"connected","message":"ok"}]""", chunkSize: 7);

        FrameReader reader = new(socket, maxMessageBytes: 4096);
        byte[] destination = new byte[4096];

        int length = await reader.ReadMessageAsync(destination, TestContext.Current.CancellationToken);

        Assert.Equal(
            """[{"ev":"status","status":"connected","message":"ok"}]""",
            Encoding.UTF8.GetString(destination, 0, length));
    }

    // The ceiling is not a tuning knob. Without it a server that never sets EndOfMessage makes the
    // client grow a buffer until it dies, which is the one unbounded path the protocol leaves open.
    [Fact]
    public async Task AMessageBeyondTheCeilingThrowsRatherThanGrowing()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueFragmented(new string('x', 512), chunkSize: 16);

        FrameReader reader = new(socket, maxMessageBytes: 64);

        MassiveStreamException error = await Assert.ThrowsAsync<MassiveStreamException>(async () =>
            await reader.ReadMessageAsync(new byte[64], TestContext.Current.CancellationToken));

        Assert.Contains("64", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLoopDeliversEveryMessageInOrder()
    {
        await using FakeWebSocket socket = new();
        socket.EnqueueText("""[{"ev":"status","status":"connected","message":"ok"}]""");
        socket.EnqueueText("""[{"ev":"status","status":"auth_success","message":"authenticated"}]""");

        List<string> seen = [];
        FrameReader reader = new(socket, maxMessageBytes: 4096);
        byte[] destination = new byte[4096];

        for (int i = 0; i < 2; i++)
        {
            int length = await reader.ReadMessageAsync(destination, TestContext.Current.CancellationToken);
            seen.Add(Encoding.UTF8.GetString(destination, 0, length));
        }

        Assert.Equal(2, seen.Count);
        Assert.Contains("connected", seen[0], StringComparison.Ordinal);
        Assert.Contains("auth_success", seen[1], StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~ReadLoopTests"`
Expected: FAIL — `FrameReader` does not exist.

- [ ] **Step 3: Implement the reader**

`src/MassiveDotNet.WebSocket/Internal/FrameReader.cs`:

```csharp
using System.Net.WebSockets;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>Reassembles one logical message from the frames a socket delivers.</summary>
/// <remarks>
/// A WebSocket message arrives in one or more frames, and nothing in the protocol bounds how many.
/// <paramref name="maxMessageBytes"/> is therefore a correctness property rather than a tuning
/// knob: it is what stops a server -- buggy or hostile -- from making the client allocate without
/// limit.
/// </remarks>
internal sealed class FrameReader(IMassiveWebSocket socket, int maxMessageBytes)
{
    /// <summary>Reads one complete message into <paramref name="destination"/>.</summary>
    /// <returns>The message length in bytes.</returns>
    /// <exception cref="MassiveStreamException">The message exceeds the configured ceiling.</exception>
    public async ValueTask<int> ReadMessageAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        int written = 0;
        ValueWebSocketReceiveResult result;

        do
        {
            if (written == destination.Length)
            {
                throw new MassiveStreamException(
                    $"The stream sent a message larger than the {maxMessageBytes} byte ceiling "
                    + $"({nameof(MassiveStreamOptions.MaxMessageBytes)}).");
            }

            result = await socket.ReceiveAsync(destination[written..], cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new MassiveStreamException("The stream closed while a message was being read.");
            }

            written += result.Count;
        }
        while (!result.EndOfMessage);

        return written;
    }
}
```

- [ ] **Step 4: Start the loop on the connection**

Add to `MassiveStreamConnection`:

```csharp
    private readonly CancellationTokenSource _shutdown = new();
    private FrameReader? _reader;

    /// <summary>Receives the payload of every message the socket delivers, in order.</summary>
    /// <remarks>
    /// Attached by the façade. It must never block: every topic shares this one loop, so a handler
    /// that waits stalls the socket, closes the receive window, and gets the connection dropped for
    /// being a slow consumer -- taking down the topics that were keeping up (D-W4).
    /// </remarks>
    public Func<ReadOnlyMemory<byte>, ValueTask>? OnMessage { get; set; }

    /// <summary>The loop's task, so shutdown can await it.</summary>
    public Task ReadLoopTask { get; private set; } = Task.CompletedTask;

    /// <summary>Begins reading. Called once authentication has succeeded.</summary>
    public void StartReading()
    {
        _reader = new FrameReader(_socket!, _options.MaxMessageBytes);
        ReadLoopTask = Task.Run(() => ReadLoopAsync(_shutdown.Token), _shutdown.Token);
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = GC.AllocateUninitializedArray<byte>(_options.MaxMessageBytes);

        while (!cancellationToken.IsCancellationRequested)
        {
            int length = await _reader!.ReadMessageAsync(buffer, cancellationToken);

            if (OnMessage is { } handler)
            {
                await handler(buffer.AsMemory(0, length));
            }
        }
    }
```

The buffer is allocated once per connection, not per message, and is reused for the connection's
whole life. It is not pooled because it is held for that whole life anyway, and returning a 4 MiB
array to the pool at the end buys nothing.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests`
Expected: PASS. Replace Task 6's placeholder `ReadMessageAsync` with a call into `FrameReader` and
confirm `HandshakeTests` is still green.

- [ ] **Step 6: Commit**

```bash
git add src/MassiveDotNet.WebSocket tests/MassiveDotNet.WebSocket.Tests/ReadLoopTests.cs
git commit -m "feat: reassemble frames into messages under a hard ceiling

A message arrives in one or more frames and nothing in the protocol bounds how
many, so MaxMessageBytes is a correctness property rather than a tuning knob: it
is the only unbounded allocation path the protocol leaves open.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 8: Subscribe and unsubscribe, with acknowledgement counting

**Files:**
- Create: `src/MassiveDotNet.WebSocket/MassiveStreamSubscriptionException.cs`
- Create: `src/MassiveDotNet.WebSocket/Internal/SubscriptionRegistry.cs`
- Modify: `src/MassiveDotNet.WebSocket/Internal/MassiveStreamConnection.cs`
- Create: `src/MassiveDotNet.WebSocket/StockTopic.cs`
- Create: `tests/MassiveDotNet.WebSocket.Tests/SubscriptionTests.cs`

**Interfaces:**
- Consumes: `MassiveStreamConnection` (Tasks 6–7).
- Produces: `public enum StockTopic { Trades, Quotes }` with `internal static string ToCode(this StockTopic)`
  returning `"T"` / `"Q"`; `internal sealed class SubscriptionRegistry` with
  `void Add(string topicCode, IEnumerable<string> tickers)`, `void Remove(...)`,
  `IReadOnlyCollection<string> Parameters { get; }`; on the connection
  `Task SubscribeAsync(string topicCode, IReadOnlyCollection<string> tickers, CancellationToken)`
  and `Task UnsubscribeAsync(...)`;
  `public sealed class MassiveStreamSubscriptionException : MassiveStreamException` exposing
  `Unacknowledged`.

The server acknowledges one `status: success` per accepted `(topic, ticker)` pair and says **nothing
at all** about a pair it does not recognise. A typed topic enum removes the only cause observed;
counting acknowledgements is the guard for the causes not yet observed (D-W1, D-W2).

- [ ] **Step 1: Write the failing test**

`tests/MassiveDotNet.WebSocket.Tests/SubscriptionTests.cs`:

```csharp
using MassiveDotNet.WebSocket.Internal;
using NodaTime;
using NodaTime.Testing;

namespace MassiveDotNet.WebSocket.Tests;

public class SubscriptionTests
{
    private const string Connected = """[{"ev":"status","status":"connected","message":"Connected Successfully"}]""";
    private const string AuthSuccess = """[{"ev":"status","status":"auth_success","message":"authenticated"}]""";

    private static async Task<MassiveStreamConnection> ConnectAsync(FakeWebSocket socket)
    {
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamConnection connection = new(
            new MassiveStreamOptions { ApiKey = "k" },
            MassiveMarket.Stocks,
            () => socket,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        return connection;
    }

    [Fact]
    public async Task ItSendsOneCommaSeparatedSubscribeAndWaitsForEveryAcknowledgement()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        socket.EnqueueText(
            """[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"},{"ev":"status","status":"success","message":"subscribed to: T.MSFT"}]""");

        await connection.SubscribeAsync("T", ["AAPL", "MSFT"], TestContext.Current.CancellationToken);

        Assert.Contains("""{"action":"subscribe","params":"T.AAPL,T.MSFT"}""", socket.Sent);
    }

    // The finding that shaped the design: the server drops an unrecognised pair in silence, so a
    // subscription that does not exist is indistinguishable from a quiet market (D-W2).
    [Fact]
    public async Task AShortfallOfAcknowledgementsThrows()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        socket.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""");

        MassiveStreamSubscriptionException error =
            await Assert.ThrowsAsync<MassiveStreamSubscriptionException>(async () =>
                await connection.SubscribeAsync("T", ["AAPL", "MSFT"], TestContext.Current.CancellationToken));

        Assert.Equal(1, error.Unacknowledged);
        Assert.Contains("T.AAPL,T.MSFT", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WildcardsSubscribeLikeAnyOtherTicker()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        socket.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: T.*"}]""");

        await connection.SubscribeAsync("T", ["*"], TestContext.Current.CancellationToken);

        Assert.Contains("""{"action":"subscribe","params":"T.*"}""", socket.Sent);
    }

    [Fact]
    public async Task TheRegistryRemembersWhatReconnectMustReplay()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        socket.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""");
        await connection.SubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        socket.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: Q.MSFT"}]""");
        await connection.SubscribeAsync("Q", ["MSFT"], TestContext.Current.CancellationToken);

        Assert.Equal(["Q.MSFT", "T.AAPL"], connection.Registry.Parameters.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task UnsubscribeSendsTheActionAndForgetsThePair()
    {
        await using FakeWebSocket socket = new();
        await using MassiveStreamConnection connection = await ConnectAsync(socket);

        socket.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""");
        await connection.SubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        await connection.UnsubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        Assert.Contains("""{"action":"unsubscribe","params":"T.AAPL"}""", socket.Sent);
        Assert.Empty(connection.Registry.Parameters);
    }

    [Theory]
    [InlineData(StockTopic.Trades, "T")]
    [InlineData(StockTopic.Quotes, "Q")]
    public void TopicsRenderAsTheirWireCodes(StockTopic topic, string expected) =>
        Assert.Equal(expected, topic.ToCode());
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~SubscriptionTests"`
Expected: FAIL — `StockTopic` and `SubscribeAsync` do not exist.

- [ ] **Step 3: Implement the topic enum, the registry, and the exception**

`src/MassiveDotNet.WebSocket/StockTopic.cs`:

```csharp
namespace MassiveDotNet.WebSocket;

/// <summary>A stock streaming topic.</summary>
/// <remarks>
/// An enum rather than a string because the server **silently ignores** a topic code it does not
/// recognise: no acknowledgement, no error, and no data ever after. A caller who mistypes a string
/// would see a healthy connection producing nothing, indefinitely. The mistake is made
/// unrepresentable instead of validated (D-W1).
/// </remarks>
public enum StockTopic
{
    /// <summary>Tick-level trades, wire code <c>T</c>.</summary>
    Trades,

    /// <summary>NBBO quotes, wire code <c>Q</c>.</summary>
    Quotes,
}

/// <summary>Extensions for <see cref="StockTopic"/>.</summary>
internal static class StockTopicExtensions
{
    public static string ToCode(this StockTopic topic) => topic switch
    {
        StockTopic.Trades => "T",
        StockTopic.Quotes => "Q",
        _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, null),
    };
}
```

`src/MassiveDotNet.WebSocket/MassiveStreamSubscriptionException.cs`:

```csharp
namespace MassiveDotNet.WebSocket;

/// <summary>The server did not acknowledge every subscription requested.</summary>
/// <remarks>
/// The server answers one <c>status: success</c> per accepted pair and says nothing about a pair it
/// does not recognise, so a shortfall is the only evidence that something was dropped. It throws
/// rather than continuing because a subscription that silently does not exist is indistinguishable
/// from a quiet market (D-W2).
/// </remarks>
public sealed class MassiveStreamSubscriptionException : MassiveStreamException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="parameters">The subscription parameter that was sent.</param>
    /// <param name="unacknowledged">How many pairs went unacknowledged.</param>
    public MassiveStreamSubscriptionException(string parameters, int unacknowledged)
        : base($"The stream acknowledged {unacknowledged} fewer subscriptions than were requested "
               + $"for '{parameters}'. The server ignores a topic code it does not recognise, so "
               + "the unacknowledged pairs would never have produced data.") =>
        Unacknowledged = unacknowledged;

    /// <summary>How many requested pairs went unacknowledged.</summary>
    public int Unacknowledged { get; }
}
```

`src/MassiveDotNet.WebSocket/Internal/SubscriptionRegistry.cs`:

```csharp
namespace MassiveDotNet.WebSocket.Internal;

/// <summary>What a reconnect replays: every live <c>topic.ticker</c> pair, deduplicated.</summary>
internal sealed class SubscriptionRegistry
{
    private readonly HashSet<string> _parameters = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Parameters => _parameters;

    public void Add(string topicCode, IEnumerable<string> tickers)
    {
        foreach (string ticker in tickers)
        {
            _parameters.Add($"{topicCode}.{ticker}");
        }
    }

    public void Remove(string topicCode, IEnumerable<string> tickers)
    {
        foreach (string ticker in tickers)
        {
            _parameters.Remove($"{topicCode}.{ticker}");
        }
    }
}
```

- [ ] **Step 4: Implement subscribe on the connection**

Add to `MassiveStreamConnection`. Acknowledgements arrive through the read loop, so a pending
subscribe registers a counter the loop decrements:

```csharp
    private readonly SemaphoreSlim _subscribeGate = new(1, 1);
    private TaskCompletionSource<int>? _pendingAcknowledgements;
    private int _outstanding;

    /// <summary>Every live subscription, for replay after a reconnect.</summary>
    public SubscriptionRegistry Registry { get; } = new();

    public async Task SubscribeAsync(string topicCode, IReadOnlyCollection<string> tickers, CancellationToken cancellationToken)
    {
        string parameters = string.Join(',', tickers.Select(ticker => $"{topicCode}.{ticker}"));

        // One subscribe in flight at a time: acknowledgements carry no correlation id, so two
        // overlapping requests could not tell whose acknowledgement arrived.
        await _subscribeGate.WaitAsync(cancellationToken);

        try
        {
            _outstanding = tickers.Count;
            _pendingAcknowledgements = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            await SendActionAsync("subscribe", parameters, cancellationToken);

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.HandshakeTimeout.ToTimeSpan());

            int acknowledged;

            try
            {
                acknowledged = await _pendingAcknowledgements.Task.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                acknowledged = tickers.Count - _outstanding;
            }

            if (acknowledged < tickers.Count)
            {
                throw new MassiveStreamSubscriptionException(parameters, tickers.Count - acknowledged);
            }

            Registry.Add(topicCode, tickers);
        }
        finally
        {
            _pendingAcknowledgements = null;
            _subscribeGate.Release();
        }
    }

    public async Task UnsubscribeAsync(string topicCode, IReadOnlyCollection<string> tickers, CancellationToken cancellationToken)
    {
        // Not acknowledgement-counted: an unsubscribe for a pair the server never had is harmless,
        // where a subscribe that silently did nothing is data the caller will never see.
        await SendActionAsync(
            "unsubscribe",
            string.Join(',', tickers.Select(ticker => $"{topicCode}.{ticker}")),
            cancellationToken);

        Registry.Remove(topicCode, tickers);
    }

    private async Task SendActionAsync(string action, string parameters, CancellationToken cancellationToken)
    {
        byte[] frame = Encoding.UTF8.GetBytes($$"""{"action":"{{action}}","params":"{{parameters}}"}""");
        await _socket!.SendAsync(frame, cancellationToken);
    }

    /// <summary>Called by the read loop for every status event, before anything else sees it.</summary>
    private void OnStatus(in StatusMessage status)
    {
        if (status.Status != StatusMessage.Success || _pendingAcknowledgements is null)
        {
            return;
        }

        if (Interlocked.Decrement(ref _outstanding) == 0)
        {
            _pendingAcknowledgements.TrySetResult(0);
        }
    }
```

Wire `OnStatus` into `ReadLoopAsync`: parse each message's status events first, feed them to
`OnStatus`, and pass the payload on to `OnMessage` only when it carries no status event. The
acknowledgement count reported on timeout is `tickers.Count - _outstanding`, so the exception names
the true shortfall rather than assuming none arrived.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests`
Expected: PASS, all six subscription tests plus the earlier ones.

- [ ] **Step 6: Commit**

```bash
git add src/MassiveDotNet.WebSocket tests/MassiveDotNet.WebSocket.Tests/SubscriptionTests.cs
git commit -m "feat: subscribe with acknowledgement counting and a typed topic

Probed 2026-09-07: the server acknowledges a valid topic with an unknown ticker
(T.NOTATICKER) and silently drops an unknown topic (ZZ.AAPL) -- no acknowledgement,
no error. So the one mistake it hides is a mistyped topic code, and the usual
posture of letting the server reject a bad value does not apply here.

The enum makes that mistake unrepresentable. Counting acknowledgements guards the
causes not yet observed, because a subscription that silently does not exist is
indistinguishable from a quiet market.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 9: The two stock event types and their converters

**Files:**
- Create: `src/MassiveDotNet.WebSocket/Events/ConditionSet.cs`
- Create: `src/MassiveDotNet.WebSocket/Events/StockTrade.cs`
- Create: `src/MassiveDotNet.WebSocket/Events/StockQuote.cs`
- Create: `src/MassiveDotNet.WebSocket/Internal/StockTradeConverter.cs`
- Create: `src/MassiveDotNet.WebSocket/Internal/StockQuoteConverter.cs`
- Create: `tests/MassiveDotNet.WebSocket.Tests/Fixtures/stock-trade.json`
- Create: `tests/MassiveDotNet.WebSocket.Tests/Fixtures/stock-quote.json`
- Create: `tests/MassiveDotNet.WebSocket.Tests/EventParsingTests.cs`

**Interfaces:**
- Consumes: `TickerPool` (Task 3), `Epoch` (Task 2), `JsonValueReader` (core).
- Produces: `public readonly record struct StockTrade`, `public readonly record struct StockQuote`,
  `public readonly struct ConditionSet` (with `Count`, `this[int]`, `AsSpan()`), and
  `internal sealed class StockTradeConverter : JsonConverter<StockTrade>` /
  `StockQuoteConverter` with constructors taking a `TickerPool`.

Both fixtures are **Massive's published sample responses verbatim** — the repo's first-preference
fixture source. The stock market was closed the day this plan was written, so no live capture was
possible; Task 14 confirms these shapes on the wire during market hours.

Note what the trade sample omits: `ds`, `trfi`, and `trft` are absent, which is the documentation's
own evidence that they are optional. The converters must tolerate any subset.

- [ ] **Step 1: Write the fixtures**

`tests/MassiveDotNet.WebSocket.Tests/Fixtures/stock-trade.json` — the published sample, wrapped in
the array the wire actually delivers:

```json
[{"ev":"T","sym":"MSFT","x":4,"i":"12345","z":3,"p":114.125,"s":100,"c":[0,12],"t":1536036818784,"pt":1536036818763,"q":3681328}]
```

`tests/MassiveDotNet.WebSocket.Tests/Fixtures/stock-quote.json`:

```json
[{"ev":"Q","sym":"MSFT","bx":4,"bp":114.125,"bs":100,"ax":7,"ap":114.128,"as":160,"c":0,"i":[604],"t":1536036818784,"q":50385480,"z":3}]
```

Both must be `<Content>` with `CopyToOutputDirectory` set, following the Rest test project's
fixture wiring.

- [ ] **Step 2: Write the failing test**

`tests/MassiveDotNet.WebSocket.Tests/EventParsingTests.cs`:

```csharp
using System.Text;
using System.Text.Json;
using MassiveDotNet;
using MassiveDotNet.WebSocket.Events;
using MassiveDotNet.WebSocket.Internal;
using NodaTime;

namespace MassiveDotNet.WebSocket.Tests;

public class EventParsingTests
{
    private static StockTrade ReadTrade(string json)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        reader.Read();  // [
        reader.Read();  // {

        return new StockTradeConverter(new TickerPool(16)).Read(ref reader, typeof(StockTrade), JsonSerializerOptions.Default);
    }

    [Fact]
    public void ThePublishedTradeSampleDeserializes()
    {
        StockTrade trade = ReadTrade(File.ReadAllText("Fixtures/stock-trade.json"));

        Assert.Equal("MSFT", trade.Ticker);
        Assert.Equal(4, trade.ExchangeId);
        Assert.Equal("12345", trade.TradeId);
        Assert.Equal(3, trade.Tape);
        Assert.Equal(114.125, trade.Price);
        Assert.Equal(100, trade.Size);
        Assert.Equal(3681328, trade.SequenceNumber);
        Assert.Equal([0, 12], trade.Conditions.AsSpan().ToArray());
    }

    // D5: the raw epoch is stored and the Instant computed on read. The streaming wire sends
    // milliseconds where REST v3 sends nanoseconds for the same conceptual field (D-W11).
    [Fact]
    public void TimestampsAreMillisecondsExposedAsInstants()
    {
        StockTrade trade = ReadTrade(File.ReadAllText("Fixtures/stock-trade.json"));

        Assert.Equal(1536036818784, trade.SipTimestampMilliseconds);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1536036818784), trade.SipTimestamp);
        Assert.Equal(Instant.FromUnixTimeMilliseconds(1536036818763), trade.ParticipantTimestamp);
    }

    // The published sample omits ds, trfi and trft, which is the documentation's own evidence
    // that a real message need not carry them.
    [Fact]
    public void OptionalFieldsAbsentFromTheSampleReadAsNull()
    {
        StockTrade trade = ReadTrade(File.ReadAllText("Fixtures/stock-trade.json"));

        Assert.Null(trade.DecimalSize);
        Assert.Null(trade.TrfId);
        Assert.Null(trade.TrfTimestampMilliseconds);
        Assert.Null(trade.TrfTimestamp);
    }

    [Fact]
    public void AnUnknownPropertyIsSkippedRatherThanThrowing()
    {
        StockTrade trade = ReadTrade("""[{"ev":"T","sym":"MSFT","p":1.0,"s":1,"i":"x","t":1,"q":1,"brandNew":{"nested":[1,2]}}]""");

        Assert.Equal("MSFT", trade.Ticker);
    }

    [Fact]
    public void ThePublishedQuoteSampleDeserializes()
    {
        Utf8JsonReader reader = new(File.ReadAllBytes("Fixtures/stock-quote.json"));
        reader.Read();
        reader.Read();

        StockQuote quote = new StockQuoteConverter(new TickerPool(16))
            .Read(ref reader, typeof(StockQuote), JsonSerializerOptions.Default);

        Assert.Equal("MSFT", quote.Ticker);
        Assert.Equal(4, quote.BidExchangeId);
        Assert.Equal(114.125, quote.BidPrice);
        Assert.Equal(100, quote.BidSize);
        Assert.Equal(7, quote.AskExchangeId);
        Assert.Equal(114.128, quote.AskPrice);
        Assert.Equal(160, quote.AskSize);
        Assert.Equal(0, quote.Condition);
        Assert.Equal([604], quote.Indicators.AsSpan().ToArray());
        Assert.Equal(50385480, quote.SequenceNumber);
        Assert.Equal(3, quote.Tape);
    }

    [Fact]
    public void TheTickerIsPooledAcrossEvents()
    {
        TickerPool pool = new(16);
        StockTradeConverter converter = new(pool);

        StockTrade first = ReadWith(converter, """[{"ev":"T","sym":"AAPL","i":"1","p":1,"s":1,"t":1,"q":1}]""");
        StockTrade second = ReadWith(converter, """[{"ev":"T","sym":"AAPL","i":"2","p":2,"s":2,"t":2,"q":2}]""");

        Assert.Same(first.Ticker, second.Ticker);

        static StockTrade ReadWith(StockTradeConverter converter, string json)
        {
            Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
            reader.Read();
            reader.Read();
            return converter.Read(ref reader, typeof(StockTrade), JsonSerializerOptions.Default);
        }
    }

    // Never truncate: more conditions than the inline buffer holds spills to a heap array rather
    // than silently dropping codes, which is the data loss this SDK refuses everywhere else.
    [Fact]
    public void MoreConditionsThanFitInlineSpillWithoutLoss()
    {
        StockTrade trade = ReadTrade(
            """[{"ev":"T","sym":"MSFT","i":"1","p":1,"s":1,"t":1,"q":1,"c":[1,2,3,4,5,6,7,8,9,10]}]""");

        Assert.Equal(10, trade.Conditions.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], trade.Conditions.AsSpan().ToArray());
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~EventParsingTests"`
Expected: FAIL — none of the event types exist.

- [ ] **Step 4: Implement `ConditionSet`**

`src/MassiveDotNet.WebSocket/Events/ConditionSet.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MassiveDotNet.WebSocket.Events;

/// <summary>Up to eight condition codes held inline, spilling to the heap beyond that.</summary>
/// <remarks>
/// <para>
/// An <c>int[]</c> would allocate on every event carrying conditions, which is most trades. Renting
/// from <c>ArrayPool</c> is not available either: the array escapes to the caller with the event, so
/// nothing could ever return it. Eight covers every condition set observed, and the field holds no
/// reference in that case, so the common path allocates nothing.
/// </para>
/// <para>
/// A ninth code spills to a heap array rather than being dropped. Truncating would be silent data
/// loss, which this SDK refuses everywhere else.
/// </para>
/// </remarks>
public readonly struct ConditionSet
{
    /// <summary>How many codes fit before spilling to the heap.</summary>
    public const int InlineCapacity = 8;

    [InlineArray(InlineCapacity)]
    private struct Buffer
    {
        private int _element0;
    }

    private readonly Buffer _inline;
    private readonly int[]? _overflow;

    internal ConditionSet(ReadOnlySpan<int> codes)
    {
        Count = codes.Length;

        if (codes.Length > InlineCapacity)
        {
            _overflow = codes.ToArray();
            return;
        }

        for (int i = 0; i < codes.Length; i++)
        {
            _inline[i] = codes[i];
        }
    }

    /// <summary>How many codes this set holds.</summary>
    public int Count { get; }

    /// <summary>The code at <paramref name="index"/>.</summary>
    /// <param name="index">A zero-based index below <see cref="Count"/>.</param>
    public int this[int index] => AsSpan()[index];

    /// <summary>The codes, as a span over inline or spilled storage.</summary>
    /// <returns>A span of exactly <see cref="Count"/> codes.</returns>
    public ReadOnlySpan<int> AsSpan() =>
        _overflow is { } overflow
            ? overflow.AsSpan(0, Count)
            : MemoryMarshal.CreateReadOnlySpan(
                in Unsafe.As<Buffer, int>(ref Unsafe.AsRef(in _inline)),
                Count);
}
```

- [ ] **Step 5: Implement the two event types**

`src/MassiveDotNet.WebSocket/Events/StockTrade.cs`:

```csharp
using NodaTime;

namespace MassiveDotNet.WebSocket.Events;

/// <summary>One streamed stock trade: the <c>T</c> topic.</summary>
/// <remarks>
/// A tick-level type, so a struct (D4). Timestamps are stored as raw Unix **millisecond** values
/// and exposed as <see cref="Instant"/> only when read (D5). The unit differs from REST v3, which
/// sends nanoseconds for the same conceptual field (D-W11).
/// </remarks>
public readonly record struct StockTrade
{
    /// <summary>The ticker symbol, interned across events.</summary>
    public required string Ticker { get; init; }

    /// <summary>The trade ID, unique per ticker, exchange, and TRF combination.</summary>
    public required string TradeId { get; init; }

    /// <summary>The exchange ID.</summary>
    public int ExchangeId { get; init; }

    /// <summary>The tape: 1 = NYSE, 2 = AMEX, 3 = Nasdaq.</summary>
    public int? Tape { get; init; }

    /// <summary>The price per share.</summary>
    public double Price { get; init; }

    /// <summary>The trade size.</summary>
    public long Size { get; init; }

    /// <summary>The trade size including fractional shares, as the wire's decimal string.</summary>
    public string? DecimalSize { get; init; }

    /// <summary>The trade conditions.</summary>
    public ConditionSet Conditions { get; init; }

    /// <summary>The raw SIP timestamp, in Unix milliseconds.</summary>
    public long SipTimestampMilliseconds { get; init; }

    /// <summary>The raw participant timestamp, in Unix milliseconds, never after the SIP timestamp.</summary>
    public long? ParticipantTimestampMilliseconds { get; init; }

    /// <summary>The sequence number, increasing and unique per ticker but not contiguous.</summary>
    public long SequenceNumber { get; init; }

    /// <summary>The Trade Reporting Facility ID, when the trade was reported through one.</summary>
    public int? TrfId { get; init; }

    /// <summary>The raw TRF timestamp, in Unix milliseconds.</summary>
    public long? TrfTimestampMilliseconds { get; init; }

    /// <summary>When the SIP received the trade.</summary>
    public Instant SipTimestamp => Epoch.FromMilliseconds(SipTimestampMilliseconds);

    /// <summary>When the trade occurred at the exchange or TRF.</summary>
    public Instant? ParticipantTimestamp =>
        ParticipantTimestampMilliseconds is { } milliseconds ? Epoch.FromMilliseconds(milliseconds) : null;

    /// <summary>When the Trade Reporting Facility received the trade.</summary>
    public Instant? TrfTimestamp =>
        TrfTimestampMilliseconds is { } milliseconds ? Epoch.FromMilliseconds(milliseconds) : null;
}
```

`src/MassiveDotNet.WebSocket/Events/StockQuote.cs`:

```csharp
using NodaTime;

namespace MassiveDotNet.WebSocket.Events;

/// <summary>One streamed NBBO stock quote: the <c>Q</c> topic.</summary>
/// <remarks>A tick-level type, so a struct (D4), with the raw epoch stored and the instant computed (D5).</remarks>
public readonly record struct StockQuote
{
    /// <summary>The ticker symbol, interned across events.</summary>
    public required string Ticker { get; init; }

    /// <summary>The bid exchange ID.</summary>
    public int BidExchangeId { get; init; }

    /// <summary>The bid price.</summary>
    public double BidPrice { get; init; }

    /// <summary>The number of shares buyers are bidding for at the bid price.</summary>
    public long BidSize { get; init; }

    /// <summary>The ask exchange ID.</summary>
    public int AskExchangeId { get; init; }

    /// <summary>The ask price.</summary>
    public double AskPrice { get; init; }

    /// <summary>The number of shares sellers are offering at the ask price.</summary>
    public long AskSize { get; init; }

    /// <summary>The quote condition. A single code, unlike a trade's set.</summary>
    public int? Condition { get; init; }

    /// <summary>The quote indicators.</summary>
    public ConditionSet Indicators { get; init; }

    /// <summary>The raw SIP timestamp, in Unix milliseconds.</summary>
    public long SipTimestampMilliseconds { get; init; }

    /// <summary>The sequence number, reset each trading session.</summary>
    public long SequenceNumber { get; init; }

    /// <summary>The tape: 1 = NYSE, 2 = AMEX, 3 = Nasdaq.</summary>
    public int? Tape { get; init; }

    /// <summary>When the SIP received the quote.</summary>
    public Instant SipTimestamp => Epoch.FromMilliseconds(SipTimestampMilliseconds);
}
```

- [ ] **Step 6: Implement the converters**

`src/MassiveDotNet.WebSocket/Internal/StockTradeConverter.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using MassiveDotNet.Serialization;
using MassiveDotNet.WebSocket.Events;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>Reads a <see cref="StockTrade"/> straight off the reader's tokens.</summary>
/// <remarks>
/// Hand-written rather than generated, because there is no OpenAPI description for the streaming
/// wire to generate from. The shape follows D32's generated struct converters exactly: ordinal
/// matching, last value wins, unknown properties skipped wholesale, and scalars read through
/// <see cref="JsonValueReader"/> so a malformed value surfaces as a <see cref="JsonException"/>.
/// </remarks>
internal sealed class StockTradeConverter(TickerPool tickers) : JsonConverter<StockTrade>
{
    private const string Model = nameof(StockTrade);

    public override StockTrade Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected an object for {Model}, but found a {reader.TokenType} token.");
        }

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

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("sym"u8))
            {
                reader.Read();
                ticker = tickers.Intern(ref reader);
            }
            else if (reader.ValueTextEquals("i"u8))
            {
                reader.Read();
                tradeId = JsonValueReader.ReadString(ref reader, Model, "i");
            }
            else if (reader.ValueTextEquals("x"u8))
            {
                reader.Read();
                exchangeId = JsonValueReader.ReadInt32(ref reader, Model, "x");
            }
            else if (reader.ValueTextEquals("z"u8))
            {
                reader.Read();
                tape = JsonValueReader.ReadNullableInt32(ref reader, Model, "z");
            }
            else if (reader.ValueTextEquals("p"u8))
            {
                reader.Read();
                price = JsonValueReader.ReadDouble(ref reader, Model, "p");
            }
            else if (reader.ValueTextEquals("s"u8))
            {
                reader.Read();
                size = JsonValueReader.ReadInt64(ref reader, Model, "s");
            }
            else if (reader.ValueTextEquals("ds"u8))
            {
                reader.Read();
                decimalSize = JsonValueReader.ReadString(ref reader, Model, "ds");
            }
            else if (reader.ValueTextEquals("c"u8))
            {
                reader.Read();
                conditions = ReadConditions(ref reader, Model, "c");
            }
            else if (reader.ValueTextEquals("t"u8))
            {
                reader.Read();
                sipTimestamp = JsonValueReader.ReadInt64(ref reader, Model, "t");
            }
            else if (reader.ValueTextEquals("pt"u8))
            {
                reader.Read();
                participantTimestamp = JsonValueReader.ReadNullableInt64(ref reader, Model, "pt");
            }
            else if (reader.ValueTextEquals("q"u8))
            {
                reader.Read();
                sequenceNumber = JsonValueReader.ReadInt64(ref reader, Model, "q");
            }
            else if (reader.ValueTextEquals("trfi"u8))
            {
                reader.Read();
                trfId = JsonValueReader.ReadNullableInt32(ref reader, Model, "trfi");
            }
            else if (reader.ValueTextEquals("trft"u8))
            {
                reader.Read();
                trfTimestamp = JsonValueReader.ReadNullableInt64(ref reader, Model, "trft");
            }
            else
            {
                // Includes "ev", which the dispatcher has already consumed to choose this
                // converter, and anything Massive adds later.
                reader.Read();
                reader.Skip();
            }
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

    /// <summary>Reads a code array into inline storage, spilling only past the inline capacity.</summary>
    internal static ConditionSet ReadConditions(ref Utf8JsonReader reader, string model, string property)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected an array for {model}.{property}, but found a {reader.TokenType} token.");
        }

        Span<int> inline = stackalloc int[ConditionSet.InlineCapacity];
        List<int>? spilled = null;
        int count = 0;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            int code = JsonValueReader.ReadInt32(ref reader, model, property);

            if (count < ConditionSet.InlineCapacity)
            {
                inline[count] = code;
            }
            else
            {
                spilled ??= [.. inline];
                spilled.Add(code);
            }

            count++;
        }

        return spilled is null ? new ConditionSet(inline[..count]) : new ConditionSet([.. spilled]);
    }

    public override void Write(Utf8JsonWriter writer, StockTrade value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("ev", "T");
        writer.WriteString("sym", value.Ticker);
        writer.WriteNumber("x", value.ExchangeId);
        writer.WriteString("i", value.TradeId);
        writer.WriteNumber("p", value.Price);
        writer.WriteNumber("s", value.Size);
        writer.WriteNumber("t", value.SipTimestampMilliseconds);
        writer.WriteNumber("q", value.SequenceNumber);
        writer.WriteEndObject();
    }
}
```

`src/MassiveDotNet.WebSocket/Internal/StockQuoteConverter.cs` follows the same shape with the quote's
own properties. Its `while` body reads, in this order: `sym` through `tickers.Intern`, `bx`
(`ReadInt32`), `bp` (`ReadDouble`), `bs` (`ReadInt64`), `ax` (`ReadInt32`), `ap` (`ReadDouble`),
`as` (`ReadInt64`), `c` (`ReadNullableInt32`), `i` (`ReadConditions`, since indicators are the same
shape as a trade's condition array), `t` (`ReadInt64`), `q` (`ReadInt64`), `z`
(`ReadNullableInt32`), everything else skipped. It requires only `sym`, throwing
`JsonException` when it is absent, and its `Write` emits `ev` as `"Q"`.

Note `as` is a C# reserved word on the wire only; the property is `AskSize`, so nothing needs
escaping.

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~EventParsingTests"`
Expected: PASS, all seven.

- [ ] **Step 8: Commit**

```bash
git add src/MassiveDotNet.WebSocket tests/MassiveDotNet.WebSocket.Tests
git commit -m "feat: parse stock trades and quotes off the reader's tokens

Fixtures are Massive's published samples verbatim, which is the repo's
first-preference source; the market was closed the day this was written, so a
live capture was not possible. The live tier confirms the shapes during market
hours.

Conditions are held in an inline array of eight: an int[] would allocate on
nearly every trade, and ArrayPool cannot be used because the array escapes to the
caller and could never be returned. A ninth code spills to the heap rather than
being dropped.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 10: Bounded per-topic sequences that drop the oldest and count it

**Files:**
- Create: `src/MassiveDotNet.WebSocket/MassiveTopicSubscription.cs`
- Create: `src/MassiveDotNet.WebSocket/Internal/ITopicSink.cs`
- Create: `src/MassiveDotNet.WebSocket/Internal/TopicSink.cs`
- Modify: `src/MassiveDotNet.WebSocket/Internal/MassiveStreamConnection.cs` (dispatch)
- Create: `tests/MassiveDotNet.WebSocket.Tests/BackpressureTests.cs`

**Interfaces:**
- Consumes: the converters (Task 9), the read loop (Task 7).
- Produces: `public sealed class MassiveTopicSubscription<T> : IAsyncEnumerable<T>` with
  `long DroppedCount { get; }`; `internal interface ITopicSink` with `string TopicCode { get; }` and
  `void Write(ref Utf8JsonReader reader)`; `internal sealed class TopicSink<T> : ITopicSink`; on the
  connection, `void AddSink(ITopicSink sink)` and the dispatch that routes by `ev`.

- [ ] **Step 1: Write the failing test**

`tests/MassiveDotNet.WebSocket.Tests/BackpressureTests.cs`:

```csharp
using System.Text;
using System.Text.Json;
using MassiveDotNet.WebSocket.Events;
using MassiveDotNet.WebSocket.Internal;

namespace MassiveDotNet.WebSocket.Tests;

public class BackpressureTests
{
    private static TopicSink<StockTrade> CreateSink(int capacity) =>
        new("T", capacity, new StockTradeConverter(new TickerPool(16)));

    private static void Feed(TopicSink<StockTrade> sink, int sequence)
    {
        byte[] json = Encoding.UTF8.GetBytes(
            $$"""{"ev":"T","sym":"MSFT","i":"{{sequence}}","p":1,"s":1,"t":1,"q":{{sequence}}}""");

        Utf8JsonReader reader = new(json);
        reader.Read();
        sink.Write(ref reader);
    }

    [Fact]
    public async Task EventsArriveInOrderWithinATopic()
    {
        TopicSink<StockTrade> sink = CreateSink(capacity: 8);

        Feed(sink, 1);
        Feed(sink, 2);
        sink.Complete();

        List<long> sequences = [];
        await foreach (StockTrade trade in sink.Subscription.WithCancellation(TestContext.Current.CancellationToken))
        {
            sequences.Add(trade.SequenceNumber);
        }

        Assert.Equal([1, 2], sequences);
    }

    // The buffer is the whole memory budget: capacity times event size, times topics subscribed.
    // Nothing grows with time or with messages received.
    [Fact]
    public async Task AFullBufferDropsTheOldestAndKeepsTheNewest()
    {
        TopicSink<StockTrade> sink = CreateSink(capacity: 2);

        Feed(sink, 1);
        Feed(sink, 2);
        Feed(sink, 3);
        sink.Complete();

        List<long> sequences = [];
        await foreach (StockTrade trade in sink.Subscription.WithCancellation(TestContext.Current.CancellationToken))
        {
            sequences.Add(trade.SequenceNumber);
        }

        Assert.Equal([2, 3], sequences);
    }

    // D-W4's cost is that a consumer who never reads this loses data quietly. The count is what
    // makes that recoverable, and it is exact rather than inferred: Channel reports the eviction.
    [Fact]
    public void EveryDropIsCountedExactly()
    {
        TopicSink<StockTrade> sink = CreateSink(capacity: 2);

        for (int i = 1; i <= 7; i++)
        {
            Feed(sink, i);
        }

        Assert.Equal(5, sink.Subscription.DroppedCount);
    }

    [Fact]
    public void WritingNeverBlocksEvenWhenTheBufferIsFull()
    {
        TopicSink<StockTrade> sink = CreateSink(capacity: 1);

        // If a full buffer could block the writer, this would deadlock: every topic shares one
        // read loop, so a waiting writer stalls the socket and gets the connection dropped.
        for (int i = 0; i < 1000; i++)
        {
            Feed(sink, i);
        }

        Assert.Equal(999, sink.Subscription.DroppedCount);
    }

    // SingleReader is a performance contract the runtime does not police, so the guard is ours.
    [Fact]
    public void ASecondEnumerationThrows()
    {
        TopicSink<StockTrade> sink = CreateSink(capacity: 4);

        _ = sink.Subscription.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.Throws<InvalidOperationException>(() =>
            sink.Subscription.GetAsyncEnumerator(TestContext.Current.CancellationToken));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~BackpressureTests"`
Expected: FAIL — `TopicSink` does not exist.

- [ ] **Step 3: Implement the subscription**

`src/MassiveDotNet.WebSocket/MassiveTopicSubscription.cs`:

```csharp
using System.Threading.Channels;

namespace MassiveDotNet.WebSocket;

/// <summary>A live subscription to one topic: the events, and how many were dropped.</summary>
/// <typeparam name="T">The event type.</typeparam>
/// <remarks>
/// One sequence per topic, and one consumer per sequence. Enumerating twice throws rather than
/// letting two loops silently steal events from each other (D-W3).
/// </remarks>
public sealed class MassiveTopicSubscription<T> : IAsyncEnumerable<T>
{
    private readonly ChannelReader<T> _reader;
    private long _dropped;
    private int _enumerated;

    internal MassiveTopicSubscription(ChannelReader<T> reader) => _reader = reader;

    /// <summary>
    /// How many events were discarded because this topic's buffer was full when they arrived.
    /// </summary>
    /// <remarks>
    /// Monotonic, and exact rather than estimated. A non-zero value means the consumer is slower
    /// than the feed: raise <see cref="MassiveStreamOptions.TopicBufferCapacity"/>, or do less work
    /// in the loop. A consumer wiring the SDK through <c>AddMassiveStream</c> is warned on their
    /// logger automatically.
    /// </remarks>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    internal void RecordDrop() => Interlocked.Increment(ref _dropped);

    /// <summary>Enumerates this topic's events. May be called only once.</summary>
    /// <param name="cancellationToken">Ends the enumeration.</param>
    /// <returns>The enumerator.</returns>
    /// <exception cref="InvalidOperationException">The sequence is already being enumerated.</exception>
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _enumerated, 1) == 1)
        {
            throw new InvalidOperationException(
                $"This {nameof(MassiveTopicSubscription<T>)} is already being enumerated. A topic has "
                + "one sequence and one consumer; fan out in your own code if several need the events.");
        }

        return _reader.ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
    }
}
```

- [ ] **Step 4: Implement the sink**

`src/MassiveDotNet.WebSocket/Internal/ITopicSink.cs`:

```csharp
using System.Text.Json;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>Where the dispatcher hands an event whose <c>ev</c> matches this topic.</summary>
internal interface ITopicSink
{
    /// <summary>The wire code this sink claims, such as <c>T</c>.</summary>
    string TopicCode { get; }

    /// <summary>Parses one event object and buffers it. Must never block.</summary>
    void Write(ref Utf8JsonReader reader);

    /// <summary>Ends the sequence.</summary>
    void Complete();
}
```

`src/MassiveDotNet.WebSocket/Internal/TopicSink.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace MassiveDotNet.WebSocket.Internal;

/// <summary>One topic's bounded buffer and the converter that fills it.</summary>
internal sealed class TopicSink<T> : ITopicSink
{
    private readonly Channel<T> _channel;
    private readonly JsonConverter<T> _converter;

    public TopicSink(string topicCode, int capacity, JsonConverter<T> converter)
    {
        TopicCode = topicCode;
        _converter = converter;

        // DropOldest with an itemDropped callback: the runtime reports the eviction, so the count
        // is exact. Inferring it from Reader.Count before a write would race with the consumer and
        // be wrong in both directions.
        _channel = Channel.CreateBounded<T>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            },
            itemDropped: _ => Subscription.RecordDrop());

        Subscription = new MassiveTopicSubscription<T>(_channel.Reader);
    }

    public string TopicCode { get; }

    /// <summary>The caller-facing sequence.</summary>
    public MassiveTopicSubscription<T> Subscription { get; }

    public void Write(ref Utf8JsonReader reader)
    {
        T value = _converter.Read(ref reader, typeof(T), JsonSerializerOptions.Default);

        // TryWrite, never WriteAsync. Every topic shares one read loop: a writer that waits stalls
        // the socket, closes the receive window, and gets the connection dropped for being a slow
        // consumer -- taking down the topics that were keeping up (D-W4). With DropOldest this
        // always succeeds, and the eviction is counted rather than hidden.
        _channel.Writer.TryWrite(value);
    }

    public void Complete() => _channel.Writer.TryComplete();
}
```

- [ ] **Step 5: Route messages by their event code**

Add to `MassiveStreamConnection`:

```csharp
    private readonly Dictionary<string, ITopicSink> _sinks = new(StringComparer.Ordinal);

    public void AddSink(ITopicSink sink) => _sinks[sink.TopicCode] = sink;

    private ValueTask DispatchAsync(ReadOnlyMemory<byte> payload)
    {
        Utf8JsonReader reader = new(payload.Span);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            throw new MassiveStreamException("The stream sent a message that is not a JSON array.");
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            // A struct copy is a free bookmark. "ev" was first in every message observed, but
            // nothing in the protocol promises that, so the position is saved and the object is
            // re-read from the start once the code is known.
            Utf8JsonReader start = reader;
            string? code = ReadEventCode(ref reader);

            if (code is not null && _sinks.TryGetValue(code, out ITopicSink? sink))
            {
                Utf8JsonReader replay = start;
                sink.Write(ref replay);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Reads an object's <c>ev</c>, leaving the reader on that object's end token.</summary>
    private static string? ReadEventCode(ref Utf8JsonReader reader)
    {
        string? code = null;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool isEventCode = reader.ValueTextEquals("ev"u8);
            reader.Read();

            if (isEventCode)
            {
                code = reader.GetString();
            }
            else
            {
                reader.Skip();
            }
        }

        return code;
    }
```

Attach `DispatchAsync` as the connection's `OnMessage` in `StartReading`, after `OnStatus` has seen
the message.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests`
Expected: PASS, all five backpressure tests plus everything earlier.

- [ ] **Step 7: Commit**

```bash
git add src/MassiveDotNet.WebSocket tests/MassiveDotNet.WebSocket.Tests/BackpressureTests.cs
git commit -m "feat: bound each topic's buffer, dropping the oldest and counting it

TryWrite rather than WriteAsync is the load-bearing choice. Every topic shares one
read loop, so a writer that waits stalls the socket, closes the receive window,
and gets the connection dropped for being a slow consumer -- losing the topics
that were keeping up. Overflow is resolved at the buffer instead.

The count is exact rather than inferred: Channel's itemDropped callback reports
the eviction, where checking Reader.Count before a write would race the consumer
and be wrong in both directions.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 11: Reconnect with backoff, replaying every subscription

**Files:**
- Modify: `src/MassiveDotNet.WebSocket/Internal/MassiveStreamConnection.cs`
- Create: `tests/MassiveDotNet.WebSocket.Tests/ReconnectTests.cs`

**Interfaces:**
- Consumes: `SubscriptionRegistry` (Task 8), `MassiveStreamReconnectOptions` (Task 4), `IClock`.
- Produces: on the connection, `int ReconnectCount { get; }`, `Instant? LastReconnected { get; }`,
  and `event Action<int>? Reconnected`.

A reconnect means missed messages. The design that counts drops has no business hiding gaps, so it
is reported the same way (D-W7). Authentication failure is terminal regardless (D-W6).

- [ ] **Step 1: Write the failing test**

`tests/MassiveDotNet.WebSocket.Tests/ReconnectTests.cs`:

```csharp
using MassiveDotNet.WebSocket.Internal;
using NodaTime;
using NodaTime.Testing;

namespace MassiveDotNet.WebSocket.Tests;

public class ReconnectTests
{
    private const string Connected = """[{"ev":"status","status":"connected","message":"Connected Successfully"}]""";
    private const string AuthSuccess = """[{"ev":"status","status":"auth_success","message":"authenticated"}]""";
    private const string SubscribedTrades = """[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""";

    private static MassiveStreamOptions FastReconnect() => new()
    {
        ApiKey = "k",
        Reconnect = new MassiveStreamReconnectOptions
        {
            InitialBackoff = Duration.FromMilliseconds(1),
            MaxBackoff = Duration.FromMilliseconds(5),
            Jitter = 0,
        },
    };

    [Fact]
    public async Task ADroppedConnectionIsReestablishedAndSubscriptionsReplayed()
    {
        FakeWebSocket first = new();
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);
        first.EnqueueText(SubscribedTrades);

        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);
        second.EnqueueText(SubscribedTrades);

        await using MassiveStreamConnection connection = new(
            FastReconnect(),
            MassiveMarket.Stocks,
            () => created++ == 0 ? first : second,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();
        await connection.SubscribeAsync("T", ["AAPL"], TestContext.Current.CancellationToken);

        TaskCompletionSource reconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Reconnected += _ => reconnected.TrySetResult();

        first.AbortNext();
        await reconnected.Task.WaitAsync(Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Equal(1, connection.ReconnectCount);
        Assert.Contains("""{"action":"auth","params":"k"}""", second.Sent);
        Assert.Contains("""{"action":"subscribe","params":"T.AAPL"}""", second.Sent);
    }

    // Reconnecting against a key the server rejects hammers it until the account is limited, and
    // the server closes abruptly after auth_failed anyway, so a retry loop reconnects into a
    // refusal (D-W6).
    [Fact]
    public async Task AuthenticationFailureIsTerminalAndNeverRetried()
    {
        int created = 0;

        FakeWebSocket first = new();
        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);

        FakeWebSocket second = new();
        second.EnqueueText(Connected);
        second.EnqueueText("""[{"ev":"status","status":"auth_failed","message":"authentication failed"}]""");

        await using MassiveStreamConnection connection = new(
            FastReconnect(),
            MassiveMarket.Stocks,
            () => created++ == 0 ? first : second,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        TaskCompletionSource<Exception> faulted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Faulted += error => faulted.TrySetResult(error);

        first.AbortNext();
        Exception error = await faulted.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.IsType<MassiveStreamAuthenticationException>(error);
        Assert.Equal(2, created);   // one retry attempt, refused, then stop
    }

    [Fact]
    public void BackoffGrowsGeometricallyAndIsCappedAtMaxBackoff()
    {
        MassiveStreamReconnectOptions options = new()
        {
            InitialBackoff = Duration.FromSeconds(1),
            MaxBackoff = Duration.FromSeconds(4),
            BackoffMultiplier = 2.0,
            Jitter = 0,
        };

        Assert.Equal(Duration.FromSeconds(1), MassiveStreamConnection.BackoffFor(options, attempt: 0, jitterFactor: 0));
        Assert.Equal(Duration.FromSeconds(2), MassiveStreamConnection.BackoffFor(options, attempt: 1, jitterFactor: 0));
        Assert.Equal(Duration.FromSeconds(4), MassiveStreamConnection.BackoffFor(options, attempt: 2, jitterFactor: 0));
        Assert.Equal(Duration.FromSeconds(4), MassiveStreamConnection.BackoffFor(options, attempt: 9, jitterFactor: 0));
    }

    [Fact]
    public void JitterMovesTheDelayWithinTheConfiguredProportion()
    {
        MassiveStreamReconnectOptions options = new()
        {
            InitialBackoff = Duration.FromSeconds(10),
            MaxBackoff = Duration.FromSeconds(60),
            Jitter = 0.2,
        };

        Assert.Equal(Duration.FromSeconds(8), MassiveStreamConnection.BackoffFor(options, attempt: 0, jitterFactor: -1));
        Assert.Equal(Duration.FromSeconds(12), MassiveStreamConnection.BackoffFor(options, attempt: 0, jitterFactor: 1));
    }

    [Fact]
    public async Task ReconnectDisabledSurfacesTheDropInsteadOfHidingIt()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamOptions options = new() { ApiKey = "k", Reconnect = null };

        await using MassiveStreamConnection connection = new(
            options, MassiveMarket.Stocks, () => socket, new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        TaskCompletionSource<Exception> faulted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Faulted += error => faulted.TrySetResult(error);

        socket.AbortNext();
        Exception error = await faulted.Task.WaitAsync(
            Duration.FromSeconds(5).ToTimeSpan(), TestContext.Current.CancellationToken);

        Assert.Equal(0, connection.ReconnectCount);
        Assert.NotNull(error);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~ReconnectTests"`
Expected: FAIL — `ReconnectCount`, `Reconnected`, `Faulted`, and `BackoffFor` do not exist.

- [ ] **Step 3: Implement reconnect**

Add to `MassiveStreamConnection`:

```csharp
    private int _reconnectAttempt;

    /// <summary>How many times this connection has been re-established.</summary>
    /// <remarks>
    /// A reconnect means messages were missed while the socket was down. The protocol offers no
    /// cursor, so the gap cannot be repaired -- only reported, which is why this counter exists
    /// beside <see cref="MassiveTopicSubscription{T}.DroppedCount"/> (D-W7).
    /// </remarks>
    public int ReconnectCount { get; private set; }

    /// <summary>When the connection was last re-established.</summary>
    public Instant? LastReconnected { get; private set; }

    /// <summary>Raised after a successful reconnect, carrying the running count.</summary>
    public event Action<int>? Reconnected;

    /// <summary>Raised when the stream has stopped for good.</summary>
    public event Action<Exception>? Faulted;

    /// <summary>The delay before a given attempt, exposed for testing.</summary>
    /// <param name="options">The backoff configuration.</param>
    /// <param name="attempt">The zero-based attempt number.</param>
    /// <param name="jitterFactor">Between -1 and 1; the tests pass the extremes, the loop randomizes.</param>
    internal static Duration BackoffFor(MassiveStreamReconnectOptions options, int attempt, double jitterFactor)
    {
        double growth = Math.Pow(options.BackoffMultiplier, attempt);
        Duration raw = options.InitialBackoff * growth;
        Duration capped = raw > options.MaxBackoff ? options.MaxBackoff : raw;

        return capped * (1.0 + (options.Jitter * jitterFactor));
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = GC.AllocateUninitializedArray<byte>(_options.MaxMessageBytes);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                int length = await _reader!.ReadMessageAsync(buffer, cancellationToken);
                await HandleMessageAsync(buffer.AsMemory(0, length));
                _reconnectAttempt = 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error) when (error is WebSocketException or MassiveStreamException)
            {
                if (!await TryReconnectAsync(error, cancellationToken))
                {
                    return;
                }
            }
        }
    }

    private async Task<bool> TryReconnectAsync(Exception cause, CancellationToken cancellationToken)
    {
        if (_options.Reconnect is not { } reconnect)
        {
            Faulted?.Invoke(cause);
            return false;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            Duration delay = BackoffFor(reconnect, _reconnectAttempt++, Random.Shared.NextDouble() * 2.0 - 1.0);
            // Boundary crossing (produce): the Duration converts here, naming no BCL type.
            await Task.Delay(delay.ToTimeSpan(), cancellationToken);

            try
            {
                if (_socket is { } previous)
                {
                    await previous.DisposeAsync();
                }

                await ConnectAsync(cancellationToken);
                _reader = new FrameReader(_socket!, _options.MaxMessageBytes);

                // Replay before reporting success: a caller told the stream is back has every
                // right to assume their subscriptions came back with it.
                foreach (string parameters in Registry.Parameters)
                {
                    await SendActionAsync("subscribe", parameters, cancellationToken);
                }

                ReconnectCount++;
                LastReconnected = _clock.GetCurrentInstant();
                Reconnected?.Invoke(ReconnectCount);

                return true;
            }
            catch (MassiveStreamAuthenticationException error)
            {
                // Terminal. Retrying a refused key hammers the service until the account is
                // limited, and the server closes abruptly after auth_failed, so every further
                // attempt would reconnect straight into the same refusal (D-W6).
                Faulted?.Invoke(error);
                return false;
            }
            catch (Exception error) when (error is WebSocketException or MassiveStreamException)
            {
                // Transient: fall through and back off again.
            }
        }

        return false;
    }
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests`
Expected: PASS, all five reconnect tests plus everything earlier.

- [ ] **Step 5: Commit**

```bash
git add src/MassiveDotNet.WebSocket tests/MassiveDotNet.WebSocket.Tests/ReconnectTests.cs
git commit -m "feat: reconnect with backoff and replay every subscription

Subscriptions are replayed before the reconnect is reported, because a caller
told the stream is back has every right to assume their subscriptions came back
with it.

Authentication failure is terminal. Retrying a refused key hammers the service
until the account is limited, and the server closes abruptly after auth_failed,
so every further attempt reconnects straight into the same refusal.

A reconnect means missed messages and the protocol offers no cursor, so the gap
is reported rather than repaired -- the same instrument the drop counter uses.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 12: The public entry point, the stocks façade, and the logging bridge

**Files:**
- Create: `src/MassiveDotNet.WebSocket/MassiveStreamClient.cs`
- Create: `src/MassiveDotNet.WebSocket/MassiveStockStream.cs`
- Create: `src/MassiveDotNet.Extensions.DependencyInjection/MassiveStreamServiceCollectionExtensions.cs`
- Modify: `src/MassiveDotNet.Extensions.DependencyInjection/MassiveDotNet.Extensions.DependencyInjection.csproj`
- Create: `tests/MassiveDotNet.WebSocket.Tests/StockStreamTests.cs`
- Create: `tests/MassiveDotNet.Extensions.DependencyInjection.Tests/AddMassiveStreamTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `public sealed class MassiveStreamClient : IAsyncDisposable` with
  `MassiveStreamClient(MassiveStreamOptions options)` and
  `Task<MassiveStockStream> ConnectStocksAsync(CancellationToken cancellationToken = default)`
  and `internal Task ConnectRawAsync(MassiveMarket market, CancellationToken cancellationToken)`,
  which performs the handshake for any market and returns nothing — it exists so Task 14 can pin
  the entitlement of the five markets #20 ships no facade for;
  `public sealed class MassiveStockStream : IAsyncDisposable` with
  `Task<MassiveTopicSubscription<StockTrade>> SubscribeTradesAsync(IReadOnlyCollection<string> tickers, CancellationToken cancellationToken = default)`,
  the same for `SubscribeQuotesAsync`, `Task UnsubscribeAsync(StockTopic topic, IReadOnlyCollection<string> tickers, CancellationToken cancellationToken = default)`,
  `int ReconnectCount { get; }`, `Instant? LastReconnected { get; }`;
  `public static IServiceCollection AddMassiveStream(this IServiceCollection services, Action<MassiveStreamOptions> configure)`.

Subscribing to a topic twice widens the ticker set and returns the **same** subscription, which is
what bounds buffer count to the number of topics rather than the number of calls (D-W3).

- [ ] **Step 1: Write the failing test**

`tests/MassiveDotNet.WebSocket.Tests/StockStreamTests.cs`:

```csharp
using MassiveDotNet.WebSocket.Events;

namespace MassiveDotNet.WebSocket.Tests;

public class StockStreamTests
{
    private const string Connected = """[{"ev":"status","status":"connected","message":"Connected Successfully"}]""";
    private const string AuthSuccess = """[{"ev":"status","status":"auth_success","message":"authenticated"}]""";

    private static async Task<(MassiveStockStream Stream, FakeWebSocket Socket)> ConnectAsync()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamClient client = new(new MassiveStreamOptions { ApiKey = "k" });
        MassiveStockStream stream = await client.ConnectStocksAsync(socket, TestContext.Current.CancellationToken);

        return (stream, socket);
    }

    [Fact]
    public async Task SubscribingTwiceToATopicWidensItRatherThanMintingASecondBuffer()
    {
        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync();
        await using MassiveStockStream _ = stream;

        socket.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""");
        MassiveTopicSubscription<StockTrade> first =
            await stream.SubscribeTradesAsync(["AAPL"], TestContext.Current.CancellationToken);

        socket.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: T.MSFT"}]""");
        MassiveTopicSubscription<StockTrade> second =
            await stream.SubscribeTradesAsync(["MSFT"], TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Contains("""{"action":"subscribe","params":"T.MSFT"}""", socket.Sent);
    }

    [Fact]
    public async Task TradesAndQuotesAreSeparateSequences()
    {
        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync();
        await using MassiveStockStream _ = stream;

        socket.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""");
        MassiveTopicSubscription<StockTrade> trades =
            await stream.SubscribeTradesAsync(["AAPL"], TestContext.Current.CancellationToken);

        socket.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: Q.AAPL"}]""");
        MassiveTopicSubscription<StockQuote> quotes =
            await stream.SubscribeQuotesAsync(["AAPL"], TestContext.Current.CancellationToken);

        socket.EnqueueText(
            """[{"ev":"T","sym":"AAPL","i":"1","p":10,"s":1,"t":1,"q":1},{"ev":"Q","sym":"AAPL","bp":9,"ap":11,"t":1,"q":2}]""");

        await using IAsyncEnumerator<StockTrade> trade = trades.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await using IAsyncEnumerator<StockQuote> quote = quotes.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await trade.MoveNextAsync());
        Assert.Equal(10, trade.Current.Price);

        Assert.True(await quote.MoveNextAsync());
        Assert.Equal(9, quote.Current.BidPrice);
    }

    [Fact]
    public async Task OneFrameCarryingSeveralEventsIsSplitAcrossTheirTopics()
    {
        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync();
        await using MassiveStockStream _ = stream;

        socket.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""");
        MassiveTopicSubscription<StockTrade> trades =
            await stream.SubscribeTradesAsync(["AAPL"], TestContext.Current.CancellationToken);

        socket.EnqueueText(
            """[{"ev":"T","sym":"AAPL","i":"1","p":1,"s":1,"t":1,"q":1},{"ev":"T","sym":"AAPL","i":"2","p":2,"s":1,"t":2,"q":2}]""");

        await using IAsyncEnumerator<StockTrade> enumerator =
            trades.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(1, enumerator.Current.SequenceNumber);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(2, enumerator.Current.SequenceNumber);
    }

    // An event for a topic nobody subscribed to must not throw, because a wildcard subscription
    // and a server-side addition both produce exactly that.
    [Fact]
    public async Task AnEventForAnUnsubscribedTopicIsIgnored()
    {
        (MassiveStockStream stream, FakeWebSocket socket) = await ConnectAsync();
        await using MassiveStockStream _ = stream;

        socket.EnqueueText("""[{"ev":"status","status":"success","message":"subscribed to: T.AAPL"}]""");
        MassiveTopicSubscription<StockTrade> trades =
            await stream.SubscribeTradesAsync(["AAPL"], TestContext.Current.CancellationToken);

        socket.EnqueueText("""[{"ev":"AM","sym":"AAPL","o":1,"c":2},{"ev":"T","sym":"AAPL","i":"1","p":1,"s":1,"t":1,"q":7}]""");

        await using IAsyncEnumerator<StockTrade> enumerator =
            trades.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(7, enumerator.Current.SequenceNumber);
    }
}
```

`ConnectStocksAsync(IMassiveWebSocket, CancellationToken)` is an `internal` overload for the tests;
the public overload builds a `ClientWebSocketAdapter`. `InternalsVisibleTo` is already in place from
Task 5.

`tests/MassiveDotNet.Extensions.DependencyInjection.Tests/AddMassiveStreamTests.cs`:

```csharp
using MassiveDotNet.WebSocket;
using Microsoft.Extensions.DependencyInjection;

namespace MassiveDotNet.Extensions.DependencyInjection.Tests;

public class AddMassiveStreamTests
{
    [Fact]
    public void ItRegistersTheClientAsASingleton()
    {
        ServiceCollection services = new();
        services.AddMassiveStream(options => options.ApiKey = "k");

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<MassiveStreamClient>(),
            provider.GetRequiredService<MassiveStreamClient>());
    }

    [Fact]
    public void ItValidatesTheOptionsEagerly()
    {
        ServiceCollection services = new();
        services.AddMassiveStream(options => options.ApiKey = null);

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(provider.GetRequiredService<MassiveStreamClient>);
    }

    // Rule 8: the ILogger bridge lives here and only here, so core never learns that logging exists.
    [Fact]
    public void TheLoggingBridgeIsRegisteredOnlyInThisPackage()
    {
        Assert.DoesNotContain(
            typeof(MassiveStreamClient).Assembly.GetReferencedAssemblies(),
            reference => reference.Name?.StartsWith("Microsoft.Extensions", StringComparison.Ordinal) == true);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/MassiveDotNet.WebSocket.Tests tests/MassiveDotNet.Extensions.DependencyInjection.Tests`
Expected: FAIL — `MassiveStreamClient` does not exist.

- [ ] **Step 3: Implement the client and the façade**

`src/MassiveDotNet.WebSocket/MassiveStreamClient.cs`:

```csharp
using MassiveDotNet.WebSocket.Internal;
using NodaTime;

namespace MassiveDotNet.WebSocket;

/// <summary>Opens streaming connections to the Massive platform.</summary>
/// <remarks>
/// Long-lived and shared. One instance can open several market streams; each owns its own socket,
/// because the market is a path segment on the feed host rather than a subscription parameter.
/// </remarks>
public sealed class MassiveStreamClient : IAsyncDisposable
{
    private readonly MassiveStreamOptions _options;
    private readonly IClock _clock;
    private readonly List<IAsyncDisposable> _streams = [];

    /// <summary>Creates a client.</summary>
    /// <param name="options">The stream configuration. Validated immediately.</param>
    public MassiveStreamClient(MassiveStreamOptions options) : this(options, SystemClock.Instance)
    {
    }

    internal MassiveStreamClient(MassiveStreamOptions options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        _clock = clock;
    }

    /// <summary>Opens an authenticated stock stream.</summary>
    /// <param name="cancellationToken">Cancels the handshake.</param>
    /// <returns>The connected stream.</returns>
    /// <exception cref="MassiveStreamAuthenticationException">
    /// The key was refused, or the plan does not include WebSocket access for this market. The
    /// server's own message is on <see cref="MassiveStreamAuthenticationException.ServerMessage"/>.
    /// </exception>
    public Task<MassiveStockStream> ConnectStocksAsync(CancellationToken cancellationToken = default) =>
        ConnectStocksAsync(new ClientWebSocketAdapter(_options), cancellationToken);

    internal async Task<MassiveStockStream> ConnectStocksAsync(IMassiveWebSocket socket, CancellationToken cancellationToken)
    {
        MassiveStreamConnection connection = new(_options, MassiveMarket.Stocks, () => socket, _clock);

        await connection.ConnectAsync(cancellationToken);
        connection.StartReading();

        MassiveStockStream stream = new(connection, _options);
        _streams.Add(stream);

        return stream;
    }

    /// <summary>
    /// Performs the handshake for any market and closes immediately, so a test can observe what
    /// the server says about a market this package ships no facade for.
    /// </summary>
    /// <remarks>
    /// Internal because #20 ships only the stocks facade; #21 adds the rest. Entitlement is a
    /// property of the handshake rather than of any facade, so pinning it needs no facade at all.
    /// </remarks>
    internal async Task ConnectRawAsync(MassiveMarket market, CancellationToken cancellationToken)
    {
        await using ClientWebSocketAdapter socket = new(_options);
        MassiveStreamConnection connection = new(_options, market, () => socket, _clock);

        await using (connection)
        {
            await connection.ConnectAsync(cancellationToken);
        }
    }

    /// <summary>Closes every stream this client opened.</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (IAsyncDisposable stream in _streams)
        {
            await stream.DisposeAsync();
        }

        _streams.Clear();
    }
}
```

`src/MassiveDotNet.WebSocket/MassiveStockStream.cs`:

```csharp
using MassiveDotNet.WebSocket.Events;
using MassiveDotNet.WebSocket.Internal;
using NodaTime;

namespace MassiveDotNet.WebSocket;

/// <summary>An authenticated stock stream. Each topic is its own sequence.</summary>
public sealed class MassiveStockStream : IAsyncDisposable
{
    private readonly MassiveStreamConnection _connection;
    private readonly MassiveStreamOptions _options;
    private readonly TickerPool _tickers;
    private TopicSink<StockTrade>? _trades;
    private TopicSink<StockQuote>? _quotes;

    internal MassiveStockStream(MassiveStreamConnection connection, MassiveStreamOptions options)
    {
        _connection = connection;
        _options = options;
        _tickers = new TickerPool(options.TickerPoolCapacity);
    }

    /// <summary>How many times the underlying connection has been re-established.</summary>
    public int ReconnectCount => _connection.ReconnectCount;

    /// <summary>When the connection was last re-established.</summary>
    public Instant? LastReconnected => _connection.LastReconnected;

    /// <summary>Raised after a reconnect, carrying the running count.</summary>
    /// <remarks>A reconnect means messages were missed; the protocol offers no way to recover them.</remarks>
    public event Action<int>? Reconnected
    {
        add => _connection.Reconnected += value;
        remove => _connection.Reconnected -= value;
    }

    /// <summary>Subscribes to tick-level trades.</summary>
    /// <param name="tickers">Symbols, or <c>*</c> for every symbol.</param>
    /// <param name="cancellationToken">Cancels the subscribe.</param>
    /// <returns>
    /// This stream's trade sequence. Calling again widens the ticker set and returns the same
    /// sequence, so a topic has one buffer and one consumer however many times it is called.
    /// </returns>
    /// <exception cref="MassiveStreamSubscriptionException">
    /// The server acknowledged fewer subscriptions than were requested.
    /// </exception>
    public async Task<MassiveTopicSubscription<StockTrade>> SubscribeTradesAsync(
        IReadOnlyCollection<string> tickers,
        CancellationToken cancellationToken = default)
    {
        if (_trades is null)
        {
            _trades = new TopicSink<StockTrade>(
                StockTopic.Trades.ToCode(),
                _options.TopicBufferCapacity,
                new StockTradeConverter(_tickers));

            _connection.AddSink(_trades);
        }

        await _connection.SubscribeAsync(StockTopic.Trades.ToCode(), tickers, cancellationToken);

        return _trades.Subscription;
    }

    /// <summary>Subscribes to NBBO quotes.</summary>
    /// <param name="tickers">Symbols, or <c>*</c> for every symbol.</param>
    /// <param name="cancellationToken">Cancels the subscribe.</param>
    /// <returns>This stream's quote sequence, on the same terms as trades.</returns>
    /// <exception cref="MassiveStreamSubscriptionException">
    /// The server acknowledged fewer subscriptions than were requested.
    /// </exception>
    public async Task<MassiveTopicSubscription<StockQuote>> SubscribeQuotesAsync(
        IReadOnlyCollection<string> tickers,
        CancellationToken cancellationToken = default)
    {
        if (_quotes is null)
        {
            _quotes = new TopicSink<StockQuote>(
                StockTopic.Quotes.ToCode(),
                _options.TopicBufferCapacity,
                new StockQuoteConverter(_tickers));

            _connection.AddSink(_quotes);
        }

        await _connection.SubscribeAsync(StockTopic.Quotes.ToCode(), tickers, cancellationToken);

        return _quotes.Subscription;
    }

    /// <summary>Stops receiving a topic for the given symbols.</summary>
    /// <param name="topic">The topic.</param>
    /// <param name="tickers">The symbols to drop.</param>
    /// <param name="cancellationToken">Cancels the unsubscribe.</param>
    /// <returns>A task completing once the message has been sent.</returns>
    public Task UnsubscribeAsync(
        StockTopic topic,
        IReadOnlyCollection<string> tickers,
        CancellationToken cancellationToken = default) =>
        _connection.UnsubscribeAsync(topic.ToCode(), tickers, cancellationToken);

    /// <summary>Closes the stream and ends every topic sequence.</summary>
    public async ValueTask DisposeAsync()
    {
        _trades?.Complete();
        _quotes?.Complete();

        await _connection.DisposeAsync();
    }
}
```

- [ ] **Step 4: Implement the DI registration and the logging bridge**

`src/MassiveDotNet.Extensions.DependencyInjection/MassiveStreamServiceCollectionExtensions.cs`:

```csharp
using MassiveDotNet.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MassiveDotNet.Extensions.DependencyInjection;

/// <summary>Registers the Massive streaming client.</summary>
public static class MassiveStreamServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="MassiveStreamClient"/> as a singleton, configured by
    /// <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options. The API key is required.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// There is no <c>IConfiguration</c> overload, for D27's reason: the options carry NodaTime
    /// <c>Duration</c> values the configuration binder silently leaves at their defaults. Read the
    /// values yourself — <c>options.ApiKey = configuration["Massive:ApiKey"]</c>.
    /// </remarks>
    public static IServiceCollection AddMassiveStream(this IServiceCollection services, Action<MassiveStreamOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<MassiveStreamOptions>().Configure(configure);
        services.AddLogging();

        services.AddSingleton(provider =>
        {
            MassiveStreamOptions options = provider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<MassiveStreamOptions>>()
                .Value;

            options.Validate();

            return new MassiveStreamClient(options);
        });

        return services;
    }

    /// <summary>
    /// Reports drops and reconnects on the application's logger.
    /// </summary>
    /// <param name="stream">The stream to watch.</param>
    /// <param name="subscription">The subscription whose drop count to report.</param>
    /// <param name="logger">The logger.</param>
    /// <typeparam name="T">The event type.</typeparam>
    /// <remarks>
    /// D-W4's accepted cost is that a consumer who never reads <c>DroppedCount</c> loses data
    /// quietly. This is what narrows it: rule 8 confines <c>Microsoft.Extensions.*</c> to this
    /// package, so the bridge lives here and core never learns that logging exists.
    /// </remarks>
    public static void LogStreamHealth<T>(
        this MassiveStockStream stream,
        MassiveTopicSubscription<T> subscription,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(logger);

        long reported = 0;

        stream.Reconnected += count =>
            logger.LogWarning(
                "The Massive stream reconnected ({Count} so far). Messages sent while it was down were missed.",
                count);

        stream.DropObserved += () =>
        {
            long dropped = subscription.DroppedCount;

            if (dropped > reported)
            {
                logger.LogWarning(
                    "The Massive stream dropped {Dropped} events because a topic buffer was full. "
                    + "Raise TopicBufferCapacity or do less work in the consuming loop.",
                    dropped - reported);

                reported = dropped;
            }
        };
    }
}
```

`MassiveStockStream` needs the `DropObserved` event this bridge subscribes to: raise it from
`TopicSink`'s `itemDropped` callback, throttled to at most once a second so a sustained overflow
does not produce a log line per event.

Add the project reference to the DI package's `.csproj`:

```xml
    <ProjectReference Include="../MassiveDotNet.WebSocket/MassiveDotNet.WebSocket.csproj" />
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS, including `TheLoggingBridgeIsRegisteredOnlyInThisPackage`.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat: expose the stocks stream and bridge its health to ILogger

Subscribing to a topic twice widens its ticker set and returns the same sequence,
which is what bounds buffer count to the number of topics rather than the number
of calls.

The logging bridge narrows D-W4's accepted cost -- that a consumer who never reads
DroppedCount loses data quietly -- without core learning that logging exists, since
rule 8 confines Microsoft.Extensions.* to the DI package.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 13: Allocation gates

**Files:**
- Modify: `tests/MassiveDotNet.WebSocket.Tests/MassiveDotNet.WebSocket.Tests.csproj` (link `Allocation.cs`)
- Create: `tests/MassiveDotNet.WebSocket.Tests/AllocationTests.cs`
- Create: `docs/performance/2026-09-07-streaming-allocation-figures.md`

**Interfaces:**
- Consumes: everything above; `Allocation.Measure` from the Rest test project.
- Produces: no production code.

"No unbounded buffering" is #20's acceptance criterion, and this is what turns it into something
that can fail a build. D31 is emphatic that a guard nobody has watched go red is indistinguishable
from a clean tree, so **every ceiling here is regressed deliberately before it is committed**.

`Allocation` is `internal` to `MassiveDotNet.Rest.Tests`. Link it rather than copying it — two
copies of a measurement harness drift, and the drift is invisible because both stay green:

```xml
  <ItemGroup>
    <Compile Include="../MassiveDotNet.Rest.Tests/Allocation.cs" Link="Allocation.cs" />
  </ItemGroup>
```

- [ ] **Step 1: Write the failing test**

`tests/MassiveDotNet.WebSocket.Tests/AllocationTests.cs`:

```csharp
using System.Text;
using System.Text.Json;
using MassiveDotNet.WebSocket.Events;
using MassiveDotNet.WebSocket.Internal;

namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// Allocation ceilings for the streaming hot path. A regression that slips past every other test —
/// same events, same wire format, more garbage — fails here.
/// </summary>
/// <remarks>
/// Each ceiling carries roughly 20% headroom over the figure measured when it was set, recorded
/// beside it. A ceiling is not a target: lowering one when the path gets cheaper is part of the
/// change that made it cheaper, and raising one needs its reason written next to the number.
/// </remarks>
[Collection("Process memory")]
public sealed class AllocationTests
{
    /// <summary>Bytes one trade event allocated when this ceiling was set, on 2026-09-07.</summary>
    private const long MeasuredPerTrade = 0;

    private static byte[] TradeFrame(int count)
    {
        StringBuilder json = new("[");

        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                json.Append(',');
            }

            json.Append(
                $$"""{"ev":"T","sym":"MSFT","x":4,"i":"{{i}}","z":3,"p":114.125,"s":100,"c":[0,12],"t":1536036818784,"pt":1536036818763,"q":{{i}}}""");
        }

        return Encoding.UTF8.GetBytes(json.Append(']').ToString());
    }

    /// <summary>
    /// The claim D-W10 makes: after the ticker is interned and the conditions are inline, a parsed
    /// trade costs nothing on the heap. The trade id is the one field that still allocates, so it
    /// is held constant here and measured separately below.
    /// </summary>
    [Fact]
    public void ParsingATradeAllocatesNothingBeyondItsTradeId()
    {
        TickerPool pool = new(16);
        StockTradeConverter converter = new(pool);
        byte[] frame = Encoding.UTF8.GetBytes(
            """[{"ev":"T","sym":"MSFT","x":4,"i":"same","z":3,"p":114.125,"s":100,"c":[0,12],"t":1536036818784,"pt":1536036818763,"q":1}]""");

        long allocated = Allocation.Measure(() =>
        {
            Utf8JsonReader reader = new(frame);
            reader.Read();
            reader.Read();
            _ = converter.Read(ref reader, typeof(StockTrade), JsonSerializerOptions.Default);
        });

        // One string for the trade id, which is unique per trade and therefore unpoolable.
        Assert.True(
            allocated <= 64,
            $"Parsing one trade allocated {Allocation.Describe(allocated)}. The ticker is pooled and "
                + "the conditions are inline, so only the trade id should remain.");
    }

    /// <summary>
    /// The ticker pool's whole purpose. Without it this figure grows linearly with event count.
    /// </summary>
    [Fact]
    public void TheTickerCostsNothingAfterTheFirstEvent()
    {
        TickerPool pool = new(16);
        pool.Intern("MSFT");

        long allocated = Allocation.Measure(() =>
        {
            for (int i = 0; i < 1_000; i++)
            {
                _ = pool.Intern("MSFT");
            }
        });

        Assert.True(
            allocated == MeasuredPerTrade,
            $"1,000 repeat interns allocated {Allocation.Describe(allocated)}, and the whole point of "
                + "the pool is that a repeat symbol costs nothing.");
    }

    /// <summary>
    /// Conditions within the inline capacity must not touch the heap; this is what an
    /// <c>int[]</c> per trade would have cost.
    /// </summary>
    [Fact]
    public void ConditionsWithinTheInlineCapacityAllocateNothing()
    {
        byte[] codes = Encoding.UTF8.GetBytes("[0,12,37]");

        long allocated = Allocation.Measure(() =>
        {
            Utf8JsonReader reader = new(codes);
            reader.Read();
            _ = StockTradeConverter.ReadConditions(ref reader, "StockTrade", "c");
        });

        Assert.True(allocated == 0, $"An inline condition set allocated {Allocation.Describe(allocated)}.");
    }

    /// <summary>
    /// #20's acceptance criterion, stated so it can fail a build: what a stream holds must not
    /// grow with how many events have passed through it.
    /// </summary>
    /// <remarks>
    /// The buffer is bounded and the oldest event is evicted, so a consumer that never reads still
    /// costs a fixed amount. This is the streaming counterpart of
    /// <c>RetainsNoMemoryProportionalToThePagesTraversed</c>, and like it, it was confirmed to go
    /// red under an unbounded channel before being committed.
    /// </remarks>
    [Fact]
    public void RetainsNoMemoryProportionalToTheEventsReceived()
    {
        TopicSink<StockTrade> sink = new("T", capacity: 8, new StockTradeConverter(new TickerPool(64)));

        long Retained(int events)
        {
            byte[] frame = TradeFrame(events);

            for (int i = 0; i < 20; i++)
            {
                Utf8JsonReader reader = new(frame);
                reader.Read();

                while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
                {
                    sink.Write(ref reader);
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            return GC.GetTotalMemory(forceFullCollection: true);
        }

        long small = Retained(10);
        long large = Retained(1_000);

        Assert.True(
            large - small < 512 * 1024,
            $"Retention grew by {Allocation.Describe(large - small)} between 200 and 20,000 events. "
                + "A bounded buffer that drops the oldest must cost the same either way.");
    }
}
```

- [ ] **Step 2: Run it and watch each ceiling fail for the right reason**

This is the step D31 exists for. For each of the four, break the thing it guards, confirm the
assertion goes red, then restore:

| Assertion | Regression that must make it fail |
|---|---|
| `ParsingATradeAllocatesNothingBeyondItsTradeId` | Replace `tickers.Intern(ref reader)` with `reader.GetString()`. |
| `TheTickerCostsNothingAfterTheFirstEvent` | Probe with `_pool.TryGetValue(new string(ticker), …)` instead of the alternate lookup. |
| `ConditionsWithinTheInlineCapacityAllocateNothing` | Make `ReadConditions` always build the spilled `List<int>`. |
| `RetainsNoMemoryProportionalToTheEventsReceived` | Swap the bounded channel for `Channel.CreateUnbounded<T>()`. |

Record each measured figure and write the real numbers into the `Measured*` constants and into
`docs/performance/2026-09-07-streaming-allocation-figures.md`, following the format of
`docs/performance/2026-09-04-allocation-figures.md`.

- [ ] **Step 3: Run the whole offline tier**

Run: `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add tests docs/performance
git commit -m "test: gate the streaming hot path on measured allocation ceilings

'No unbounded buffering' is issue #20's acceptance criterion; this is what makes
it something that can fail a build rather than a claim.

Every ceiling was set by regressing the path it guards and watching the assertion
go red, per D31: a guard nobody has seen fail is indistinguishable from a clean
tree. The retention assertion was confirmed against an unbounded channel.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

### Task 14: The live tier, and the documentation the work owes

**Files:**
- Create: `tests/MassiveDotNet.IntegrationTests/StreamHandshakeLiveTests.cs`
- Modify: `tests/MassiveDotNet.IntegrationTests/MassiveDotNet.IntegrationTests.csproj`
- Modify: `CLAUDE.md` (boundary table, layout, decisions, testing tiers)
- Modify: `README.md`
- Modify: `samples/MassiveDotNet.AotSmokeTest/Program.cs`

**Interfaces:**
- Consumes: the whole public surface.
- Produces: no production code.

The live tier covers only what fixtures structurally cannot: that authentication works against the
real service, that the wire format still matches, and what the description does not state. Only
`stocks` is entitled on this key, so that bounds the tier honestly.

- [ ] **Step 1: Write the live tests**

`tests/MassiveDotNet.IntegrationTests/StreamHandshakeLiveTests.cs`:

```csharp
using MassiveDotNet.WebSocket;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// What a fixture structurally cannot verify: that the handshake works against the real service,
/// and that the entitlements and hosts observed on 2026-09-07 still hold.
/// </summary>
public sealed class StreamHandshakeLiveTests : LiveApiTest
{
    private static MassiveStreamOptions Options()
    {
        // LiveApiTest exposes Client and Ct, not the key: the key reaches a test through
        // LiveCredentials, behind the skip that keeps a missing .env honest rather than red.
        Assert.SkipUnless(LiveCredentials.IsAvailable, LiveCredentials.MissingKeyReason);

        return new MassiveStreamOptions { ApiKey = LiveCredentials.ApiKey };
    }

    [Fact]
    public async Task StocksAuthenticatesAndAcknowledgesASubscription()
    {
        await using MassiveStreamClient client = new(Options());
        await using MassiveStockStream stream = await client.ConnectStocksAsync(Ct);

        // A wildcard is accepted like any other subscription. No assertion is made that data
        // arrives: the market is closed outside trading hours, and a test that passes only during
        // them fails for a reason unrelated to the SDK.
        await stream.SubscribeTradesAsync(["AAPL"], Ct);
        await stream.SubscribeQuotesAsync(["AAPL"], Ct);

        Assert.Equal(0, stream.ReconnectCount);
    }

    /// <summary>
    /// Pinned observation, 2026-09-07: this key reaches only the stocks feed. Every other market
    /// answers auth_failed with an entitlement message rather than a credential one.
    /// </summary>
    /// <remarks>
    /// D21's posture: the observation is pinned and dated so it flips the day the entitlement
    /// changes, where a skip would read as green.
    /// </remarks>
    [Fact]
    public async Task TheOtherFiveMarketsAnswerWithTheEntitlementMessage()
    {
        await using MassiveStreamClient client = new(Options());

        // ConnectRawAsync rather than a crypto facade: #20 ships only the stocks facade, and #21
        // adds the rest. The entitlement is a property of the handshake, not of any facade.
        MassiveStreamAuthenticationException error =
            await Assert.ThrowsAsync<MassiveStreamAuthenticationException>(
                async () => await client.ConnectRawAsync(MassiveMarket.Crypto, Ct));

        Assert.Contains("websocket access", error.ServerMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Pinned observation, 2026-09-07: a nonsense topic is silently dropped, which is why
    /// StockTopic is an enum (D-W1) and why subscriptions are acknowledgement-counted (D-W2).
    /// </summary>
    /// <remarks>
    /// This is the highest-value test in the streaming tier. If Massive ever starts rejecting an
    /// unknown topic properly, this fails and D-W2's guard can be reconsidered.
    /// </remarks>
    [Fact]
    public async Task AnUnknownTopicIsStillSilentlyIgnored()
    {
        await using MassiveStreamClient client = new(Options());
        await using MassiveStockStream stream = await client.ConnectStocksAsync(Ct);

        await Assert.ThrowsAsync<MassiveStreamSubscriptionException>(
            async () => await stream.SubscribeRawAsync("ZZ", ["AAPL"], Ct));
    }

    /// <summary>
    /// Pinned observation, 2026-09-07: launchpad presents the ingress default certificate on both
    /// domains, so MassiveFeeds exposes no property for it (D-W8).
    /// </summary>
    [Fact]
    public async Task TheLaunchpadHostIsStillNotProvisioned()
    {
        MassiveStreamOptions options = Options();
        options.Feed = new Uri("wss://launchpad.massive.com");

        await using MassiveStreamClient client = new(options);

        await Assert.ThrowsAnyAsync<Exception>(async () => await client.ConnectStocksAsync(Ct));
    }
}
```

`SubscribeRawAsync(string topicCode, …)` is an `internal` escape hatch on `MassiveStockStream` that
exists only so a live test can send a topic the enum cannot express. It is `internal`, so no
consumer can reach it, and `InternalsVisibleTo` for the integration test project is added to the
`.WebSocket` csproj alongside the one from Task 5.

- [ ] **Step 2: Run the live tier**

```bash
set -a; . ./.env >/dev/null 2>&1; set +a
dotnet test MassiveDotNet.slnx --filter "Category=Integration"
```

Expected: PASS. If a pinned observation has changed, that is the point of the pin — update the test
and its date, and record what moved.

**Never** print, echo, or open `.env`.

- [ ] **Step 3: Root the streaming types in the AOT smoke test**

Add to `samples/MassiveDotNet.AotSmokeTest/Program.cs` a call that constructs a
`MassiveStreamClient`, builds both converters, and reads one event from a literal frame, so ILC
roots the hand-written converters and `InlineArray`. Do not connect: the sample publishes in CI,
which has no key and no network (rule 13).

```bash
dotnet publish samples/MassiveDotNet.AotSmokeTest -r osx-arm64 -c Release
```

Expected: **zero** IL warnings.

- [ ] **Step 4: Update the documentation**

`CLAUDE.md`:

- **Layout** — add `src/MassiveDotNet.WebSocket` with a one-line description.
- **Known boundary points** — promote the two anticipated rows to real ones, naming their sites:
  `ClientWebSocketOptions.KeepAliveInterval` → produce → `keepAlive.ToTimeSpan()` →
  `ClientWebSocketAdapter` ctor; `CancellationTokenSource.CancelAfter` → produce →
  `cts.CancelAfter(timeout.ToTimeSpan())` → `MassiveStreamConnection.ConnectAsync`. Add
  `Task.Delay` → produce → `MassiveStreamConnection.TryReconnectAsync`. Remove the two rows from
  the "Anticipated" table.
- **Testing** — add `MassiveDotNet.WebSocket.Tests` to the offline tier table.
- **Architecture decisions** — add D33 through D35, carrying the spec's arguments in one line each:

  | ID | Decision | Why |
  |----|----------|-----|
  | D33 | Streaming topics are a typed enum and every subscription is acknowledgement-counted; a shortfall throws. | The server acknowledges a valid topic with an unknown ticker and **silently drops an unknown topic** — no acknowledgement, no error (probed 2026-09-07). So the usual posture of letting the server reject a bad value (D9) does not apply: it does not reject, it ignores, and a caller would see a healthy connection producing nothing forever. The enum makes the observed mistake unrepresentable; counting guards the ones not yet observed, because a subscription that silently does not exist is indistinguishable from a quiet market (D29's argument). |
  | D34 | Each topic owns one bounded buffer that drops the **oldest** event and counts it exactly; the read loop never blocks, and the DI package bridges the count to `ILogger`. | Every topic shares one socket, so a writer that waits stalls the read loop, closes the receive window, and gets the connection dropped for being a slow consumer — losing the topics that were keeping up. Throwing on overflow was rejected because market data is bursty and the open auction alone would trip it. The accepted cost is that a consumer who never reads `DroppedCount` loses data quietly, which the logging bridge narrows without core learning that logging exists (rule 8). |
  | D35 | Authentication is a message, not a header, and its failure is terminal: the exception carries the server's message verbatim and reconnect never retries it. | `auth_failed` carries two unrelated failures — a refused key and a plan without WebSocket access — distinguished only by prose, and on the test key five of six markets answer the latter. The SDK cannot categorise them without reading prose, so it reports what the server said. Retrying would hammer the service until the account is limited, and the server closes abruptly after `auth_failed` anyway. Rule 11 gets harder here than under D2: a key in a frame body is one `ToString()` away from a log file, so the frame is built into a rented buffer, sent, wiped, and never rendered into an exception. |

`README.md` — add a streaming example, and note that `MassiveDotNet.WebSocket` is the fourth
package.

- [ ] **Step 5: Full verification**

```bash
dotnet build MassiveDotNet.slnx                                    # warning-free
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/
dotnet publish samples/MassiveDotNet.AotSmokeTest -r osx-arm64 -c Release
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "test: pin the live streaming observations and document the package

Only stocks is entitled on this key, so the tier is bounded by what can actually
be reached; the other five markets are pinned with their entitlement message and
the date, per D21, so the pin flips when the entitlement changes rather than
reading as green.

The highest-value test here asserts that an unknown topic is still silently
ignored. If Massive ever starts rejecting one properly, it fails and D33's
acknowledgement counting can be reconsidered.

Records D33 through D35 and promotes two anticipated rule 12 boundary rows to
real ones.

Claude-Session: https://claude.ai/code/session_01GQnnMUgFsvPrAq2DXpt6kB"
```

---

## Self-Review

Run against the spec after the plan was written.

**Spec coverage.** Every decision has a task: D-W1 and D-W2 → Task 8 and Task 14; D-W3 → Tasks 10
and 12; D-W4 → Task 10; D-W5 → Task 12; D-W6 → Tasks 6 and 11; D-W7 → Task 11; D-W8 → Task 1;
D-W9 → Task 6; D-W10 → Tasks 3, 7, 9, and 13; D-W11 → Task 2. The spec's Scope housekeeping is
covered by Task 1 (assembly list, count-to-set, CLAUDE.md line 244) and Task 14 (boundary rows,
layout, decisions); the two issue corrections were made before this plan was written.

**Placeholders.** None. Every code step carries the code; the one deliberate abbreviation is
`StockQuoteConverter` in Task 9 Step 6, which lists its full property-to-reader mapping in prose
rather than repeating 90 near-identical lines — an implementer has the complete field list and the
sibling file to copy from.

**Type consistency.** `MassiveTopicSubscription<T>` is the return of both subscribe methods in Task
12 and the type constructed in Task 10. `ConditionSet` is produced by `ReadConditions` (Task 9) and
asserted in Tasks 9 and 13. `StockTopic.ToCode()` is defined in Task 8 and used in Task 12.
`Allocation.Measure` comes from the linked file in Task 13. `BackoffFor` is `internal static` on the
connection, used only by Task 11's tests.

This check found two real defects, both fixed above rather than left for an implementer: Task 14's
live tests called `ConnectCryptoAsync`, which no task defines — #20 ships only the stocks facade —
and read an `ApiKey` member `LiveApiTest` does not expose, where the key actually reaches a test
through `LiveCredentials` behind a skip guard. Both are the exact failure the no-placeholder rule
names, and both would have blocked an implementer at Task 14 with nothing in the plan to resolve
them.

**Two knowingly deferred items**, both flagged rather than hidden:

1. `ConditionSet.AsSpan()` reads the inline buffer through `Unsafe.AsRef` and
   `MemoryMarshal.CreateReadOnlySpan`. If the compiler refuses indexing a `readonly` inline-array
   field directly, that helper is the fallback the code already uses; if it refuses both, drop
   `readonly` from the struct and keep the tests unchanged.
2. The `DropObserved` event on `MassiveStockStream` (Task 12) needs a throttle so a sustained
   overflow does not log per event. One second is stated; the implementer chooses the mechanism.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-09-07-websocket-transport.md`. Two execution
options:

**1. Subagent-Driven (recommended)** — a fresh subagent per task, reviewed between tasks, fast
iteration.

**2. Inline Execution** — tasks executed in this session with checkpoints for review.
