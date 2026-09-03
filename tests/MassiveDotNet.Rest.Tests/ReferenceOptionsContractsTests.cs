using MassiveDotNet.Http;
using MassiveDotNet.Rest.Models;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Options contracts: the first <see cref="ContractType"/> parameter (D-R6), calendar-date and
/// strike ranges, a colon in a path segment, and one model reused between the list and the get
/// (decision D16).
/// </summary>
public sealed class ReferenceOptionsContractsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MassiveRestClient Client, MassiveHttpTransport Transport) Create(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler) { BaseAddress = MassiveEndpoints.Production };
        MassiveHttpTransport transport = new(httpClient);
        return (new MassiveRestClient(transport), transport);
    }

    [Fact]
    public async Task RendersEveryParameterInDeclarationOrder()
    {
        // AbsoluteUri rather than ToString(): the deprecated ticker parameter carries a colon,
        // which is percent-escaped on the way out, and AbsoluteUri reports it as sent.
        StubHandler handler = new(Fixtures.ReferenceOptionsContracts);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.ListOptionsContractsAsync(
                underlyingTicker: "AAPL",
                ticker: "O:AAPL211119C00085000",
                contractType: ContractType.Call,
                expirationDate: RangeFilter.Between(new LocalDate(2021, 11, 1), new LocalDate(2021, 11, 30)),
                asOf: new LocalDate(2021, 11, 1),
                strikePrice: RangeFilter.Lte(90d),
                expired: true,
                order: SortOrder.Ascending,
                limit: 2,
                sort: "strike_price",
                cancellationToken: Ct);
        }

        Assert.Equal(
            "https://api.massive.com/v3/reference/options/contracts"
                + "?underlying_ticker=AAPL&ticker=O%3AAAPL211119C00085000&contract_type=call"
                + "&expiration_date.gte=2021-11-01&expiration_date.lte=2021-11-30"
                + "&as_of=2021-11-01&strike_price.lte=90&expired=true&order=asc&limit=2&sort=strike_price",
            handler.LastRequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task DeserializesTheListSampleThroughTheAdditionalUnderlyings()
    {
        StubHandler handler = new(Fixtures.ReferenceOptionsContracts);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        MassivePage<OptionsContract> page;

        using (client)
        using (transport)
        {
            page = await client.Reference.ListOptionsContractsAsync(underlyingTicker: "AAPL", cancellationToken: Ct);
        }

        Assert.Equal(2, page.Results.Length);

        OptionsContract plain = page.Results[0];
        Assert.Equal("O:AAPL211119C00085000", plain.Ticker);
        Assert.Equal("AAPL", plain.UnderlyingTicker);
        Assert.Equal("call", plain.ContractType);
        Assert.Equal("american", plain.ExerciseStyle);
        Assert.Equal(new LocalDate(2021, 11, 19), plain.ExpirationDate);
        Assert.Equal(85d, plain.StrikePrice);
        Assert.Equal(100d, plain.SharesPerContract);
        Assert.Equal("BATO", plain.PrimaryExchange);
        Assert.Equal("OCASPS", plain.Cfi);
        Assert.Null(plain.Correction);
        Assert.Null(plain.AdditionalUnderlyings);

        OptionsContract adjusted = page.Results[1];
        Assert.NotNull(adjusted.AdditionalUnderlyings);
        Assert.Equal(2, adjusted.AdditionalUnderlyings.Length);
        Assert.Equal("VMW", adjusted.AdditionalUnderlyings[0].Underlying);
        Assert.Equal("equity", adjusted.AdditionalUnderlyings[0].Type);
        Assert.Equal(44d, adjusted.AdditionalUnderlyings[0].Amount);
        Assert.Equal(6.53, adjusted.AdditionalUnderlyings[1].Amount);

        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task GetEscapesTheColonInThePathSegment()
    {
        StubHandler handler = new(Fixtures.ReferenceOptionsContract);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        using (client)
        using (transport)
        {
            await client.Reference.GetOptionsContractAsync("O:AAPL211119C00085000", asOf: new LocalDate(2021, 11, 1), cancellationToken: Ct);
        }

        // A caller-supplied path segment is percent-escaped; the colon stays escaped in the
        // absolute form because it is reserved in a path.
        Assert.Equal(
            "https://api.massive.com/v3/reference/options/contracts/O%3AAAPL211119C00085000?as_of=2021-11-01",
            handler.LastRequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task GetDeserializesTheSameModel()
    {
        StubHandler handler = new(Fixtures.ReferenceOptionsContract);
        (MassiveRestClient client, MassiveHttpTransport transport) = Create(handler);

        OptionsContract contract;

        using (client)
        using (transport)
        {
            contract = await client.Reference.GetOptionsContractAsync("O:AAPL211119C00085000", cancellationToken: Ct);
        }

        Assert.Equal("O:AAPL211119C00085000", contract.Ticker);
        Assert.Equal(85d, contract.StrikePrice);
        Assert.NotNull(contract.AdditionalUnderlyings);
        Assert.Equal("USD", contract.AdditionalUnderlyings[1].Underlying);
    }
}
