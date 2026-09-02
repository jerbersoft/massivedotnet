using Xunit;

namespace MassiveDotNet.CodeGen.Tests;

/// <summary>How a model property's schema becomes a C# type (D-N2, D-N3, D-N5, D-N6, D-N7).</summary>
public sealed class ModelBindingTests
{
    /// <summary>A document with one operation whose result items have the given schema.</summary>
    private static string Document(string item) =>
        Harness.Document(new Operation("ListThings", "/v1/things", Harness.Envelope(item)));

    /// <summary>A map declaring <c>Thing</c> over the result items, with the given property rows and extra models.</summary>
    private static string MapDocument(string thingProperties = "{}", string otherModels = "") => Harness.MapDocument(
        $$"""
        "Thing": {
          "schema": { "operationId": "ListThings", "pointer": "results/items" },
          "properties": {{thingProperties}}
        }{{(otherModels.Length == 0 ? "" : "," + otherModels)}}
        """,
        Harness.Endpoint("ListThings", "Thing"));

    private static string Thing(Dictionary<string, string> files) => files[Path.Combine("Models", "Thing.g.cs")];

    [Fact]
    public void BindsArraysByTheirElement()
    {
        string spec = Document("""
            {
              "type": "object",
              "required": ["tags"],
              "properties": {
                "tags":   { "type": "array", "items": { "type": "string" } },
                "scores": { "type": "array", "items": { "type": "number" } },
                "counts": { "type": "array", "items": { "type": "integer", "format": "int64" } },
                "dates":  { "type": "array", "items": { "type": "string", "format": "date" } },
                "loose":  { "type": "array" }
              }
            }
            """);

        string model = Thing(Harness.Generate(spec, MapDocument()));

        Assert.Contains("public required string[] Tags { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("public double[]? Scores { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("public long[]? Counts { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("public LocalDate[]? Dates { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("public string[]? Loose { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("using NodaTime;", model, StringComparison.Ordinal);
    }

    [Fact]
    public void BindsADateTimeStringToInstant()
    {
        string spec = Document("""
            {
              "type": "object",
              "required": ["published"],
              "properties": {
                "published": { "type": "string", "format": "date-time" },
                "updated":   { "type": "string", "format": "date-time" }
              }
            }
            """);

        string model = Thing(Harness.Generate(spec, MapDocument()));

        // A value type: required, but no modifier (D-N6).
        Assert.Contains("public Instant Published { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("public Instant? Updated { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("using NodaTime;", model, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistersBothConvertersOnTheContext()
    {
        string context = Harness.Generate(Document("""{ "type": "object" }"""), MapDocument())["MassiveRestJsonContext.g.cs"];

        Assert.Contains("typeof(LocalDateJsonConverter), typeof(InstantJsonConverter)", context, StringComparison.Ordinal);
    }
}
