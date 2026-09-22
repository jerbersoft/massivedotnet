# Closing Handshake Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The SDK sends a close frame before tearing down a socket, so the server learns the
disconnect was intentional rather than a network failure.

**Architecture:** `IMassiveWebSocket.CloseAsync` is replaced by `CloseOutputAsync` — send-only, no
wait for the server's reply — and called from the two places a socket is torn down:
`MassiveStreamConnection.DisposeAsync` and the reconnect handover in `TryReconnectAsync`. Both calls
go through one private helper that bounds the send and swallows every failure. Nothing public
changes.

**Tech Stack:** .NET 10, C# 14, xUnit v3, NodaTime, `System.Net.WebSockets`.

**Spec:** `docs/superpowers/specs/2026-09-22-websocket-closing-handshake-design.md`

## Global Constraints

- **Rule 9 — builds are warning-free.** `TreatWarningsAsErrors` is on. A `<see cref>` to a member
  that does not exist yet is CS1574 and breaks the build.
- **Rule 10 — every public member of a shipped library carries XML documentation.** Nothing in this
  plan adds a public member; internal members still get docs where the file's neighbours have them.
- **Rule 11 — API keys are never logged, echoed in exception messages, or written to disk.** The
  close frame's `statusDescription` is `null` throughout. Never render anything into it.
- **Rule 12 — NodaTime is the only temporal vocabulary.** No BCL `TimeSpan`, `DateTime`,
  `DateTimeOffset`, `DateOnly` or `TimeOnly` may be **named** in source. `Duration.ToTimeSpan()` at a
  call site is the sanctioned crossing and names no type. Enforced by `TemporalTypeTests` and by
  `BannedSymbols.txt` (RS0030) at compile time.
- **Rule 13 — CI runs entirely offline.** Every test in this plan runs against `FakeWebSocket`. No
  key, no network.
- **Rule 14 — every public member has a `PublicAPI.Unshipped.txt` entry.** This plan adds **no**
  public members, so **no `PublicAPI` file is touched.** If you find yourself editing one, stop:
  something has gone wrong (D-W25).
- **`IMassiveWebSocket` is `internal`.** Changing it costs no `PublicAPI` entry.
- **Commits:** commit at the end of each task, as each task's final step specifies.
- Test command for the offline tier:
  `dotnet test MassiveDotNet.slnx --filter "Category!=Integration"`
- Baseline before this plan starts: **962 tests passing.**

### The one fact the whole design rests on

`ClientWebSocket.CloseAsync` sends a close frame **and waits for the server's close reply**.
Receiving that reply needs something pumping `ReceiveAsync`. On both teardown paths, nothing is —
`DisposeAsync` has already cancelled `_shutdown` and joined `ReadLoopTask`, and `TryReconnectAsync`
is called *from* the read loop. So the full handshake is unreachable here, and `CloseOutputAsync`
(send, do not wait) is the primitive used throughout.

### The trap that will silently disable this feature

The close **must not** be cancelled by `_shutdown`. In `DisposeAsync`, `_shutdown` is already
cancelled before the socket is reached, so linking the close's token to it would cancel the send
instantly and the frame would never go out — with every test that asserts "did not throw" still
passing. The helper creates a **standalone** `CancellationTokenSource`. Do not link it to
`_shutdown.Token`, and do not thread `cancellationToken` into it.

---

### Task 1: Replace `CloseAsync` with `CloseOutputAsync`, and let the fake observe it

