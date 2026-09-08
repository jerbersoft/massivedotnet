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
| `RetainsNoMemoryProportionalToTheEventsReceived` | `TopicSink<StockTrade>` retention, capacity-8 channel, minimum of 8 samples of 200 vs. 20,000 events written | -1,350,736 to -1,338,968 B (min-of-8) | 256 KB | `Channel.CreateBounded` → `Channel.CreateUnbounded<T>()`: 1,671,264 B (min-of-8) |
| `ParsingAnAggregateAllocatesNothingBeyondItsDecimalVolumes` | Parse one `A`/`AM` aggregate carrying `dv`/`dav`, ticker pooled | 64 B | 80 B | `walk.Ticker(…)` → `walk.String(…)`: 96 B |
| `ParsingAnAggregateWithoutDecimalVolumesAllocatesNothing` | Parse the same bar without `dv`/`dav` | 0 B | exactly 0 | same unpooled-ticker change: 32 B |
| `ParsingALimitUpLimitDownBandAllocatesNothing` | Parse one `LULD` band, ticker pooled, indicators inline | 0 B | exactly 0 | `ConditionSetSerialization.Read` forced to always spill: 144 B |
| `ParsingAnImbalanceAllocatesNothingBeyondItsAuctionType` | Parse one `NOI` imbalance, ticker pooled | 24 B | 32 B | `walk.Ticker(…)` → `walk.String(…)`: 56 B |

