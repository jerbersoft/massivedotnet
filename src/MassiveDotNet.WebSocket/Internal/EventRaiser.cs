namespace MassiveDotNet.WebSocket.Internal;

/// <summary>
/// Raises a consumer-facing event so that one misbehaving subscriber cannot take down the stream
/// it was merely being told about.
/// </summary>
/// <remarks>
/// This exists because the same defect appeared four times on the streaming work, once per event,
/// each time twenty lines from an already-fixed sibling: a bare <c>Handler?.Invoke(...)</c> runs
/// consumer code synchronously on the read-loop thread, and every raise site sits inside a
/// <c>try</c> whose filters do not match an arbitrary handler exception. The exception therefore
/// escapes to the read loop's outer catch, which treats it as terminal -- so a notification about
/// a reconnect, or about a handful of dropped events, would end the entire live feed. That is
/// strictly worse than whatever was being reported, and in the reconnect case it discards a
/// connection that had just been successfully re-established.
///
/// Two properties are load-bearing, and only one of them is obvious. Each subscriber gets its OWN
/// <c>try</c> because a multicast delegate stops invoking subscribers the instant one throws, so a
/// single <c>try</c> wrapped around the whole invocation would still starve every handler
/// registered after the throwing one. And the raise must never propagate, because the caller is
/// the read loop.
///
/// Handler exceptions are swallowed rather than logged because core has no logger to hand them to
/// -- rule 8 confines <c>Microsoft.Extensions.*</c> to the DI package, so there is nowhere honest
/// to report a misbehaving handler from here.
/// </remarks>
internal static class EventRaiser
{
    /// <summary>Raises a single-argument event, isolating each subscriber's failure.</summary>
    /// <typeparam name="T">The argument type.</typeparam>
    /// <param name="handlers">The event's invocation list, or <see langword="null"/> if empty.</param>
    /// <param name="argument">The argument to pass each subscriber.</param>
    public static void Raise<T>(Action<T>? handlers, T argument)
    {
        foreach (Delegate handler in handlers?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action<T>)handler)(argument);
            }
            catch
            {
                // See the remarks above: a consumer's handler throwing is not the stream's problem,
                // and must never end the stream for every other consumer too.
            }
        }
    }

    /// <summary>Raises a two-argument event, isolating each subscriber's failure.</summary>
    /// <typeparam name="T1">The first argument's type.</typeparam>
    /// <typeparam name="T2">The second argument's type.</typeparam>
    /// <param name="handlers">The event's invocation list, or <see langword="null"/> if empty.</param>
    /// <param name="argument1">The first argument to pass each subscriber.</param>
    /// <param name="argument2">The second argument to pass each subscriber.</param>
    public static void Raise<T1, T2>(Action<T1, T2>? handlers, T1 argument1, T2 argument2)
    {
        foreach (Delegate handler in handlers?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action<T1, T2>)handler)(argument1, argument2);
            }
            catch
            {
                // See the remarks above.
            }
        }
    }
}
