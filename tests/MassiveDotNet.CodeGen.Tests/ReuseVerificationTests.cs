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
}
