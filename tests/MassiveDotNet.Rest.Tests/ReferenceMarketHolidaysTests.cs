using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The first body-array result: a bare JSON array with no envelope, deserialized as an array of
/// the model and coalesced to empty when the body is null (D-S5).
/// </summary>
public sealed class ReferenceMarketHolidaysTests
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
        StubHandler handler = new(Fixtures.MarketHolidays);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListMarketHolidaysAsync(Ct);
        }

        Assert.Equal("https://api.massive.com/v1/marketstatus/upcoming", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSample()
    {
        StubHandler handler = new(Fixtures.MarketHolidays);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MarketHoliday[] holidays;

        using (client)
        using (transport)
        {
            holidays = await client.Reference.ListMarketHolidaysAsync(Ct);
        }

        Assert.Equal(5, holidays.Length);

        Assert.Equal(new LocalDate(2020, 11, 26), holidays[0].Date);
        Assert.Equal("NYSE", holidays[0].Exchange);
        Assert.Equal("Thanksgiving", holidays[0].Name);
        Assert.Equal("closed", holidays[0].Status);
        Assert.Null(holidays[0].Open);
        Assert.Null(holidays[0].Close);

        Assert.Equal("early-close", holidays[3].Status);
        Assert.Equal("NASDAQ", holidays[3].Exchange);
        Assert.Equal(Instant.FromUtc(2020, 11, 27, 14, 30), holidays[3].Open);
        Assert.Equal(Instant.FromUtc(2020, 11, 27, 18, 0), holidays[3].Close);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task AnEmptyOrNullBodyYieldsAnEmptyArray(string body)
    {
        StubHandler handler = new(body);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MarketHoliday[] holidays;

        using (client)
        using (transport)
        {
            holidays = await client.Reference.ListMarketHolidaysAsync(Ct);
        }

        Assert.Empty(holidays);
    }
}
