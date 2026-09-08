namespace MassiveDotNet.WebSocket;

/// <summary>The server did not acknowledge every subscription requested.</summary>
/// <remarks>
/// The server answers one <c>status: success</c> per accepted pair and says nothing about a pair it
/// does not recognise, so a shortfall is the only evidence that something was dropped. It throws
/// rather than continuing because a subscription that silently does not exist is indistinguishable
/// from a quiet market (D-W2).
/// </remarks>
public sealed class MassiveStreamSubscriptionException : MassiveStreamException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="parameters">The subscription parameter that was sent.</param>
    /// <param name="unacknowledged">How many pairs went unacknowledged.</param>
    public MassiveStreamSubscriptionException(string parameters, int unacknowledged)
        : base($"The stream acknowledged {unacknowledged} fewer subscriptions than were requested "
               + $"for '{parameters}'. The server ignores a topic code it does not recognise, so "
               + "the unacknowledged pairs would never have produced data.") =>
        Unacknowledged = unacknowledged;

    /// <summary>
    /// How many requested pairs went unacknowledged. A caller sees this as evidence, not detail: a
    /// non-zero count means at least one <c>topic.ticker</c> pair in the request was silently
    /// dropped by the server rather than accepted, so the subscription is incomplete and the
    /// missing pairs will never deliver data until the request is corrected and retried.
    /// </summary>
    public int Unacknowledged { get; }
}
