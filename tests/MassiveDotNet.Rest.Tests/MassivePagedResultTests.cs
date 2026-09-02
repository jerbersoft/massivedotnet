using Xunit;

namespace MassiveDotNet.Rest.Tests;

public sealed class MassivePagedResultTests
{
    private sealed record Series(int[] Values);

    [Fact]
    public void ExposesTheResultItWasGiven()
    {
        Series series = new([1, 2, 3]);

        MassivePagedResult<Series> page = new(series, hasMore: true, requestId: "abc");

        Assert.Same(series, page.Result);
        Assert.True(page.HasMore);
        Assert.Equal("abc", page.RequestId);
    }

    [Fact]
    public void ReportsNoRequestIdWhenTheEndpointSendsNone()
    {
        MassivePagedResult<Series> page = new(new Series([]), hasMore: false, requestId: null);

        Assert.False(page.HasMore);
        Assert.Null(page.RequestId);
    }

    [Fact]
    public void RefusesANullResult()
    {
        // The generated Send method throws MassiveApiException before constructing a page whose
        // payload is missing (D-S4), so this guard is what keeps Result's "never null" promise from
        // depending on every caller remembering that.
        Assert.Throws<ArgumentNullException>(() => new MassivePagedResult<Series>(null!, hasMore: false, requestId: null));
    }
}
