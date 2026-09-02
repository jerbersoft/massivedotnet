using System.Text.Json;
using Xunit;

namespace MassiveDotNet.CodeGen.Tests;

/// <summary>
/// The harness generates from inline documents. This proves the plumbing before any diagnostic
/// test depends on it.
/// </summary>
public sealed class HarnessTests
{
    [Fact]
    public void GeneratesAModelFromAnInlineFragment()
    {
        string spec = Harness.Document(new Operation("ListThings", "/v1/things", Harness.Envelope("""
            {
              "type": "object",
              "required": ["name"],
              "properties": {
                "name": { "type": "string" },
                "score": { "type": "number" }
              }
            }
            """)));

        string map = Harness.MapDocument(
            """
            "Thing": { "schema": { "operationId": "ListThings", "pointer": "results/items" } }
            """,
            Harness.Endpoint("ListThings", "Thing"));

        Dictionary<string, string> files = Harness.Generate(spec, map);

        string model = files[Path.Combine("Models", "Thing.g.cs")];
        Assert.Contains("public required string Name { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("public double? Score { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("public Task<Thing[]> ListThingsAsync(", files["ReferenceGroup.g.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsAModelReferenceFromAPropertyRow()
    {
        Map map = Map.Parse(Harness.MapDocument(
            """
            "Thing": {
              "schema": { "operationId": "ListThings", "pointer": "results/items" },
              "properties": { "publisher": { "name": "Publisher", "model": "Publisher" } }
            }
            """,
            Harness.Endpoint("ListThings", "Thing")));

        MapProperty row = map.Models[0].Properties["publisher"];
        Assert.Equal("Publisher", row.Model);
        Assert.Null(row.Type);
    }

    [Fact]
    public void ReadsAnOmittedPropertyAsTheBody()
    {
        Map map = Map.Parse(Harness.MapDocument(
            """
            "Thing": { "schema": { "operationId": "GetThing" } }
            """,
            """
            {
              "operationId": "GetThing",
              "group": "Reference",
              "method": "GetThing",
              "result": { "kind": "object", "model": "Thing" }
            }
            """));

        Assert.Null(map.Endpoints[0].Result.Property);
        Assert.Equal("object", map.Endpoints[0].Result.Kind);

        // An omitted pointer is the success schema's root, which Spec.Navigate reads as "" (D-S1).
        Assert.Equal("", map.Models[0].SchemaPointer);
        Assert.Null(map.Models[0].Items);
    }

    [Fact]
    public void ReadsItemsFromAModelRow()
    {
        Map map = Map.Parse(Harness.MapDocument(
            """
            "Series": {
              "schema": { "operationId": "ListSeries", "pointer": "results" },
              "items": "values",
              "properties": { "values": { "name": "Values", "model": "Value" } }
            }
            """,
            Harness.Endpoint("ListSeries", "Series")));

        Assert.Equal("values", map.Models[0].Items);
        Assert.Equal("results", map.Endpoints[0].Result.Property);
    }

    // The expected shape travels as a string: SchemaShape is internal to the generator, and an
    // internal type cannot appear in a public test method's signature.
    [Theory]
    [InlineData("""{ "type": "string" }""", "Scalar")]
    [InlineData("""{ "enum": ["asc", "desc"] }""", "Scalar")]
    [InlineData("""{ "type": "object" }""", "Object")]
    [InlineData("""{ "properties": { "a": { "type": "string" } } }""", "Object")]
    [InlineData("""{ "allOf": [ { "properties": { "a": { "type": "string" } } } ] }""", "Object")]
    [InlineData("""{ "type": "array", "items": { "type": "string" } }""", "Array")]
    [InlineData("""{ "type": "array" }""", "Array")]
    [InlineData("""{ "type": "array", "items": { "type": "object" } }""", "ArrayOfObjects")]
    [InlineData("""{ "type": "array", "items": { "properties": { "a": { "type": "string" } } } }""", "ArrayOfObjects")]
    [InlineData("""{ "type": "array", "items": { "type": "array", "items": { "type": "string" } } }""", "ArrayOfArrays")]
    public void ClassifiesSchemaShapes(string schema, string expected)
    {
        using JsonDocument document = JsonDocument.Parse(schema);
        Assert.Equal(Enum.Parse<SchemaShape>(expected), Spec.Shape(document.RootElement));
    }
}
