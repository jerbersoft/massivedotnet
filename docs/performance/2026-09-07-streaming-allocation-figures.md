# Streaming allocation figures

Measured 2026-09-08 for Task 13 of the WebSocket transport plan, which turns issue #20's
acceptance criterion — "no unbounded buffering" — into something that can fail a build. These are
the figures behind `MassiveDotNet.WebSocket.Tests.AllocationTests` and decision D-W10's claim that
a parsed trade costs nothing on the heap beyond its trade id.

| | |
|---|---|
| Host | Apple M4 Max, 16 physical cores, macOS Sequoia 15.7.4 |
| Runtime | .NET 10.0.2 (10.0.225.61305), Arm64 RyuJIT AdvSIMD, Concurrent Workstation GC |
| SDK | 10.0.102 |
| Tool | `AllocationTests`, `GC.GetAllocatedBytesForCurrentThread` (three) / `GC.GetTotalMemory` (one), Debug configuration, `TieredCompilation=false` |

Reproduce with `dotnet test tests/MassiveDotNet.WebSocket.Tests --filter FullyQualifiedName~AllocationTests`.
There is no `MassiveDotNet.Benchmarks` class for the streaming path yet — unlike the REST figures in
`2026-09-04-allocation-figures.md`, everything below comes from the gate itself, not a second,
fully-warmed instrument. That is a narrower guarantee than the REST suite has: nothing here checks
whether a PGO-enabled path would report a different number, the way #47 discovered the REST gate
and the benchmark disagreeing was itself the bug. If a streaming benchmark class is added later,
this file is the place to reconcile the two.

---

## Why this task's deliverable is the regression, not the number

D31: "a guard nobody has seen fail is indistinguishable from a clean tree." Every ceiling below was
set by deliberately breaking the code path it guards, confirming the assertion went red for the
right reason, and only then restoring the code and writing the ceiling down. The table in the next
section names the regression that proved each one; all four were applied directly to `src/` on this
branch, watched fail, and reverted — `git diff --stat -- src/` is empty on the commit this file
ships with.

## The gate's own figures

| Assertion | Path | Measured | Ceiling | Regression that proved it |
|---|---|---:|---:|---|
| `ParsingATradeAllocatesNothingBeyondItsTradeId` | Parse one trade, ticker pooled, conditions inline | 32 B | 40 B | `tickers.Intern(ref reader)` → `reader.GetString()`: 64 B |
| `TheTickerCostsNothingAfterTheFirstEvent` | 1,000 repeat interns of a pooled ticker | 0 B | exactly 0 | alternate lookup → `_pool.TryGetValue(new string(ticker), …)`: 32,000 B |
| `ConditionsWithinTheInlineCapacityAllocateNothing` | Read a 3-code condition array (inline capacity is 8) | 0 B | exactly 0 | `ConditionSetSerialization.Read` forced to always spill: 320 B |
| `RetainsNoMemoryProportionalToTheEventsReceived` | `TopicSink<StockTrade>` retention, capacity-8 channel, 200 vs. 20,000 events written | ~92,000-111,000 B | 512 KB | `Channel.CreateBounded` → `Channel.CreateUnbounded<T>()`: 7,007,240 B |

The first three are `GC.GetAllocatedBytesForCurrentThread` deltas, byte-exact on every run — the
same instrument `MassiveDotNet.Rest.Tests.Allocation` uses, linked rather than copied (see the
task's csproj change). The trade-parse and repeat-intern ceilings carry the headroom D31 asks for;
the two exactly-zero claims are asserted with strict equality, because headroom there would defeat
the point. `ParsingATradeAllocatesNothingBeyondItsTradeId`'s 40 B ceiling is 25%, not 20%, over its
32 B measured figure: 20% of 32 B (6.4 B) is under one allocator size-class step, so a flat
percentage would have rounded down to nothing.

The fourth is `GC.GetTotalMemory`, which is process-wide rather than per-thread, and its ceiling
follows a different convention — see the next section.

## The retention test's ceiling, and a flakiness finding fixed rather than tuned around

`GC.GetTotalMemory` is what `CursorTraversalTests.RetainsNoMemoryProportionalToThePagesTraversed`
in `MassiveDotNet.Rest.Tests` already uses for the same class of claim, and that test's own comment
explains why its bound (8 MB) is far looser than a 20%-headroom figure would be: the measurement is
process-wide, so it carries noise from whatever else is resident on the heap, not just the code
under test. `RetainsNoMemoryProportionalToTheEventsReceived` inherits the same shape and the same
reasoning, with a 512 KB bound: the correct, bounded-channel implementation measured 91,912-110,640
B of growth across repeated isolated runs (a capacity-8 channel with drop-oldest retains a constant
number of events regardless of how many pass through it, so this is close to the process's own
baseline noise), and the regression to an unbounded channel measured 7,007,240 B — roughly 60-75x
the correct figure and 13x the bound. That gap is wide enough for a loose bound to still mean
something.

**A genuine flaky-gate finding, found and fixed before this ceiling was committed, per this task's
explicit ask to report rather than tune around it.** Run alongside the rest of
`MassiveDotNet.WebSocket.Tests` — not alongside its own regression, alongside the *other* passing
tests in the project — the same correct implementation measured between 12,283,416 B and
104,633,120 B of "growth" across five repeated runs of the whole project, comfortably over the 512
KB bound on every one of them. That is not the code under test: `ReconnectTests`, `ReadLoopTests`,
and the other async, socket-driven test classes in this project run real background tasks, and
xUnit v3 runs test collections concurrently by default, so `GC.GetTotalMemory` was reading their
allocations, not just this test's. `MassiveDotNet.Rest.Tests` does not hit this, because its test
classes are overwhelmingly synchronous stub-handler tests with nothing running on another thread
while `CursorTraversalTests` measures.

The fix is `tests/MassiveDotNet.WebSocket.Tests/AssemblyAttributes.cs`:
`[assembly: CollectionBehavior(DisableTestParallelization = true)]`. This is an execution-ordering
fix, not a threshold tune — the number the correct code produces did not change, only what else was
allowed to run while it was being measured. Confirmed stable across 10 repeated runs of the full
offline suite after the fix (see the task report), all 801 tests passing every time, where 5 runs
before the fix would have failed on this one alone. The cost is real and accepted: the whole
`MassiveDotNet.WebSocket.Tests` project now runs serially rather than in parallel, which is why its
own duration moved from roughly 650 ms to roughly 1 s in the numbers recorded in the task report.
That is the same trade `TieredCompilation=false` already makes in this project's `.csproj` for the
same reason — a deterministic gate is worth more than the wall-clock it costs, per D31.

## What this does not check

`MeasuredTradeParse` and the trade-id-only claim hold for a trade carrying two condition codes and
a short numeric trade id (`"same"`, then `"0"`-`"999"` in the retention test's frames) — a
pathologically long trade id would allocate more, proportionally, since it is the one field this
converter cannot pool. That is expected and is what the assertion's own message says: only the
trade id should remain.
