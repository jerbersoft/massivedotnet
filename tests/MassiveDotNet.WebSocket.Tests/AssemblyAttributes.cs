using Xunit;

// AllocationTests.RetainsNoMemoryProportionalToTheEventsReceived measures GC.GetTotalMemory,
// which is process-wide rather than per-thread: every other test collection running concurrently
// in this process pollutes the figure. [Collection("Process memory")] only keeps that test from
// running alongside its own siblings; this keeps every collection in the assembly from running
// alongside every other one, which is what a process-wide measurement actually needs. Confirmed
// necessary on 2026-09-08: without it, the retention test measured between 12 MB and 104 MB of
// "growth" across repeated runs of the full project, purely from concurrent reconnect and
// read-loop tests allocating on other threads while it ran.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
