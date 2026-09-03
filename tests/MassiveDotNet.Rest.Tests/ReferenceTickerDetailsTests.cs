using System.Net;
using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Ticker details: a singular result under <c>results</c> with two nested models, a bare-string
/// date bound to <see cref="LocalDate"/> from the map (D-R9), and the D17 failure on a 200 with
/// no payload.
/// </summary>
public sealed class ReferenceTickerDetailsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task BuildsTheDocumentedRequestPath()
    {
        StubHandler handler = new(Fixtures.ReferenceTickerDetails);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.GetTickerAsync("AAPL", date: new LocalDate(2024, 1, 16), cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/v3/reference/tickers/AAPL?date=2024-01-16", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSampleThroughBothNestedModels()
    {
        StubHandler handler = new(Fixtures.ReferenceTickerDetails);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        TickerDetails details;

        using (client)
        using (transport)
        {
            details = await client.Reference.GetTickerAsync("AAPL", cancellationToken: Ct);
        }

        Assert.Equal("AAPL", details.Ticker);
        Assert.Equal("Apple Inc.", details.Name);
        Assert.True(details.IsActive);
        Assert.Equal("usd", details.CurrencyName);
        Assert.Equal("stocks", details.Market);
        Assert.Equal("us", details.Locale);
        Assert.Equal("XNAS", details.PrimaryExchange);
        Assert.Equal("CS", details.Type);
        Assert.Equal(new LocalDate(1980, 12, 12), details.ListDate);
        Assert.Equal(2771126040150d, details.MarketCap);
        Assert.Equal(100d, details.RoundLot);
        Assert.Equal(16406400000d, details.ShareClassSharesOutstanding);
        Assert.Equal(16334371000d, details.WeightedSharesOutstanding);
        Assert.Equal(154000d, details.TotalEmployees);
        Assert.Equal("3571", details.SicCode);
        Assert.Equal("ELECTRONIC COMPUTERS", details.SicDescription);
        Assert.Equal("AAPL", details.TickerRoot);
        Assert.Null(details.TickerSuffix);
        Assert.Null(details.DelistedUtc);
        Assert.Equal("(408) 996-1010", details.PhoneNumber);
        Assert.Equal("https://www.apple.com", details.HomepageUrl);
        Assert.StartsWith("Apple designs", details.Description, StringComparison.Ordinal);

        Assert.NotNull(details.Address);
        Assert.Equal("One Apple Park Way", details.Address.Address1);
        Assert.Null(details.Address.Address2);
        Assert.Equal("Cupertino", details.Address.City);
        Assert.Equal("CA", details.Address.State);
        Assert.Equal("95014", details.Address.PostalCode);

        Assert.NotNull(details.Branding);
        Assert.EndsWith("2022-01-10_logo.svg", details.Branding.LogoUrl, StringComparison.Ordinal);
        Assert.EndsWith("2022-01-10_icon.png", details.Branding.IconUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASuccessWithoutAPayloadIsReported()
    {
        StubHandler handler = new(Fixtures.SingularWithoutResults);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(
                () => client.Reference.GetTickerAsync("AAPL", cancellationToken: Ct));

            Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
            Assert.Contains("carried no 'results' payload", exception.Message, StringComparison.Ordinal);
        }
    }
}
