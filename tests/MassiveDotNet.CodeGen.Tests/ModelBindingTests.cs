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

    private const string Publisher = """
        {
          "type": "object",
          "required": ["name"],
          "properties": {
            "name": { "type": "string" },
            "url":  { "type": "string" }
          }
        }
        """;

    private const string Insight = """
        {
          "type": "object",
          "required": ["ticker"],
          "properties": { "ticker": { "type": "string" } }
        }
        """;

    private static string Article(string requiredNames) => $$"""
        {
          "type": "object",
          "required": [{{requiredNames}}],
          "properties": {
            "title":     { "type": "string" },
            "publisher": {{Publisher}},
            "insights":  { "type": "array", "items": {{Insight}} }
          }
        }
        """;

    private const string NestedModels = """
        "Publisher": { "schema": { "operationId": "ListThings", "pointer": "results/items/publisher" } },
        "Insight":   { "schema": { "operationId": "ListThings", "pointer": "results/items/insights/items" } }
        """;

    private const string NestedRows = """
        { "publisher": { "model": "Publisher" }, "insights": { "model": "Insight" } }
        """;

    [Fact]
    public void PropertiesFollowTheMapsDeclarationOrder()
    {
        // The description stores properties alphabetically; the map's order keeps related fields
        // together (OHLCV). Rows the map does not declare follow, in the description's order.
        string spec = Document("""
            { "type": "object", "properties": { "a": { "type": "string" }, "b": { "type": "string" }, "c": { "type": "string" } } }
            """);

        string thing = Thing(Harness.Generate(spec, MapDocument("""{ "c": { "name": "Third" }, "a": { "name": "First" } }""")));

        int c = thing.IndexOf("[JsonPropertyName(\"c\")]", StringComparison.Ordinal);
        int a = thing.IndexOf("[JsonPropertyName(\"a\")]", StringComparison.Ordinal);
        int b = thing.IndexOf("[JsonPropertyName(\"b\")]", StringComparison.Ordinal);
        Assert.True(c >= 0 && c < a && a < b, $"expected c, a, b; found offsets {c}, {a}, {b}");
    }

    [Fact]
    public void ADuplicatePropertyRowIsRefused()
    {
        string message = Harness.Refusal(
            Document("""{ "type": "object", "properties": { "a": { "type": "string" } } }"""),
            MapDocument("""{ "a": { "name": "One" }, "a": { "name": "Two" } }"""));

        Assert.Contains("Model 'Thing' declares a row for 'a' more than once", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnboundObjectPropertyNamesTheRowToAdd()
    {
        string message = Harness.Refusal(Document(Article("\"publisher\"")), MapDocument());

        Assert.Contains("Operation 'ListThings': property 'publisher' of model 'Thing' is an object with no binding", message, StringComparison.Ordinal);
        Assert.Contains("\"schema\": { \"operationId\": \"ListThings\", \"pointer\": \"results/items/publisher\" }", message, StringComparison.Ordinal);
        Assert.Contains("set \"model\": \"<Name>\" on the 'publisher' row of 'Thing'", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnboundArrayOfObjectsNamesTheItemsPointer()
    {
        string map = MapDocument("""{ "publisher": { "model": "Publisher" } }""", NestedModels);

        string message = Harness.Refusal(Document(Article("")), map);

        Assert.Contains("property 'insights' of model 'Thing' is an array of objects with no binding", message, StringComparison.Ordinal);
        Assert.Contains("\"pointer\": \"results/items/insights/items\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnboundEnvelopeObjectSaysHowToBindIt()
    {
        string spec = Harness.Document(new Operation("ListThings", "/v1/things", """
            {
              "type": "object",
              "properties": {
                "results": { "type": "array", "items": { "type": "object", "properties": { "name": { "type": "string" } } } },
                "meta":    { "type": "object", "properties": { "count": { "type": "integer" } } }
              }
            }
            """));

        string message = Harness.Refusal(spec, MapDocument());

        Assert.Contains("Operation 'ListThings': envelope property 'meta' is an object", message, StringComparison.Ordinal);
        Assert.Contains("set \"property\": \"meta\" on the result row", message, StringComparison.Ordinal);
        Assert.Contains("omit \"property\" and declare the body as the result", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposesARequiredObjectAndAnOptionalArray()
    {
        Dictionary<string, string> files = Harness.Generate(Document(Article("\"publisher\"")), MapDocument(NestedRows, NestedModels));

        string thing = Thing(files);
        Assert.Contains("public required Publisher Publisher { get; init; }", thing, StringComparison.Ordinal);
        Assert.Contains("public Insight[]? Insights { get; init; }", thing, StringComparison.Ordinal);

        string publisher = files[Path.Combine("Models", "Publisher.g.cs")];
        Assert.Contains("public sealed partial record Publisher", publisher, StringComparison.Ordinal);
        Assert.Contains("public required string Name { get; init; }", publisher, StringComparison.Ordinal);
        Assert.Contains("public string? Url { get; init; }", publisher, StringComparison.Ordinal);
        Assert.Contains("public required string Ticker { get; init; }", files[Path.Combine("Models", "Insight.g.cs")], StringComparison.Ordinal);
    }

    [Fact]
    public void ComposesAnOptionalObjectAndARequiredArray()
    {
        string thing = Thing(Harness.Generate(Document(Article("\"insights\"")), MapDocument(NestedRows, NestedModels)));

        Assert.Contains("public Publisher? Publisher { get; init; }", thing, StringComparison.Ordinal);
        Assert.Contains("public required Insight[] Insights { get; init; }", thing, StringComparison.Ordinal);
    }

    [Fact]
    public void AStructModelIsNeverMarkedRequired()
    {
        string models = """
            "Publisher": { "kind": "struct", "schema": { "operationId": "ListThings", "pointer": "results/items/publisher" } },
            "Insight":   { "schema": { "operationId": "ListThings", "pointer": "results/items/insights/items" } }
            """;

        string thing = Thing(Harness.Generate(Document(Article("\"publisher\"")), MapDocument(NestedRows, models)));

        Assert.Contains("public Publisher Publisher { get; init; }", thing, StringComparison.Ordinal);
        Assert.DoesNotContain("required Publisher", thing, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelAndTypeOnOneRowConflict()
    {
        string map = MapDocument("""{ "publisher": { "model": "Publisher", "type": "Publisher" }, "insights": { "model": "Insight" } }""", NestedModels);

        string message = Harness.Refusal(Document(Article("")), map);

        Assert.Contains("property 'publisher' carries both \"model\" and \"type\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AModelMustBeDeclared()
    {
        string map = MapDocument("""{ "publisher": { "model": "Nope" }, "insights": { "model": "Insight" } }""", NestedModels);

        string message = Harness.Refusal(Document(Article("")), map);

        Assert.Contains("property 'publisher' names model 'Nope', which is not declared", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AModelOnAScalarIsRefused()
    {
        string map = MapDocument("""{ "title": { "model": "Publisher" }, "publisher": { "model": "Publisher" }, "insights": { "model": "Insight" } }""", NestedModels);

        string message = Harness.Refusal(Document(Article("")), map);

        Assert.Contains("property 'title' names model 'Publisher', but its schema is a scalar", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFreeFormObjectTakesAVerbatimTypeAndIsARequiredReference()
    {
        string spec = Document("""
            {
              "type": "object",
              "required": ["counts"],
              "properties": { "counts": { "type": "object", "description": "A map of exchange id to size." } }
            }
            """);

        string thing = Thing(Harness.Generate(spec, MapDocument("""{ "counts": { "type": "Dictionary<string, double>" } }""")));

        Assert.Contains("public required Dictionary<string, double> Counts { get; init; }", thing, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueTypeIsNeverMarkedRequired()
    {
        string spec = Document("""
            {
              "type": "object",
              "required": ["when", "count", "flag", "ratio"],
              "properties": {
                "when":  { "type": "string", "format": "date" },
                "count": { "type": "integer" },
                "flag":  { "type": "boolean" },
                "ratio": { "type": "number" }
              }
            }
            """);

        string thing = Thing(Harness.Generate(spec, MapDocument()));

        Assert.Contains("public LocalDate When { get; init; }", thing, StringComparison.Ordinal);
        Assert.Contains("public int Count { get; init; }", thing, StringComparison.Ordinal);
        Assert.Contains("public bool Flag { get; init; }", thing, StringComparison.Ordinal);
        Assert.Contains("public double Ratio { get; init; }", thing, StringComparison.Ordinal);
        Assert.DoesNotContain("required", thing, StringComparison.Ordinal);
    }

    [Fact]
    public void SeesThroughAOneBranchOneOf()
    {
        // The ticker events items are the description's one response-side oneOf, and it has a
        // single branch. Without the unwrap the array has no item type and binds string[] (D-R2).
        string spec = Document("""
            {
              "type": "object",
              "properties": {
                "name":   { "type": "string" },
                "events": {
                  "type": "array",
                  "items": {
                    "oneOf": [
                      {
                        "type": "object",
                        "required": ["date"],
                        "properties": {
                          "date":          { "type": "string", "format": "date" },
                          "ticker_change": { "type": "object", "properties": { "ticker": { "type": "string" } } }
                        }
                      }
                    ]
                  }
                }
              }
            }
            """);

        Dictionary<string, string> files = Harness.Generate(spec, MapDocument(
            """{ "events": { "name": "Events", "model": "Event" } }""",
            """
            "Event":  { "schema": { "operationId": "ListThings", "pointer": "results/items/events/items" }, "properties": { "ticker_change": { "name": "Change", "model": "Change" } } },
            "Change": { "schema": { "operationId": "ListThings", "pointer": "results/items/events/items/ticker_change" } }
            """));

        Assert.Contains("public Event[]? Events { get; init; }", Thing(files), StringComparison.Ordinal);

        string @event = files[Path.Combine("Models", "Event.g.cs")];
        Assert.Contains("public LocalDate Date { get; init; }", @event, StringComparison.Ordinal);
        Assert.Contains("public Change? Change { get; init; }", @event, StringComparison.Ordinal);
        Assert.Contains("public string? Ticker { get; init; }", files[Path.Combine("Models", "Change.g.cs")], StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsAScalarUnionAsAScalar()
    {
        // The news parameters declare a two-branch oneOf of strings, and parameters go through
        // the same Shape. A union of scalars must keep reading as a scalar, or news stops
        // generating. This passed before the unwrap existed and pins that the refusal below is
        // narrower than "any multi-branch oneOf".
        string spec = Document("""
            {
              "type": "object",
              "properties": {
                "when": { "oneOf": [ { "type": "string" }, { "type": "string", "format": "date-time" } ] }
              }
            }
            """);

        Assert.Contains("public string? When { get; init; }", Thing(Harness.Generate(spec, MapDocument())), StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAUnionWithAnObjectBranch()
    {
        string spec = Document("""
            {
              "type": "object",
              "properties": {
                "payload": {
                  "oneOf": [
                    { "type": "object", "properties": { "a": { "type": "string" } } },
                    { "type": "object", "properties": { "b": { "type": "string" } } }
                  ]
                }
              }
            }
            """);

        string message = Harness.Refusal(spec, MapDocument());

        Assert.Contains("oneOf with 2 branches", message, StringComparison.Ordinal);
        Assert.Contains("D24", message, StringComparison.Ordinal);
    }
}
