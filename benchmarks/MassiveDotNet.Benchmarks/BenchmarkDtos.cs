using System.Text.Json.Serialization;

namespace MassiveDotNet.Benchmarks;

/// <summary>
/// A bar with the same eight fields as <c>MassiveDotNet.Rest.Models.Agg</c>, declared as a
/// <see langword="record"/> class rather than a <see langword="readonly record struct"/>.
/// This is the shape D4 rejected for tick-level types, and the arm that says what that cost.
/// </summary>
public sealed record ClassAgg
{
    [JsonPropertyName("o")]
    public double Open { get; init; }

    [JsonPropertyName("h")]
    public double High { get; init; }

    [JsonPropertyName("l")]
    public double Low { get; init; }

    [JsonPropertyName("c")]
    public double Close { get; init; }

    [JsonPropertyName("v")]
    public double Volume { get; init; }

    [JsonPropertyName("vw")]
    public double? VolumeWeightedAveragePrice { get; init; }

    [JsonPropertyName("t")]
    public long TimestampMilliseconds { get; init; }

    [JsonPropertyName("n")]
    public long? TransactionCount { get; init; }

    [JsonPropertyName("otc")]
    public bool IsOtc { get; init; }
}

/// <summary>
/// The same bar as a struct, so the class arm is compared against an identical local baseline
/// rather than against the SDK. The difference between this and <see cref="ClassAgg"/> is D4;
/// the difference between this and the SDK's own path is what the SDK adds on top.
/// </summary>
public readonly record struct StructAgg
{
    [JsonPropertyName("o")]
    public double Open { get; init; }

    [JsonPropertyName("h")]
    public double High { get; init; }

    [JsonPropertyName("l")]
    public double Low { get; init; }

    [JsonPropertyName("c")]
    public double Close { get; init; }

    [JsonPropertyName("v")]
    public double Volume { get; init; }

    [JsonPropertyName("vw")]
    public double? VolumeWeightedAveragePrice { get; init; }

    [JsonPropertyName("t")]
    public long TimestampMilliseconds { get; init; }

    [JsonPropertyName("n")]
    public long? TransactionCount { get; init; }

    [JsonPropertyName("otc")]
    public bool IsOtc { get; init; }
}

public sealed record ClassAggResponse
{
    [JsonPropertyName("results")]
    public ClassAgg[]? Results { get; init; }

    [JsonPropertyName("next_url")]
    public string? NextUrl { get; init; }

    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }
}

public sealed record StructAggResponse
{
    [JsonPropertyName("results")]
    public StructAgg[]? Results { get; init; }

    [JsonPropertyName("next_url")]
    public string? NextUrl { get; init; }

    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }
}

/// <summary>
/// Source generation, not reflection — the baselines have to be serialized the same way the SDK
/// is, or the comparison would measure the serializer mode instead of the shape of the row.
/// </summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ClassAggResponse))]
[JsonSerializable(typeof(StructAggResponse))]
internal sealed partial class BenchmarkJsonContext : JsonSerializerContext;
