using System.Text.Json.Serialization;
using MassiveDotNet;
using MassiveDotNet.Rest;
using MassiveDotNet.Rest.Models;
using Microsoft.AspNetCore.Diagnostics;

// A minimal API registering the SDK exactly as the console sample does. Nothing about AddMassive
// changes in a web host: it registers singletons, which resolve from a request scope unchanged.
//
// CreateSlimBuilder and a source-generated serializer context are what make the host itself
// publish Native AOT clean; the SDK carries no reflection either way (rule 3).

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, SampleJsonContext.Default));

// Read explicitly rather than binding a section: the binder cannot convert the NodaTime Duration
// on MassiveClientOptions.Timeout and drops it silently (D27).
builder.Services.AddMassive(options =>
{
    options.ApiKey = builder.Configuration["Massive:ApiKey"]
        ?? Environment.GetEnvironmentVariable("MASSIVE_API_KEY");
    options.UserAgent = "massivedotnet-websample/1.0";
});

WebApplication app = builder.Build();

// MassiveRestClient arrives by injection like any other service.
app.MapGet("/tickers", async (MassiveRestClient massive, CancellationToken cancellationToken) =>
{
    MassivePage<TickerSummary> page = await massive.Reference.ListTickersAsync(
        market: MarketType.Stocks,
        active: true,
        limit: 5,
        cancellationToken: cancellationToken);

    return page.Results.Select(static ticker => new TickerRow(ticker.Ticker, ticker.Name)).ToArray();
});

// A failed call surfaces as MassiveApiException carrying the status the service returned, so an
// exception handler can pass it straight through rather than flattening everything to a 500.
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    if (context.Features.Get<IExceptionHandlerFeature>()?.Error is MassiveApiException massive)
    {
        context.Response.StatusCode = (int)massive.StatusCode;
        await context.Response.WriteAsJsonAsync(new ErrorRow(massive.Message), SampleJsonContext.Default.ErrorRow);
    }
}));

app.Run();

internal sealed record TickerRow(string Ticker, string Name);

internal sealed record ErrorRow(string Message);

[JsonSerializable(typeof(TickerRow[]))]
[JsonSerializable(typeof(ErrorRow))]
internal sealed partial class SampleJsonContext : JsonSerializerContext;
