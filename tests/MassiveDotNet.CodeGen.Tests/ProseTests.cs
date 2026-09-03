using Xunit;

namespace MassiveDotNet.CodeGen.Tests;

/// <summary>
/// The description ends many parameter descriptions with a sentence naming the wire format.
/// On a parameter whose .NET type already states it, the sentence reads as if the caller still
/// passed a string, so it is dropped (#35). The rule is enumerated: these are the five shapes
/// the description uses, and nothing else is touched.
/// </summary>
public sealed class ProseTests
{
    [Theory]
    [InlineData("The ex-dividend date. Value must be formatted 'yyyy-mm-dd'.", "The ex-dividend date.")]
    [InlineData("The cash amount. Value must be a floating point number.", "The cash amount.")]
    [InlineData("The payout frequency. Value must be an integer.", "The payout frequency.")]
    [InlineData(
        "The trade timestamp. Value must be an integer timestamp in nanoseconds, formatted 'yyyy-mm-dd', or ISO 8601/RFC 3339 (e.g. '2024-05-28T20:27:41Z').",
        "The trade timestamp.")]
    [InlineData(
        "The bar timestamp. Value must be an integer timestamp in seconds, formatted 'yyyy-mm-dd', or ISO 8601/RFC 3339 (e.g. '2024-05-28T20:27:41Z').",
        "The bar timestamp.")]
    public void DropsTheWireFormatSentenceTheDescriptionAppends(string description, string expected)
    {
        Assert.Equal(expected, Prose.WithoutWireFormat(description));
    }

    [Fact]
    public void RestoresThePeriodTwoDescriptionsOmit()
    {
        // The dividends ex_dividend_date and one floating-point field run straight into the
        // sentence with no period; dropping it must leave a sentence, not a fragment.
        Assert.Equal(
            "Date when the stock begins trading without the dividend value.",
            Prose.WithoutWireFormat("Date when the stock begins trading without the dividend value Value must be formatted 'yyyy-mm-dd'."));
    }

    [Theory]
    [InlineData("Limit the number of results. Must be positive.")]
    [InlineData("Value must be an integer.")]
    [InlineData("The value must be a floating point number.")]
    public void LeavesEveryOtherSentenceAlone(string description)
    {
        Assert.Equal(description, Prose.WithoutWireFormat(description));
    }
}
