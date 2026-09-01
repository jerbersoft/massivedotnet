namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Response bodies taken verbatim from the Massive endpoint catalog's published samples, so
/// deserialization is exercised against the shape the service actually documents.
/// </summary>
internal static class Fixtures
{
    /// <summary>The documented sample for GET /v2/aggs/ticker/{stocksTicker}/range/...</summary>
    public const string StocksAggregates = """
        {
          "adjusted": true,
          "next_url": "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/1578114000000/2020-01-10?cursor=bGltaXQ9MiZzb3J0PWFzYw",
          "queryCount": 2,
          "request_id": "6a7e466379af0a71039d60cc78e72282",
          "results": [
            {
              "c": 75.0875,
              "h": 75.15,
              "l": 73.7975,
              "n": 1,
              "o": 74.06,
              "t": 1577941200000,
              "v": 135647456,
              "vw": 74.6099
            },
            {
              "c": 74.3575,
              "h": 75.145,
              "l": 74.125,
              "n": 1,
              "o": 74.2875,
              "t": 1578027600000,
              "v": 146535512,
              "vw": 74.7026
            }
          ],
          "resultsCount": 2,
          "status": "OK",
          "ticker": "AAPL"
        }
        """;

    /// <summary>
    /// The documented sample for GET /stocks/v1/dividends, with one departure from the published
    /// text: the sample shows <c>"request_id": 1</c>, a number, while the envelope schema declares
    /// a string and every other endpoint returns one. The fixture uses the string so it matches the
    /// schema the SDK is generated from; a numeric id would fail deserialization, which is the
    /// sample's error rather than the service's.
    /// </summary>
    public const string StocksDividends = """
        {
          "request_id": "1",
          "results": [
            {
              "cash_amount": 0.26,
              "currency": "USD",
              "declaration_date": "2025-07-31",
              "distribution_type": "recurring",
              "ex_dividend_date": "2025-08-11",
              "frequency": 4,
              "historical_adjustment_factor": 0.997899,
              "id": "Ed2c9da60abda1e3f0e99a43f6465863c137b671e1f5cd3f833d1fcb4f4eb27fe",
              "pay_date": "2025-08-14",
              "record_date": "2025-08-11",
              "split_adjusted_cash_amount": 0.26,
              "ticker": "AAPL"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>An error body in the shape Massive returns for an unauthorized request.</summary>
    public const string Unauthorized = """
        {
          "status": "ERROR",
          "request_id": "b1a2c3d4e5f60718293a4b5c6d7e8f90",
          "error": "Unknown API Key"
        }
        """;
}