**Files:**
- Modify: `src/MassiveDotNet.WebSocket/Internal/IMassiveWebSocket.cs:28-29`
- Modify: `src/MassiveDotNet.WebSocket/Internal/ClientWebSocketAdapter.cs:33-36`
- Modify: `tests/MassiveDotNet.WebSocket.Tests/FakeWebSocket.cs:415-419`
- Test: `tests/MassiveDotNet.WebSocket.Tests/FakeWebSocketTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `IMassiveWebSocket.CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)` returning `Task` — replaces `CloseAsync`, which no longer exists.
  - On `FakeWebSocket`: `int CloseSentCount { get; }`, `WebSocketCloseStatus? SentCloseStatus { get; }`,
    `string? SentCloseDescription { get; }`, `int CloseSentCountAtDispose { get; }`,
    `bool ThrowOnClose { get; set; }`, `void GateNextClose()`, `void ReleaseClose()` — and its
    `CloseOutputAsync` honours the adapter's own `State is Open or CloseReceived` guard.

**Naming trap — read before writing any code.** `FakeWebSocket` **already has**
`CloseStatus` and `CloseStatusDescription` (`:41` and `:44`). Those record the status of a close
frame the test *delivered inbound*, mirroring `ClientWebSocket.CloseStatus`. They are a different
thing from what this task records, which is a close the SDK *sent outbound*. Do **not** reuse or
overwrite them — tests depend on the existing meaning. The new members carry the `Sent` prefix.

- [ ] **Step 1: Write the failing test**

Append to `tests/MassiveDotNet.WebSocket.Tests/FakeWebSocketTests.cs`, inside the existing class:

```csharp
    // The fake has to be able to see an outbound close before anything can assert the SDK sends
    // one. Its existing CloseStatus/CloseStatusDescription record an INBOUND close the test
    // delivered, which is the opposite direction and must keep meaning that.
    [Fact]
    public async Task ItRecordsACloseItWasAskedToSend()
    {
        await using FakeWebSocket socket = new();
        await socket.ConnectAsync(new Uri("wss://example.test"), TestContext.Current.CancellationToken);

        await socket.CloseOutputAsync(
            WebSocketCloseStatus.NormalClosure,
            statusDescription: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, socket.CloseSentCount);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.SentCloseStatus);
        Assert.Null(socket.SentCloseDescription);
    }

    // An inbound close and an outbound close are recorded separately. Folding them into one pair of
    // properties would make every assertion in Task 2 and Task 3 pass against a socket that only
    // RECEIVED a close and never sent one.
    [Fact]
    public async Task AnInboundCloseIsNotRecordedAsOneItSent()
    {
        await using FakeWebSocket socket = new();
        await socket.ConnectAsync(new Uri("wss://example.test"), TestContext.Current.CancellationToken);
        socket.EnqueueClose(WebSocketCloseStatus.EndpointUnavailable, "going away");

        await socket.ReceiveAsync(new byte[16], TestContext.Current.CancellationToken);

        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, socket.CloseStatus);
        Assert.Equal(0, socket.CloseSentCount);
        Assert.Null(socket.SentCloseStatus);
    }

    // The fake must honour the same State guard the real adapter does. It stands in for the SOCKET,
    // which sits below ClientWebSocketAdapter, so nothing else in a connection-level test enforces
    // it -- and without it, every "sends nothing on a dead socket" assertion in ClosingHandshakeTests
    // would pass vacuously.
    [Fact]
    public async Task ItSendsNothingWhenTheSocketWasNeverOpened()
    {
        await using FakeWebSocket socket = new();

        await socket.CloseOutputAsync(
            WebSocketCloseStatus.NormalClosure,
            statusDescription: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, socket.CloseSentCount);
        Assert.Null(socket.SentCloseStatus);
    }
```

`FakeWebSocketTests.cs` already has `using System.Net.WebSockets;`, so no using needs adding.

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~FakeWebSocketTests"
```
Expected: **compile error**, `'FakeWebSocket' does not contain a definition for 'CloseOutputAsync'`
(and the same for `CloseSentCount`). That is the correct RED for this task — the member does not
exist yet. Record the exact output.

- [ ] **Step 3: Replace the interface member**

In `src/MassiveDotNet.WebSocket/Internal/IMassiveWebSocket.cs`, replace lines 28-29 entirely:

```csharp
    /// <summary>
    /// Sends a close frame, if the connection is still open, and returns without waiting for the
    /// server's reply.
    /// </summary>
    /// <remarks>
    /// Send-only on purpose (D-W22). The full handshake's reply can only be collected by something
    /// pumping <see cref="ReceiveAsync"/>, and on both teardown paths nothing is: disposal has
    /// already joined the read loop, and the reconnect handover runs ON that loop. Waiting there
    /// would park every teardown until its timeout for a reply that cannot arrive.
    /// </remarks>
    Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken);
```

