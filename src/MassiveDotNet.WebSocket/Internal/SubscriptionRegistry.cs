namespace MassiveDotNet.WebSocket.Internal;

/// <summary>What a reconnect replays: every live <c>topic.ticker</c> pair, deduplicated.</summary>
/// <remarks>
/// A <see cref="HashSet{T}"/> rather than a list: subscribing to a pair already held must not
/// double it (the parameter would be sent twice on replay for no effect), and unsubscribing must be
/// able to remove exactly the pair it names without scanning for duplicates. Ordinal comparison
/// because a wire parameter is an opaque ASCII code, never something a culture should reinterpret.
/// </remarks>
internal sealed class SubscriptionRegistry
{
    // Reconnect (Task 11) made this type cross-thread: TryReconnectAsync enumerates Parameters on
    // the read-loop thread while a caller mutates it from SubscribeAsync/UnsubscribeAsync on its
    // own. HashSet{T}'s enumeration-invalidation is asymmetric -- a concurrent Add bumps the
    // version and throws InvalidOperationException (loud), a concurrent Remove does not bump it at
    // all, so the enumeration silently visits fewer elements with no exception and no signal
    // (Task 11 review round 1, finding 1). This lock, plus Parameters returning a snapshot rather
    // than the live set, closes both halves.
    private readonly object _lock = new();
    private readonly HashSet<string> _parameters = new(StringComparer.Ordinal);

    /// <summary>
    /// Every live subscription parameter, e.g. <c>T.AAPL</c>. This is exactly what reconnect must
    /// re-send to restore the current subscription state -- no more, no less.
    /// </summary>
    /// <remarks>
    /// A snapshot taken under the lock, not a live view: returning the underlying set directly let
    /// a caller's concurrent Add/Remove race whoever was enumerating it, either throwing
    /// mid-replay or silently dropping a pair from it (finding 1). A snapshot is immune to a
    /// mutation that happens after it was taken, by construction.
    /// </remarks>
    public IReadOnlyCollection<string> Parameters
    {
        get
        {
            lock (_lock)
            {
                return [.. _parameters];
            }
        }
    }

    /// <summary>Records a subscription as live, for every ticker under one topic code.</summary>
    /// <param name="topicCode">The wire topic code, such as <c>T</c>.</param>
    /// <param name="tickers">The tickers accepted alongside <paramref name="topicCode"/>.</param>
    public void Add(string topicCode, IEnumerable<string> tickers)
    {
        lock (_lock)
        {
            foreach (string ticker in tickers)
            {
                _parameters.Add($"{topicCode}.{ticker}");
            }
        }
    }

    /// <summary>Forgets a subscription, for every ticker under one topic code.</summary>
    /// <param name="topicCode">The wire topic code, such as <c>T</c>.</param>
    /// <param name="tickers">The tickers no longer subscribed alongside <paramref name="topicCode"/>.</param>
    public void Remove(string topicCode, IEnumerable<string> tickers)
    {
        lock (_lock)
        {
            foreach (string ticker in tickers)
            {
                _parameters.Remove($"{topicCode}.{ticker}");
            }
        }
    }
}
