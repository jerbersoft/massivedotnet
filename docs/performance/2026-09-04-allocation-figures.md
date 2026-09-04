# Allocation and throughput figures

Measured 2026-09-04 for issue #7. These are the numbers behind the constitution's third design
priority, and the baselines that say what decisions D4, D5, and D15 actually bought.

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

**The two report different numbers for the same path, and both are correct.** Deserializing 50,000
rows measures 38,737,296 B in the gate and 33,542,195 B here. The difference is 5,195,101 B, or
103.9 bytes per row — suspiciously close to one boxed 88-byte row plus a 16-byte header, which
suggests an allocation the fully-warmed JIT elides and the cold one does not. That reading has not
been confirmed against the generated code, and nothing here depends on it: what matters is that
the two instruments bracket the same path rather than contradict each other. The gate takes the
higher, colder figure because it is the deterministic one: with tiering left on, the same binary reported either
38,737,296 or 36,473,232 bytes across repeated runs depending on which tier the measured call
happened to execute at, and a gate that fails 1 run in 3 is not a gate.

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

### 50,000 rows

| Arm | Mean | Allocated | vs SDK |
|---|---:|---:|---|
| SDK, struct rows, streamed | 24,102.9 us | 32,756 KB | baseline |
| Local struct rows, streamed | 24,100.7 us | 32,755 KB | 1.00x |
| Local class rows, streamed | 28,030.9 us | 18,606 KB | 16% slower, **0.57x the allocation** |
| Local struct rows, body buffered into a string | 25,595.2 us | 50,696 KB | 1.55x the allocation |

### 10,000 rows

| Arm | Mean | Allocated | vs SDK |
|---|---:|---:|---|
| SDK, struct rows, streamed | 5,050.5 us | 7,120 KB | baseline |
| Local struct rows, streamed | 5,197.4 us | 7,119 KB | 1.00x |
| Local class rows, streamed | 5,315.7 us | 3,774 KB | 0.53x the allocation |
| Local struct rows, body buffered into a string | 5,736.5 us | 10,686 KB | 1.50x the allocation |

### 1,000 rows

| Arm | Mean | Allocated | vs SDK |
|---|---:|---:|---|
| SDK, struct rows, streamed | 508.2 us | 608 KB | baseline |
| Local struct rows, streamed | 501.2 us | 607 KB | 1.00x |
| Local class rows, streamed | 456.8 us | 369 KB | 0.61x the allocation |
| Local struct rows, body buffered into a string | 516.8 us | 964 KB | 1.59x the allocation |

*(Deserialization arms ran as `ShortRun`: 3 warmup and 3 target iterations. The allocation columns
are exact regardless; the means carry wider error bars than the URI table's.)*

### What these say

**The SDK adds nothing measurable over a raw deserialize.** 32,756 KB against 32,755 KB at 50,000
rows: the transport, the URI, the status check, and the page wrapper together cost about a
kilobyte, which is the result the two-level API and the pooled builder were supposed to produce.

**Streaming the body is worth 50%.** Reading into a string before parsing costs 1.50x to 1.59x the
allocation at every size, and it is slower. The constitution's "deserialize from the response
stream; never buffer a body into a string first" is worth what it claims, and
`AllocationTests.DeserializingAggregateRowsStaysUnderItsCeiling` fails at all three sizes when the
transport is changed to buffer.

**D4 needs both halves of the story, and this table is only one of them.** Class rows allocate
roughly half what struct rows do transiently — the opposite of what "structs allocate less" would
suggest, and the whole of issue #47. What they do not do is retain less:

| 50,000 rows, retained | Bytes | Objects |
|---|---:|---:|
| `Agg[]` (`readonly record struct`, 88 B inline) | 4,400,024 | 1 |
| `ClassAgg[]` (`record` class, 88 B + 16 B header, plus an 8 B reference) | 5,600,024 | 50,001 |

*(Arithmetic from the type layouts, not a measurement: `Unsafe.SizeOf<Agg>()` is 88, a 64-bit
CoreCLR object header is 16 bytes, and a reference is 8.)*

So the struct array holds 21% less memory in one object that the GC traces once, where the class
array hands the collector fifty thousand and one. D4 is about what a 50,000-row response leaves
behind, and on that measure it is right. The transient cost above is a defect in **how rows are
read**, not evidence about **what rows should be** — see the next section.

---

## Issue #47, in one line

`System.Text.Json` materializes a JSON array through `List<T>` and then `ToArray()`. `List<T>`
doubles its backing array as it grows, and for an 88-byte struct every growth slot costs 88 bytes
where a class costs 8 for the reference. That is why `Agg[]` costs 8.8x the array it returns while
`ClassAgg[]` lands near its own floor, and why the fix is a converter or pooling decision rather
than a reversal of D4.

The gate's ceilings pin the current figures so the defect cannot get worse. They come down with the
change that makes it better.

---

## The gate's own figures

What `MassiveDotNet.Rest.Tests.AllocationTests` measured when its ceilings were set. Every one of
these was verified by deliberately regressing its path and watching the assertion fail.

| Path | Measured | Ceiling | Regression that proved it |
|---|---:|---:|---|
| Group navigation, 2,000 hops | **0 B** | exactly 0 | one allocation added to the property: 24,000 B |
| Aggregates request URI | 200 B | 256 B | `AppendPathLiteral` materializing its span: 376 B |
| Six comparator forms | 648 B | 744 B | a concatenated string per form: 824 B |
| Deserialize 1,000 rows | 727,040 B | 880,000 B | body buffered into a string: 988,552 B |
| Deserialize 10,000 rows | 8,326,496 B | 10,000,000 B | body buffered into a string: 10,940,008 B |
| Deserialize 50,000 rows | 38,737,296 B | 46,000,000 B | body buffered into a string: 51,910,808 B |
| Traverse 100 pages x 10 rows | 10,274 B/page | 12,288 B/page | pages accumulated per iteration: 54,738 B/page |

The last row is the one worth keeping. Under an accumulating traversal,
`CursorTraversalTests.RetainsNoMemoryProportionalToThePagesTraversed` **still passed** — it measures
what survives a traversal, and an accumulator released at the end survives nothing. Allocation is
what catches quadratic work that memory profiling cannot see.
