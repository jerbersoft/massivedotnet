namespace MassiveDotNet.Extensions.DependencyInjection;

/// <summary>
/// Records that the HTTP pipeline has already been built, so a second <c>AddMassive</c> call
/// layers another options delegate without attaching a second authentication handler.
/// </summary>
/// <remarks>
/// A dedicated type rather than a probe for one of the registered services: a caller who
/// registered <c>MassiveHttpTransport</c> themselves would otherwise silently suppress the whole
/// pipeline.
/// </remarks>
internal sealed class MassiveRegistrationMarker;
