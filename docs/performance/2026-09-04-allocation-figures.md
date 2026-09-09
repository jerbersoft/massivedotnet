# Allocation and throughput figures

Measured 2026-09-04 for issue #7, and the deserialization figures re-measured the same day after
issue #47 was fixed. These are the numbers behind the constitution's third design priority, and the
baselines that say what decisions D4, D5, D15, and D32 actually bought.

| | |
|---|---|
| Host | Apple M4 Max, 16 physical cores, macOS Sequoia 15.7.4 |
| Runtime | .NET 10.0.2 (10.0.225.61305), Arm64 RyuJIT AdvSIMD, Concurrent Workstation GC |
| SDK | 10.0.102 |
| Tool | BenchmarkDotNet 0.15.2, `MemoryDiagnoser`, `DefaultJob` unless noted |

Reproduce with `dotnet run --project benchmarks/MassiveDotNet.Benchmarks -c Release`. Timings are
machine-specific and will not match on other hardware; **allocation columns should match exactly**,
because allocation is byte-exact.

---

## Two instruments, on purpose

Allocation is gated; time is not. The split is not squeamishness — it is that one of the two is
reproducible on a shared CI runner and the other is not.

| | `AllocationTests` (gate) | `MassiveDotNet.Benchmarks` (figures) |
|---|---|---|
| Runs in CI | yes, with the rest of the offline suite | never |
| Costs | ~360 ms | minutes |
| Asserts | byte ceilings, fails the build | nothing |
| Measures | one call, `GC.GetAllocatedBytesForCurrentThread` | many calls, BenchmarkDotNet |
| JIT | tiering off (`TieredCompilation=false`) | tiering and dynamic PGO on, fully warmed |

**The two used to report different numbers for the same path, and the gap turned out to be the
bug.** Before #47, deserializing 50,000 rows measured 38,737,296 B in the gate and 33,542,195 B
here — 5,195,101 B apart, or 103.9 bytes per row, suspiciously close to one boxed 88-byte row plus
a 16-byte header. The guess recorded here was an allocation the fully-warmed JIT elides and the
cold one does not. It was close: D32 removed exactly that per-row boxing, and the two instruments
now agree to within a kilobyte — 4,402,864 B in the gate against 4,401,858 B here, on a path that
allocates 4,400,000 B of array.

The gate still takes the colder figure, because it is the deterministic one: with tiering left on,
the same binary reported either 38,737,296 or 36,473,232 bytes across repeated runs depending on
which tier the measured call happened to execute at, and a gate that fails 1 run in 3 is not a
gate.

So the gate's ceilings are conservative against what a warmed client pays, and the accepted cost is
real: a regression visible only on the PGO-enabled path would not fail the build. Nothing observed
so far has that shape, and the alternative is no gate at all.

---

## Request URI building

`RequestUriBuilder` writes into caller-supplied stack space and grows into pooled memory, so the
returned string is meant to be the only thing that survives the call.

| Path | Mean | Allocated | vs baseline |
|---|---:|---:|---|
| `RequestUriBuilder`, aggregates | **51.27 ns** | **200 B** | baseline |
| `UriBuilder` + `Dictionary`, aggregates | 409.87 ns | 1,856 B | 8.0x slower, 9.3x the allocation |
| `RequestUriBuilder`, six comparator forms | **162.11 ns** | **648 B** | baseline |
| `StringBuilder` + `string.Join`, six comparator forms | 161.30 ns | 1,264 B | same speed, 1.95x the allocation |

200 B for the aggregates URI is the returned string and nothing else: 84 characters of UTF-16 plus
a string's header and length field. There is no buffer left over.

Read the second pair honestly. **A hand-rolled `StringBuilder` renders comparator filters exactly
as fast**, within noise, and the only thing the SDK's builder wins is half the garbage. D15 was
never argued on speed — it was argued on 1,182 flat parameters collapsing to one filter-typed
parameter per field, with rendering implemented once instead of in 93 generated files. This table
says the ergonomic win costs nothing and saves allocation; it does not say the builder is faster.

---

## Reading a page of aggregate bars

One payload, four arms. The SDK arm goes through the public API — the JSON context is `internal`,
so calling the serializer directly would skip a transport that no consumer can skip. The other
three share one `HttpClient` and one handler and differ from each other only in the thing named.

Re-measured after #47. The `Gen0/1/2` columns are collections per 1,000 operations and are the
reason the means below must not be read on their own.

### 50,000 rows

| Arm | Mean | Allocated | Gen0 / Gen1 / Gen2 |
|---|---:|---:|---:|
| SDK, struct rows, streamed | 33,304.2 us | **4,299 KB** | **0 / 0 / 0** |
| Local struct rows, streamed | 24,347.5 us | 32,756 KB | 3,188 / 1,094 / 1,094 |
| Local class rows, streamed | 28,123.3 us | 18,606 KB | 2,469 / 969 / 469 |
| Local struct rows, body buffered into a string | 25,064.5 us | 50,696 KB | 3,500 / 781 / 781 |

