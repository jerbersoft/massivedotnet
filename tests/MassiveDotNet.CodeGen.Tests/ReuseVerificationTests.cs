using Xunit;

namespace MassiveDotNet.CodeGen.Tests;

/// <summary>
/// A model name may cover only one shape. Every site that names a model is compared with the
/// schema the model was generated from: names exactly, model-required properties required at the
/// site, recursively; never scalar types (D-N4).
/// </summary>
public sealed class ReuseVerificationTests
{
    /// <summary>The publisher <c>Publisher</c> is generated from, on the first operation.</summary>
    private const string OriginPublisher = """
        {
          "type": "object",
          "required": ["name"],
          "properties": {
            "name":    { "type": "string" },
            "url":     { "type": "string" },
            "address": { "type": "object", "properties": { "city": { "type": "string" }, "zip": { "type": "string" } } }
          }
        }
        """;

    private static string Item(string publisher) => $$"""
        { "type": "object", "properties": { "title": { "type": "string" }, "publisher": {{publisher}} } }
        """;

    /// <summary>Two operations: things, whose publisher defines the model, and others, which reuse it.</summary>
    private static string Document(string sitePublisher) => Harness.Document(
        new Operation("ListThings", "/v1/things", Harness.Envelope(Item(OriginPublisher))),
        new Operation("ListOthers", "/v1/others", Harness.Envelope(Item(sitePublisher))));

    private static readonly string MapDocument = Harness.MapDocument(
        """
        "Thing":     { "schema": { "operationId": "ListThings", "pointer": "results/items" }, "properties": { "publisher": { "model": "Publisher" } } },
        "Other":     { "schema": { "operationId": "ListOthers", "pointer": "results/items" }, "properties": { "publisher": { "model": "Publisher" } } },
        "Publisher": { "schema": { "operationId": "ListThings", "pointer": "results/items/publisher" }, "properties": { "address": { "model": "Address" } } },
        "Address":   { "schema": { "operationId": "ListThings", "pointer": "results/items/publisher/address" } }
        """,
        Harness.Endpoint("ListThings", "Thing") + "," + Harness.Endpoint("ListOthers", "Other"));

    [Fact]
    public void ASiteIdenticalToTheModelPasses()
    {
        Dictionary<string, string> files = Harness.Generate(Document(OriginPublisher), MapDocument);

        Assert.Contains("public Publisher? Publisher { get; init; }", files[Path.Combine("Models", "Other.g.cs")], StringComparison.Ordinal);
    }

    [Fact]
    public void ASiteWhoseScalarTypesDifferPasses()
    {
        // The model row is the curated truth for types; the description disagrees with itself at
        // sites that are plainly the same thing.
        string site = OriginPublisher.Replace("\"url\":     { \"type\": \"string\" }", "\"url\":     { \"type\": \"integer\" }", StringComparison.Ordinal);

        Dictionary<string, string> files = Harness.Generate(Document(site), MapDocument);

        Assert.Contains("Other.g.cs", string.Join(";", files.Keys), StringComparison.Ordinal);
    }

    [Fact]
    public void ASiteRequiringMoreThanTheModelPasses()
    {
        string site = OriginPublisher.Replace("\"required\": [\"name\"]", "\"required\": [\"name\", \"url\"]", StringComparison.Ordinal);

        Dictionary<string, string> files = Harness.Generate(Document(site), MapDocument);

        Assert.Contains("Other.g.cs", string.Join(";", files.Keys), StringComparison.Ordinal);
    }

