using System.Net;
using System.Text;
using MassiveDotNet;
using MassiveDotNet.Http;
using MassiveDotNet.Rest;
using MassiveDotNet.Rest.Models;
using NodaTime;
using NodaTime.Text;

// Exercises the full request-building and deserialization path with no network, so that
// `dotnet publish` proves the SDK is Native AOT clean. Reflection-based serialization is
// disabled in this project, so any reflection fallback fails here rather than in production.

StubHandler handler = new();
HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };

using MassiveHttpTransport transport = new(httpClient);
using MassiveRestClient client = new(transport);

MassivePage<Agg> page = await client.Stocks.ListAggregatesAsync(
    "AAPL",
    1,
    AggregateTimespan.Day,
    new LocalDate(2020, 1, 1),
    new LocalDate(2020, 1, 10),
    adjusted: true,
    sort: SortOrder.Ascending);

Agg[] bars = page.Results;

Console.WriteLine($"request : {handler.LastRequestUri}");
Console.WriteLine($"bars    : {bars.Length} (more: {page.HasMore}, request id: {page.RequestId})");

foreach (Agg bar in bars)
{
    Console.WriteLine(Describe(bar));
}

// The traversal is rooted here deliberately. This project is the enforcement mechanism the
// constitution names for rules 3 and 4, and an unreferenced generic instantiation is simply
// trimmed away: with nothing calling EnumerateAsync<TEnvelope, TItem>, a clean publish would
// prove nothing about it, nor about the async-iterator state machine the compiler builds behind
// it. The stub advertises a cursor on the uncursored request, so this really does cross a page
// boundary rather than exercising a single-page shortcut.
Console.WriteLine("\nenumerating every page:");

int enumerated = 0;

await foreach (Agg bar in client.Stocks.EnumerateAggregatesAsync(
    "AAPL",
    1,
    AggregateTimespan.Day,
    new LocalDate(2020, 1, 1),
    new LocalDate(2020, 1, 10),
    adjusted: true,
    sort: SortOrder.Ascending))
{
    enumerated++;
    Console.WriteLine(Describe(bar));
}

Console.WriteLine($"\nrequests: {handler.Requests}");

if (bars.Length != 2 || !page.HasMore)
{
    Console.Error.WriteLine("FAIL: expected a first page of 2 bars reporting more.");
    return 1;
}

// Two pages of the enumeration plus the single-page call above.
if (enumerated != 3 || handler.Requests != 3)
{
    Console.Error.WriteLine(
        $"FAIL: expected 3 enumerated bars over 3 requests; got {enumerated} over {handler.Requests}.");
    return 1;
}

Console.WriteLine("\nAOT smoke test passed.");
return 0;

static string Describe(Agg bar) =>
    $"  {LocalDatePattern.Iso.Format(bar.Timestamp.InUtc().Date)}  O {bar.Open,9:F4}  H {bar.High,9:F4}  "
    + $"L {bar.Low,9:F4}  C {bar.Close,9:F4}  V {bar.Volume,12:N0}";

internal sealed class StubHandler : HttpMessageHandler
{
    private const string FirstPage = """
        {
          "adjusted": true,
          "next_url": "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/1578027600000/2020-01-10?cursor=next",
          "queryCount": 2,
          "request_id": "6a7e466379af0a71039d60cc78e72282",
          "results": [
            { "c": 75.0875, "h": 75.15, "l": 73.7975, "n": 1, "o": 74.06, "t": 1577941200000, "v": 135647456, "vw": 74.6099 },
            { "c": 74.3575, "h": 75.145, "l": 74.125,  "n": 1, "o": 74.2875, "t": 1578027600000, "v": 146535512, "vw": 74.7026 }
          ],
          "resultsCount": 2,
          "status": "OK",
          "ticker": "AAPL"
        }
        """;

    private const string FinalPage = """
        {
          "adjusted": true,
          "queryCount": 1,
          "request_id": "6a7e466379af0a71039d60cc78e72282",
          "results": [
            { "c": 74.9499, "h": 75.225, "l": 74.37, "n": 1, "o": 74.955, "t": 1578286800000, "v": 118578580, "vw": 74.8358 }
          ],
          "resultsCount": 1,
          "status": "OK",
          "ticker": "AAPL"
        }
        """;

    public Uri? LastRequestUri { get; private set; }

    public int Requests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;
        Requests++;

        // Keyed on the cursor rather than on a request counter, so the single-page call and the
        // traversal below it stay independent of the order they happen to run in.
        bool cursored = request.RequestUri?.Query.Contains("cursor=", StringComparison.Ordinal) == true;

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(cursored ? FinalPage : FirstPage, Encoding.UTF8, "application/json"),
        });
    }
}
