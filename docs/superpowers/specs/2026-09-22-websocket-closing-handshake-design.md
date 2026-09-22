# The SDK closes politely, without waiting for a reply it cannot receive

Issue #66. Touches `MassiveDotNet.WebSocket` only. No public API change.

## Why

`IMassiveWebSocket.CloseAsync` is declared (`Internal/IMassiveWebSocket.cs:29`), implemented with a
correct state guard (`Internal/ClientWebSocketAdapter.cs:33-36`), and **has no call site**. Every
teardown aborts instead:

- Disposal — `Internal/MassiveStreamConnection.cs:1212` → `await _socket.DisposeAsync()`
- The reconnect handover — `Internal/MassiveStreamConnection.cs:351` → `await previous.DisposeAsync()`

and `ClientWebSocketAdapter.DisposeAsync` (`:38-42`) is `_socket.Dispose()`, which **aborts**. It does
not perform the closing handshake. So the SDK never tells the server it is leaving.

Closing politely is correct on its own terms — the handshake exists so a peer can tell an intentional
disconnect from a network failure — and it matters more than usual here: paid individual Massive plans
allow one simultaneous connection per cluster, and on Stocks a second connection authenticating closes
the older one. On an account with a budget of one, how quickly the server reclaims a departed
connection is not academic.

## What is established, and what is not

**Established:** the method exists, is guarded, and is never called. Read from the tree.

**Not established, and deliberately not claimed:** that this has ever caused anything. A month of
production logs shows the aborts going the *other* way — `"The remote party closed the WebSocket
connection without completing the close handshake"`, 17 times in one session, the **server** dropping
us without a frame. Whether a server holds an aborted connection long enough to collide with our own
reconnect is unmeasured and undocumented by the vendor.

This is filed and fixed as a correctness defect that happens to matter on a connection-limited
account, not as a diagnosed cause of any disconnect. Stated plainly because #64 was filed on an
inference that did not survive checking.

## The finding that reshapes the fix

`ClientWebSocket.CloseAsync` sends a close frame **and waits for the server's close reply**. Receiving
that reply requires something pumping `ReceiveAsync`. On both teardown paths, nothing is:

- **Disposal** (`:1161`) cancels `_shutdown` first, then `await ReadLoopTask`. The read loop is
  **already joined** before control reaches the socket at `:1212`.
- **The reconnect handover** (`:351`) is reached *from* `ReadLoopAsync` — the loop is inside this call,
  not pumping.

So "call `CloseAsync` before `DisposeAsync`", taken literally, converts every teardown into a
guaranteed wait for a reply nobody will ever receive, bounded only by whatever timeout it is given.

Closing *earlier*, while the loop still pumps, trades that for a worse failure: the loop receives the
close frame, and `Internal/FrameReader.cs:50-53` turns a `WebSocketMessageType.Close` into
`MassiveStreamException("The stream closed while a message was being read.")` — which G3 treats as
**reconnectable**. A polite close would trigger a reconnect attempt on the way down.

## Decisions

### D-W22 · The close is send-only, and `CloseOutputAsync` replaces `CloseAsync` on the interface

`IMassiveWebSocket.CloseAsync` is **removed** and `CloseOutputAsync` takes its place, keeping the same
`State is Open or CloseReceived` guard:

```csharp
/// <summary>Sends a close frame, if the connection is still open, without waiting for a reply.</summary>
Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken);
```

Replaced rather than added beside. Adding `CloseOutputAsync` and leaving `CloseAsync` in place would
leave a declared, implemented, uncalled member behind — the exact defect #66 exists to remove, merely
relocated onto a different method. Keeping the name `CloseAsync` while forwarding to the socket's
`CloseOutputAsync` was also rejected: the name would claim a handshake the method deliberately does
not perform, and a name that misleads is worse than a rename. `IMassiveWebSocket` is internal, so this
costs no `PublicAPI` entry; exactly two types implement it, `ClientWebSocketAdapter` and the tests'
`FakeWebSocket`.

Send-only gets the whole benefit the issue argues for. The server learns the disconnect was
intentional the moment the frame lands, which is what a reclaim would key on; the reply would only
confirm it heard us. The accepted cost is that we never obtain that confirmation, and the full
RFC 6455 handshake stays unimplemented — deferred rather than refused, and revisitable if evidence
ever shows the confirmation is worth restructuring disposal for.

### D-W23 · The close fires on both teardown paths, and the reconnect one is not a no-op

**Disposal:** inside the existing `if (_socket is not null)` block, immediately before
`await _socket.DisposeAsync()`. Deliberately not earlier. `_shutdown` is already cancelled and the read
loop already joined at that point, and that is precisely what makes it safe — with no pump alive,
nothing can receive our own close frame and mistake it for the drop `FrameReader` would report.

**The reconnect handover:** before `await previous.DisposeAsync()`. This looks like dead code, since
`TryReconnectAsync` is reached only after the read loop caught a failure — but the guard makes it
load-bearing in two real cases:

- A message over `MaxMessageBytes` throws `MassiveStreamException` from `FrameReader` while the socket
  is **still `Open`**. The close fires and does real work.
- The server sent a close frame, which `FrameReader.cs:50-53` reports as `MassiveStreamException` while
  leaving the socket `CloseReceived`. The guard's second arm means the SDK now **answers** that close,
  which it has never done.

