# MassiveDotNet.Extensions.DependencyInjection

Dependency injection and `IHttpClientFactory` wiring for the
[MassiveDotNet](https://github.com/jerbersoft/massivedotnet) SDK — ASP.NET Core, the Generic Host,
or anything with an `IServiceCollection`.

```bash
dotnet add package MassiveDotNet.Extensions.DependencyInjection
```

## Quick start

```csharp
builder.Services.AddMassive(options =>
{
    options.ApiKey = builder.Configuration["Massive:ApiKey"];
    options.UserAgent = "my-app/1.0";
});
```

`MassiveRestClient` then arrives by injection like any other service.

`AddMassive` returns the `IHttpClientBuilder` for the underlying named client, so resilience,
logging or any other handler goes on the same pipeline:

```csharp
builder.Services.AddMassive(apiKey)
       .AddStandardResilienceHandler();   // Microsoft.Extensions.Http.Resilience
```

## Two deliberate choices

**Singletons, not the typed-client pattern.** The transport and the client register as singletons
over one `HttpClient` resolved from the factory, rather than through `AddHttpClient<T>`. A typed
client registers *transient*, and `MassiveRestClient` is `IDisposable` — so the container would
track one undisposed instance per resolution for the process lifetime, a leak that grows with
traffic, and it would contradict the type's own documented contract that it is long-lived and
shared. The primary handler carries `PooledConnectionLifetime`, which is what actually keeps DNS
fresh.

**No `IConfiguration` overload.** Read the values yourself:
`options.ApiKey = configuration["Massive:ApiKey"]`. `MassiveClientOptions.Timeout` is a NodaTime
`Duration`, which the configuration binder cannot convert — and it fails *silently*, binding clean
and leaving the default in place, which is the worst kind of wrong. One line at the call site costs
less than a setting that quietly does nothing.

This is the only package in the SDK permitted to reference `Microsoft.Extensions.*`, and CI asserts
that, so a REST-only consumer never pulls the DI stack.

Licensed MIT. Issues and source at
[github.com/jerbersoft/massivedotnet](https://github.com/jerbersoft/massivedotnet).