`CloseAsync` is **removed**, not kept beside it. Leaving it would preserve the exact defect issue
#66 was filed over — a declared, implemented, uncalled member — merely moved onto another method.

- [ ] **Step 4: Update the real adapter**

In `src/MassiveDotNet.WebSocket/Internal/ClientWebSocketAdapter.cs`, replace lines 33-36:

```csharp
    public Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
        _socket.State is WebSocketState.Open or WebSocketState.CloseReceived
            ? _socket.CloseOutputAsync(closeStatus, statusDescription, cancellationToken)
            : Task.CompletedTask;
```

The guard is unchanged and both arms are live (D-W23): `Open` is the ordinary case, and
`CloseReceived` is the server having sent a close first, which the SDK now answers.

- [ ] **Step 5: Update the fake**

In `tests/MassiveDotNet.WebSocket.Tests/FakeWebSocket.cs`, add these members beside the existing
`CloseStatus` / `CloseStatusDescription` declarations (around `:44`):

```csharp
    /// <summary>How many close frames this socket was asked to SEND.</summary>
    /// <remarks>
    /// Distinct from <see cref="CloseStatus"/>, which records a close frame the test DELIVERED
    /// inbound. Collapsing the two would let an assertion about the SDK closing politely pass
    /// against a socket that only received a close.
    /// </remarks>
    public int CloseSentCount { get; private set; }

    /// <summary>The status of the most recent close frame this socket was asked to send.</summary>
    public WebSocketCloseStatus? SentCloseStatus { get; private set; }

    /// <summary>The description of the most recent close frame this socket was asked to send.</summary>
    public string? SentCloseDescription { get; private set; }

    /// <summary>
    /// The value <see cref="CloseSentCount"/> held at the moment <see cref="DisposeAsync"/> ran, so
    /// a test can pin that the close was sent BEFORE the socket was torn down rather than merely
    /// that both happened.
    /// </summary>
    public int CloseSentCountAtDispose { get; private set; }

    /// <summary>
    /// When set, <see cref="CloseOutputAsync"/> records the attempt and then throws, standing in for
    /// a socket that will not accept a close frame.
    /// </summary>
    public bool ThrowOnClose { get; set; }

    private TaskCompletionSource? _closeGate;

    /// <summary>
    /// Makes the next <see cref="CloseOutputAsync"/> wait until <see cref="ReleaseClose"/> is
    /// called, so a test can pin that a close which never completes does not hang teardown. Same
    /// shape, and same reason, as <see cref="GateNextConnect"/>.
    /// </summary>
    public void GateNextClose() => _closeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Releases a close parked by <see cref="GateNextClose"/>.</summary>
    public void ReleaseClose() => _closeGate?.TrySetResult();
```

Then replace `CloseAsync` at `:415-419` with:

```csharp
    public async Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        // The same guard ClientWebSocketAdapter applies. This fake replaces the SOCKET, which sits
        // below the adapter, so without this a connection-level test asserting "nothing was sent on
        // a dead socket" would pass no matter what the production code did.
        if (State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        if (_closeGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        CloseSentCount++;
        SentCloseStatus = closeStatus;
        SentCloseDescription = statusDescription;

        if (ThrowOnClose)
        {
            throw new WebSocketException(WebSocketError.InvalidState, "the socket is not connected.");
        }

        State = WebSocketState.Closed;
    }
```

Finally, add one line to the fake's existing `DisposeAsync` (`:421`), as its first statement, so
ordering can be asserted:

```csharp
        CloseSentCountAtDispose = CloseSentCount;
```

- [ ] **Step 6: Run the focused test to verify it passes**

Run:
```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~FakeWebSocketTests"
```
Expected: PASS, including the three new tests.

- [ ] **Step 7: Run the whole suite**

Run:
```bash
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
```
Expected: **965 passing, zero warnings.** Nothing called `CloseAsync`, so removing it breaks no
caller — if anything fails to compile, a call site exists that the issue's analysis missed. Stop and
report that rather than working around it.