Where the socket genuinely aborted — the case the production logs actually show — the state is
`Aborted`, the guard skips, and nothing is sent. The fix makes no claim about that path; a server that
vanishes without a frame is the server's behaviour, not ours.

### D-W24 · The timeout is an internal constant, and every failure is swallowed

```csharp
private static readonly Duration CloseTimeout = Duration.FromSeconds(2);
```

Even send-only, `CloseOutputAsync` awaits a network write, and on a wedged connection that blocks until
the OS gives up. `DisposeAsync()` takes no `CancellationToken`, so the close is bounded by its own
`CancellationTokenSource` with `CancelAfter(CloseTimeout.ToTimeSpan())` — a rule 12 boundary crossing,
which gains a row in `CLAUDE.md`'s Known boundary points table.

Two seconds because a close frame is one small write on an already-established connection: if it has
not landed by then the connection is gone and waiting longer buys nothing. `HandshakeTimeout`'s 10s is
sized for a round trip plus the server's own auth work, which is a different quantity.

Not a `MassiveStreamOptions` property. That would be a public API addition — an entry under rule 14,
docs under rule 10, validation beside the existing positive-`Duration` checks, and permanent surface —
for a number no consumer can meaningfully tune, since a teardown either lands promptly or the
connection is already gone. Reusing `HandshakeTimeout` was rejected separately: in this codebase
"handshake" means the **opening** one, read by `ConnectAsync`, `SubscribeAsync` and
`VerifyReplayAsync`, and overloading it onto teardown makes one knob mean two things.

Every exception from the close is swallowed, matching the two `catch (Exception)` blocks already in
`DisposeAsync`: a courtesy frame failing to send must never be how a caller's teardown throws. The
status is `WebSocketCloseStatus.NormalClosure` with a **`null`** description — nothing is rendered into
it, which puts rule 11 out of reach by construction rather than by care taken at the site.

### D-W25 · `MassiveStockStream` gains no public shutdown member

`DisposeAsync` (`MassiveStockStream.cs:462`) stays its only teardown member. A consumer writing
`await using` gets the polite close for free, and `DisposeAsync` is already .NET's idiomatic way to say
"I am leaving".

A second public teardown member would invite "what is the difference?" when the honest answer is
"almost nothing" — and since the close is send-only, awaiting it would confirm nothing the dispose path
does not already do. D33's "a second signal is strictly less surface for the same information" is **not**
being invoked here, and does not apply: there it argued for adding a signal rather than overloading an
existing one's meaning, where the alternative on offer here is adding nothing at all. Additive
members can be added later without a break, so deferring costs nothing structurally, and this keeps
#66 a pure internal correctness fix.

### D40 · The row for `CLAUDE.md`

The four decisions above collapse to one row at the next free id, **D40**: send-only and why the
handshake is structurally unreachable on both teardown paths, the guard's two live arms, the internal
constant, and why no public member.

## Scope

**`MassiveDotNet.WebSocket`**

- `Internal/IMassiveWebSocket.cs` — `CloseAsync` removed, `CloseOutputAsync` declared.
- `Internal/ClientWebSocketAdapter.cs` — forwards to `_socket.CloseOutputAsync` under the same guard.
- `Internal/MassiveStreamConnection.cs` — `CloseTimeout`; the close in `DisposeAsync` before
  `_socket.DisposeAsync()`; the close in `TryReconnectAsync` before `previous.DisposeAsync()`.

**Tests**

- `FakeWebSocket` — records that a close was sent and its status, replacing a `CloseAsync` that
  recorded nothing (`:415-419`).

**Docs**

- `CLAUDE.md` — the D40 row, and one Known boundary points row for
  `CancellationTokenSource.CancelAfter` in `MassiveStreamConnection.DisposeAsync`.
- `CHANGELOG.md` — a `### Fixed` bullet under `## [Unreleased]`.

## Testing

Offline tier only; `FakeWebSocket` drives all of it, so no key is involved (rule 13).

**The property being protected:** disposing a connected stream sends a close frame before the socket is
disposed. That test does not exist today and cannot, because the fake records nothing — extending it is
part of the work, not a detail of it.

- Disposing a connected stream sends `NormalClosure`, and sends it **before** the dispose.
- A socket already `Aborted` is sent nothing, and disposal still completes.
- A never-opened stream disposes cleanly and sends nothing.
- A close that never completes does not hang disposal past `CloseTimeout`.
- The reconnect handover closes when the previous socket is still `Open`, and skips when it is not.
- A close that throws does not propagate out of `DisposeAsync`.

Watched failing first per D31: a test asserting only "disposal completed" passes against the defect.

**Allocation.** Unchanged ceilings are the assertion; a close on teardown is not on any measured path.

## Non-goals

- **The full RFC 6455 handshake.** It needs a pump alive during teardown, which means reordering
  disposal and teaching the read loop to recognise its own deliberate close so G3 does not reconnect
  through it. Deferred under D-W22, not refused.
- **Any public API addition** — no `MassiveStockStream` member (D-W25), no `MassiveStreamOptions`
  property (D-W24).
- **The server aborting on us.** The 17 logged aborts are the server closing without a frame. Nothing
  here changes what we do about that; #64 covered the adjacent question of reporting the cause.
- **Auditing other `IAsyncDisposable` paths for the same omission.** `MassiveStreamConnection` owns the
  only socket.
