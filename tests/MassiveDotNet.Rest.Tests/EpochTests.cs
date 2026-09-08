using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

public class EpochTests
{
    [Fact]
    public void MillisecondsConvertToTheSameInstantNodaTimeWould() =>
        Assert.Equal(
            Instant.FromUnixTimeMilliseconds(1536036818784),
            Epoch.FromMilliseconds(1536036818784));

    // The whole reason this helper exists rather than Instant.FromUnixTimeTicks: ticks are 100ns,
    // so a nanosecond value that is not a multiple of 100 would be silently truncated.
    [Fact]
    public void NanosecondsKeepPrecisionBelowTheTickBoundary()
    {
        Instant instant = Epoch.FromNanoseconds(1536036818784123456);

        Assert.Equal(
            1536036818784123456L,
            (instant - NodaConstants.UnixEpoch).ToInt64Nanoseconds());
    }
}
