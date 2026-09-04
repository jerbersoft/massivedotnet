namespace MassiveDotNet.Extensions.DependencyInjection;

/// <summary>
/// A handler that adds nothing, registered where a resilience handler would sit when the caller
/// has not opted into that feature.
/// </summary>
/// <remarks>
/// <c>AddHttpMessageHandler</c> builds the pipeline once, from delegates that run before any
/// request is sent, and offers no way to decline a slot after the fact. Registering a forwarding
/// handler keeps the pipeline's shape independent of configuration, which matters because the
/// order of the slots is what D30 turns on: conditionally skipping a registration would make the
/// positions of the remaining handlers depend on which options happened to be set.
/// </remarks>
internal sealed class PassThroughHandler : DelegatingHandler
{
}
