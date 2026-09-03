using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Options contracts against the real service. The get takes its ticker from the list (D-R13):
/// a hard-coded contract expires, and an expired one answers 404.
/// </summary>
public sealed class ReferenceOptionsContractsLiveTests : LiveApiTest
{
    [Fact]
    public async Task ListThenGetRoundTrips()
    {
        MassivePage<OptionsContract> page = await Client.Reference.ListOptionsContractsAsync(
            underlyingTicker: "AAPL",
            contractType: ContractType.Call,
            expired: false,
            limit: 1,
            cancellationToken: Ct);

        OptionsContract listed = Assert.Single(page.Results);
        Assert.NotNull(listed.Ticker);
        Assert.Equal("call", listed.ContractType);
        Assert.Equal("AAPL", listed.UnderlyingTicker);

        OptionsContract fetched = await Client.Reference.GetOptionsContractAsync(listed.Ticker, cancellationToken: Ct);

        Assert.Equal(listed.Ticker, fetched.Ticker);
        Assert.Equal(listed.StrikePrice, fetched.StrikePrice);
        Assert.Equal(listed.ExpirationDate, fetched.ExpirationDate);
    }
}
