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

    /// <summary>
    /// The documented sample for GET /v2/reference/news, verbatim. Its <c>next_url</c> names the
    /// origin with an explicit <c>:443</c>, which the same-origin check (D14) must treat as the
    /// configured base address.
    /// </summary>
    public const string ReferenceNews = """
        {
          "count": 1,
          "next_url": "https://api.massive.com:443/v2/reference/news?cursor=eyJsaW1pdCI6MSwic29ydCI6InB1Ymxpc2hlZF91dGMiLCJvcmRlciI6ImFzY2VuZGluZyIsInRpY2tlciI6e30sInB1Ymxpc2hlZF91dGMiOnsiZ3RlIjoiMjAyMS0wNC0yNiJ9LCJzZWFyY2hfYWZ0ZXIiOlsxNjE5NDA0Mzk3MDAwLG51bGxdfQ",
          "request_id": "831afdb0b8078549fed053476984947a",
          "results": [
            {
              "amp_url": "https://m.uk.investing.com/news/stock-market-news/markets-are-underestimating-fed-cuts-ubs-3559968?ampMode=1",
              "article_url": "https://uk.investing.com/news/stock-market-news/markets-are-underestimating-fed-cuts-ubs-3559968",
              "author": "Sam Boughedda",
              "description": "UBS analysts warn that markets are underestimating the extent of future interest rate cuts by the Federal Reserve, as the weakening economy is likely to justify more cuts than currently anticipated.",
              "id": "8ec638777ca03b553ae516761c2a22ba2fdd2f37befae3ab6fdab74e9e5193eb",
              "image_url": "https://i-invdn-com.investing.com/news/LYNXNPEC4I0AL_L.jpg",
              "insights": [
                {
                  "sentiment": "positive",
                  "sentiment_reasoning": "UBS analysts are providing a bullish outlook on the extent of future Federal Reserve rate cuts, suggesting that markets are underestimating the number of cuts that will occur.",
                  "ticker": "UBS"
                }
              ],
              "keywords": [
                "Federal Reserve",
                "interest rates",
                "economic data"
              ],
              "published_utc": "2024-06-24T18:33:53Z",
              "publisher": {
                "favicon_url": "https://s3.massive.com/public/assets/news/favicons/investing.ico",
                "homepage_url": "https://www.investing.com/",
                "logo_url": "https://s3.massive.com/public/assets/news/logos/investing.png",
                "name": "Investing.com"
              },
              "tickers": [
                "UBS"
              ],
              "title": "Markets are underestimating Fed cuts: UBS By Investing.com - Investing.com UK"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// A hand-written final page in the news envelope's shape, with no <c>next_url</c>, so a
    /// traversal that starts from <see cref="ReferenceNews"/> ends after two requests. The service
    /// cannot be asked for "the page after the published sample", which is why this is written
    /// rather than captured.
    /// </summary>
    public const string ReferenceNewsLastPage = """
        {
          "count": 1,
          "request_id": "0d5b6f1e9f3c4a7b8e2d1c0f9a8b7c6d",
          "results": [
            {
              "article_url": "https://example.com/second",
              "author": "Second Author",
              "id": "second",
              "published_utc": "2024-06-25T09:00:00Z",
              "publisher": {
                "homepage_url": "https://example.com/",
                "logo_url": "https://example.com/logo.png",
                "name": "Example News"
              },
              "tickers": ["UBS"],
              "title": "Second article"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// The documented sample for GET /v1/indicators/sma/{stockTicker}, verbatim. One value, and
    /// two underlying aggregates as <c>expand_underlying</c> returns them.
    /// </summary>
    public const string StocksSma = """
        {
          "next_url": "https://api.massive.com/v1/indicators/sma/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "a47d1beb8c11b6ae897ab76cdbbf35a3",
          "results": {
            "underlying": {
              "aggregates": [
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
              "url": "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2003-01-01/2022-07-25"
            },
            "values": [
              {
                "timestamp": 1517562000016,
                "value": 140.139
              }
            ]
          },
          "status": "OK"
        }
        """;

    /// <summary>
    /// A hand-written final page in the SMA envelope's shape, with no <c>next_url</c> and only the
    /// underlying's URL, so a traversal that starts from <see cref="StocksSma"/> ends after two
    /// requests. The service cannot be asked for "the page after the published sample".
    /// </summary>
    public const string StocksSmaLastPage = """
        {
          "request_id": "0d5b6f1e9f3c4a7b8e2d1c0f9a8b7c6d",
          "results": {
            "underlying": {
              "url": "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2003-01-01/2022-07-24"
            },
            "values": [
              {
                "timestamp": 1517475600016,
                "value": 139.871
              }
            ]
          },
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /v2/last/trade/{stocksTicker}, verbatim.</summary>
    public const string StocksLastTrade = """
        {
          "request_id": "f05562305bd26ced64b98ed68b3c5d96",
          "results": {
            "T": "AAPL",
            "c": [
              37
            ],
            "ds": "25.0",
            "f": 1617901342969796400,
            "i": "118749",
            "p": 129.8473,
            "q": 3135876,
            "r": 202,
            "s": 25,
            "t": 1617901342969834000,
            "x": 4,
            "y": 1617901342968000000,
            "z": 3
          },
          "status": "OK"
        }
        """;

    /// <summary>
    /// The documented sample for GET /v1/open-close/{stocksTicker}/{date}, verbatim. The body is
    /// the payload: there is no <c>results</c> wrapper, and <c>status</c> sits beside the prices.
    /// </summary>
    public const string StocksOpenClose = """
        {
          "afterHours": 322.1,
          "close": 325.12,
          "from": "2023-01-09",
          "high": 326.2,
          "low": 322.3,
          "open": 324.66,
          "preMarket": 324.5,
          "status": "OK",
          "symbol": "AAPL",
          "volume": 26122646
        }
        """;

    /// <summary>
    /// The documented sample for GET /v1/marketstatus/upcoming, verbatim. The body is a bare array
    /// with no envelope at all; the early-close entries carry timestamps with a fraction.
    /// </summary>
    public const string MarketHolidays = """
        [
          {
            "date": "2020-11-26",
            "exchange": "NYSE",
            "name": "Thanksgiving",
            "status": "closed"
          },
          {
            "date": "2020-11-26",
            "exchange": "NASDAQ",
            "name": "Thanksgiving",
            "status": "closed"
          },
          {
            "date": "2020-11-26",
            "exchange": "OTC",
            "name": "Thanksgiving",
            "status": "closed"
          },
          {
            "close": "2020-11-27T18:00:00.000Z",
            "date": "2020-11-27",
            "exchange": "NASDAQ",
            "name": "Thanksgiving",
            "open": "2020-11-27T14:30:00.000Z",
            "status": "early-close"
          },
          {
            "close": "2020-11-27T18:00:00.000Z",
            "date": "2020-11-27",
            "exchange": "NYSE",
            "name": "Thanksgiving",
            "open": "2020-11-27T14:30:00.000Z",
            "status": "early-close"
          }
        ]
        """;

    /// <summary>
    /// The documented sample for GET /v2/aggs/grouped/locale/us/market/stocks/{date}, with one
    /// departure from the published text: the sample's <c>request_id</c> is a schema fragment (an
    /// object carrying a <c>description</c> and a <c>type</c>) pasted where a value belongs, while
    /// the envelope schema declares a string and every other endpoint returns one. The fixture
    /// uses a string; the object would fail deserialization, which is the sample's error rather
    /// than the service's.
    /// </summary>
    public const string StocksGroupedDaily = """
        {
          "adjusted": true,
          "queryCount": 3,
          "request_id": "6a7e466379af0a71039d60cc78e72282",
          "results": [
            {
              "T": "KIMpL",
              "c": 25.9102,
              "h": 26.25,
              "l": 25.91,
              "n": 74,
              "o": 26.07,
              "t": 1602705600000,
              "v": 4369,
              "vw": 26.0407
            },
            {
              "T": "TANH",
              "c": 23.4,
              "h": 24.763,
              "l": 22.65,
              "n": 1096,
              "o": 24.5,
              "t": 1602705600000,
              "v": 25933.6,
              "vw": 23.493
            },
            {
              "T": "VSAT",
              "c": 34.24,
              "h": 35.47,
              "l": 34.21,
              "n": 4966,
              "o": 34.9,
              "t": 1602705600000,
              "v": 312583,
              "vw": 34.4736
            }
          ],
          "resultsCount": 3,
          "status": "OK"
        }
        """;

    /// <summary>
    /// The documented sample for GET /v2/aggs/ticker/{stocksTicker}/prev, verbatim. The result
    /// carries a <c>T</c> the schema does not declare; the model follows the schema, so the field
    /// is ignored on the way in.
    /// </summary>
    public const string StocksPreviousClose = """
        {
          "adjusted": true,
          "queryCount": 1,
          "request_id": "6a7e466379af0a71039d60cc78e72282",
          "results": [
            {
              "T": "AAPL",
              "c": 115.97,
              "h": 117.59,
              "l": 114.13,
              "o": 115.55,
              "t": 1605042000000,
              "v": 131704427,
              "vw": 116.3058
            }
          ],
          "resultsCount": 1,
          "status": "OK",
          "ticker": "AAPL"
        }
        """;

    /// <summary>The documented sample for GET /v2/last/nbbo/{stocksTicker}, verbatim.</summary>
    public const string StocksLastQuote = """
        {
          "request_id": "b84e24636301f19f88e0dfbf9a45ed5c",
          "results": {
            "P": 127.98,
            "S": 7,
            "T": "AAPL",
            "X": 19,
            "p": 127.96,
            "q": 83480742,
            "s": 1,
            "t": 1617827221349730300,
            "x": 11,
            "y": 1617827221349366000,
            "z": 3
          },
          "status": "OK"
        }
        """;

    /// <summary>An envelope in the singular shape with its payload missing: a 200 the caller cannot use.</summary>
    public const string SingularWithoutResults = """
        {
          "status": "OK",
          "request_id": "r"
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
