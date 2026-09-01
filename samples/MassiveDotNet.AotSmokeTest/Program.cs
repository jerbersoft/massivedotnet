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

Agg[] bars = await client.Stocks.ListAggregatesAsync(
    "AAPL",
    1,
    AggregateTimespan.Day,
    new LocalDate(2020, 1, 1),
    new LocalDate(2020, 1, 10),
    adjusted: true,
    sort: SortOrder.Ascending);

Console.WriteLine($"request : {handler.LastRequestUri}");
Console.WriteLine($"bars    : {bars.Length}");

foreach (Agg bar in bars)
{
    Console.WriteLine(
        $"  {LocalDatePattern.Iso.Format(bar.Timestamp.InUtc().Date)}  O {bar.Open,9:F4}  H {bar.High,9:F4}  "
        + $"L {bar.Low,9:F4}  C {bar.Close,9:F4}  V {bar.Volume,12:N0}");
}

if (bars.Length != 2)
{
    Console.Error.WriteLine("FAIL: expected 2 bars.");
    return 1;
}

Console.WriteLine("\nAOT smoke test passed.");
return 0;

internal sealed class StubHandler : HttpMessageHandler
{
    private const string Body = """
        {
          "adjusted": true,
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

    public Uri? LastRequestUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Body, Encoding.UTF8, "application/json"),
        });
    }
}