- [ ] **Step 8: Commit**

```bash
git add src/MassiveDotNet.WebSocket/Internal/IMassiveWebSocket.cs \
        src/MassiveDotNet.WebSocket/Internal/ClientWebSocketAdapter.cs \
        tests/MassiveDotNet.WebSocket.Tests/FakeWebSocket.cs \
        tests/MassiveDotNet.WebSocket.Tests/FakeWebSocketTests.cs
git commit -m "refactor: make the socket seam's close send-only"
```

---

### Task 2: Send a close when the connection is disposed

**Files:**
- Modify: `src/MassiveDotNet.WebSocket/Internal/MassiveStreamConnection.cs` — a new field near
  `:164`, a new private helper, and the disposal block at `:1210-1213`
- Create: `tests/MassiveDotNet.WebSocket.Tests/ClosingHandshakeTests.cs`

**Interfaces:**
- Consumes: `IMassiveWebSocket.CloseOutputAsync(...)`, and `FakeWebSocket`'s `CloseSentCount`,
  `SentCloseStatus`, `SentCloseDescription`, `GateNextClose()`, `ReleaseClose()` — all from Task 1.
- Produces: `private static async Task CloseQuietlyAsync(IMassiveWebSocket socket)` and
  `private static readonly Duration CloseTimeout` on `MassiveStreamConnection`, both used again by
  Task 3.

- [ ] **Step 1: Write the failing tests**

Create `tests/MassiveDotNet.WebSocket.Tests/ClosingHandshakeTests.cs`:

