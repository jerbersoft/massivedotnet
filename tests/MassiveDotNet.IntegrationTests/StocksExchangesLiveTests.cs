using MassiveDotNet.Rest.Models;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>One call for exchanges (D-G8): the list is short, and NYSE is in it.</summary>
public sealed class StocksExchangesLiveTests : LiveApiTest
{
    [Fact]
    public async Task ListsTheExchanges()
    {
        MassivePage<StockExchange> page = await Client.Stocks.ListExchangesAsync(cancellationToken: Ct);

        Assert.NotEmpty(page.Results);
        Assert.All(page.Results, exchange => Assert.False(string.IsNullOrEmpty(exchange.Id)));
        Assert.All(page.Results, exchange => Assert.False(string.IsNullOrEmpty(exchange.Name)));
        Assert.Contains(page.Results, exchange => exchange.Mic == "XNYS");
    }
}
