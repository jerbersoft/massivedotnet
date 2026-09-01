using System.Net;
using MassiveDotNet.Rest;
using NodaTime;
using Xunit;

namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Verifies the SDK's error handling against the envelope the service actually returns.
/// </summary>
/// <remarks>
/// This is the highest-value integration test in the suite. The OpenAPI description declares every
/// error response with an empty schema, so <c>MassiveErrorPayload</c> is an informed guess. Only a
/// live call can confirm the guess is right.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class ErrorHandlingLiveTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RejectsAnInvalidApiKeyWithAParsedMessage()
    {
        Assert.SkipUnless(LiveCredentials.IsAvailable, LiveCredentials.MissingKeyReason);

        using MassiveRestClient client = new("this-key-is-deliberately-invalid");

        MassiveApiException exception = await Assert.ThrowsAsync<MassiveApiException>(() =>
            client.Stocks.ListAggregatesAsync(
                "AAPL", 1, AggregateTimespan.Day, new LocalDate(2026, 8, 3), new LocalDate(2026, 8, 7),
                cancellationToken: Ct));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);

        // The point of the test: the message came from the server's body, not from the status line.
        Assert.False(
            string.IsNullOrWhiteSpace(exception.Message),
            "The server's error body should have produced a message.");
        Assert.NotEqual("Unauthorized", exception.Message);
    }
}
