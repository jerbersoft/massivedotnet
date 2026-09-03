using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Ticker events: the one response whose items sit behind a one-branch <c>oneOf</c> (D-R2), and
/// the one whose discriminator the description misnames (D-R10). Experimental, so the test
/// project suppresses MASSIVE0001 in its project file.
/// </summary>
public sealed class ReferenceTickerEventsTests
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
        StubHandler handler = new(Fixtures.ReferenceTickerEvents);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.GetTickerEventsAsync("META", types: "ticker_change", cancellationToken: Ct);
        }

        Assert.Equal("https://api.massive.com/vX/reference/tickers/META/events?types=ticker_change", handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task DeserializesThePublishedSampleWithTheDiscriminatorAbsent()
    {
        StubHandler handler = new(Fixtures.ReferenceTickerEvents);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        TickerEvents events;

        using (client)
        using (transport)
        {
            events = await client.Reference.GetTickerEventsAsync("META", cancellationToken: Ct);
        }

        Assert.Equal("Meta Platforms, Inc. Class A Common Stock", events.Name);
        Assert.NotNull(events.Events);
        Assert.Equal(2, events.Events.Length);

        TickerEvent latest = events.Events[0];
        Assert.Equal(new LocalDate(2022, 6, 9), latest.Date);
        Assert.NotNull(latest.TickerChange);
        Assert.Equal("META", latest.TickerChange.Ticker);

        // The wire spells the discriminator "type"; the schema says "event_type". The model
        // follows the schema, so the property reads as absent (D-R10).
        Assert.Null(latest.EventType);

        Assert.Equal("FB", events.Events[1].TickerChange?.Ticker);
    }
}
