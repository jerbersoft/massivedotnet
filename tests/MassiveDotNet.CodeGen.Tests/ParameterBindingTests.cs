using Xunit;

namespace MassiveDotNet.CodeGen.Tests;

/// <summary>Parameters with no default binding are refused, naming the row to fix (D-N3, D-N7).</summary>
public sealed class ParameterBindingTests
{
    private const string Item = """{ "type": "object", "properties": { "name": { "type": "string" } } }""";

    private static string Document(string parameters) =>
        Harness.Document(new Operation("ListThings", "/v1/things", Harness.Envelope(Item), parameters));

    private static string MapDocument(string parameters = "{}") => Harness.MapDocument(
        """
        "Thing": { "schema": { "operationId": "ListThings", "pointer": "results/items" } }
        """,
        Harness.Endpoint("ListThings", "Thing", parameters));

    [Fact]
    public void AnObjectParameterHasNoDefaultBinding()
    {
        string spec = Document("""[ { "name": "filter", "in": "query", "schema": { "type": "object" } } ]""");

        string message = Harness.Refusal(spec, MapDocument());

        Assert.Contains("Operation 'ListThings'", message, StringComparison.Ordinal);
        Assert.Contains("parameter 'filter' is an object", message, StringComparison.Ordinal);
        Assert.Contains("Set \"type\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADateTimeParameterHasNoDefaultBinding()
    {
        string spec = Document("""[ { "name": "since", "in": "query", "schema": { "type": "string", "format": "date-time" } } ]""");

        string message = Harness.Refusal(spec, MapDocument());

        Assert.Contains("parameter 'since' is a date-time string", message, StringComparison.Ordinal);
        Assert.Contains("RFC 3339", message, StringComparison.Ordinal);
        Assert.Contains("Set \"type\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADateTimeComparatorGroupHasNoDefaultBinding()
    {
        // All four range suffixes, so the group resolves to RangeFilter before the element type
        // is asked for; a lone suffix would be refused earlier as an unrecognised shape.
        string spec = Document("""
            [
              { "name": "since",     "in": "query", "schema": { "type": "string", "format": "date-time" } },
              { "name": "since.gt",  "in": "query", "schema": { "type": "string", "format": "date-time" } },
              { "name": "since.gte", "in": "query", "schema": { "type": "string", "format": "date-time" } },
              { "name": "since.lt",  "in": "query", "schema": { "type": "string", "format": "date-time" } },
              { "name": "since.lte", "in": "query", "schema": { "type": "string", "format": "date-time" } }
            ]
            """);

        string message = Harness.Refusal(spec, MapDocument());

        Assert.Contains("parameter 'since' is a date-time string", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMapTypeSatisfiesTheRefusal()
    {
        string spec = Document("""[ { "name": "since", "in": "query", "schema": { "type": "string", "format": "date-time" } } ]""");

        Dictionary<string, string> files = Harness.Generate(spec, MapDocument("""{ "since": { "type": "LocalDate" } }"""));

        Assert.Contains("LocalDate? since = null", files["ReferenceGroup.g.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void AnArrayOfStringsStaysAStringArray()
    {
        string spec = Document("""[ { "name": "tickers", "in": "query", "schema": { "type": "array", "items": { "type": "string" } } } ]""");

        Dictionary<string, string> files = Harness.Generate(spec, MapDocument());

        string group = files["ReferenceGroup.g.cs"];
        Assert.Contains("string[]? tickers = null", group, StringComparison.Ordinal);
        // The plain query line resolves to the builder's array overload, which renders the
        // elements comma-joined (D19); no array-specific emission exists.
        Assert.Contains("builder.AppendQuery(\"tickers\", tickers);", group, StringComparison.Ordinal);
    }

    [Fact]
    public void AnArrayOfAnUnsupportedElementHasNoBinding()
    {
        string spec = Document("""[ { "name": "flags", "in": "query", "schema": { "type": "array", "items": { "type": "boolean" } } } ]""");

        string message = Harness.Refusal(spec, MapDocument());

        Assert.Contains("Operation 'ListThings'", message, StringComparison.Ordinal);
        Assert.Contains("parameter 'flags' is an array of 'bool'", message, StringComparison.Ordinal);
        Assert.Contains("Set \"type\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASeriesTypeParameterRendersItsWireValue()
    {
        string spec = Document("""[ { "name": "series_type", "in": "query", "schema": { "type": "string", "enum": ["open", "high", "low", "close"] } } ]""");

        Dictionary<string, string> files = Harness.Generate(spec, MapDocument("""{ "series_type": { "name": "seriesType", "type": "SeriesType" } }"""));

        string group = files["ReferenceGroup.g.cs"];
        Assert.Contains("SeriesType? seriesType = null", group, StringComparison.Ordinal);
        Assert.Contains("builder.AppendQuery(\"series_type\", seriesType?.ToWireValue());", group, StringComparison.Ordinal);
    }
}