```csharp
using System.Net.WebSockets;
using MassiveDotNet.WebSocket.Internal;
using NodaTime;
using NodaTime.Testing;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// Issue #66 / D-W22-24. The SDK used to abort every connection: ClientWebSocket.Dispose() does not
/// perform the closing handshake, so a server could not tell a deliberate disconnect from a network
/// failure -- which matters on a plan that allows one connection per cluster, where how fast the
/// server reclaims a departed connection decides whether our own reconnect collides with us.
/// </summary>
public class ClosingHandshakeTests
{
    private const string Connected = """[{"ev":"status","status":"connected","message":"Connected Successfully"}]""";
    private const string AuthSuccess = """[{"ev":"status","status":"auth_success","message":"authenticated"}]""";

    private static MassiveStreamConnection CreateConnection(FakeWebSocket socket)
    {
        MassiveStreamOptions options = new() { ApiKey = "test-key" };

        return new MassiveStreamConnection(options, MassiveMarket.Stocks, () => socket, new FakeClock(Instant.FromUnixTimeSeconds(0)));
    }

    [Fact]
    public async Task DisposingAConnectedStreamSendsANormalClosure()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamConnection connection = CreateConnection(socket);
        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        await connection.DisposeAsync();

        Assert.Equal(1, socket.CloseSentCount);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.SentCloseStatus);

        // Ordering is the requirement, not merely that both happened: a close sent AFTER the socket
        // was torn down reaches a dead socket and tells the server nothing.
        Assert.Equal(1, socket.CloseSentCountAtDispose);
    }

    // Rule 11 by construction rather than by care taken at the site: nothing is rendered into the
    // description, so there is no path by which a key could reach one.
    [Fact]
    public async Task TheCloseCarriesNoDescription()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamConnection connection = CreateConnection(socket);
        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        await connection.DisposeAsync();

        Assert.Null(socket.SentCloseDescription);
    }

    // A stream that never opened has no socket to close. This is the guard on `_socket is not null`,
    // and it must not become a NullReferenceException on the disposal path.
    [Fact]
    public async Task DisposingANeverOpenedStreamSendsNothingAndCompletes()
    {
        FakeWebSocket socket = new();

        MassiveStreamConnection connection = CreateConnection(socket);

        await connection.DisposeAsync();

        Assert.Equal(0, socket.CloseSentCount);
    }

    // The case the production logs actually show: the remote aborted without a close frame, leaving
    // the socket Aborted. Nothing may be pushed onto it, and disposal must still complete.
    [Fact]
    public async Task DisposingAnAbortedStreamSendsNothingAndCompletes()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);
        socket.AbortNext();

        MassiveStreamOptions options = new() { ApiKey = "test-key", Reconnect = null };
        MassiveStreamConnection connection = new(
            options,
            MassiveMarket.Stocks,
            () => socket,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await connection.ReadLoopTask.WaitAsync(TestContext.Current.CancellationToken));

        await connection.DisposeAsync();

        Assert.Equal(0, socket.CloseSentCount);
    }

    // The failure this guards: a close that never completes must not hold disposal open. Without
    // CloseTimeout the await below never returns and the test hangs until the runner's own much
    // longer bound.
    [Fact]
    public async Task ACloseThatNeverCompletesDoesNotHangDisposal()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamConnection connection = CreateConnection(socket);
        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        socket.GateNextClose();

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(Duration.FromSeconds(10).ToTimeSpan());

        await connection.DisposeAsync().AsTask().WaitAsync(cts.Token);

        socket.ReleaseClose();
    }

    // Disposal is a caller saying "I am leaving". A courtesy frame that will not send is not their
    // problem and must never be how their teardown throws.
    [Fact]
    public async Task ACloseThatThrowsDoesNotEscapeDisposal()
    {
        FakeWebSocket socket = new();
        socket.EnqueueText(Connected);
        socket.EnqueueText(AuthSuccess);

        MassiveStreamConnection connection = CreateConnection(socket);
        await connection.ConnectAsync(TestContext.Current.CancellationToken);

        socket.ThrowOnClose = true;

        await connection.DisposeAsync();

        // The attempt is recorded before the throw, so this distinguishes "swallowed the failure"
        // from "never tried".
        Assert.Equal(1, socket.CloseSentCount);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:
```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~ClosingHandshakeTests"
```
Expected: `DisposingAConnectedStreamSendsANormalClosure`, `TheCloseCarriesNoDescription` and
`ACloseThatThrowsDoesNotEscapeDisposal` FAIL with
`Assert.Equal() Failure: Expected: 1, Actual: 0` — disposal sends nothing today. Record the output.

- [ ] **Step 3: Add the timeout and the helper**

In `src/MassiveDotNet.WebSocket/Internal/MassiveStreamConnection.cs`, add beside the existing
`NoReconnectYet` constant (around `:164`):

```csharp
    // A close frame is one small write on an already-established connection: if it has not landed
    // in two seconds the connection is gone and waiting longer buys nothing. Deliberately not a
    // MassiveStreamOptions property (D-W24) -- that would be public surface, under rules 10 and 14,
    // for a number no consumer can meaningfully tune. HandshakeTimeout is not reused because in
    // this codebase "handshake" means the OPENING one, and its 10s is sized for a round trip plus
    // the server's own auth work.
    private static readonly Duration CloseTimeout = Duration.FromSeconds(2);
```

Then add this private helper immediately above `DisposeAsync`:

```csharp
    // Send-only, and never awaited for a reply (D-W22): CloseOutputAsync returns once the frame is
    // written. The full handshake's reply could only be collected by something pumping
    // ReceiveAsync, and neither caller has that -- DisposeAsync has already joined ReadLoopTask,
    // and TryReconnectAsync runs ON the read loop. Waiting would park every teardown until this
    // timeout for a reply that cannot arrive.
    //
    // The token source is standalone ON PURPOSE. Linking it to _shutdown would cancel the send
    // instantly on the disposal path, because DisposeAsync cancels _shutdown before it ever reaches
    // the socket -- the frame would silently never go out while every "it did not throw" assertion
    // still passed.
    //
    // Every failure is swallowed, matching the two catch blocks inside DisposeAsync: a courtesy
    // frame that will not send is not the caller's problem and must never be how their teardown
    // throws.
    private static async Task CloseQuietlyAsync(IMassiveWebSocket socket)
    {
        using CancellationTokenSource timeout = new();
        // Boundary crossing (produce): the domain Duration converts here and nowhere above.
        timeout.CancelAfter(CloseTimeout.ToTimeSpan());

        try
        {
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, statusDescription: null, timeout.Token);
        }
        catch (Exception)
        {
            // A socket that will not take a close frame is a socket already gone. Nothing here is
            // actionable, and the abort that follows in the caller reclaims it either way.
        }
    }
