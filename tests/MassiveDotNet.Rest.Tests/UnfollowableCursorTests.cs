using System.Net;
using MassiveDotNet.Http;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The guard a generated <c>Get</c> calls when its operation's schema declares <c>next_url</c> but
/// its result is one object that cannot be paged (D-S3). Tested directly: the generated line
/// carries no logic of its own.
/// </summary>
public sealed class UnfollowableCursorTests
{
    private const string RequestUri = "/v3/snapshot/options/AAPL/O:AAPL230616C00150000";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PassesABlankCursor(string? nextUrl)
    {
        // A blank next_url is the absence it means, exactly as EnumerateAsync treats it (D-P5).
        MassiveHttpTransport.ThrowIfUnfollowableCursor(nextUrl, RequestUri, requestId: "r");
    }

    [Fact]
    public void ThrowsForARealCursorNamingTheRequestAndCarryingTheRequestId()
    {
        MassiveApiException exception = Assert.Throws<MassiveApiException>(() =>
            MassiveHttpTransport.ThrowIfUnfollowableCursor(
                "https://api.massive.com/v3/snapshot/options/AAPL/O:AAPL230616C00150000?cursor=abc",
                RequestUri,
                requestId: "r"));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("r", exception.RequestId);
        Assert.Contains(RequestUri, exception.Message, StringComparison.Ordinal);
        Assert.Contains("next_url", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThrowsWithoutARequestIdWhenTheEnvelopeHasNone()
    {
        MassiveApiException exception = Assert.Throws<MassiveApiException>(() =>
            MassiveHttpTransport.ThrowIfUnfollowableCursor("https://api.massive.com/x?cursor=abc", RequestUri, requestId: null));

        Assert.Null(exception.RequestId);
    }

    [Fact]
    public void RequiresARequestUriToName()
    {
        Assert.Throws<ArgumentException>(() =>
            MassiveHttpTransport.ThrowIfUnfollowableCursor("https://api.massive.com/x?cursor=abc", " ", requestId: null));
    }
}
