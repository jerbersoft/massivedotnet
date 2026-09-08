using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// The collection every process-wide memory measurement joins, run with parallelization off.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AllocationTests.RetainsNoMemoryProportionalToTheEventsReceived"/> measures
/// <see cref="GC.GetTotalMemory(bool)"/>, which is process-wide rather than per-thread, so anything
/// running concurrently in this process lands in the figure. That is not hypothetical here:
/// measured on 2026-09-08, the correct bounded-channel implementation reported between 12,283,416 B
/// and 104,633,120 B of "growth" when run alongside the project's other test classes, purely from
/// <c>ReconnectTests</c> and <c>ReadLoopTests</c> allocating on background threads while it ran.
/// </para>
/// <para>
/// Disabling parallelization for this collection is enough, and is deliberately narrower than the
/// assembly-wide <c>CollectionBehavior(DisableTestParallelization = true)</c> this started as: xUnit
/// honours the per-collection knob by keeping this collection from running beside any other, which
/// is exactly what a process-wide measurement needs, while every other collection in the assembly
/// still runs in parallel. Measured across 23 unfiltered runs, the narrow form holds the project at
/// roughly its original parallel duration where the assembly-wide form cost about 340 ms.
/// </para>
/// <para>
/// The other three ceilings in <see cref="AllocationTests"/> do not need this. They go through
/// <c>Allocation.Measure</c>, which reads <see cref="GC.GetAllocatedBytesForCurrentThread"/> --
/// thread-local, and therefore immune to whatever else is running.
/// </para>
/// </remarks>
[CollectionDefinition("Process memory", DisableParallelization = true)]
public sealed class ProcessMemoryTests;
