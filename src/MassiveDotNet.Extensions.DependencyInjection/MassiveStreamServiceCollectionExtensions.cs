using MassiveDotNet.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

// Namespace note (Task 12 pre-flight, an eighth inconsistency beyond the seven the brief already
// flags): the brief's code block declares `namespace MassiveDotNet.Extensions.DependencyInjection`
// for this file, but its own AddMassiveStreamTests.cs imports only
// `using Microsoft.Extensions.DependencyInjection;` and calls `services.AddMassiveStream(...)` with
// no other using -- which only resolves if this extension method lives in
// Microsoft.Extensions.DependencyInjection, exactly where the existing AddMassive already does
// (MassiveServiceCollectionExtensions.cs). Followed the test, not the prose, the same way H4 says to.

/// <summary>Registers the Massive streaming client.</summary>
public static partial class MassiveStreamServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="MassiveStreamClient"/> as a singleton, configured by
    /// <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the options. The API key is required.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// There is no <c>IConfiguration</c> overload, for D27's reason: the options carry NodaTime
    /// <c>Duration</c> values the configuration binder silently leaves at their defaults. Read the
    /// values yourself — <c>options.ApiKey = configuration["Massive:ApiKey"]</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddMassiveStream(this IServiceCollection services, Action<MassiveStreamOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<MassiveStreamOptions>().Configure(configure);

        services.AddSingleton(provider =>
        {
            MassiveStreamOptions options = provider
                .GetRequiredService<IOptions<MassiveStreamOptions>>()
                .Value;

            // H6 (Task 12 pre-flight): MassiveStreamClient's own constructor already calls
            // options.Validate(), so calling it again here was a duplicate, not a stronger
            // guarantee -- and the duplicate's own exception message was indistinguishable from
            // the constructor's.
            return new MassiveStreamClient(options);
        });

        return services;
    }

    /// <summary>
    /// Reports drops and reconnects on the application's logger.
    /// </summary>
    /// <param name="stream">The stream to watch.</param>
    /// <param name="logger">The logger.</param>
    /// <remarks>
    /// D-W4's accepted cost is that a consumer who never reads a subscription's own
    /// <c>DroppedCount</c> loses data quietly. This is what narrows it: rule 8 confines
    /// <c>Microsoft.Extensions.*</c> to this package, so the bridge lives here and core never
    /// learns that logging exists.
    /// <para>
    /// F7 (Task 12 review round 1): this used to take one <c>MassiveTopicSubscription&lt;T&gt;</c>
    /// and read its <c>DroppedCount</c> directly, which had no correct calling pattern for a
    /// consumer streaming more than one topic -- calling it once per subscription registered a
    /// separate <c>Reconnected</c> handler each time, so every reconnect was logged once per topic;
    /// calling it once covered only the one topic it was given, so every other topic's drops went
    /// unreported. <see cref="MassiveStockStream.DropObserved"/> now carries the topic code and
    /// that topic's own running count, so one subscription here, regardless of how many topics the
    /// stream ever opens, reports every one of them honestly.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="stream"/> or <paramref name="logger"/> is <see langword="null"/>.
    /// </exception>
    public static void LogStreamHealth(this MassiveStockStream stream, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(logger);

        stream.Reconnected += count => LogReconnected(logger, count);

        // Per-topic, not a single scalar: DropObserved now multiplexes every topic the stream
        // opens over one event, so the "how much is genuinely new since I last logged" delta has
        // to be tracked per topic code, not once for the whole stream -- otherwise a trades drop
        // and a quotes drop interleaved on the same event would each appear to be the other's
        // continuation. Read and written only from DropObserved's own handler, which the read loop
        // invokes one call at a time, so no further synchronization is needed here.
        Dictionary<string, long> reported = new(StringComparer.Ordinal);

        stream.DropObserved += (topicCode, droppedCount) =>
        {
            long previouslyReported = reported.TryGetValue(topicCode, out long value) ? value : 0;

            if (droppedCount > previouslyReported)
            {
                LogDropped(logger, droppedCount - previouslyReported, topicCode);
                reported[topicCode] = droppedCount;
            }
        };
    }

    // CA1848: LoggerMessage delegates rather than the plain ILogger.LogWarning(...) extension,
    // which allocates a params array and boxes every argument on every call. Neither message ever
    // interpolates anything key-shaped -- a reconnect count, a topic's wire code, and a drop count,
    // never options.ApiKey or anything derived from it (rule 11).
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The Massive stream reconnected ({Count} so far). Messages sent while it was down were missed.")]
    private static partial void LogReconnected(ILogger logger, int count);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The Massive stream dropped {Dropped} events on topic {TopicCode} because a topic buffer was full. "
            + "Raise TopicBufferCapacity or do less work in the consuming loop.")]
    private static partial void LogDropped(ILogger logger, long dropped, string topicCode);
}
