namespace MassiveDotNet.WebSocket;

/// <summary>The server did not acknowledge every subscription requested.</summary>
/// <remarks>
/// The server answers one <c>status: success</c> per accepted pair and says nothing about a pair it
/// does not recognise, so a shortfall is the only evidence that something was dropped. It throws
/// rather than continuing because a subscription that silently does not exist is indistinguishable
/// from a quiet market (D-W2).
/// <para>
/// It reaches a caller two ways. <c>SubscribeAsync</c> throws it, because a caller is awaiting that
/// request. A reconnect's replay instead raises it through
/// <see cref="MassiveStockStream.SubscriptionsLost"/>, because nobody is awaiting a replay -- the
/// same evidence, delivered as a signal rather than a throw (D33).
/// </para>
/// </remarks>
public sealed class MassiveStreamSubscriptionException : MassiveStreamException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="parameters">The subscription parameter that was sent.</param>
    /// <param name="unacknowledged">How many pairs went unacknowledged.</param>
    public MassiveStreamSubscriptionException(string parameters, int unacknowledged)
        : base($"The stream acknowledged {unacknowledged} fewer subscriptions than were requested "
               + $"for '{parameters}'. The server ignores a topic code it does not recognise, so "
               + "the unacknowledged pairs would never have produced data.")
    {
        Parameters = parameters;
        Unacknowledged = unacknowledged;
    }

    /// <summary>
    /// The subscription parameter this exception is about, such as <c>T.AAPL,T.MSFT</c>.
    /// </summary>
    /// <remarks>
    /// Retained rather than left only inside <see cref="Exception.Message"/> so a handler can act on
    /// it -- re-subscribe, or report which topics went dark -- without parsing prose. D-W2's own
    /// argument for counting acknowledgements rather than reading the server's wording applies to
    /// this SDK's wording too: a message is the part of a contract nobody versions.
    /// <para>
    /// Raised out-of-band through <see cref="MassiveStockStream.SubscriptionsLost"/>, this names
    /// only the pairs a reconnect's replay failed to restore, not everything that was replayed.
    /// </para>
    /// </remarks>
    public string Parameters { get; }

    /// <summary>
    /// How many requested pairs went unacknowledged. A caller sees this as evidence, not detail: a
    /// non-zero count means at least one <c>topic.ticker</c> pair in the request was silently
    /// dropped by the server rather than accepted, so the subscription is incomplete and the
    /// missing pairs will never deliver data until the request is corrected and retried.
    /// </summary>
    public int Unacknowledged { get; }
}
