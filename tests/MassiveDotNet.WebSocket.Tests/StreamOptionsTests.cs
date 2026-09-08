using NodaTime;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

public class StreamOptionsTests
{
    [Fact]
    public void DefaultsMatchThePlannedValues()
    {
        MassiveStreamOptions options = new();

        Assert.Equal(MassiveFeeds.RealTime, options.Feed);
        Assert.Equal(1024, options.TopicBufferCapacity);
        Assert.Equal(16384, options.TickerPoolCapacity);
        Assert.Equal(4 * 1024 * 1024, options.MaxMessageBytes);
        Assert.Equal(Duration.FromSeconds(10), options.HandshakeTimeout);
        Assert.Equal(Duration.FromSeconds(20), options.KeepAliveInterval);
    }

    // Unlike rate limiting and retry (D30), reconnect is on by default: a stream that gives up on
    // the first dropped connection is not a streaming client, it is a demo.
    [Fact]
    public void ReconnectIsOnByDefaultAndCanBeDisabled()
    {
        MassiveStreamOptions options = new();

        Assert.NotNull(options.Reconnect);
        Assert.Equal(Duration.FromMilliseconds(500), options.Reconnect!.InitialBackoff);
        Assert.Equal(Duration.FromSeconds(30), options.Reconnect.MaxBackoff);
        Assert.Equal(2.0, options.Reconnect.BackoffMultiplier);
        Assert.Equal(0.2, options.Reconnect.Jitter);

        options.Reconnect = null;
        Assert.Null(options.Reconnect);
    }

    [Fact]
    public void AWhitespaceKeyIsTreatedAsAbsent()
    {
        MassiveStreamOptions options = new() { ApiKey = "   " };

        Assert.Null(options.ApiKey);
    }

    [Fact]
    public void ValidateRejectsAMissingKey()
    {
        MassiveStreamOptions options = new();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains(nameof(MassiveStreamOptions.ApiKey), error.Message, StringComparison.Ordinal);
    }

    // Rule 11: the key is a frame body here (D-W9), so it must not travel in a message either.
    [Fact]
    public void ValidateNeverEchoesTheKey()
    {
        MassiveStreamOptions options = new() { ApiKey = "super-secret-key", Feed = new Uri("/relative", UriKind.Relative) };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.DoesNotContain("super-secret-key", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateRejectsANonPositiveBufferCapacity(int capacity)
    {
        MassiveStreamOptions options = new() { ApiKey = "k", TopicBufferCapacity = capacity };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void ValidateRejectsBackoffThatCannotGrow()
    {
        MassiveStreamOptions options = new()
        {
            ApiKey = "k",
            Reconnect = new MassiveStreamReconnectOptions { BackoffMultiplier = 0.5 },
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    // Math.Pow(NaN, 0) == 1 under IEEE 754, so a NaN multiplier lets the first reconnect attempt
    // through silently; Validate() is what stops it before Task 11's background loop ever sees it.
    [Fact]
    public void ValidateRejectsANaNBackoffMultiplier()
    {
        MassiveStreamOptions options = new()
        {
            ApiKey = "k",
            Reconnect = new MassiveStreamReconnectOptions { BackoffMultiplier = double.NaN },
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void ValidateRejectsANaNJitter()
    {
        MassiveStreamOptions options = new()
        {
            ApiKey = "k",
            Reconnect = new MassiveStreamReconnectOptions { Jitter = double.NaN },
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }
}
