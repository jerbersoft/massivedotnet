using MassiveDotNet.Http;
using NodaTime;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// The wire forms of <see cref="DateOrNanoseconds"/>, the tick-level counterpart of
/// <see cref="DateOrTimestamp"/> (D20): the same four factories, with an instant rendered as Unix
/// nanoseconds rather than milliseconds.
/// </summary>
public sealed class DateOrNanosecondsTests
{
    // One microsecond after the epoch is 1000 ns, 10 ticks, and 0 ms. Each unit gives a different
    // answer, so a render of "1000" proves the nanosecond path and nothing else.
    private static readonly Instant OneMicrosecond = NodaConstants.UnixEpoch + Duration.FromNanoseconds(1000);

    [Fact]
    public void ADateRendersAsIso()
    {
        Assert.Equal("2024-01-16", DateOrNanoseconds.FromDate(new LocalDate(2024, 1, 16)).ToString());
    }

    [Fact]
    public void AnInstantRendersAsUnixNanoseconds()
    {
        Assert.Equal("1000", DateOrNanoseconds.FromInstant(OneMicrosecond).ToString());
    }

    [Fact]
    public void ANanosecondCountRendersUnchanged()
    {
        Assert.Equal("1517562000016036600", DateOrNanoseconds.FromUnixNanoseconds(1517562000016036600L).ToString());
    }

    [Fact]
    public void ALiteralRendersVerbatim()
    {
        Assert.Equal("2024-01-16", DateOrNanoseconds.FromLiteral("2024-01-16").ToString());
    }

    [Fact]
    public void ABlankLiteralIsRefused()
    {
        Assert.Throws<ArgumentException>(() => DateOrNanoseconds.FromLiteral("  "));
    }

    [Fact]
    public void ImplicitConversionsCoverEveryForm()
    {
        DateOrNanoseconds fromDate = new LocalDate(2024, 1, 16);
        DateOrNanoseconds fromInstant = OneMicrosecond;
        DateOrNanoseconds fromCount = 1000L;
        DateOrNanoseconds fromLiteral = "2024-01-16";

        Assert.Equal("2024-01-16", fromDate.ToString());
        Assert.Equal("1000", fromInstant.ToString());
        Assert.Equal("1000", fromCount.ToString());
        Assert.Equal("2024-01-16", fromLiteral.ToString());
    }

    [Fact]
    public void EqualityFollowsTheValue()
    {
        Assert.Equal(DateOrNanoseconds.FromUnixNanoseconds(1000), DateOrNanoseconds.FromInstant(OneMicrosecond));
        Assert.Equal(
            DateOrNanoseconds.FromUnixNanoseconds(1000).GetHashCode(),
            DateOrNanoseconds.FromInstant(OneMicrosecond).GetHashCode());
        Assert.True(DateOrNanoseconds.FromDate(new LocalDate(2024, 1, 16)) == DateOrNanoseconds.FromLiteral("2024-01-16"));
        Assert.True(DateOrNanoseconds.FromUnixNanoseconds(1000) != DateOrNanoseconds.FromUnixNanoseconds(1001));
    }

    [Fact]
    public void ToWireValueNanosecondsRendersNanosecondsNotTicks()
    {
        // The extension existed before any caller did, and rendered ToUnixTimeTicks: 100 ns
        // units, which would have asked a tick endpoint for a moment a hundred times too early.
        Assert.Equal("1000", OneMicrosecond.ToWireValueNanoseconds());
    }

    [Fact]
    public void RendersThroughTheBuilderLikeAnyOtherElement()
    {
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery<DateOrNanoseconds>("f", RangeFilter.Between(
            DateOrNanoseconds.FromInstant(OneMicrosecond),
            DateOrNanoseconds.FromDate(new LocalDate(2024, 1, 16))));

        Assert.Equal("/x?f.gte=1000&f.lte=2024-01-16", builder.ToUriString());
    }

    [Fact]
    public void LiteralsArePercentEscapedLikeAnyOtherCallerSuppliedValue()
    {
        // FromLiteral accepts any non-whitespace string and ToString returns it unchanged, so an
        // unescaped render would let a crafted literal inject a second query parameter.
        RequestUriBuilder builder = new(stackalloc char[128]);
        builder.AppendPathLiteral("/x");
        builder.AppendQuery<DateOrNanoseconds>("f", RangeFilter.Gte(DateOrNanoseconds.FromLiteral("2024-01-16&limit=50000")));

        Assert.Equal("/x?f.gte=2024-01-16%26limit%3D50000", builder.ToUriString());
    }
}
