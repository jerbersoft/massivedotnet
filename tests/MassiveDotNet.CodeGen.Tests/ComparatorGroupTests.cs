using Xunit;

namespace MassiveDotNet.CodeGen.Tests;

/// <summary>
/// The refusals a comparator group can raise, and the two spec shapes the whole-branch review of
/// the filter work found the plan had wrong (#34). The refusals predate these tests, which pin
/// them so a regression in a diagnostic is caught here rather than by whoever next maps a group.
/// </summary>
public sealed class ComparatorGroupTests
{
    private const string Item = """{ "type": "object", "properties": { "name": { "type": "string" } } }""";

    private static string Document(string parameters) =>
        Harness.Document(new Operation("ListThings", "/v1/things", Harness.Envelope(Item), parameters));

    private static string MapDocument(string parameters = "{}") => Harness.MapDocument(
        """
        "Thing": { "schema": { "operationId": "ListThings", "pointer": "results/items" } }
        """,
        Harness.Endpoint("ListThings", "Thing", parameters));

    private static string Variants(string name, string schema, params string[] suffixes) =>
        string.Join(",\n", suffixes.Select(suffix => $$"""{ "name": "{{name}}.{{suffix}}", "in": "query", "schema": {{schema}} }"""));

    [Fact]
    public void AnUnknownSuffixSetIsRefused()
    {
        string spec = Document($$"""
            [
              { "name": "ticker", "in": "query", "schema": { "type": "string" } },
              {{Variants("ticker", """{ "type": "string" }""", "gt", "any_of")}}
            ]
            """);

        string message = Harness.Refusal(spec, MapDocument());

        Assert.Contains("Operation 'ListThings' declares comparators [any_of gt] on 'ticker'", message, StringComparison.Ordinal);
        Assert.Contains("not a recognised shape", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFilterElementOutsideTheClosedSetIsRefused()
    {
        string spec = Document($$"""
            [
              { "name": "flag", "in": "query", "schema": { "type": "boolean" } },
              {{Variants("flag", """{ "type": "boolean" }""", "gt", "gte", "lt", "lte")}}
            ]
            """);

        string message = Harness.Refusal(spec, MapDocument());

        Assert.Contains("Operation 'ListThings' filters on 'flag' with element type 'bool'", message, StringComparison.Ordinal);
        Assert.Contains("the element type, never the filter type", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARequiredBaseFieldCarryingComparatorsIsRefused()
    {
        string spec = Document($$"""
            [
              { "name": "ticker", "in": "query", "required": true, "schema": { "type": "string" } },
              {{Variants("ticker", """{ "type": "string" }""", "gt", "gte", "lt", "lte")}}
            ]
            """);

        string message = Harness.Refusal(spec, MapDocument());

        Assert.Contains("Operation 'ListThings' requires 'ticker', which also carries comparators", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABaseLessGroupThatIsNotAnyOfOnlyIsRefused()
    {
        string spec = Document($$"""
            [ {{Variants("date", """{ "type": "string", "format": "date" }""", "gt", "gte", "lt", "lte")}} ]
            """);

        string message = Harness.Refusal(spec, MapDocument());

        Assert.Contains("Operation 'ListThings' declares 'date' with no plain field", message, StringComparison.Ordinal);
        Assert.Contains("RangeFilter<T>, not SetFilter<T>", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABaseLessAnyOfOnlyGroupPassesTheExactFormFlag()
    {
        // The one shape that may lack a plain field (/v1/summaries): equality travels as a
        // one-element any_of, which the builder's hasExactForm parameter selects.
        string spec = Document($$"""
            [ {{Variants("ticker", """{ "type": "string" }""", "any_of")}} ]
            """);

        string group = Harness.Generate(spec, MapDocument())["ReferenceGroup.g.cs"];

        Assert.Contains("SetFilter<string>? ticker = null", group, StringComparison.Ordinal);
        Assert.Contains("builder.AppendQuery(\"ticker\", ticker, hasExactForm: false);", group, StringComparison.Ordinal);
        Assert.Contains("Accepts one or more values.", group, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ticker.gte")]
    [InlineData("tickr")]
    public void ARowKeyedByAVariantOrAnUndeclaredNameIsRefused(string key)
    {
        string spec = Document($$"""
            [
              { "name": "ticker", "in": "query", "schema": { "type": "string" } },
              {{Variants("ticker", """{ "type": "string" }""", "gt", "gte", "lt", "lte")}}
            ]
            """);

        string message = Harness.Refusal(spec, MapDocument($$"""{ "{{key}}": { "name": "symbol" } }"""));

        Assert.Contains($"Operation 'ListThings' maps parameter '{key}', which it does not declare", message, StringComparison.Ordinal);
        Assert.Contains("comparator variants such as 'ticker.gte' are grouped under 'ticker'", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABaseDeclaredAfterItsVariantsStillSourcesTheGroup()
    {
        // /v3/snapshot/indices declares ticker.any_of ahead of ticker. The base carries the prose
        // and the exact form; the group stands where its first member was declared.
        string spec = Document("""
            [
              { "name": "ticker.any_of", "in": "query", "description": "Any of these.", "schema": { "type": "string" } },
              { "name": "limit", "in": "query", "schema": { "type": "integer" } },
              { "name": "ticker", "in": "query", "description": "The ticker.", "schema": { "type": "string" } }
            ]
            """);

        string group = Harness.Generate(spec, MapDocument())["ReferenceGroup.g.cs"];

        Assert.Contains("        SetFilter<string>? ticker = null,\n        int? limit = null,\n", group, StringComparison.Ordinal);
        Assert.Contains("The ticker. Accepts an exact value or a set of values.", group, StringComparison.Ordinal);
        Assert.DoesNotContain("Any of these.", group, StringComparison.Ordinal);
    }

    [Fact]
    public void AVariantTypedStringOverANumericBaseBindsByTheBase()
    {
        // Benzinga earnings types importance.any_of as a string, the comma-joined wire form of a
        // set, while the base is an integer. The base's schema is the element type.
        string spec = Document($$"""
            [
              { "name": "importance", "in": "query", "schema": { "type": "integer" } },
              {{Variants("importance", """{ "type": "integer" }""", "gt", "gte", "lt", "lte")}},
              { "name": "importance.any_of", "in": "query", "schema": { "type": "string" } }
            ]
            """);

        string group = Harness.Generate(spec, MapDocument())["ReferenceGroup.g.cs"];

        Assert.Contains("Filter<int>? importance = null", group, StringComparison.Ordinal);
    }
}
