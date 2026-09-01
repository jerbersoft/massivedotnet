using System.Text.Json.Serialization;
using MassiveDotNet.Http;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// A minimal paged envelope, so transport-level traversal can be tested without depending on
/// any particular generated endpoint.
/// </summary>
internal sealed class FakePage : IPagedEnvelope<int>
{
    [JsonPropertyName("results")]
    public int[]? Results { get; init; }

    [JsonPropertyName("next_url")]
    public string? NextUrl { get; init; }
}

/// <summary>Source-generated metadata for the test envelopes, so no test relies on reflection.</summary>
[JsonSerializable(typeof(FakePage))]
internal sealed partial class TestJsonContext : JsonSerializerContext;