```

- [ ] **Step 4: Call it from disposal**

In `DisposeAsync`, replace the block at `:1210-1213`:

```csharp
        if (_socket is not null)
        {
            await CloseQuietlyAsync(_socket);
            await _socket.DisposeAsync();
        }
```

Placed here and not earlier deliberately: `_shutdown` is cancelled and `ReadLoopTask` joined by this
point, so no pump is alive to receive our own close frame and report it through `FrameReader` as the
drop G3 would reconnect over.

- [ ] **Step 5: Run the focused tests to verify they pass**

Run:
```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~ClosingHandshakeTests"
```
Expected: PASS, all six.

- [ ] **Step 6: Run the whole suite**

Run:
```bash
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
```
Expected: **971 passing** (962 + 3 from Task 1 + 6 here), zero warnings. Watch
particularly for `ReadLoopTests` and `ReconnectTests`: they dispose connections constantly, and a
close on that path is new behaviour their fakes now see.

- [ ] **Step 7: Commit**

```bash
git add src/MassiveDotNet.WebSocket/Internal/MassiveStreamConnection.cs \
        tests/MassiveDotNet.WebSocket.Tests/ClosingHandshakeTests.cs \
        tests/MassiveDotNet.WebSocket.Tests/FakeWebSocket.cs
git commit -m "fix: tell the server we are leaving before disposing the socket"
```

---

### Task 3: Send a close on the reconnect handover

**Files:**
- Modify: `src/MassiveDotNet.WebSocket/Internal/MassiveStreamConnection.cs:349-352` (the
  `if (_socket is { } previous)` block inside `TryReconnectAsync`)
- Test: `tests/MassiveDotNet.WebSocket.Tests/ClosingHandshakeTests.cs`

**Interfaces:**
- Consumes: `CloseQuietlyAsync(IMassiveWebSocket)` and `CloseTimeout` from Task 2; `CloseSentCount`
  from Task 1.
- Produces: nothing later tasks rely on.

**Why this is not dead code.** `TryReconnectAsync` is reached only after the read loop caught a
failure, so the instinct is that the previous socket is always already broken and the adapter's
`State is Open or CloseReceived` guard will skip. Two real cases say otherwise:

1. A message beyond `MaxMessageBytes` throws `MassiveStreamException` from `FrameReader` while the
   socket is **still `Open`**. The close fires and does real work.
2. The server sent a close frame, which `FrameReader.cs:50-53` reports as `MassiveStreamException`
   while leaving the socket `CloseReceived`. The SDK now **answers** that close, which it never has.

Where the socket genuinely aborted — the case production logs actually show — state is `Aborted` and
the guard skips.

- [ ] **Step 1: Write the failing tests**

Append to `ClosingHandshakeTests.cs`, inside the class:

```csharp
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

    // Case 1 from D-W23: a message past MaxMessageBytes faults the read loop while the socket is
    // still OPEN, so the handover has a live connection to close politely. This is the case that
    // makes the reconnect call site load-bearing rather than decorative.
    [Fact]
    public async Task TheReconnectHandoverClosesAPreviousSocketThatIsStillOpen()
    {
        FakeWebSocket first = new();
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);
        first.EnqueueFragmented(new string('x', 4096), chunkSize: 64);

        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        MassiveStreamOptions options = FastReconnect();
        options.MaxMessageBytes = 256;

        await using MassiveStreamConnection connection = new(
            options,
            MassiveMarket.Stocks,
            () => created++ == 0 ? first : second,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        await WaitUntilAsync(() => second.ConnectCount > 0);

        Assert.Equal(1, first.CloseSentCount);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, first.SentCloseStatus);
    }

    // The case production actually shows: the remote aborted with no close frame, leaving the
    // socket Aborted. The adapter's guard must skip, and nothing may be sent onto a dead socket.
    [Fact]
    public async Task TheReconnectHandoverSendsNothingOnAnAbortedSocket()
    {
        FakeWebSocket first = new();
        FakeWebSocket second = new();
        int created = 0;

        first.EnqueueText(Connected);
        first.EnqueueText(AuthSuccess);
        first.AbortNext();

        second.EnqueueText(Connected);
        second.EnqueueText(AuthSuccess);

        await using MassiveStreamConnection connection = new(
            FastReconnect(),
            MassiveMarket.Stocks,
            () => created++ == 0 ? first : second,
            new FakeClock(Instant.FromUnixTimeSeconds(0)));

        await connection.ConnectAsync(TestContext.Current.CancellationToken);
        connection.StartReading();

        await WaitUntilAsync(() => second.ConnectCount > 0);

        Assert.Equal(0, first.CloseSentCount);
    }

    // Polling rather than a signal: the handover happens inside the read loop, which exposes no
    // hook a test can await. Bounded so a regression fails readably instead of hanging.
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource cts = new();
        cts.CancelAfter(Duration.FromSeconds(5).ToTimeSpan());

        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(Duration.FromMilliseconds(20).ToTimeSpan(), cts.Token);
        }
    }