The first three are `GC.GetAllocatedBytesForCurrentThread` deltas, byte-exact on every run — the
same instrument `MassiveDotNet.Rest.Tests.Allocation` uses, linked rather than copied (see the
task's csproj change). The trade-parse and repeat-intern ceilings carry the headroom D31 asks for;
the two exactly-zero claims are asserted with strict equality, because headroom there would defeat
the point. `ParsingATradeAllocatesNothingBeyondItsTradeId`'s 40 B ceiling is 25%, not 20%, over its
32 B measured figure: 20% of 32 B (6.4 B) is under one allocator size-class step, so a flat
percentage would have rounded down to nothing.

The last four rows were measured on 2026-09-08 alongside issue #21, gating the three new parse
paths (`StockAggregate`, `StockImbalance`, `StockLimitUpLimitDown`) the same way. Both exactly-zero
claims — the aggregate without `dv`/`dav`, and the limit up-limit down band — are asserted with
strict equality for the same reason the two pre-existing zero claims above are: headroom on a zero
would defeat the point (D31). The two ceilings carry roughly 20% headroom rounded up to the next
multiple of 8: 64 B → 80 B for the aggregate's two decimal-volume strings, and 24 B → 32 B for the
imbalance's one-character auction type. The imbalance ceiling's 33% headroom is the same
mechanical effect as the trade ceiling's 25% above: 20% of 24 B (4.8 B) is under one allocator
size-class step, so rounding the 20% target up to the next multiple of 8 lands a full step (8 B)
above the measured figure rather than the fractional amount 20% alone would give. The aggregate
ceiling's 25% is a plainer case of the same rounding rule rather than extra slack: 64 B + 20% is
76.8 B, and the nearest multiple of 8 at or above that is 80 B. Each of the four was watched failing under its own
regression before being committed, one at a time, restoring in between; the same
`ConditionSetSerialization.Read` regression used for `ConditionsWithinTheInlineCapacityAllocateNothing`
also reddens `ParsingALimitUpLimitDownBandAllocatesNothing`, since one implementation serves the
trade, quote, and limit up-limit down converters alike (see `ConditionSetSerialization`'s own
remarks) — expected, not a sign the ceiling measures the wrong thing.

The fourth is `GC.GetTotalMemory`, which is process-wide rather than per-thread, and its ceiling
follows a different convention — see the next section.

## The retention test's ceiling, and a flakiness finding fixed rather than tuned around

`GC.GetTotalMemory` is what `CursorTraversalTests.RetainsNoMemoryProportionalToThePagesTraversed`
in `MassiveDotNet.Rest.Tests` already uses for the same class of claim, and that test's own comment
explains why its bound (8 MB) is far looser than a 20%-headroom figure would be: the measurement is
process-wide, so it carries noise from whatever else is resident on the heap, not just the code
under test. `RetainsNoMemoryProportionalToTheEventsReceived` inherits the same shape and the same
reasoning, with a 256 KB bound.

The bound was 512 KB when first committed and was tightened to 256 KB before the task closed. At
512 KB it sat roughly 4.6x above the highest single-sample figure ever observed from correct code
at the time (91,912-110,640 B), which would have caught an unbounded channel but slept through a
regression that merely tripled retention. 256 KB is about 2.3x that maximum: still far looser than
D31's usual 20% headroom, deliberately, because a process-wide reading is noisier than a
thread-local allocation delta -- but tight enough that a doubling fails. That single-sample
instrument is superseded by the minimum-of-eight scheme below; the bound itself never moved.

**A genuine flaky-gate finding, found and fixed before this ceiling was committed, per this task's
explicit ask to report rather than tune around it.** Run alongside the rest of
`MassiveDotNet.WebSocket.Tests` — not alongside its own regression, alongside the *other* passing
tests in the project — the same correct implementation measured between 12,283,416 B and
104,633,120 B of "growth" across five repeated runs of the whole project, comfortably over the
bound on every one of them. That is not the code under test: `ReconnectTests`, `ReadLoopTests`,
and the other async, socket-driven test classes in this project run real background tasks, and
xUnit v3 runs test collections concurrently by default, so `GC.GetTotalMemory` was reading their
allocations, not just this test's. `MassiveDotNet.Rest.Tests` does not hit this, because its test
classes are overwhelmingly synchronous stub-handler tests with nothing running on another thread
while `CursorTraversalTests` measures.

The first fix was `tests/MassiveDotNet.WebSocket.Tests/ProcessMemoryTests.cs`:
`[CollectionDefinition("Process memory", DisableParallelization = true)]`. This is an
execution-ordering fix, not a threshold tune — the number the correct code produces did not change,
only what else was allowed to run while it was being measured. It started as the assembly-wide
`[assembly: CollectionBehavior(DisableTestParallelization = true)]`, which worked but was broader
than the problem: only the one test reading `GC.GetTotalMemory` is vulnerable, because the other
three ceilings go through `Allocation.Measure`, which reads `GC.GetAllocatedBytesForCurrentThread`
— thread-local, and immune to whatever else is running. Disabling parallelization for just this
collection keeps it from running beside any other xUnit collection while leaving the rest of the
assembly parallel, and it is measurably cheaper: 688 ms against roughly 1 s for the assembly-wide
form. Confirmed stable across 14 unfiltered runs of the project in the narrow form at the time —
but every one of those 14 runs, and the ones behind the paragraph above, went through the native
xUnit v3 runner directly, never `dotnet test`.

## Follow-up, 2026-09-08: xUnit-level isolation was necessary but not sufficient

The collection fix above is real and stays: it stops this test's reading from being polluted by
this project's *own* xUnit collections, proven again below. It turned out not to be the whole
story, because `dotnet test` — the command every release gate and CI job actually runs, never the
native runner directly — routes execution through the `xunit.runner.visualstudio` / VSTest bridge,
an in-process test host with its own background activity that no `[Collection]` attribute can
reach, because it is not an xUnit collection at all. Found when this project's 226 tests, run
repeatedly via `dotnet test`, failed this test roughly one run in three despite the fix above; the
native runner, both in its default parallel mode and fully serial (`-parallel none`), stayed clean
across ten unfiltered runs on the same binary. Forcing xUnit's own parallelism off through
`dotnet test`'s passthrough flags (`-- xunit.parallelizeAssembly=false xunit.maxParallelThreads=1`)
made no measurable difference, which is what pinned the pollution to the bridge process rather than
to anything xUnit schedules.

The pollution was not classical jitter: every failure under `dotnet test` read
`Retention grew by 1,037,880 B` — the same figure, exactly, every time — landing on one side or the
other of a single small/large pair. A fixed step that lands on one raw `GC.GetTotalMemory` reading
or the other is exactly the shape a minimum-of-several-samples estimator is built for: the step
only ever adds to a reading, never subtracts, so the sample it happens not to land on is a genuine,
unpolluted reading of retention, and a real regression -- additive on top of the same step -- still
raises the samples the step misses.

Before adopting that, `GC.GetAllocatedBytesForCurrentThread` — immune to the step entirely, and
already the instrument behind the first three rows in the table above — was tried and measured
first, per this task's explicit instruction not to substitute it blindly. Measured across the same
200-event and 20,000-event passes: 4,800 B and 638,400 B respectively — proportional to events
processed, not flat. That is expected once named: every trade's id string is genuine, unpooled,
per-event garbage (only the ticker is pooled), so this instrument counts it as allocated whether or
not the bounded buffer ever retains it past that write. It measures a different property
("processing allocates proportionally", which is already covered by
`ParsingATradeAllocatesNothingBeyondItsTradeId`) than the one this test is named for
("retention does not grow"), so it was rejected for this test specifically.

`RetainsNoMemoryProportionalToTheEventsReceived` now takes the minimum `large - small` delta across
eight independent samples in one run, asserted against the same, unmoved 256 KB bound. Measured
2026-09-08 across six repeated runs of the correct implementation, the minimum-of-eight landed
between -1,350,736 B and -1,338,968 B every time (negative because the step reliably landed on the
small side of the first pair — a valid reading of a subtraction between two noisy process-wide
probes). Regression-tested against `Channel.CreateUnbounded<T>()`, the same scenario the original
7,007,240 B single-sample figure came from: every one of eight samples came back close together and
the minimum was 1,671,264 B, 6.4x the 256 KB bound, confirmed reproducible across three repeated
failing runs. A second candidate regression -- inflating `capacity` on the still-bounded channel to
1,000,000 rather than switching channel types -- was tried first and rejected on its own measured
evidence: later samples in the same run land near-zero once the channel's backing storage has
already grown once to accommodate a large burst, so the minimum of eight hides exactly the
regression it exists to catch. That is not a property of *this* fix; it is a property of
minimum-of-samples estimators in general, worth naming so nobody reaches for a large fixed capacity
as a regression proxy again.

**Verified clean across 12 consecutive full-project runs under `dotnet test`** (8 required, 4 more
for margin) after the sampling change — zero failures, where the same command failed
roughly one run in three before it.

`MassiveDotNet.Rest.Tests` uses neither mechanism for its own `CursorTraversalTests`. That is
latently safe rather than protected: its 497 tests are synchronous stub-handler tests with nothing
running on another thread, so nothing pollutes the reading today. The first genuinely
background-threaded async test added to that project inherits this exact bug, and the fix there is
the same one-line collection definition.

## What this does not check

`MeasuredTradeParse` and the trade-id-only claim hold for a trade carrying two condition codes and
a short numeric trade id (`"same"`, then `"0"`-`"999"` in the retention test's frames) — a
pathologically long trade id would allocate more, proportionally, since it is the one field this
converter cannot pool. That is expected and is what the assertion's own message says: only the
trade id should remain.
