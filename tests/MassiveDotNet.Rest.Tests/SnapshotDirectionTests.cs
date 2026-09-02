using Xunit;

namespace MassiveDotNet.Rest.Tests;

public sealed class SnapshotDirectionTests
{
    [Theory]
    [InlineData(SnapshotDirection.Gainers, "gainers")]
    [InlineData(SnapshotDirection.Losers, "losers")]
    public void RendersTheWireLiteral(SnapshotDirection value, string expected)
    {
        Assert.Equal(expected, value.ToWireValue());
    }

    [Fact]
    public void RefusesAnUndefinedMember()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((SnapshotDirection)42).ToWireValue());
    }
}
