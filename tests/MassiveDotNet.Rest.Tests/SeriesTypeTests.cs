using Xunit;

namespace MassiveDotNet.Rest.Tests;

public sealed class SeriesTypeTests
{
    [Theory]
    [InlineData(SeriesType.Open, "open")]
    [InlineData(SeriesType.High, "high")]
    [InlineData(SeriesType.Low, "low")]
    [InlineData(SeriesType.Close, "close")]
    public void RendersTheWireLiteral(SeriesType value, string expected)
    {
        Assert.Equal(expected, value.ToWireValue());
    }

    [Fact]
    public void RefusesAnUndefinedMember()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((SeriesType)42).ToWireValue());
    }
}
