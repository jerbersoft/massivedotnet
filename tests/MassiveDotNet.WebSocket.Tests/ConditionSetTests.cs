using System.Reflection;
using MassiveDotNet.WebSocket.Events;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// Pins the bound on <see cref="ConditionSet.AsSpan"/>'s inline branch (#128), the one place this
/// type hands <c>MemoryMarshal.CreateReadOnlySpan</c> a length it does not check.
/// </summary>
public class ConditionSetTests
{
    // The boundary the inline branch has to get exactly right: a full buffer is still inline, and
    // every code in it comes back, through the span and the indexer alike.
    [Fact]
    public void ASetHoldingExactlyTheInlineCapacityReadsBackEveryCode()
    {
        ConditionSet set = new([1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.Equal(ConditionSet.InlineCapacity, set.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], set.AsSpan().ToArray());
        Assert.Equal(8, set[ConditionSet.InlineCapacity - 1]);
    }

    // The constructor never builds this value: a Count above InlineCapacity always comes with a
    // spilled array. A torn read can, though. Copying a struct is not atomic, so a ConditionSet read
    // while another thread overwrites the same field can take its null _overflow from an inline set
    // and its Count from a spilled one. The inline branch then spanned twelve ints over an
    // eight-int buffer and read whatever lay past it, without throwing. Reflection builds that
    // pairing deterministically, and it must fail as an out-of-range read rather than return bytes
    // that are not condition codes.
    [Fact]
    public void ACountPastTheInlineBufferThrowsRatherThanReadingBeyondIt()
    {
        FieldInfo? count = typeof(ConditionSet).GetField("<Count>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(count);

        object boxed = new ConditionSet([1, 2, 3]);
        count.SetValue(boxed, 12);
        ConditionSet torn = (ConditionSet)boxed;

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = torn.AsSpan(); });
        Assert.Throws<ArgumentOutOfRangeException>(() => torn[9]);
    }
}