```

**Why the second test can assert an absence honestly:** `FakeWebSocket.CloseOutputAsync` already
carries the adapter's own `State is Open or CloseReceived` guard, added in Task 1. Without it that
assertion would pass no matter what the production code did.

- [ ] **Step 2: Run the tests to verify they fail**

Run:
```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~ClosingHandshakeTests"
```
Expected: `TheReconnectHandoverClosesAPreviousSocketThatIsStillOpen` FAILS with
`Assert.Equal() Failure: Expected: 1, Actual: 0`. `TheReconnectHandoverSendsNothingOnAnAbortedSocket`
may pass already — it asserts an absence. That is fine and expected; it is the regression guard for
the change you are about to make, not its RED.

- [ ] **Step 3: Call the helper from the handover**

In `TryReconnectAsync`, replace the block at `:349-352`:

```csharp
                if (_socket is { } previous)
                {
                    await CloseQuietlyAsync(previous);
                    await previous.DisposeAsync();
                }
```

- [ ] **Step 4: Run the focused tests to verify they pass**

Run:
```bash
dotnet test tests/MassiveDotNet.WebSocket.Tests --filter "FullyQualifiedName~ClosingHandshakeTests"
```
Expected: PASS, all eight.

- [ ] **Step 5: Run the whole suite**

Run:
```bash
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
```
Expected: **973 passing**, zero warnings. `ReconnectTests` is the file most likely to notice this
change; if one of its tests goes red, read it before adjusting it — a genuine behaviour change there
is a finding, not a test to update.

- [ ] **Step 6: Commit**

```bash
git add src/MassiveDotNet.WebSocket/Internal/MassiveStreamConnection.cs \
        tests/MassiveDotNet.WebSocket.Tests/ClosingHandshakeTests.cs
git commit -m "fix: close the old socket politely on the reconnect handover"
```

---

### Task 4: Record the decision

**Files:**
- Modify: `CLAUDE.md` — one decision row after D39, and one row in the Known boundary points table
  after `:283`
- Modify: `CHANGELOG.md` — one bullet in the existing `### Fixed` block under `## [Unreleased]`

**Interfaces:**
- Consumes: the implemented behaviour from Tasks 1-3.
- Produces: nothing.

- [ ] **Step 1: Add the D40 row to `CLAUDE.md`**

Append immediately after the D39 row in the Architecture decisions table. One line, pipe-delimited,
matching its neighbours:

