using System.Text.Json;
using BenchmarkDotNet.Attributes;
using MassiveDotNet.Http;
using MassiveDotNet.Rest;
using MassiveDotNet.Rest.Models;
using NodaTime;

namespace MassiveDotNet.Benchmarks;

/// <summary>
/// What a caller pays to read a page of bars, and what the two decisions behind that path bought.
/// </summary>
/// <remarks>
/// <para>
/// Four arms over one payload. The SDK arm goes through the public API, which is the only honest
/// way to measure it: the JSON context is internal, so a direct serializer call would skip the
/// transport a consumer cannot skip. The other three are local baselines sharing one
/// <c>HttpClient</c> and one handler, so they differ from each other only in the thing named.
/// </para>
/// <para>
/// Struct against class is D4. Streamed against buffered is the rule that says never to read a
/// body into a string first. SDK against local struct is the SDK's own overhead, and is the
/// number to watch: it should stay small, because everything above the deserializer is meant to
/// be a URI and a status check.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class DeserializationBenchmarks : IDisposable
{
    private HttpClient _httpClient = null!;
    private MassiveHttpTransport _transport = null!;
    private MassiveRestClient _client = null!;

    [Params(1_000, 10_000, 50_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _httpClient = new HttpClient(new InlineHandler(Payloads.Aggregates(Rows)))
        {
            BaseAddress = MassiveEndpoints.Production,
        };

        _transport = new MassiveHttpTransport(_httpClient);
        _client = new MassiveRestClient(_transport);
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <summary>
    /// Releases the client, transport, and <c>HttpClient</c> built in <see cref="Setup"/>.
    /// BenchmarkDotNet calls <see cref="Cleanup"/>; this exists because the analyzers are right
    /// that a type holding three disposables should say so.
    /// </summary>
    public void Dispose()
    {
        _client?.Dispose();
        _transport?.Dispose();
        _httpClient?.Dispose();

        GC.SuppressFinalize(this);
    }

    [Benchmark(Baseline = true, Description = "SDK, struct rows, streamed")]
    public async Task<int> Sdk()
    {
        MassivePage<Agg> page = await _client.Stocks.ListAggregatesAsync(
            "AAPL", 1, AggregateTimespan.Day,
            new LocalDate(2024, 1, 1), new LocalDate(2024, 3, 1));

        return page.Results.Length;
    }

    [Benchmark(Description = "Local struct rows, streamed")]
    public async Task<int> StructStreamed()
    {
        using HttpResponseMessage response = await Send();
        await using Stream content = await response.Content.ReadAsStreamAsync();

        StructAggResponse? payload = await JsonSerializer.DeserializeAsync(
            content, BenchmarkJsonContext.Default.StructAggResponse);

        return payload?.Results?.Length ?? 0;
    }

    [Benchmark(Description = "Local class rows, streamed (D4's baseline)")]
    public async Task<int> ClassStreamed()
    {
        using HttpResponseMessage response = await Send();
        await using Stream content = await response.Content.ReadAsStreamAsync();

        ClassAggResponse? payload = await JsonSerializer.DeserializeAsync(
            content, BenchmarkJsonContext.Default.ClassAggResponse);

        return payload?.Results?.Length ?? 0;
    }

    [Benchmark(Description = "Local struct rows, body buffered into a string")]
    public async Task<int> StructBuffered()
    {
        using HttpResponseMessage response = await Send();
        string content = await response.Content.ReadAsStringAsync();

        StructAggResponse? payload = JsonSerializer.Deserialize(
            content, BenchmarkJsonContext.Default.StructAggResponse);

        return payload?.Results?.Length ?? 0;
    }

    private Task<HttpResponseMessage> Send()
    {
        HttpRequestMessage request = new(HttpMethod.Get, "/v2/aggs/ticker/AAPL/range/1/day/2024-01-01/2024-03-01");

        return _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }
}
