# MassiveDotNet

Core primitives for the [MassiveDotNet](https://github.com/jerbersoft/massivedotnet) SDK — a .NET 10
client for the **Massive** market data platform (formerly Polygon.io).

**You probably want [`MassiveDotNet.Rest`](https://www.nuget.org/packages/MassiveDotNet.Rest) or
[`MassiveDotNet.WebSocket`](https://www.nuget.org/packages/MassiveDotNet.WebSocket) instead.** This
package is what they share, and it is pulled in automatically.

## What is in here

- `MassiveClientOptions` — API key, base address, timeout, throttling and retry.
- The HTTP transport, the two authentication schemes, and the rate-limit and retry handlers.
- `MassiveApiException` and the error payload.
- Allocation-conscious serialization helpers: pooled URI building, pooled array converters, and the
  scalar readers the generated struct converters read tokens through.

## Design

- **Native AOT clean.** No reflection-based serialization anywhere; `System.Text.Json` source
  generation only. The package sets `IsAotCompatible` and `IsTrimmable`.
- **NodaTime for every date and time.** `Instant`, `LocalDate`, `Duration` and `ZonedDateTime`
  rather than `DateTime`, because a bar window, a tick timestamp and an ex-dividend date are three
  different things that `DateTime` collapses into one.
- **Two dependencies, deliberately**: NodaTime and `System.Threading.RateLimiting`. Nothing else.

Licensed MIT. Issues and source at
[github.com/jerbersoft/massivedotnet](https://github.com/jerbersoft/massivedotnet).