### 10,000 rows

| Arm | Mean | Allocated | Gen0 / Gen1 / Gen2 |
|---|---:|---:|---:|
| SDK, struct rows, streamed | 5,284.8 us | **862 KB** | 141 / 141 / 141 |
| Local struct rows, streamed | 5,281.7 us | 7,119 KB | 1,422 / 992 / 992 |
| Local class rows, streamed | 5,340.8 us | 3,774 KB | 445 / 164 / 78 |
| Local struct rows, body buffered into a string | 5,405.3 us | 10,686 KB | 1,320 / 766 / 766 |

### 1,000 rows

| Arm | Mean | Allocated | Gen0 / Gen1 / Gen2 |
|---|---:|---:|---:|
| SDK, struct rows, streamed | 550.3 us | **88 KB** | 27 / 27 / 27 |
| Local struct rows, streamed | 528.3 us | 607 KB | 55 / 55 / 55 |
| Local class rows, streamed | 463.8 us | 369 KB | 45 / 11 / 0 |
| Local struct rows, body buffered into a string | 540.4 us | 964 KB | 138 / 138 / 138 |

*(Deserialization arms ran as `ShortRun`: 3 warmup and 3 target iterations. The allocation columns
are exact regardless; the means carry wider error bars than the URI table's.)*

### What these say

**The SDK now allocates 7.6x less than a raw deserialize, and pays for it in wall clock.** This is
the honest headline and it goes first. At 50,000 rows the SDK arm is 4,299 KB against the naive
32,756 KB, and 33,304 us against 24,348 — **37% slower**. At 10,000 rows the two are the same speed
within noise (5,285 against 5,282) for 8.3x less allocation, and at 1,000 rows the SDK is 4% slower
for 6.9x less.

**The 50,000-row mean is measured in the one setting that flatters the naive arm.** BenchmarkDotNet
runs one operation at a time in a quiet process, so garbage collection is nearly free: nothing else
is competing for the heap and a Gen2 pause stalls no one but the benchmark. The Gen columns say
what that hides. Per 1,000 operations the SDK arm triggers **no collections at all**, where the
naive arm triggers 3,188 Gen0, 1,094 Gen1, and 1,094 Gen2. A Gen2 collection is process-wide: in a
service reading market data on one thread while serving requests on others, that cost lands on
every one of them, and it does not appear in this table. The 37% is real and is not being explained
away — it is being placed next to a number the benchmark cannot charge to the arm that causes it.

**Where the 37% comes from.** A custom collection converter cannot be resumed mid-buffer, so
`System.Text.Json` buffers the whole array and scans it once to find its extent before the
converter parses it again. That double scan is inherent to reading an array through a converter of
our own on the streaming path, and it is the price of the pooled buffer. Two things that looked
like the cause were measured and were not: replacing the pooled buffer with a plain `List<T>` made
no difference (89.9 ms against 89.1 ms on a tiering-off harness), and the per-element
`JsonSerializer.Deserialize` call **was** a real cost, worth 121.9 ms to 89.1 ms once the element
converter was resolved once and called directly.

**Streaming the body is worth 50%.** Reading into a string before parsing costs 1.50x to 1.59x the
allocation at every size, and it is slower. The constitution's "deserialize from the response
stream; never buffer a body into a string first" is worth what it claims, and
`AllocationTests.DeserializingAggregateRowsStaysUnderItsCeiling` fails at all three sizes when the
transport is changed to buffer.

**D4 needed both halves of the story, and #47 has now settled the half that was against it.** The
naive class arm still allocates roughly half what the naive struct arm does transiently, which is
what made #47 look like an argument against D4. It never was: the SDK's struct path now allocates
**4.3x less than the naive class path** (4,299 KB against 18,606 KB) and is 16% faster than it.
Retention was always the other half, and it always favoured the struct:

| 50,000 rows, retained | Bytes | Objects |
|---|---:|---:|
| `Agg[]` (`readonly record struct`, 88 B inline) | 4,400,024 | 1 |
| `ClassAgg[]` (`record` class, 88 B + 16 B header, plus an 8 B reference) | 5,600,024 | 50,001 |

*(Arithmetic from the type layouts, not a measurement: `Unsafe.SizeOf<Agg>()` is 88, a 64-bit
CoreCLR object header is 16 bytes, and a reference is 8.)*

So the struct array holds 21% less memory in one object that the GC traces once, where the class
array hands the collector fifty thousand and one. D4 is about what a 50,000-row response leaves
behind, and on that measure it was always right. The transient cost was a defect in **how rows are
read**, not evidence about **what rows should be** — see the next section.

---

## Issue #47, and what fixing it cost

The issue reported that reading 50,000 aggregate rows allocates 8.8x the array it returns, and
attributed it to one cause: `System.Text.Json` materializes a JSON array through a doubling
`List<T>` and then `ToArray()`, and for an 88-byte struct every growth slot costs the whole value
where a class costs 8 bytes for a reference.

That was about 30% of it. Measured, the 38,737,296 B split two ways:

| Cause | Bytes | Share | Removed by |
|---|---:|---:|---|
| The doubling `List<T>` chain | 11,534,432 | 30% | `PooledArrayConverter<T>` |
| Per-element object machinery, ~456 B/row | 22,800,000 | 59% | a generated converter per struct model (D32) |
| The array itself, plus request and envelope | 4,402,864 | 11% | nothing; this is the floor |

Both halves shipped. 50,000 rows now cost 4,402,864 B — 1.0007x the array — and the remainder is a
fixed 2,864 B of request and envelope that does not grow with the row count, so the ratio improves
with size rather than degrading: 1.03x at 1,000 rows, 1.0007x at 50,000.

**The cost was wall clock, and it was not free.** The table above has the figures: no measurable
change at 10,000 rows, 4% at 1,000, and 37% at 50,000, against a naive arm that pays for its speed
in collections the benchmark does not charge it for. Whether that trade is the right one is a
judgement the constitution has already made — allocation is a named design priority and throughput
is not — but it is a trade, and it is written down here rather than left for someone to discover.

The AOT image got *smaller*: 8,104,248 bytes against 8,337,640 before #47, because
`System.Text.Json`'s object machinery for seventeen struct models is no longer rooted.

---

## The gate's own figures

What `MassiveDotNet.Rest.Tests.AllocationTests` measured when its ceilings were set. Every one of
these was verified by deliberately regressing its path and watching the assertion fail.

| Path | Measured | Ceiling | Regression that proved it |
|---|---:|---:|---|
| Group navigation, 2,000 hops | **0 B** | exactly 0 | one allocation added to the property: 24,000 B |
| Aggregates request URI | 200 B | 256 B | `AppendPathLiteral` materializing its span: 376 B |
| Six comparator forms | 648 B | 744 B | a concatenated string per form: 824 B |
| Deserialize 1,000 rows | 90,864 B | 109,000 B | converter emission disabled: 546,864 B |
| Deserialize 10,000 rows | 882,864 B | 1,059,000 B | converter emission disabled: 5,442,864 B |
| Deserialize 50,000 rows | 4,402,864 B | 5,283,000 B | converter emission disabled: 27,202,864 B |
| Traverse 100 pages x 10 rows | 2,909 B/page | 3,490 B/page | converter emission disabled: 7,469 B/page |
| Deserialize 1,000 trade rows | 154,424 B | 170,000 B | `decimal_size` back to `string`: 186,424 B |
| Deserialize 10,000 trade rows | 1,522,432 B | 1,675,000 B | `decimal_size` back to `string`: 1,762,432 B |
| Deserialize 50,000 trade rows | 7,922,432 B | 8,715,000 B | `decimal_size` back to `string`: 9,122,432 B |

The four deserialization rows were re-measured on 2026-09-04 after issue #47 was fixed. Their
earlier figures, against which the same ceilings were first set, were 727,040 B, 8,326,496 B,
38,737,296 B, and 10,274 B/page; the regression that proved those was buffering the body into a
string, which cost 988,552 B, 10,940,008 B, 51,910,808 B, and 54,738 B/page. Both regressions still
fail all four, and the current column names the cheaper one to reproduce -- turning off the
generator's converter emission and regenerating.

The three trade rows were added on 2026-09-09 with D38, and their headroom is 10% rather than the
20% every row above them carries. That is deliberate and is the whole reason they can catch
anything: a trades page allocates one unpoolable string per row for `id`, so the regression these
guard against -- a second string per row, which is what `decimal_size` was -- is itself about 20%
of the total. A 20% ceiling would sit exactly on top of the defect and never see it.

The comparison is worth reading in full, because the row gets *bigger*: `Trade` grows from 112 to
120 bytes when an 8-byte reference becomes a 16-byte value, so at 50,000 rows the returned array
gains 400,000 B. The heap still loses 1,200,000 B, and 50,000 objects the collector no longer has
to track. A string for a number costs more than the number, even when the number is 16 bytes wide.

The traversal row is the one worth keeping. Under an accumulating traversal,
`CursorTraversalTests.RetainsNoMemoryProportionalToThePagesTraversed` **still passed** — it measures
what survives a traversal, and an accumulator released at the end survives nothing. Allocation is
what catches quadratic work that memory profiling cannot see.
