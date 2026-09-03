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

    /// <summary>The documented sample for GET /v3/trades/{stockTicker}, verbatim.</summary>
    public const string StocksTrades = """
        {
          "next_url": "https://api.massive.com/v3/trades/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "a47d1beb8c11b6ae897ab76cdbbf35a3",
          "results": [
            {
              "conditions": [
                12,
                41
              ],
              "decimal_size": "100.0",
              "exchange": 11,
              "id": "1",
              "participant_timestamp": 1517562000015577000,
              "price": 171.55,
              "sequence_number": 1063,
              "sip_timestamp": 1517562000016036600,
              "size": 100,
              "tape": 3
            },
            {
              "conditions": [
                12,
                41
              ],
              "decimal_size": "100.0",
              "exchange": 11,
              "id": "2",
              "participant_timestamp": 1517562000015577600,
              "price": 171.55,
              "sequence_number": 1064,
              "sip_timestamp": 1517562000016038100,
              "size": 100,
              "tape": 3
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// A hand-written final page in the trades envelope's shape, with no <c>next_url</c> and one
    /// trade later than the sample's, so a traversal that starts from <see cref="StocksTrades"/>
    /// ends after two requests. The service cannot be asked for "the page after the published
    /// sample".
    /// </summary>
    public const string StocksTradesLastPage = """
        {
          "request_id": "0d5b6f1e9f3c4a7b8e2d1c0f9a8b7c6d",
          "results": [
            {
              "conditions": [
                12
              ],
              "decimal_size": "50.0",
              "exchange": 11,
              "id": "3",
              "participant_timestamp": 1517562000015580000,
              "price": 171.56,
              "sequence_number": 1065,
              "sip_timestamp": 1517562000016040000,
              "size": 50,
              "tape": 3
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /v3/quotes/{stockTicker}, verbatim.</summary>
    public const string StocksQuotes = """
        {
          "next_url": "https://api.massive.com/v3/quotes/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "a47d1beb8c11b6ae897ab76cdbbf35a3",
          "results": [
            {
              "ask_exchange": 0,
              "ask_price": 0,
              "ask_size": 0,
              "bid_exchange": 11,
              "bid_price": 102.7,
              "bid_size": 60,
              "conditions": [
                1
              ],
              "participant_timestamp": 1517562000065321200,
              "sequence_number": 2060,
              "sip_timestamp": 1517562000065700400,
              "tape": 3
            },
            {
              "ask_exchange": 0,
              "ask_price": 0,
              "ask_size": 0,
              "bid_exchange": 11,
              "bid_price": 170,
              "bid_size": 2,
              "conditions": [
                1
              ],
              "participant_timestamp": 1517562000065408300,
              "sequence_number": 2061,
              "sip_timestamp": 1517562000065791500,
              "tape": 3
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// A hand-written final page in the quotes envelope's shape, with no <c>next_url</c> and one
    /// quote later than the sample's, so a traversal that starts from <see cref="StocksQuotes"/>
    /// ends after two requests. The service cannot be asked for "the page after the published
    /// sample", since its own last page depends on the day's data.
    /// </summary>
    public const string StocksQuotesLastPage = """
        {
          "request_id": "0d5b6f1e9f3c4a7b8e2d1c0f9a8b7c6d",
          "results": [
            {
              "ask_exchange": 0,
              "ask_price": 0,
              "ask_size": 0,
              "bid_exchange": 11,
              "bid_price": 170.1,
              "bid_size": 3,
              "conditions": [
                1
              ],
              "participant_timestamp": 1517562000065500000,
              "sequence_number": 2062,
              "sip_timestamp": 1517562000065900000,
              "tape": 3
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /v2/snapshot/locale/us/markets/stocks/tickers/{stocksTicker}, verbatim.</summary>
    public const string StocksSnapshot = """
        {
          "request_id": "657e430f1ae768891f018e08e03598d8",
          "status": "OK",
          "ticker": {
            "day": {
              "c": 120.4229,
              "dv": "28727868.0",
              "h": 120.53,
              "l": 118.81,
              "o": 119.62,
              "v": 28727868,
              "vw": 119.725
            },
            "lastQuote": {
              "P": 120.47,
              "S": 4,
              "p": 120.46,
              "s": 8,
              "t": 1605195918507251700
            },
            "lastTrade": {
              "c": [
                14,
                41
              ],
              "ds": "236.0",
              "i": "4046",
              "p": 120.47,
              "s": 236,
              "t": 1605195918306274000,
              "x": 10
            },
            "min": {
              "av": 28724441,
              "c": 120.4201,
              "dav": "28724441.0",
              "dv": "270796.0",
              "h": 120.468,
              "l": 120.37,
              "n": 762,
              "o": 120.435,
              "t": 1684428720000,
              "v": 270796,
              "vw": 120.4129
            },
            "prevDay": {
              "c": 119.49,
              "h": 119.63,
              "l": 116.44,
              "o": 117.19,
              "v": 110597265,
              "vw": 118.4998
            },
            "ticker": "AAPL",
            "todaysChange": 0.98,
            "todaysChangePerc": 0.82,
            "updated": 1605195918306274000
          }
        }
        """;

    /// <summary>
    /// The documented sample for GET /v2/snapshot/locale/us/markets/stocks/tickers, verbatim. The
    /// envelope carries a <c>count</c> and no <c>request_id</c>.
    /// </summary>
    public const string StocksSnapshots = """
        {
          "count": 1,
          "status": "OK",
          "tickers": [
            {
              "day": {
                "c": 20.506,
                "dv": "37216.0",
                "h": 20.64,
                "l": 20.506,
                "o": 20.64,
                "v": 37216,
                "vw": 20.616
              },
              "lastQuote": {
                "P": 20.6,
                "S": 22,
                "p": 20.5,
                "s": 13,
                "t": 1605192959994246100
              },
              "lastTrade": {
                "c": [
                  14,
                  41
                ],
                "ds": "2416.0",
                "i": "71675577320245",
                "p": 20.506,
                "s": 2416,
                "t": 1605192894630916600,
                "x": 4
              },
              "min": {
                "av": 37216,
                "c": 20.506,
                "dav": "37216.0",
                "dv": "5000.0",
                "h": 20.506,
                "l": 20.506,
                "n": 1,
                "o": 20.506,
                "t": 1684428600000,
                "v": 5000,
                "vw": 20.5105
              },
              "prevDay": {
                "c": 20.63,
                "h": 21,
                "l": 20.5,
                "o": 20.79,
                "v": 292738,
                "vw": 20.6939
              },
              "ticker": "BCAT",
              "todaysChange": -0.124,
              "todaysChangePerc": -0.601,
              "updated": 1605192894630916600
            }
          ]
        }
        """;

    /// <summary>The documented sample for GET /v2/snapshot/locale/us/markets/stocks/{direction}, verbatim.</summary>
    public const string StocksMovers = """
        {
          "status": "OK",
          "tickers": [
            {
              "day": {
                "c": 14.2284,
                "dv": "133963.0",
                "h": 15.09,
                "l": 14.2,
                "o": 14.33,
                "v": 133963,
                "vw": 14.5311
              },
              "lastQuote": {
                "P": 14.44,
                "S": 11,
                "p": 14.2,
                "s": 25,
                "t": 1605195929997325600
              },
              "lastTrade": {
                "c": [
                  63
                ],
                "ds": "536.0",
                "i": "79372124707124",
                "p": 14.2284,
                "s": 536,
                "t": 1605195848258266000,
                "x": 4
              },
              "min": {
                "av": 133963,
                "c": 14.2284,
                "dav": "133963.0",
                "dv": "6108.0",
                "h": 14.325,
                "l": 14.2,
                "n": 5,
                "o": 14.28,
                "t": 1684428600000,
                "v": 6108,
                "vw": 14.2426
              },
              "prevDay": {
                "c": 0.73,
                "h": 0.799,
                "l": 0.73,
                "o": 0.75,
                "v": 1568097,
                "vw": 0.7721
              },
              "ticker": "PDS",
              "todaysChange": 13.498,
              "todaysChangePerc": 1849.096,
              "updated": 1605195848258266000
            }
          ]
        }
        """;

    /// <summary>
    /// A live movers response captured on 2026-09-02 and trimmed to one ticker, kept because the
    /// description marks <c>lastTrade.c</c> required while the service omits it.
    /// </summary>
    public const string StocksMoversWithoutConditions = """
        {
          "status": "OK",
          "request_id": "9620b682d70299e8f7d2a3983e388728",
          "tickers": [
            {
              "ticker": "KTTAW",
              "todaysChangePerc": 174.07407407407408,
              "todaysChange": 0.0047,
              "updated": 1788364320000000000,
              "day": {
                "dv": "10900.0",
                "o": 0.0035,
                "h": 0.0074,
                "l": 0.0035,
                "c": 0.0074,
                "v": 10900,
                "vw": 0.0037
              },
              "lastQuote": {
                "P": 0.0074,
                "S": 17900,
                "p": 0.0036,
                "s": 10000,
                "t": 1788379210421010190
              },
              "lastTrade": {
                "i": "1",
                "p": 0.0074,
                "s": 200,
                "t": 1788364314024650914,
                "x": 11,
                "ds": "200.0"
              },
              "min": {
                "dv": "200.0",
                "dav": "10900.0",
                "av": 10900,
                "t": 1788364260000,
                "n": 1,
                "o": 0.0074,
                "h": 0.0074,
                "l": 0.0074,
                "c": 0.0074,
                "v": 200,
                "vw": 0.0074
              },
              "prevDay": {
                "o": 0.0049,
                "h": 0.0049,
                "l": 0.0027,
                "c": 0.0027,
                "v": 30000,
                "vw": 0.003096
              }
            }
          ]
        }
        """;

    /// <summary>
    /// A live whole-market snapshot captured on 2026-09-02 and trimmed to one ticker, kept because
    /// the description marks <c>lastTrade.ds</c> and <c>min.dav</c> required while the service
    /// omits them, along with <c>lastTrade.c</c>, on a ticker that has not traded.
    /// </summary>
    public const string StocksSnapshotsMissingDecimals = """
        {
          "status": "OK",
          "request_id": "c9b98c194a3c8563199e44ed6238f13d",
          "count": 1,
          "tickers": [
            {
              "ticker": "AACI",
              "todaysChangePerc": 0,
              "todaysChange": 0,
              "updated": 0,
              "day": {
                "o": 0,
                "h": 0,
                "l": 0,
                "c": 0,
                "v": 0,
                "vw": 0
              },
              "lastQuote": {
                "P": 10.14,
                "S": 100,
                "p": 4.02,
                "s": 200,
                "t": 1788385980252379425
              },
              "lastTrade": {
                "i": "",
                "p": 0,
                "s": 0,
                "t": 0,
                "x": 0
              },
              "min": {
                "av": 0,
                "t": 0,
                "n": 0,
                "o": 0,
                "h": 0,
                "l": 0,
                "c": 0,
                "v": 0,
                "vw": 0
              },
              "prevDay": {
                "o": 10.03,
                "h": 10.05,
                "l": 10.03,
                "c": 10.0499,
                "v": 553,
                "vw": 10.0436
              }
            }
          ]
        }
        """;

    /// <summary>The documented sample for GET /v1/indicators/ema/{stockTicker}, verbatim.</summary>
    public const string StocksEma = """
        {
          "next_url": "https://api.massive.com/v1/indicators/ema/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "a47d1beb8c11b6ae897ab76cdbbf35a3",
          "results": {
            "underlying": {
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

    /// <summary>The documented sample for GET /v1/indicators/rsi/{stockTicker}, verbatim.</summary>
    public const string StocksRsi = """
        {
          "next_url": "https://api.massive.com/v1/indicators/rsi/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "a47d1beb8c11b6ae897ab76cdbbf35a3",
          "results": {
            "underlying": {
              "url": "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2003-01-01/2022-07-25"
            },
            "values": [
              {
                "timestamp": 1517562000016,
                "value": 82.19
              }
            ]
          },
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /v1/indicators/macd/{stockTicker}, verbatim.</summary>
    public const string StocksMacd = """
        {
          "next_url": "https://api.massive.com/v1/indicators/macd/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "a47d1beb8c11b6ae897ab76cdbbf35a3",
          "results": {
            "underlying": {
              "url": "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2003-01-01/2022-07-25"
            },
            "values": [
              {
                "histogram": 38.3801666667,
                "signal": 106.9811666667,
                "timestamp": 1517562000016,
                "value": 145.3613333333
              },
              {
                "histogram": 41.098859136,
                "signal": 102.7386283473,
                "timestamp": 1517562001016,
                "value": 143.8374874833
              }
            ]
          },
          "status": "OK"
        }
        """;

    /// <summary>
    /// A hand-written final page in the MACD envelope's shape, with no <c>next_url</c>, so a
    /// traversal that starts from <see cref="StocksMacd"/> ends after two requests.
    /// </summary>
    public const string StocksMacdLastPage = """
        {
          "request_id": "0d5b6f1e9f3c4a7b8e2d1c0f9a8b7c6d",
          "results": {
            "underlying": {
              "url": "https://api.massive.com/v2/aggs/ticker/AAPL/range/1/day/2003-01-01/2022-07-24"
            },
            "values": [
              {
                "histogram": 40.1,
                "signal": 101.2,
                "timestamp": 1517562002016,
                "value": 141.3
              }
            ]
          },
          "status": "OK"
        }
        """;

    /// <summary>
    /// The documented sample for GET /v2/ticks/stocks/trades/{ticker}/{date}, verbatim. The
    /// schema marks <c>T</c>, <c>f</c>, <c>e</c>, and <c>r</c> required; the sample omits all
    /// four, which is why the map types them nullable (D-G5). The <c>map</c> member is a key
    /// legend the schema does not declare, and is ignored on the way in.
    /// </summary>
    public const string StocksHistoricTrades = """
        {
          "db_latency": 11,
          "map": {
            "I": {
              "name": "orig_id",
              "type": "string"
            },
            "c": {
              "name": "conditions",
              "type": "int"
            },
            "e": {
              "name": "correction",
              "type": "int"
            },
            "f": {
              "name": "trf_timestamp",
              "type": "int64"
            },
            "i": {
              "name": "id",
              "type": "string"
            },
            "p": {
              "name": "price",
              "type": "float64"
            },
            "q": {
              "name": "sequence_number",
              "type": "int64"
            },
            "r": {
              "name": "trf_id",
              "type": "int"
            },
            "s": {
              "name": "size",
              "type": "int"
            },
            "t": {
              "name": "sip_timestamp",
              "type": "int64"
            },
            "x": {
              "name": "exchange",
              "type": "int"
            },
            "y": {
              "name": "participant_timestamp",
              "type": "int64"
            },
            "z": {
              "name": "tape",
              "type": "int"
            }
          },
          "results": [
            {
              "c": [
                12,
                41
              ],
              "i": "1",
              "p": 171.55,
              "q": 1063,
              "s": 100,
              "t": 1517562000016036600,
              "x": 11,
              "y": 1517562000015577000,
              "z": 3
            },
            {
              "c": [
                12,
                41
              ],
              "i": "2",
              "p": 171.55,
              "q": 1064,
              "s": 100,
              "t": 1517562000016038100,
              "x": 11,
              "y": 1517562000015577600,
              "z": 3
            }
          ],
          "results_count": 2,
          "success": true,
          "ticker": "AAPL"
        }
        """;

    /// <summary>
    /// The documented sample for GET /v2/ticks/stocks/nbbo/{ticker}/{date}, verbatim. The schema
    /// marks <c>T</c>, <c>f</c>, and <c>i</c> required; the sample omits all three, which is why
    /// the map types them nullable (D-G5).
    /// </summary>
    public const string StocksHistoricQuotes = """
        {
          "db_latency": 43,
          "map": {
            "P": {
              "name": "ask_price",
              "type": "float64"
            },
            "S": {
              "name": "ask_size",
              "type": "int"
            },
            "X": {
              "name": "ask_exchange",
              "type": "int"
            },
            "c": {
              "name": "conditions",
              "type": "int"
            },
            "f": {
              "name": "trf_timestamp",
              "type": "int64"
            },
            "i": {
              "name": "indicators",
              "type": "int"
            },
            "p": {
              "name": "bid_price",
              "type": "float64"
            },
            "q": {
              "name": "sequence_number",
              "type": "int"
            },
            "s": {
              "name": "bid_size",
              "type": "int"
            },
            "t": {
              "name": "sip_timestamp",
              "type": "int64"
            },
            "x": {
              "name": "bid_exchange",
              "type": "int"
            },
            "y": {
              "name": "participant_timestamp",
              "type": "int64"
            },
            "z": {
              "name": "tape",
              "type": "int"
            }
          },
          "results": [
            {
              "P": 0,
              "S": 0,
              "X": 0,
              "c": [
                1
              ],
              "p": 102.7,
              "q": 2060,
              "s": 60,
              "t": 1517562000065700400,
              "x": 11,
              "y": 1517562000065321200,
              "z": 3
            },
            {
              "P": 0,
              "S": 0,
              "X": 0,
              "c": [
                1
              ],
              "p": 170,
              "q": 2061,
              "s": 2,
              "t": 1517562000065791500,
              "x": 11,
              "y": 1517562000065408300,
              "z": 3
            }
          ],
          "results_count": 2,
          "success": true,
          "ticker": "AAPL"
        }
        """;

    /// <summary>
    /// The documented sample for GET /stocks/v1/splits, with one departure from the published
    /// text: the sample shows <c>"request_id": 1</c>, a number, while the envelope schema declares
    /// a string and every other endpoint returns one. The fixture uses the string, as the
    /// dividends fixture does for the same defect.
    /// </summary>
    public const string StocksSplits = """
        {
          "request_id": "1",
          "results": [
            {
              "adjustment_type": "forward_split",
              "execution_date": "2005-02-28",
              "historical_adjustment_factor": 0.017857,
              "id": "E90a77bdf742661741ed7c8fc086415f0457c2816c45899d73aaa88bdc8ff6025",
              "split_from": 1,
              "split_to": 2,
              "ticker": "AAPL"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// The documented sample for GET /stocks/v1/exchanges, with one departure from the published
    /// text: <c>"request_id": 1</c> becomes a string, for the reason given on
    /// <see cref="StocksSplits"/>. The <c>count</c> member is not in the schema and is ignored.
    /// </summary>
    public const string StocksExchanges = """
        {
          "count": 2,
          "request_id": "1",
          "results": [
            {
              "id": "10",
              "locale": "US",
              "mic": "XNYS",
              "name": "New York Stock Exchange",
              "operating_mic": "XNYS",
              "participant_id": "N",
              "type": "exchange",
              "url": "https://www.nyse.com"
            },
            {
              "id": "12",
              "locale": "US",
              "mic": "XNAS",
              "name": "Nasdaq",
              "operating_mic": "XNAS",
              "participant_id": "T",
              "type": "exchange",
              "url": "https://www.nasdaq.com"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// A hand-written final page in the exchanges envelope's shape, with no <c>next_url</c>, so a
    /// traversal from a cursored copy of <see cref="StocksExchanges"/> ends after two requests.
    /// The published sample has no cursor of its own; the test adds one.
    /// </summary>
    public const string StocksExchangesLastPage = """
        {
          "count": 1,
          "request_id": "2",
          "results": [
            {
              "id": "15",
              "locale": "US",
              "mic": "IEXG",
              "name": "Investors Exchange",
              "operating_mic": "IEXG",
              "participant_id": "V",
              "type": "exchange",
              "url": "https://www.iextrading.com"
            }
          ],
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

    /// <summary>
    /// A hand-written page in the shape the description gives GET /stocks/dev/trades/{ticker}.
    /// The service answered a plain-text 404 for the route when this was written (D22), so
    /// there is no published sample and nothing to capture; the values mirror the v3 trades
    /// sample where the fields coincide. <c>size_fraction</c> is required and described only as
    /// the fractional size, with no unit, so its values here are placeholders that prove only
    /// that a 64-bit integer round-trips.
    /// </summary>
    public const string StocksDevTrades = """
        {
          "next_url": "https://api.massive.com/stocks/dev/trades/AAPL?cursor=YXA9MTA2NCZhcz0mbGltaXQ9Mg",
          "request_id": "3f1c9e2b7a5d4c6e8b0a1f2d3c4e5b6a",
          "results": [
            {
              "conditions": [12, 41],
              "exchange": 11,
              "id": "1",
              "participant_timestamp": 1517562000015577000,
              "price": 171.55,
              "sequence_number": 1063,
              "sip_timestamp": 1517562000016036600,
              "size": 100,
              "size_fraction": 0,
              "tape": 3,
              "ticker": "AAPL"
            },
            {
              "conditions": [12, 37],
              "correction": 0,
              "exchange": 4,
              "id": "2",
              "participant_timestamp": 1517562000015578000,
              "price": 171.56,
              "sequence_number": 1064,
              "sip_timestamp": 1517562000016038100,
              "size": 1,
              "size_fraction": 250000000,
              "tape": 3,
              "ticker": "AAPL",
              "trf_id": 202,
              "trf_timestamp": 1517562000015000000
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /v3/reference/tickers. It carries a cursor of its own.</summary>
    public const string ReferenceTickers = """
        {
          "count": 1,
          "next_url": "https://api.massive.com/v3/reference/tickers?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "e70013d92930de90e089dc8fa098888e",
          "results": [
            {
              "active": true,
              "cik": "0001090872",
              "composite_figi": "BBG000BWQYZ5",
              "currency_name": "usd",
              "last_updated_utc": "2021-04-25T00:00:00Z",
              "locale": "us",
              "market": "stocks",
              "name": "Agilent Technologies Inc.",
              "primary_exchange": "XNYS",
              "share_class_figi": "BBG001SCTQY4",
              "ticker": "A",
              "type": "CS"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// A hand-written final page in the tickers envelope's shape, with no <c>next_url</c>, so a
    /// traversal from <see cref="ReferenceTickers"/> ends after two requests.
    /// </summary>
    public const string ReferenceTickersLastPage = """
        {
          "count": 1,
          "request_id": "e70013d92930de90e089dc8fa098888f",
          "results": [
            {
              "active": true,
              "cik": "0000006201",
              "composite_figi": "BBG005P7Q881",
              "currency_name": "usd",
              "last_updated_utc": "2021-04-25T00:00:00Z",
              "locale": "us",
              "market": "stocks",
              "name": "American Airlines Group Inc.",
              "primary_exchange": "XNAS",
              "share_class_figi": "BBG005P7Q907",
              "ticker": "AAL",
              "type": "CS"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /v3/reference/tickers/{ticker}.</summary>
    public const string ReferenceTickerDetails = """
        {
          "request_id": "31d59dda-80e5-4721-8496-d0d32a654afe",
          "results": {
            "active": true,
            "address": {
              "address1": "One Apple Park Way",
              "city": "Cupertino",
              "postal_code": "95014",
              "state": "CA"
            },
            "branding": {
              "icon_url": "https://api.massive.com/v1/reference/company-branding/d3d3LmFwcGxlLmNvbQ/images/2022-01-10_icon.png",
              "logo_url": "https://api.massive.com/v1/reference/company-branding/d3d3LmFwcGxlLmNvbQ/images/2022-01-10_logo.svg"
            },
            "cik": "0000320193",
            "composite_figi": "BBG000B9XRY4",
            "currency_name": "usd",
            "description": "Apple designs a wide variety of consumer electronic devices, including smartphones (iPhone), tablets (iPad), PCs (Mac), smartwatches (Apple Watch), AirPods, and TV boxes (Apple TV), among others. The iPhone makes up the majority of Apple's total revenue. In addition, Apple offers its customers a variety of services such as Apple Music, iCloud, Apple Care, Apple TV+, Apple Arcade, Apple Card, and Apple Pay, among others. Apple's products run internally developed software and semiconductors, and the firm is well known for its integration of hardware, software and services. Apple's products are distributed online as well as through company-owned stores and third-party retailers. The company generates roughly 40% of its revenue from the Americas, with the remainder earned internationally.",
            "homepage_url": "https://www.apple.com",
            "list_date": "1980-12-12",
            "locale": "us",
            "market": "stocks",
            "market_cap": 2771126040150,
            "name": "Apple Inc.",
            "phone_number": "(408) 996-1010",
            "primary_exchange": "XNAS",
            "round_lot": 100,
            "share_class_figi": "BBG001S5N8V8",
            "share_class_shares_outstanding": 16406400000,
            "sic_code": "3571",
            "sic_description": "ELECTRONIC COMPUTERS",
            "ticker": "AAPL",
            "ticker_root": "AAPL",
            "total_employees": 154000,
            "type": "CS",
            "weighted_shares_outstanding": 16334371000
          },
          "status": "OK"
        }
        """;

    /// <summary>
    /// GET /v3/reference/tickers/types?asset_class=stocks&amp;locale=us, captured from the live
    /// service on 2026-09-03 because the description publishes only a CSV example for it
    /// (D-R12). Reviewed: it carries no account identifier and no URL embeds a key.
    /// </summary>
    public const string ReferenceTickerTypes = """
        {
          "count": 24,
          "request_id": "b226ee899a65f4be25300c7af02eed7d",
          "results": [
            { "asset_class": "stocks", "code": "CS", "description": "Common Stock", "locale": "us" },
            { "asset_class": "stocks", "code": "PFD", "description": "Preferred Stock", "locale": "us" },
            { "asset_class": "stocks", "code": "WARRANT", "description": "Warrant", "locale": "us" },
            { "asset_class": "stocks", "code": "RIGHT", "description": "Rights", "locale": "us" },
            { "asset_class": "stocks", "code": "BOND", "description": "Corporate Bond", "locale": "us" },
            { "asset_class": "stocks", "code": "ETF", "description": "Exchange Traded Fund", "locale": "us" },
            { "asset_class": "stocks", "code": "ETN", "description": "Exchange Traded Note", "locale": "us" },
            { "asset_class": "stocks", "code": "ETV", "description": "Exchange Traded Vehicle", "locale": "us" },
            { "asset_class": "stocks", "code": "SP", "description": "Structured Product", "locale": "us" },
            { "asset_class": "stocks", "code": "ADRC", "description": "American Depository Receipt Common", "locale": "us" },
            { "asset_class": "stocks", "code": "ADRP", "description": "American Depository Receipt Preferred", "locale": "us" },
            { "asset_class": "stocks", "code": "ADRW", "description": "American Depository Receipt Warrants", "locale": "us" },
            { "asset_class": "stocks", "code": "ADRR", "description": "American Depository Receipt Rights", "locale": "us" },
            { "asset_class": "stocks", "code": "FUND", "description": "Fund", "locale": "us" },
            { "asset_class": "stocks", "code": "BASKET", "description": "Basket", "locale": "us" },
            { "asset_class": "stocks", "code": "UNIT", "description": "Unit", "locale": "us" },
            { "asset_class": "stocks", "code": "LT", "description": "Liquidating Trust", "locale": "us" },
            { "asset_class": "stocks", "code": "OS", "description": "Ordinary Shares", "locale": "us" },
            { "asset_class": "stocks", "code": "GDR", "description": "Global Depository Receipts", "locale": "us" },
            { "asset_class": "stocks", "code": "OTHER", "description": "Other Security Type", "locale": "us" },
            { "asset_class": "stocks", "code": "NYRS", "description": "New York Registry Shares", "locale": "us" },
            { "asset_class": "stocks", "code": "AGEN", "description": "Agency Bond", "locale": "us" },
            { "asset_class": "stocks", "code": "EQLK", "description": "Equity Linked Bond", "locale": "us" },
            { "asset_class": "stocks", "code": "ETS", "description": "Single-security ETF", "locale": "us" }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// The documented sample for GET /vX/reference/tickers/{id}/events. Each event spells its
    /// discriminator <c>type</c>, where the schema declares a required <c>event_type</c>; the
    /// live wire agrees with the sample, so the map types that property nullable and the SDK
    /// reads it as absent (D-R10). The fixture is the sample verbatim.
    /// </summary>
    public const string ReferenceTickerEvents = """
        {
          "request_id": "31d59dda-80e5-4721-8496-d0d32a654afe",
          "results": {
            "events": [
              {
                "date": "2022-06-09",
                "ticker_change": {
                  "ticker": "META"
                },
                "type": "ticker_change"
              },
              {
                "date": "2012-05-18",
                "ticker_change": {
                  "ticker": "FB"
                },
                "type": "ticker_change"
              }
            ],
            "name": "Meta Platforms, Inc. Class A Common Stock"
          },
          "status": "OK"
        }
        """;

    /// <summary>
    /// The documented sample for GET /v1/related-companies/{ticker}. The envelope's
    /// <c>stock_symbol</c> is not in the schema, which declares <c>ticker</c> there instead, and
    /// is ignored.
    /// </summary>
    public const string ReferenceRelatedCompanies = """
        {
          "request_id": "31d59dda-80e5-4721-8496-d0d32a654afe",
          "results": [
            { "ticker": "MSFT" },
            { "ticker": "GOOGL" },
            { "ticker": "AMZN" },
            { "ticker": "FB" },
            { "ticker": "TSLA" },
            { "ticker": "NVDA" },
            { "ticker": "INTC" },
            { "ticker": "ADBE" },
            { "ticker": "NFLX" },
            { "ticker": "PYPL" }
          ],
          "status": "OK",
          "stock_symbol": "AAPL"
        }
        """;

    /// <summary>
    /// The documented sample for GET /v1/marketstatus/now: the body is the payload, with no
    /// envelope (decision D17). The sample omits <c>indicesGroups</c>, which the live service
    /// sends; the model leaves it null here.
    /// </summary>
    public const string ReferenceMarketStatus = """
        {
          "afterHours": true,
          "currencies": {
            "crypto": "open",
            "fx": "open"
          },
          "earlyHours": false,
          "exchanges": {
            "nasdaq": "extended-hours",
            "nyse": "extended-hours",
            "otc": "closed"
          },
          "market": "extended-hours",
          "serverTime": "2020-11-10T17:37:37-05:00"
        }
        """;

    /// <summary>The documented sample for GET /v3/reference/conditions.</summary>
    public const string ReferenceConditions = """
        {
          "count": 1,
          "request_id": "31d59dda-80e5-4721-8496-d0d32a654afe",
          "results": [
            {
              "asset_class": "stocks",
              "data_types": [
                "trade"
              ],
              "id": 2,
              "name": "Average Price Trade",
              "sip_mapping": {
                "CTA": "B",
                "UTP": "W"
              },
              "type": "condition",
              "update_rules": {
                "consolidated": {
                  "updates_high_low": false,
                  "updates_open_close": false,
                  "updates_volume": true
                },
                "market_center": {
                  "updates_high_low": false,
                  "updates_open_close": false,
                  "updates_volume": true
                }
              }
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// GET /v3/reference/exchanges?asset_class=stocks&amp;locale=us, captured from the live
    /// service on 2026-09-03 because the description publishes only a CSV example for it
    /// (D-R12). Reviewed: it carries no account identifier and no URL embeds a key.
    /// </summary>
    public const string ReferenceExchanges = """
        {
          "count": 27,
          "request_id": "33edc6f450e0bb88f54bb3e1329f10e7",
          "results": [
            { "acronym": "AMEX", "asset_class": "stocks", "id": 1, "locale": "us", "mic": "XASE", "name": "NYSE American, LLC", "operating_mic": "XNYS", "participant_id": "A", "type": "exchange", "url": "https://www.nyse.com/markets/nyse-american" },
            { "asset_class": "stocks", "id": 2, "locale": "us", "mic": "XBOS", "name": "Nasdaq Texas, Inc.", "operating_mic": "XNAS", "participant_id": "B", "type": "exchange", "url": "https://www.nasdaq.com/solutions/nasdaq-bx-stock-market" },
            { "acronym": "NSX", "asset_class": "stocks", "id": 3, "locale": "us", "mic": "XCIS", "name": "NYSE National, Inc.", "operating_mic": "XNYS", "participant_id": "C", "type": "exchange", "url": "https://www.nyse.com/markets/nyse-national" },
            { "asset_class": "stocks", "id": 4, "locale": "us", "mic": "XADF", "name": "FINRA Alternative Display Facility", "operating_mic": "FINR", "participant_id": "D", "type": "TRF", "url": "https://www.finra.org" },
            { "asset_class": "stocks", "id": 5, "locale": "us", "name": "Unlisted Trading Privileges", "operating_mic": "XNAS", "participant_id": "E", "type": "SIP", "url": "https://www.utpplan.com" },
            { "asset_class": "stocks", "id": 6, "locale": "us", "mic": "XISE", "name": "International Securities Exchange, LLC - Stocks", "operating_mic": "XNAS", "participant_id": "I", "type": "TRF", "url": "https://nasdaq.com/solutions/nasdaq-ise" },
            { "asset_class": "stocks", "id": 7, "locale": "us", "mic": "EDGA", "name": "Cboe EDGA", "operating_mic": "XCBO", "participant_id": "J", "type": "exchange", "url": "https://www.cboe.com/us/equities" },
            { "asset_class": "stocks", "id": 8, "locale": "us", "mic": "EDGX", "name": "Cboe EDGX", "operating_mic": "XCBO", "participant_id": "K", "type": "exchange", "url": "https://www.cboe.com/us/equities" },
            { "asset_class": "stocks", "id": 9, "locale": "us", "mic": "XCHI", "name": "NYSE Texas, Inc.", "operating_mic": "XNYS", "participant_id": "M", "type": "exchange", "url": "https://www.nyse.com/markets/nyse-texas" },
            { "asset_class": "stocks", "id": 10, "locale": "us", "mic": "XNYS", "name": "New York Stock Exchange", "operating_mic": "XNYS", "participant_id": "N", "type": "exchange", "url": "https://www.nyse.com" },
            { "asset_class": "stocks", "id": 11, "locale": "us", "mic": "ARCX", "name": "NYSE Arca, Inc.", "operating_mic": "XNYS", "participant_id": "P", "type": "exchange", "url": "https://www.nyse.com/markets/nyse-arca" },
            { "asset_class": "stocks", "id": 12, "locale": "us", "mic": "XNAS", "name": "Nasdaq", "operating_mic": "XNAS", "participant_id": "T", "type": "exchange", "url": "https://www.nasdaq.com" },
            { "asset_class": "stocks", "id": 13, "locale": "us", "name": "Consolidated Tape Association", "operating_mic": "XNYS", "participant_id": "S", "type": "SIP", "url": "https://www.nyse.com/data/cta" },
            { "asset_class": "stocks", "id": 14, "locale": "us", "mic": "LTSE", "name": "Long-Term Stock Exchange", "operating_mic": "LTSE", "participant_id": "L", "type": "exchange", "url": "https://www.ltse.com" },
            { "asset_class": "stocks", "id": 15, "locale": "us", "mic": "IEXG", "name": "Investors Exchange", "operating_mic": "IEXG", "participant_id": "V", "type": "exchange", "url": "https://www.iextrading.com" },
            { "asset_class": "stocks", "id": 16, "locale": "us", "mic": "CBSX", "name": "Cboe Stock Exchange", "operating_mic": "XCBO", "participant_id": "W", "type": "TRF", "url": "https://www.cboe.com" },
            { "asset_class": "stocks", "id": 17, "locale": "us", "mic": "XPHL", "name": "Nasdaq Philadelphia Exchange LLC", "operating_mic": "XNAS", "participant_id": "X", "type": "exchange", "url": "https://www.nasdaq.com/solutions/nasdaq-phlx" },
            { "asset_class": "stocks", "id": 18, "locale": "us", "mic": "BATY", "name": "Cboe BYX", "operating_mic": "XCBO", "participant_id": "Y", "type": "exchange", "url": "https://www.cboe.com/us/equities" },
            { "asset_class": "stocks", "id": 19, "locale": "us", "mic": "BATS", "name": "Cboe BZX", "operating_mic": "XCBO", "participant_id": "Z", "type": "exchange", "url": "https://www.cboe.com/us/equities" },
            { "asset_class": "stocks", "id": 20, "locale": "us", "mic": "EPRL", "name": "MIAX Pearl", "operating_mic": "MIHI", "participant_id": "H", "type": "exchange", "url": "https://www.miaxoptions.com/alerts/pearl-equities" },
            { "asset_class": "stocks", "id": 21, "locale": "us", "mic": "MEMX", "name": "Members Exchange", "operating_mic": "MEMX", "participant_id": "U", "type": "exchange", "url": "https://www.memx.com" },
            { "acronym": "24X", "asset_class": "stocks", "id": 22, "locale": "us", "mic": "24EQ", "name": "24X National Exchange LLC", "operating_mic": "24EQ", "participant_id": "G", "type": "exchange", "url": "https://24exchange.com/" },
            { "acronym": "TXSE", "asset_class": "stocks", "id": 23, "locale": "us", "mic": "TXSE", "name": "Texas Stock Exchange LLC", "operating_mic": "TXSE", "participant_id": "F", "type": "exchange", "url": "https://txse.com/" },
            { "asset_class": "stocks", "id": 62, "locale": "us", "mic": "OOTC", "name": "OTC Equity Security", "operating_mic": "FINR", "type": "ORF", "url": "https://www.finra.org/filing-reporting/over-the-counter-reporting-facility-orf" },
            { "asset_class": "stocks", "id": 201, "locale": "us", "mic": "FINY", "name": "FINRA NYSE TRF", "operating_mic": "FINR", "type": "TRF", "url": "https://www.finra.org" },
            { "asset_class": "stocks", "id": 202, "locale": "us", "mic": "FINN", "name": "FINRA Nasdaq TRF Carteret", "operating_mic": "FINR", "type": "TRF", "url": "https://www.finra.org" },
            { "asset_class": "stocks", "id": 203, "locale": "us", "mic": "FINC", "name": "FINRA Nasdaq TRF Chicago", "operating_mic": "FINR", "type": "TRF", "url": "https://www.finra.org" }
          ],
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /v3/reference/dividends. It carries a cursor of its own.</summary>
    public const string ReferenceDividends = """
        {
          "next_url": "https://api.massive.com/v3/reference/dividends/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "6a7e466379af0a71039d60cc78e72282",
          "results": [
            {
              "cash_amount": 0.22,
              "declaration_date": "2021-10-28",
              "dividend_type": "CD",
              "ex_dividend_date": "2021-11-05",
              "frequency": 4,
              "id": "E8e3c4f794613e9205e2f178a36c53fcc57cdabb55e1988c87b33f9e52e221444",
              "pay_date": "2021-11-11",
              "record_date": "2021-11-08",
              "ticker": "AAPL"
            },
            {
              "cash_amount": 0.22,
              "declaration_date": "2021-07-27",
              "dividend_type": "CD",
              "ex_dividend_date": "2021-08-06",
              "frequency": 4,
              "id": "E6436c5475706773f03490acf0b63fdb90b2c72bfeed329a6eb4afc080acd80ae",
              "pay_date": "2021-08-12",
              "record_date": "2021-08-09",
              "ticker": "AAPL"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /v3/reference/splits. It carries a cursor of its own.</summary>
    public const string ReferenceSplits = """
        {
          "next_url": "https://api.massive.com/v3/splits/AAPL?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "6a7e466379af0a71039d60cc78e72282",
          "results": [
            {
              "execution_date": "2020-08-31",
              "id": "E36416cce743c3964c5da63e1ef1626c0aece30fb47302eea5a49c0055c04e8d0",
              "split_from": 1,
              "split_to": 4,
              "ticker": "AAPL"
            },
            {
              "execution_date": "2005-02-28",
              "id": "E90a77bdf742661741ed7c8fc086415f0457c2816c45899d73aaa88bdc8ff6025",
              "split_from": 1,
              "split_to": 2,
              "ticker": "AAPL"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /v3/reference/options/contracts.</summary>
    public const string ReferenceOptionsContracts = """
        {
          "request_id": "603902c0-a5a5-406f-bd08-f030f92418fa",
          "results": [
            {
              "cfi": "OCASPS",
              "contract_type": "call",
              "exercise_style": "american",
              "expiration_date": "2021-11-19",
              "primary_exchange": "BATO",
              "shares_per_contract": 100,
              "strike_price": 85,
              "ticker": "O:AAPL211119C00085000",
              "underlying_ticker": "AAPL"
            },
            {
              "additional_underlyings": [
                {
                  "amount": 44,
                  "type": "equity",
                  "underlying": "VMW"
                },
                {
                  "amount": 6.53,
                  "type": "currency",
                  "underlying": "USD"
                }
              ],
              "cfi": "OCASPS",
              "contract_type": "call",
              "exercise_style": "american",
              "expiration_date": "2021-11-19",
              "primary_exchange": "BATO",
              "shares_per_contract": 100,
              "strike_price": 90,
              "ticker": "O:AAPL211119C00090000",
              "underlying_ticker": "AAPL"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>The documented sample for GET /v3/reference/options/contracts/{options_ticker}.</summary>
    public const string ReferenceOptionsContract = """
        {
          "request_id": "603902c0-a5a5-406f-bd08-f030f92418fa",
          "results": {
            "additional_underlyings": [
              {
                "amount": 44,
                "type": "equity",
                "underlying": "VMW"
              },
              {
                "amount": 6.53,
                "type": "currency",
                "underlying": "USD"
              }
            ],
            "cfi": "OCASPS",
            "contract_type": "call",
            "exercise_style": "american",
            "expiration_date": "2021-11-19",
            "primary_exchange": "BATO",
            "shares_per_contract": 100,
            "strike_price": 85,
            "ticker": "O:AAPL211119C00085000",
            "underlying_ticker": "AAPL"
          },
          "status": "OK"
        }
        """;

    /// <summary>
    /// The documented sample for GET /vX/reference/ipos. Its <c>issue_start_date</c> and
    /// <c>issue_end_date</c> are not in the schema and are ignored.
    /// </summary>
    public const string ReferenceIpos = """
        {
          "next_url": "https://api.massive.com/vX/reference/ipos?cursor=YWN0aXZlPXRydWUmZGF0ZT0yMDIxLTA0LTI1JmxpbWl0PTEmb3JkZXI9YXNjJnBhZ2VfbWFya2VyPUElN0M5YWRjMjY0ZTgyM2E1ZjBiOGUyNDc5YmZiOGE1YmYwNDVkYzU0YjgwMDcyMWE2YmI1ZjBjMjQwMjU4MjFmNGZiJnNvcnQ9dGlja2Vy",
          "request_id": "6a7e466379af0a71039d60cc78e72282",
          "results": [
            {
              "announced_date": "2024-06-01",
              "currency_code": "USD",
              "final_issue_price": 17,
              "highest_offer_price": 17,
              "ipo_status": "history",
              "isin": "US75383L1026",
              "issue_end_date": "2024-06-06",
              "issue_start_date": "2024-06-01",
              "issuer_name": "Rapport Therapeutics Inc.",
              "last_updated": "2024-06-27",
              "listing_date": "2024-06-07",
              "lot_size": 100,
              "lowest_offer_price": 17,
              "max_shares_offered": 8000000,
              "min_shares_offered": 1000000,
              "primary_exchange": "XNAS",
              "security_description": "Ordinary Shares",
              "security_type": "CS",
              "shares_outstanding": 35376457,
              "ticker": "RAPP",
              "total_offer_size": 136000000,
              "us_code": "75383L102"
            }
          ],
          "status": "OK"
        }
        """;

    /// <summary>
    /// The documented sample for GET /v1/reference/ipos, with three departures from the
    /// published text: the schema types <c>announced_date</c>, <c>last_updated</c>, and
    /// <c>listing_date</c> as 64-bit integers, while the sample shows the same calendar dates as
    /// the <c>vX</c> sample. The model follows the schema, so the fixture carries each date as
    /// the Unix nanosecond count of its midnight UTC, the unit the operation's own
    /// <c>listing_date</c> filter documents (D-R10). The route answered a plain-text 404 on
    /// 2026-09-03, so the wire cannot settle this; the live pin flips when it can.
    /// </summary>
    public const string ReferenceIposV1 = """
        {
          "request_id": "6a7e466379af0a71039d60cc78e72282",
          "results": [
            {
              "announced_date": 1717200000000000000,
              "currency_code": "USD",
              "final_issue_price": 17,
              "highest_offer_price": 17,
              "ipo_status": "history",
              "isin": "US75383L1026",
              "issuer_name": "Rapport Therapeutics Inc.",
              "last_updated": 1719446400000000000,
              "listing_date": 1717718400000000000,
              "lot_size": 100,
              "lowest_offer_price": 17,
              "max_shares_offered": 8000000,
              "min_shares_offered": 1000000,
              "primary_exchange": "XNAS",
              "security_description": "Ordinary Shares",
              "security_type": "CS",
              "shares_outstanding": 35376457,
              "ticker": "RAPP",
              "total_offer_size": 136000000,
              "us_code": "75383L102"
            }
          ],
          "status": "OK"
        }
        """;
}
