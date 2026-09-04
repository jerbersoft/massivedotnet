using BenchmarkDotNet.Running;

namespace MassiveDotNet.Benchmarks;

/// <summary>
/// Entry point for the benchmark suite.
/// </summary>
/// <remarks>
/// <para>
/// Run everything with <c>dotnet run --project benchmarks/MassiveDotNet.Benchmarks -c Release</c>,
/// or one class with <c>--filter "*Uri*"</c>.
/// </para>
/// <para>
/// This project is deliberately absent from CI. Elapsed time is not reproducible on a shared
/// runner, so the numbers here are produced locally and committed to <c>docs/performance/</c>
/// where a reviewer can see them move. What CI enforces instead is
/// <c>MassiveDotNet.Rest.Tests.AllocationTests</c>, whose byte counts are exact and therefore can
/// fail a build honestly.
/// </para>
/// </remarks>
public static class Program
{
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