    [Fact]
    public void AnExtraPropertyAtTheSiteIsRefused()
    {
        string site = OriginPublisher.Replace("\"url\":     { \"type\": \"string\" },", "\"url\": { \"type\": \"string\" }, \"extra\": { \"type\": \"string\" },", StringComparison.Ordinal);

        string message = Harness.Refusal(Document(site), MapDocument);

        Assert.Contains("Model 'Other' (operation 'ListOthers'): property 'publisher' names model 'Publisher'", message, StringComparison.Ordinal);
        Assert.Contains("generated from operation 'ListThings' at 'results/items/publisher'", message, StringComparison.Ordinal);
        Assert.Contains("'extra' is declared at the site but not on the model", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingPropertyAtTheSiteIsRefused()
    {
        string site = OriginPublisher.Replace("\"url\":     { \"type\": \"string\" },", "", StringComparison.Ordinal);

        string message = Harness.Refusal(Document(site), MapDocument);

        Assert.Contains("'url' is on the model but not declared at the site", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AModelRequiredPropertyOptionalAtTheSiteIsRefused()
    {
        string site = OriginPublisher.Replace("\"required\": [\"name\"],", "", StringComparison.Ordinal);

        string message = Harness.Refusal(Document(site), MapDocument);

        Assert.Contains("'name' is required on the model but optional at the site", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMismatchTwoLevelsDownIsRefused()
    {
        string site = OriginPublisher.Replace(", \"zip\": { \"type\": \"string\" }", "", StringComparison.Ordinal);

        string message = Harness.Refusal(Document(site), MapDocument);

        Assert.Contains("'address/zip' is on the model but not declared at the site", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AShapeMismatchIsRefused()
    {
        string site = OriginPublisher.Replace(
            "\"address\": { \"type\": \"object\", \"properties\": { \"city\": { \"type\": \"string\" }, \"zip\": { \"type\": \"string\" } } }",
            "\"address\": { \"type\": \"string\" }",
            StringComparison.Ordinal);

        string message = Harness.Refusal(Document(site), MapDocument);

        Assert.Contains("'address' is an object on the model but a scalar at the site", message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDifferenceIsListedTogether()
    {
        string site = OriginPublisher
            .Replace("\"required\": [\"name\"],", "", StringComparison.Ordinal)
            .Replace(", \"zip\": { \"type\": \"string\" }", "", StringComparison.Ordinal);

        string message = Harness.Refusal(Document(site), MapDocument);

        Assert.Contains("'name' is required on the model but optional at the site", message, StringComparison.Ordinal);
        Assert.Contains("'address/zip' is on the model but not declared at the site", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An array-shaped property versus a scalar one at the reuse site is a shape mismatch just as
    /// object-versus-scalar is (F3): array-ness is a shape fact, not a scalar type.
    /// </summary>
    [Fact]
    public void AnArrayVersusScalarMismatchAtTheSiteIsRefused()
    {
        string origin = OriginPublisher.Replace(
            "\"url\":     { \"type\": \"string\" },",
            "\"url\":     { \"type\": \"string\" }, \"tags\": { \"type\": \"array\", \"items\": { \"type\": \"string\" } },",
            StringComparison.Ordinal);

        string site = origin.Replace(
            "\"tags\": { \"type\": \"array\", \"items\": { \"type\": \"string\" } }",
            "\"tags\": { \"type\": \"string\" }",
            StringComparison.Ordinal);

        string document = Harness.Document(
            new Operation("ListThings", "/v1/things", Harness.Envelope(Item(origin))),
            new Operation("ListOthers", "/v1/others", Harness.Envelope(Item(site))));

        string message = Harness.Refusal(document, MapDocument);

        Assert.Contains("'tags' is an array of scalars on the model but a scalar at the site", message, StringComparison.Ordinal);
    }
}

/// <summary>
/// An endpoint's <c>result</c> row names a model the same way a property row does, and is compared
/// with the schema the model was generated from the same way (F1): a model name may cover only one
/// shape, whether it is reused as a property or as the endpoint's own result.
/// </summary>
public sealed class ResultReuseVerificationTests
{
    /// <summary>The item schema <c>Thing</c> is generated from, on the origin operation.</summary>
    private const string OriginItem = """
        { "type": "object", "properties": { "title": { "type": "string" } } }
        """;

    /// <summary>An item schema that adds a scalar and a nested object the model does not declare.</summary>
    private const string ItemWithExtras = """
        {
          "type": "object",
          "properties": {
            "title":  { "type": "string" },
            "extra":  { "type": "string" },
            "nested": { "type": "object", "properties": { "x": { "type": "string" } } }
          }
        }
        """;

    /// <summary>An endpoint row whose result reuses model <c>Thing</c>, with its own method name.</summary>
    private static string ResultEndpoint(string operationId, string method) => $$"""
        {
          "operationId": "{{operationId}}",
          "group": "Reference",
          "method": "{{method}}",
          "result": { "kind": "array", "model": "Thing", "property": "results" },
          "parameters": {}
        }
        """;

    /// <summary>Two operations: things, whose items define the model, and more-things, which reuse it.</summary>
    private static string Document(string secondSiteItem) => Harness.Document(
        new Operation("ListThings", "/v1/things", Harness.Envelope(OriginItem)),
        new Operation("ListMoreThings", "/v1/more-things", Harness.Envelope(secondSiteItem)));

    private static readonly string MapDocument = Harness.MapDocument(
        """
        "Thing": { "schema": { "operationId": "ListThings", "pointer": "results/items" } }
        """,
        ResultEndpoint("ListThings", "ListThings") + "," + ResultEndpoint("ListMoreThings", "ListMoreThings"));

    [Fact]
    public void AResultSiteIdenticalToTheModelPasses()
    {
        Dictionary<string, string> files = Harness.Generate(Document(OriginItem), MapDocument);

        Assert.Contains("public Thing[]? Results { get; init; }", files["Envelopes.g.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void AResultSiteWithExtraPropertiesIsRefused()
    {
        string message = Harness.Refusal(Document(ItemWithExtras), MapDocument);

        Assert.Contains("Endpoint 'ListMoreThings' (operation 'ListMoreThings'): result names model 'Thing'", message, StringComparison.Ordinal);
        Assert.Contains("generated from operation 'ListThings' at 'results/items'", message, StringComparison.Ordinal);
        Assert.Contains("'extra' is declared at the site but not on the model", message, StringComparison.Ordinal);
        Assert.Contains("'nested' is declared at the site but not on the model", message, StringComparison.Ordinal);
    }
}
