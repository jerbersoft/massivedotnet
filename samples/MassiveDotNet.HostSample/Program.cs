using MassiveDotNet;
using MassiveDotNet.Rest;
using MassiveDotNet.Rest.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// A Generic Host console app: the shape a scheduled job or a worker service takes. The web sample
// beside this one registers the SDK identically — AddMassive returns singletons, which resolve the
// same way from a request scope as they do from the root provider.

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Configuration is read explicitly rather than bound from a section. The binder cannot convert
// MassiveClientOptions.Timeout, a NodaTime Duration, and drops it without a word (D27); the
// indexer below uses no reflection, so it also stays Native AOT clean.
string? apiKey = builder.Configuration["Massive:ApiKey"]
    ?? Environment.GetEnvironmentVariable("MASSIVE_API_KEY");

if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set MASSIVE_API_KEY, or Massive:ApiKey in configuration, and run again.");
    return 1;
}

builder.Services.AddMassive(options =>
{
    options.ApiKey = apiKey;
    options.UserAgent = "massivedotnet-hostsample/1.0";
});

using IHost host = builder.Build();

// Resolved from the root provider here; in a worker service it arrives through a constructor.
MassiveRestClient client = host.Services.GetRequiredService<MassiveRestClient>();

MassivePage<TickerSummary> page = await client.Reference.ListTickersAsync(
    market: MarketType.Stocks,
    active: true,
    limit: 5);

Console.WriteLine($"{page.Results.Length} tickers, more available: {page.HasMore}");

foreach (TickerSummary ticker in page.Results)
{
    Console.WriteLine($"  {ticker.Ticker,-8} {ticker.Name}");
}

return 0;
