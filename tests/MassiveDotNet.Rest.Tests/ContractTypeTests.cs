using Xunit;

namespace MassiveDotNet.Rest.Tests;

public sealed class ContractTypeTests
{
    [Theory]
    [InlineData(ContractType.Call, "call")]
    [InlineData(ContractType.Put, "put")]
    public void RendersTheWireLiteral(ContractType value, string expected)
    {
        Assert.Equal(expected, value.ToWireValue());
    }

    [Fact]
    public void RefusesAnUndefinedMember()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((ContractType)42).ToWireValue());
    }
}
