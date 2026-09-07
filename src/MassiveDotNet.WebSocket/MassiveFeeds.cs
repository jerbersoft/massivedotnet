namespace MassiveDotNet.WebSocket;

/// <summary>
/// Well-known feed hosts for the Massive streaming platform.
/// </summary>
/// <remarks>
/// Eight hosts, established by certificate on 2026-09-07: both <c>massive.com</c> and
/// <c>polygon.io</c> answer DNS with wildcard records, so a name resolving proves nothing, while a
/// provisioned host presents a certificate naming itself and an unprovisioned one falls through to
/// the ingress default. A <c>launchpad</c> host is named in issue #20 but is not provisioned on
/// either domain, so no property exposes it.
/// </remarks>
public static class MassiveFeeds
{
    /// <summary>The real-time feed, <c>wss://socket.massive.com</c>.</summary>
    public static Uri RealTime { get; } = new("wss://socket.massive.com", UriKind.Absolute);

    /// <summary>The 15-minute delayed feed, <c>wss://delayed.massive.com</c>.</summary>
    public static Uri Delayed { get; } = new("wss://delayed.massive.com", UriKind.Absolute);

    /// <summary>The business-plan feed, <c>wss://business.massive.com</c>.</summary>
    public static Uri Business { get; } = new("wss://business.massive.com", UriKind.Absolute);

    /// <summary>The delayed business-plan feed, <c>wss://delayed-business.massive.com</c>.</summary>
    public static Uri DelayedBusiness { get; } = new("wss://delayed-business.massive.com", UriKind.Absolute);

    /// <summary>The PolyFeed host, <c>wss://polyfeed.massive.com</c>.</summary>
    public static Uri PolyFeed { get; } = new("wss://polyfeed.massive.com", UriKind.Absolute);

    /// <summary>The PolyFeed Plus host, <c>wss://polyfeedplus.massive.com</c>.</summary>
    public static Uri PolyFeedPlus { get; } = new("wss://polyfeedplus.massive.com", UriKind.Absolute);

    /// <summary>The Nasdaq Basic host, <c>wss://nasdaqfeed.massive.com</c>.</summary>
    public static Uri NasdaqFeed { get; } = new("wss://nasdaqfeed.massive.com", UriKind.Absolute);

    /// <summary>The starter-plan host, <c>wss://starterfeed.massive.com</c>.</summary>
    public static Uri StarterFeed { get; } = new("wss://starterfeed.massive.com", UriKind.Absolute);

    /// <summary>
    /// The same eight hosts on the legacy <c>polygon.io</c> domain, which Massive operates in
    /// parallel exactly as <see cref="MassiveEndpoints.Legacy"/> describes for REST.
    /// </summary>
    public static class Legacy
    {
        /// <summary>The real-time feed, <c>wss://socket.polygon.io</c>.</summary>
        public static Uri RealTime { get; } = new("wss://socket.polygon.io", UriKind.Absolute);

        /// <summary>The 15-minute delayed feed, <c>wss://delayed.polygon.io</c>.</summary>
        public static Uri Delayed { get; } = new("wss://delayed.polygon.io", UriKind.Absolute);

        /// <summary>The business-plan feed, <c>wss://business.polygon.io</c>.</summary>
        public static Uri Business { get; } = new("wss://business.polygon.io", UriKind.Absolute);

        /// <summary>The delayed business-plan feed, <c>wss://delayed-business.polygon.io</c>.</summary>
        public static Uri DelayedBusiness { get; } = new("wss://delayed-business.polygon.io", UriKind.Absolute);

        /// <summary>The PolyFeed host, <c>wss://polyfeed.polygon.io</c>.</summary>
        public static Uri PolyFeed { get; } = new("wss://polyfeed.polygon.io", UriKind.Absolute);

        /// <summary>The PolyFeed Plus host, <c>wss://polyfeedplus.polygon.io</c>.</summary>
        public static Uri PolyFeedPlus { get; } = new("wss://polyfeedplus.polygon.io", UriKind.Absolute);

        /// <summary>The Nasdaq Basic host, <c>wss://nasdaqfeed.polygon.io</c>.</summary>
        public static Uri NasdaqFeed { get; } = new("wss://nasdaqfeed.polygon.io", UriKind.Absolute);

        /// <summary>The starter-plan host, <c>wss://starterfeed.polygon.io</c>.</summary>
        public static Uri StarterFeed { get; } = new("wss://starterfeed.polygon.io", UriKind.Absolute);
    }
}
