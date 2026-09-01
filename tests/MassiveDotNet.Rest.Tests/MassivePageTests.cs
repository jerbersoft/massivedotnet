using Xunit;

namespace MassiveDotNet.Rest.Tests;

public sealed class MassivePageTests
{
    [Fact]
    public void ExposesTheResultsItWasGiven()
    {
        MassivePage<int> page = new([1, 2, 3], hasMore: true, requestId: "abc");

        Assert.Equal([1, 2, 3], page.Results);
        Assert.True(page.HasMore);
        Assert.Equal("abc", page.RequestId);
    }

    [Fact]
    public void ReportsAnEmptyArrayRatherThanNullWhenTheServerSentNoResults()
    {
        MassivePage<int> page = new(null, hasMore: false, requestId: null);

        Assert.Empty(page.Results);
        Assert.False(page.HasMore);
        Assert.Null(page.RequestId);
    }

    [Fact]
    public void ReportsAnEmptyArrayForADefaultInstance()
    {
        // A struct can always be default-constructed, so the non-null guarantee
        // has to survive that path too.
        MassivePage<int> page = default;

        Assert.Empty(page.Results);
    }
}
