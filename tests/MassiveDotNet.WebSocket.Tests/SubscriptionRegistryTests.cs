using MassiveDotNet.WebSocket.Internal;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// Task 11 review round 1, finding 1 (CRITICAL): <c>SubscriptionRegistry</c> held a bare
/// <see cref="HashSet{T}"/> with no lock, and <c>Parameters</c> returned the live set rather than a
/// snapshot. <c>TryReconnectAsync</c> enumerates it on the read-loop thread while a caller mutates
/// it from <c>SubscribeAsync</c>/<c>UnsubscribeAsync</c> on its own thread -- and
/// <see cref="HashSet{T}"/>'s enumeration-invalidation is asymmetric: a concurrent <c>Add</c> bumps
/// the version and throws <see cref="InvalidOperationException"/> (loud), but a concurrent
/// <c>Remove</c> does not bump the version at all, so the enumeration simply visits fewer elements
/// with no exception and no signal (silent). Both tests below reproduce their half
/// deterministically and single-threadedly, by holding an enumerator across a mutation -- exactly
/// the interleaving a real concurrent Add/Remove could produce, without depending on real thread
/// timing to land on the same instant.
/// </summary>
public class SubscriptionRegistryTests
{
    // The loud half. Pre-fix, Parameters returned the live HashSet, so mutating the registry after
    // taking the enumerator invalidates it and the next MoveNext() throws
    // InvalidOperationException -- exactly what turns an ordinary transient drop plus one concurrent
    // subscribe into a permanently dead stream (StopPermanently's catch-all is the only thing that
    // would ever see it, and it is not in the reconnect filter). Post-fix, Parameters returns a
    // snapshot array taken under a lock, disconnected from the live set, so a mutation afterward
    // cannot affect an enumeration already in progress.
    [Fact]
    public void ParametersIsASnapshotSoAConcurrentAddDuringEnumerationDoesNotThrow()
    {
        SubscriptionRegistry registry = new();
        registry.Add("T", ["AAPL", "MSFT"]);

        using IEnumerator<string> enumerator = registry.Parameters.GetEnumerator();
        Assert.True(enumerator.MoveNext());

        registry.Add("T", ["TSLA"]);

        // No InvalidOperationException here: the enumerator is walking a snapshot taken before the
        // Add ran, not the live set.
        List<string> seen = [enumerator.Current];

        while (enumerator.MoveNext())
        {
            seen.Add(enumerator.Current);
        }

        Assert.Equal(2, seen.Count);
        Assert.DoesNotContain("T.TSLA", seen);
    }

    // The silent half -- the one that will rot if untested, precisely because nothing throws.
    // Pre-fix, HashSet<T>.Remove does not bump the enumeration version, so enumerating the live set
    // across a concurrent Remove comes back one short with no exception anywhere: a reconnect's
    // replay would simply never re-send a pair the caller still holds, and nothing reports it. This
    // is exactly the class of data loss this SDK refuses everywhere else, arriving with no
    // diagnostic at all. Post-fix, the snapshot was already copied before the Remove ran, so the
    // removed entry is (correctly, from the snapshot's point of view) still present.
    [Fact]
    public void ParametersIsASnapshotSoAConcurrentRemoveDuringEnumerationCannotSilentlyDropAnEntry()
    {
        SubscriptionRegistry registry = new();
        registry.Add("T", ["AAPL", "MSFT", "GOOG"]);

        using IEnumerator<string> enumerator = registry.Parameters.GetEnumerator();
        Assert.True(enumerator.MoveNext());

        registry.Remove("T", ["GOOG"]);

        List<string> seen = [enumerator.Current];

        while (enumerator.MoveNext())
        {
            seen.Add(enumerator.Current);
        }

        Assert.Equal(3, seen.Count);
        Assert.Contains("T.GOOG", seen);
    }
}
