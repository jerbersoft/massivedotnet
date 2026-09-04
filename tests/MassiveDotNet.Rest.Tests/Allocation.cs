namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Measures what a code path allocates, so the constitution's third priority — minimal allocation —
/// is a gate rather than a claim.
/// </summary>
/// <remarks>
/// <para>
/// Allocation is byte-exact where elapsed time is not: the same code allocates the same number of
/// bytes on every run and on every machine. That is what lets these be ordinary offline tests
/// costing milliseconds rather than a benchmark job — a shared CI runner can fail them honestly,
/// which it could never do for a timing assertion.
/// </para>
/// <para>
/// <see cref="GC.GetAllocatedBytesForCurrentThread"/> counts one thread. An operation whose
/// continuation resumed elsewhere would be undercounted, and an undercount fails in the direction
/// that looks like success — so <see cref="MeasureTask"/> refuses a task that did not already
/// complete rather than reporting a number it cannot stand behind.
/// </para>
/// </remarks>
internal static class Allocation
{
    /// <summary>
    /// Iterations run before measuring, so tiered JIT, static constructors, and the serializer's
    /// per-type metadata are all paid for outside the measured region.
    /// </summary>
    private const int WarmupIterations = 3;

    /// <summary>Bytes allocated by one run of <paramref name="action"/>, after warming up.</summary>
    /// <param name="action">The path to measure. Run several times; must be repeatable.</param>
    public static long Measure(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        for (int i = 0; i < WarmupIterations; i++)
        {
            action();
        }

        Settle();

        long before = GC.GetAllocatedBytesForCurrentThread();
        action();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>
    /// Bytes allocated by one run of <paramref name="action"/>, which must complete synchronously.
    /// </summary>
    /// <param name="action">The path to measure. Run several times; must be repeatable.</param>
    /// <exception cref="InvalidOperationException">
    /// The returned task had not completed when it was handed back, so part of the work ran on a
    /// thread this measurement cannot see.
    /// </exception>
    public static long MeasureTask(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        for (int i = 0; i < WarmupIterations; i++)
        {
            RunInline(action);
        }

        Settle();

        long before = GC.GetAllocatedBytesForCurrentThread();
        RunInline(action);

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>
    /// Formats a byte count for an assertion message. Failures are read by a person deciding
    /// whether a ceiling moved for a good reason, and megabytes are what they think in.
    /// </summary>
    /// <param name="bytes">The count to format.</param>
    public static string Describe(long bytes) =>
        bytes < 1024L * 1024L
            ? $"{bytes:N0} B"
            : $"{bytes / (1024.0 * 1024.0):N2} MB ({bytes:N0} B)";

    private static void RunInline(Func<Task> action)
    {
        Task task = action();

        if (!task.IsCompleted)
        {
            throw new InvalidOperationException(
                "The measured operation did not complete synchronously. "
                    + "GC.GetAllocatedBytesForCurrentThread counts only the calling thread, so anything "
                    + "a continuation allocated elsewhere would go uncounted and the assertion would "
                    + "pass on a number that is too low. Drive the path from a handler that answers "
                    + "inline, or measure it another way.");
        }

        // Observe the result rather than discarding it, so a fault surfaces as itself instead of
        // as an implausibly small allocation figure.
        task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Collects the warm-up's garbage before the measured region opens. The counter is cumulative
    /// allocation rather than live bytes, so this changes no figure; it keeps a collection that
    /// this thread's own garbage would have triggered from landing mid-measurement.
    /// </summary>
    private static void Settle()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
