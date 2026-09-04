using System.Text;

namespace MassiveDotNet.Benchmarks;

/// <summary>
/// Builds the response bodies the benchmarks read, in the shape the aggregates endpoint returns.
/// </summary>
internal static class Payloads
{
    /// <summary>An aggregates envelope carrying <paramref name="rows"/> bars and no cursor.</summary>
    /// <param name="rows">How many bars to emit.</param>
    public static string Aggregates(int rows)
    {
        StringBuilder body = new(rows * 96);

        body.Append("""{"ticker":"AAPL","adjusted":true,"status":"OK","request_id":"bench","results":[""");

        for (int i = 0; i < rows; i++)
        {
            if (i > 0)
            {
                body.Append(',');
            }

            body.Append("{\"v\":").Append(1_000 + i)
                .Append(",\"vw\":190.5,\"o\":190.1,\"c\":190.9,\"h\":191.2,\"l\":189.8,\"t\":")
                .Append(1704085200000L + (i * 60_000L))
                .Append(",\"n\":42}");
        }

        return body.Append("]}").ToString();
    }
}
