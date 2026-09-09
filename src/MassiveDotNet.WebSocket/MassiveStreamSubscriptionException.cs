namespace MassiveDotNet.WebSocket;

/// <summary>The server did not acknowledge every subscription requested.</summary>
/// <remarks>
/// The server answers one <c>status: success</c> per accepted pair and says nothing about a pair it
/// does not recognise, so a shortfall is the only evidence that something was dropped. It throws
/// rather than continuing because a subscription that silently does not exist is indistinguishable
/// from a quiet market (D-W2).
/// <para>
/// A pair can also be refused out loud, and then the server's own words are the better evidence:
/// see <see cref="ServerMessage"/> and D37.
/// </para>
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
    /// <param name="serverMessage">
    /// The server's own reason, verbatim, or <see langword="null"/> when it gave none.
    /// </param>
    public MassiveStreamSubscriptionException(
        string parameters, int unacknowledged, string? serverMessage = null)
        : base(BuildMessage(parameters, unacknowledged, serverMessage))
    {
        Parameters = parameters;
        Unacknowledged = unacknowledged;
        ServerMessage = serverMessage;
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

    /// <summary>
    /// What the server said when it refused the request, verbatim, or <see langword="null"/> when
    /// it said nothing at all.
    /// </summary>
    /// <remarks>
    /// Both cases are real and they mean different things (D37). A refused subscribe is answered --
    /// <c>not authorized</c>, for a topic the key's plan does not include -- while an unrecognised
    /// topic code is dropped in silence, which is what leaves <see cref="Unacknowledged"/> as the
    /// only evidence available. <see cref="Exception.Message"/> says whichever happened.
    /// <para>
    /// The words are the server's and are reported rather than categorised: <c>error</c> covers
    /// unrelated failures distinguished only by prose, exactly as <c>auth_failed</c> does for
    /// <see cref="MassiveStreamAuthenticationException.ServerMessage"/> (D-W6), and a category
    /// invented from one observation is one that drifts. A consumer who wants to handle a refusal
    /// separately from a silent drop filters on this being non-<see langword="null"/>.
    /// </para>
    /// </remarks>
    public string? ServerMessage { get; }

    private static string BuildMessage(string parameters, int unacknowledged, string? serverMessage)
    {
        // The count is true either way, so only the second half -- why it happened -- turns on
        // whether the server gave a reason. Rewording unconditionally would trade one wrong message
        // for another: the inference below is exactly right when nothing was said.
        string cause = serverMessage is null
            ? "The server ignores a topic code it does not recognise, so the unacknowledged pairs "
              + "would never have produced data."
            : $"The server refused with: {serverMessage}";

        return $"The stream acknowledged {unacknowledged} fewer subscriptions than were requested "
            + $"for '{parameters}'. {cause}";
    }
}
