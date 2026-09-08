using MassiveDotNet.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MassiveDotNet.Extensions.DependencyInjection.Tests;

public class AddMassiveStreamTests
{
    // A ninth inconsistency the pre-flight rulings do not name (Task 12 report): the brief's own
    // code sample disposes the provider with a synchronous `using`, but MassiveStreamClient is
    // IAsyncDisposable only (by design -- see MassiveStreamClient's own remarks). The container
    // then refuses a synchronous Dispose() on a scope holding an IAsyncDisposable-only singleton,
    // throwing InvalidOperationException from the TEST'S own cleanup, not from anything under test
    // -- watched failing exactly that way before this fix. `await using` is the correct disposal
    // for this container, matching what MassiveStreamClient itself asks of its own callers.
    [Fact]
    public async Task ItRegistersTheClientAsASingleton()
    {
        ServiceCollection services = new();
        services.AddMassiveStream(options => options.ApiKey = "k");

        await using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<MassiveStreamClient>(),
            provider.GetRequiredService<MassiveStreamClient>());
    }

    // H7 (Task 12 pre-flight): renamed from ItValidatesTheOptionsEagerly, which claimed validation
    // at REGISTRATION -- AddMassiveStream itself never calls Validate(); it only configures the
    // options delegate. What actually validates is MassiveStreamClient's own constructor, run the
    // first time the singleton factory above is invoked, i.e. at first RESOLUTION. The test body is
    // unchanged; only the name now says what it checks.
    [Fact]
    public void ItValidatesTheOptionsAtFirstResolution()
    {
        ServiceCollection services = new();
        services.AddMassiveStream(options => options.ApiKey = null);

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(provider.GetRequiredService<MassiveStreamClient>);
    }

    // H7: renamed from TheLoggingBridgeIsRegisteredOnlyInThisPackage, which named no bridge at all
    // and, correctly, asserted something else entirely -- rule 8's actual claim: the WebSocket
    // assembly (where MassiveStreamClient itself lives) references no Microsoft.Extensions.*
    // package.
    //
    // LogStreamHealth itself is not exercised end-to-end from this project: MassiveStockStream's
    // constructor, and the internal MassiveWebSocketFactory-driven ConnectStocksAsync overload the
    // WebSocket test project uses to drive one offline, are both internal to MassiveDotNet.WebSocket
    // with InternalsVisibleTo granted only to its own test project -- so building a connected stream
    // here would need a live socket. Reviewed instead by inspection: LogStreamHealth's two log calls
    // interpolate only a reconnect count and a drop count, never options.ApiKey or anything derived
    // from it (see the Task 12 report).
    [Fact]
    public void TheWebSocketAssemblyReferencesNoMicrosoftExtensions()
    {
        Assert.DoesNotContain(
            typeof(MassiveStreamClient).Assembly.GetReferencedAssemblies(),
            reference => reference.Name?.StartsWith("Microsoft.Extensions", StringComparison.Ordinal) == true);
    }

    // H6 (Task 12 pre-flight): the brief's AddMassiveStream called services.AddLogging(), even
    // though nothing in the registration resolves a logger -- the bridge (LogStreamHealth) is an
    // extension method the consumer calls by hand, not a registered service. Silently adding
    // logging services to a consumer's container is a side effect nobody asked for; this asserts
    // the registration adds nothing logging-shaped.
    [Fact]
    public void ItDoesNotRegisterAnyLoggingServices()
    {
        ServiceCollection services = new();
        services.AddMassiveStream(options => options.ApiKey = "k");

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType.Namespace?.StartsWith("Microsoft.Extensions.Logging", StringComparison.Ordinal) == true);
    }
}
