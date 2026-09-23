# MassiveDotNet.WebSocket

WebSocket streaming client for the **Massive** market data platform (formerly Polygon.io), for
.NET 10.

```bash
dotnet add package MassiveDotNet.WebSocket
```

## Quick start

```csharp
using MassiveDotNet.WebSocket;
using MassiveDotNet.WebSocket.Events;

await using MassiveStreamClient client = new(new MassiveStreamOptions { ApiKey = apiKey });
await using MassiveStockStream stream = await client.ConnectStocksAsync();

MassiveTopicSubscription<StockTrade> trades = await stream.SubscribeTradesAsync(["AAPL"]);

await foreach (StockTrade trade in trades)
{
    Console.WriteLine($"{trade.Ticker}  {trade.Price:N2} x {trade.Size}  at {trade.SipTimestamp}");
}
```

Six stock topics, each returning a `MassiveTopicSubscription<T>` over its own event type:
`SubscribeTradesAsync`, `SubscribeQuotesAsync`, `SubscribeSecondAggregatesAsync`,
`SubscribeMinuteAggregatesAsync`, `SubscribeImbalancesAsync`, `SubscribeLimitUpLimitDownAsync`.

## Three things worth knowing

**Topics are a typed enum, not a string.** The server silently drops a topic code it does not
recognise — no acknowledgement, no error — so a caller who mistyped a string would see a healthy
connection producing nothing, indefinitely. Every subscribe is acknowledgement-counted for the same
reason and throws `MassiveStreamSubscriptionException` when the server accepted fewer pairs than
were asked for. When the server refuses out loud, the exception carries what it actually said.

**A slow consumer drops the oldest event, and never blocks.** Each topic owns one bounded buffer
(1024 events by default). Every topic shares one socket, so a writer that waited would stall the
read loop and cost you the topics that were keeping up. Drops are counted exactly on
`DroppedCount` and raised as an event; the DI package bridges that count to `ILogger`.

**A malformed field costs one event, not the connection.** The wire occasionally sends a value the
SDK's schema won't parse — a `z` value `Utf8JsonReader` refuses, say. That event is dropped and counted
rather than thrown out of the read loop; because the connection is multiplexed, one bad field on
one symbol used to take every topic sharing the socket down with it, this package included. It's
counted exactly on `MalformedCount` and raised as `MalformedObserved`, carrying the `JsonException`
so you can see what the wire actually sent; the DI package bridges that to `ILogger` too.

Reconnect is automatic, with backoff, and replays every subscription. If the replay's
acknowledgements fall short, `SubscriptionsLost` names the pairs that went unanswered rather than
leaving you with a connection that looks healthy.

**An eviction says so.** Massive closes a connection to make room for another one on the same key,
and it announces that first — the only disconnect cause that does. Without it, the abort reads
exactly like a slow consumer or a network drop, and you go looking at your own throughput when the
real problem is a second process. `EvictionCount` and `LastEvictionMessage` carry the server's own
words; read them from a `Reconnected` handler, since reconnect is on by default and an eviction
normally ends in one. With reconnect off, `Faulted` carries a `MassiveStreamEvictedException`
instead. The SDK reports and keeps reconnecting — what to do about the other process is your call.

Native AOT clean, hand-written event converters reading straight off `Utf8JsonReader`, and parsing
a frame allocates nothing beyond the event itself.

Licensed MIT. Issues and source at
[github.com/jerbersoft/massivedotnet](https://github.com/jerbersoft/massivedotnet).
