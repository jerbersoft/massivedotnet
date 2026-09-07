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
    private readonly HashSet<string> _parameters = new(StringComparer.Ordinal);

    /// <summary>
    /// Every live subscription parameter, e.g. <c>T.AAPL</c>. This is exactly what reconnect must
    /// re-send to restore the current subscription state -- no more, no less.
    /// </summary>
    public IReadOnlyCollection<string> Parameters => _parameters;

    /// <summary>Records a subscription as live, for every ticker under one topic code.</summary>
    /// <param name="topicCode">The wire topic code, such as <c>T</c>.</param>
    /// <param name="tickers">The tickers accepted alongside <paramref name="topicCode"/>.</param>
    public void Add(string topicCode, IEnumerable<string> tickers)
    {
        foreach (string ticker in tickers)
        {
            _parameters.Add($"{topicCode}.{ticker}");
        }
    }

    /// <summary>Forgets a subscription, for every ticker under one topic code.</summary>
    /// <param name="topicCode">The wire topic code, such as <c>T</c>.</param>
    /// <param name="tickers">The tickers no longer subscribed alongside <paramref name="topicCode"/>.</param>
    public void Remove(string topicCode, IEnumerable<string> tickers)
    {
        foreach (string ticker in tickers)
        {
            _parameters.Remove($"{topicCode}.{ticker}");
        }
    }
}
