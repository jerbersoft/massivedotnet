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
    /// <param name="subscription">The subscription whose drop count to report.</param>
    /// <param name="logger">The logger.</param>
    /// <typeparam name="T">The event type.</typeparam>
    /// <remarks>
    /// D-W4's accepted cost is that a consumer who never reads <c>DroppedCount</c> loses data
    /// quietly. This is what narrows it: rule 8 confines <c>Microsoft.Extensions.*</c> to this
    /// package, so the bridge lives here and core never learns that logging exists.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="stream"/>, <paramref name="subscription"/>, or <paramref name="logger"/> is
    /// <see langword="null"/>.
    /// </exception>
    public static void LogStreamHealth<T>(
        this MassiveStockStream stream,
        MassiveTopicSubscription<T> subscription,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(logger);

        long reported = 0;

        stream.Reconnected += count => LogReconnected(logger, count);

        stream.DropObserved += () =>
        {
            long dropped = subscription.DroppedCount;

            if (dropped > reported)
            {
                LogDropped(logger, dropped - reported);
                reported = dropped;
            }
        };
    }

    // CA1848: LoggerMessage delegates rather than the plain ILogger.LogWarning(...) extension,
    // which allocates a params array and boxes every argument on every call. Neither message ever
    // interpolates anything key-shaped -- a reconnect count and a drop count, never
    // options.ApiKey or anything derived from it (rule 11).
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The Massive stream reconnected ({Count} so far). Messages sent while it was down were missed.")]
    private static partial void LogReconnected(ILogger logger, int count);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The Massive stream dropped {Dropped} events because a topic buffer was full. "
            + "Raise TopicBufferCapacity or do less work in the consuming loop.")]
    private static partial void LogDropped(ILogger logger, long dropped);
}