```
| D40 | Teardown sends a close frame and does not wait for the reply: `IMassiveWebSocket.CloseOutputAsync` replaces `CloseAsync`, called from `DisposeAsync` and from the reconnect handover through one swallow-everything helper bounded by a private 2-second `Duration`. No public member is added. | `ClientWebSocket.Dispose()` **aborts** -- it does not perform the closing handshake -- so the SDK never told the server a disconnect was deliberate. That matters more than hygiene on a plan allowing one connection per cluster, where a client that aborts and immediately reconnects may collide with a connection the server has not yet reaped. The handshake is not merely skipped but structurally **unreachable** on both teardown paths: `CloseAsync` waits for the server's close reply, and collecting it needs something pumping `ReceiveAsync` -- `DisposeAsync` has already cancelled `_shutdown` and joined `ReadLoopTask`, and the reconnect handover runs ON the read loop. So the literal fix issue #66 proposed would park every teardown until a timeout for a reply that cannot arrive. Closing earlier instead is worse: the loop would receive our own close frame, which `FrameReader` reports as `MassiveStreamException` and G3 treats as reconnectable, so a polite close would trigger a reconnect on the way down. `CloseOutputAsync` **replaces** `CloseAsync` rather than joining it, because an added member would leave the declared-implemented-uncalled shape #66 exists to remove, merely relocated; keeping the old name over send-only semantics was rejected as a name that misleads. The reconnect call site is not decorative: the adapter's `State is Open or CloseReceived` guard has two live arms -- a message past `MaxMessageBytes` faults the loop with the socket still `Open`, and a server-sent close leaves it `CloseReceived`, which the SDK now answers for the first time. A genuine abort leaves it `Aborted` and is skipped, which is the case the production logs actually show, so this makes no claim about those. The timeout is a private constant, not a `MassiveStreamOptions` property, because it is public surface under rules 10 and 14 for a number no consumer can tune; `HandshakeTimeout` is not reused because "handshake" here means the opening one and its 10s is sized for a different quantity. The token source is standalone rather than linked to `_shutdown`, which is load-bearing: `DisposeAsync` cancels `_shutdown` before reaching the socket, so linking would silently stop the frame ever being sent while every "did not throw" test still passed. Every failure is swallowed, matching `DisposeAsync`'s two existing catch blocks, and the description is always `null`, which puts rule 11 out of reach by construction. `MassiveStockStream` gains no public shutdown member: `await using` already gets the close, a second teardown member would be more surface for the same thing, and an additive member can be added later without a break. The full RFC 6455 handshake is deferred, not refused -- it needs a pump alive during teardown, which means reordering disposal and teaching the read loop to recognise its own deliberate close so G3 does not reconnect through it. |
```

- [ ] **Step 2: Add the rule 12 boundary row**

In the Known boundary points table, immediately after the `VerifyReplayAsync` row at `:283`:

```
| `CancellationTokenSource.CancelAfter` | produce | `timeout.CancelAfter(CloseTimeout.ToTimeSpan())` | `MassiveStreamConnection.CloseQuietlyAsync` |
```

- [ ] **Step 3: Add the CHANGELOG bullet**

In `CHANGELOG.md`, append to the existing `### Fixed` list under `## [Unreleased]`:

```markdown
- **`MassiveDotNet.WebSocket`** — the SDK now tells the server it is leaving. Every teardown used to
  abort the socket, because `ClientWebSocket.Dispose()` does not perform the closing handshake, so a
  server could not tell a deliberate disconnect from a network failure — which matters on a plan
  allowing one connection per cluster. The close is send-only and bounded: the server's reply could
  only be collected by a read loop that has already stopped by then. No public API change.
```

- [ ] **Step 4: Verify the build and the docs gates**

Run:
```bash
dotnet build MassiveDotNet.slnx
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"
```
Expected: 0 warnings, **973 passing**. `TemporalTypeTests` reads the boundary table's surrounding
rules, and `BuildGateTests` reads `CLAUDE.md` — a malformed table row can fail them, so this is a
real gate and not a formality.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md CHANGELOG.md
git commit -m "docs: record D40 -- teardown closes politely without waiting"
```

---

## Verification

After all four tasks:

```bash
dotnet build MassiveDotNet.slnx                                                  # 0 warnings
dotnet test MassiveDotNet.slnx --filter "Category!=Integration"                  # 973 passing
dotnet run --project tools/MassiveDotNet.CodeGen && git diff --exit-code src/    # clean
git diff --stat master -- '*PublicAPI*'                                          # MUST be empty
```

The last one is the check that this stayed an internal fix (D-W25). Any `PublicAPI` diff means a
public member was added and the design was not followed.
