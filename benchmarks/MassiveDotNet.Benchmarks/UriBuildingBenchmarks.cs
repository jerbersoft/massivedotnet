using System.Text;
using BenchmarkDotNet.Attributes;
using MassiveDotNet.Http;
using NodaTime;

namespace MassiveDotNet.Benchmarks;

/// <summary>
/// What the SDK's pooled URI building buys over the two shapes it replaced.
/// </summary>
/// <remarks>
/// The naive baselines are not straw men: <c>UriBuilder</c> plus a dictionary of parameters is the
/// shape most .NET clients use, and a <c>StringBuilder</c> with <c>string.Join</c> is what a
/// hand-written endpoint looks like. All four produce the same string; the difference is what they
/// leave behind on a path a caller may walk thousands of times a second.
/// <para>
/// The inputs are instance fields rather than constants so that every arm reads its arguments the
/// same way, and so none of them can be folded away by the JIT as a compile-time constant. The
/// filter arms build the same URI as
/// <c>MassiveDotNet.Rest.Tests.AllocationTests.RenderingComparatorFiltersStaysOnTheBuilder</c>, so
/// the benchmark's allocation column and that test's ceiling are figures for the same thing.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class UriBuildingBenchmarks
{
    private readonly string _ticker = "AAPL";
    private readonly int _limit = 50_000;
    private readonly string[] _tickers = ["AAPL", "MSFT", "NVDA"];
    private readonly string[] _tags2 = ["earnings", "guidance"];
    private readonly SetFilter<string>? _tickerFilter = SetFilter.AnyOf("AAPL", "MSFT", "NVDA");

    private readonly RangeFilter<LocalDate>? _exDividendDates =
        RangeFilter.Gt(new LocalDate(2024, 1, 1)).Lte(new LocalDate(2024, 12, 31));

    private readonly ArrayFilter<string>? _tags = ArrayFilter.AllOf("earnings", "guidance");

    private readonly Filter<int>? _frequency = 4;

    [Benchmark(Baseline = true, Description = "RequestUriBuilder (aggregates)")]
    public string PooledAggregates()
    {
        RequestUriBuilder builder = new(stackalloc char[256]);

        builder.AppendPathLiteral("/v2/aggs/ticker/");
        builder.AppendPathSegment(_ticker);
        builder.AppendPathLiteral("/range/");
        builder.AppendPathSegment(1);
        builder.AppendPathLiteral("/day/2024-01-01/2024-03-01");
        builder.AppendQuery("adjusted", true);
        builder.AppendQuery("sort", "asc");
        builder.AppendQuery("limit", _limit);

        return builder.ToUriString();
    }

    [Benchmark(Description = "UriBuilder + Dictionary (aggregates)")]
    public string UriBuilderAggregates()
    {
        Dictionary<string, string> query = new(StringComparer.Ordinal)
        {
            ["adjusted"] = "true",
            ["sort"] = "asc",
            ["limit"] = _limit.ToString(provider: null),
        };

        UriBuilder uri = new()
        {
            Scheme = "https",
            Host = "api.massive.com",
            Path = $"/v2/aggs/ticker/{Uri.EscapeDataString(_ticker)}/range/1/day/2024-01-01/2024-03-01",
            Query = string.Join('&', query.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}")),
        };

        return uri.Uri.PathAndQuery;
    }

    [Benchmark(Description = "RequestUriBuilder (comparator filters)")]
    public string PooledFiltered()
    {
        RequestUriBuilder builder = new(stackalloc char[256]);

        builder.AppendPathLiteral("/v3/reference/dividends");
        builder.AppendQuery("ticker", _tickerFilter);
        builder.AppendQuery("ex_dividend_date", _exDividendDates);
        builder.AppendQuery("tags", _tags);
        builder.AppendQuery("frequency", _frequency);
        builder.AppendQuery("limit", 1_000);

        return builder.ToUriString();
    }

    [Benchmark(Description = "StringBuilder + string.Join (comparator filters)")]
    public string StringBuilderFiltered()
    {
        StringBuilder builder = new("/v3/reference/dividends");

        builder.Append("?ticker.any_of=").Append(string.Join(',', _tickers.Select(Uri.EscapeDataString)));
        builder.Append("&ex_dividend_date.gt=").Append(Uri.EscapeDataString("2024-01-01"));
        builder.Append("&ex_dividend_date.lte=").Append(Uri.EscapeDataString("2024-12-31"));
        builder.Append("&tags.all_of=").Append(string.Join(',', _tags2.Select(Uri.EscapeDataString)));
        builder.Append("&frequency=").Append(4.ToString(provider: null));
        builder.Append("&limit=").Append(1_000.ToString(provider: null));

        return builder.ToString();
    }
}
