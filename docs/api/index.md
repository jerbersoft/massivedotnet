# API reference

Every public member of all four packages, generated from the XML documentation comments in the source. The
same comments ship inside the packages, so what is here is what IntelliSense shows you at the call site.

`GenerateDocumentationFile` and `TreatWarningsAsErrors` are both on for every project under `src/`, so a
public member without a documentation comment does not compile in this repository. There is no exception and
no `#pragma warning disable CS1591` anywhere in the shipped source — including the 199 generated files, whose
comments are written by the generator from the OpenAPI description's own prose.

## Namespaces

| Namespace | Package | Contents |
|---|---|---|
| <xref:MassiveDotNet> | `MassiveDotNet` | <xref:MassiveDotNet.MassiveClientOptions>, the <xref:MassiveDotNet.MassiveApiException> family, the four filter types, <xref:MassiveDotNet.MassivePage`1> and <xref:MassiveDotNet.MassivePagedResult`1>, the two date-or-timestamp value types, and the shared enums |
| <xref:MassiveDotNet.Http> | `MassiveDotNet` | <xref:MassiveDotNet.Http.MassiveHttpTransport>, <xref:MassiveDotNet.Http.RequestUriBuilder>, and the three `DelegatingHandler`s — auth, retry and the rate limiter |
| <xref:MassiveDotNet.Serialization> | `MassiveDotNet` | The `JsonConverter<T>` implementations and the two token-level readers and writers the generated converters are built on |
| <xref:MassiveDotNet.Rest> | `MassiveDotNet.Rest` | <xref:MassiveDotNet.Rest.MassiveRestClient> and the two endpoint groups reached through it, <xref:MassiveDotNet.Rest.StocksGroup> and <xref:MassiveDotNet.Rest.ReferenceGroup> |
| <xref:MassiveDotNet.Rest.Models> | `MassiveDotNet.Rest` | The response models — what each endpoint returns |
| <xref:MassiveDotNet.WebSocket> | `MassiveDotNet.WebSocket` | <xref:MassiveDotNet.WebSocket.MassiveStreamClient>, <xref:MassiveDotNet.WebSocket.MassiveStockStream>, <xref:MassiveDotNet.WebSocket.StockTopic>, the options types, and the stream exception family |
| <xref:MassiveDotNet.WebSocket.Events> | `MassiveDotNet.WebSocket` | The five streaming event types and <xref:MassiveDotNet.WebSocket.Events.ConditionSet> |
| <xref:Microsoft.Extensions.DependencyInjection> | `MassiveDotNet.Extensions.DependencyInjection` | `AddMassive` and `AddMassiveStream`, as extension methods on `IServiceCollection` |

## Four things no single member's page can say

**The registration package has no namespace of its own.** Everything public in
`MassiveDotNet.Extensions.DependencyInjection` sits in `Microsoft.Extensions.DependencyInjection`, so
`AddMassive` is in scope wherever a container is being configured and no extra `using` is needed. It is also
the only project permitted to reference `Microsoft.Extensions.*` at all — a CI assertion on the restore graph
enforces that, which is why the core has no `AddMassive` and never will.

**Two attributes on a method are a statement about the platform, not the SDK.** A method marked
`[Obsolete(… DiagnosticId = "MASSIVE0002")]` is one Massive's own description flags as deprecated, and the
message names the .NET method that supersedes it. A method marked `[Experimental("MASSIVE0001")]` is on a
route Massive itself versions as `vX` or `dev` — an error until you opt in with
`<NoWarn>$(NoWarn);MASSIVE0001</NoWarn>`, deliberately, because several of those routes do not answer yet.
Both are read from the description rather than declared by hand, so neither can drift from what Massive says.
Nothing is ever silently omitted from this SDK: a deprecated operation ships, marked.

**Most list endpoints have two methods, and the difference is memory.** `ListXxxAsync` returns one page and
tells you whether more exist; `EnumerateXxxAsync` returns an `IAsyncEnumerable<T>` that walks every page with
one request in flight at a time and retains nothing proportional to the pages traversed. The split follows the
BCL's own `Directory.GetFiles` / `Directory.EnumerateFiles` distinction. Endpoints that do not paginate return
an array, or the value itself.

**The tick-level models are structs on purpose, and they are read by generated converters.**
<xref:MassiveDotNet.Rest.Models.Agg>, <xref:MassiveDotNet.Rest.Models.Trade> and
<xref:MassiveDotNet.Rest.Models.Quote> arrive fifty thousand at a time, so a 50,000-row response allocates one
array rather than 50,000 objects — and each one is read straight off the JSON tokens rather than through
`System.Text.Json`'s own object machinery, which costs about 456 bytes a row. Reference types such as
<xref:MassiveDotNet.Rest.Models.TickerDetails> and <xref:MassiveDotNet.Rest.Models.Dividend> stay classes,
where nullability is meaningful and rows arrive in tens.

The reasoning behind these — and the measured figures — is in the [README](../../README.md), rendered on this
site as Reference. [Allocation](../../README.md#allocation) is the one to read first.
