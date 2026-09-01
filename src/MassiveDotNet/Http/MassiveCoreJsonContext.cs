using System.Text.Json.Serialization;

namespace MassiveDotNet.Http;

/// <summary>
/// Source-generated serialization metadata for types owned by the core package. Using a
/// context rather than reflection keeps the SDK Native AOT compatible.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(MassiveErrorPayload))]
internal sealed partial class MassiveCoreJsonContext : JsonSerializerContext;
