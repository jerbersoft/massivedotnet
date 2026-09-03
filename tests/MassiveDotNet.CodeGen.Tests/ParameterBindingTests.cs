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

    [Fact]
    public void ASnapshotDirectionPathParameterRendersItsWireLiteral()
    {
        string spec = Harness.Document(new Operation(
            "ListThings",
            "/v1/things/{direction}",
            Harness.Envelope(Item),
            """[ { "name": "direction", "in": "path", "required": true, "schema": { "type": "string", "enum": ["gainers", "losers"] } } ]"""));

        Dictionary<string, string> files = Harness.Generate(spec, MapDocument("""{ "direction": { "name": "direction", "type": "SnapshotDirection" } }"""));

        string group = files["ReferenceGroup.g.cs"];
        Assert.Contains("SnapshotDirection direction,", group, StringComparison.Ordinal);
        // An enum wire value is a fixed literal, so it takes the unescaped path method, as
        // AggregateTimespan does on the aggregates route.
        Assert.Contains("builder.AppendPathLiteral(direction.ToWireValue());", group, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEnumMemberTheCoreEnumLacksIsRefused()
    {
        string spec = Document("""[ { "name": "order", "in": "query", "schema": { "type": "string", "enum": ["asc", "desc", "random"] } } ]""");

        string message = Harness.Refusal(spec, MapDocument("""{ "order": { "type": "SortOrder" } }"""));

        Assert.Contains("Operation 'ListThings'", message, StringComparison.Ordinal);
        Assert.Contains("parameter 'order' declares [random]", message, StringComparison.Ordinal);
        Assert.Contains("SortOrder", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACoreEnumMayCoverMoreThanTheOperationDeclares()
    {
        // The indicators omit `second` from timespan and deliberately reuse AggregateTimespan
        // (D-S7): the check runs one way, so a member the operation does not accept is the
        // server's to reject, not generation's to refuse. This passed before the check existed
        // and is here so the direction cannot be widened silently.
        string spec = Document("""[ { "name": "timespan", "in": "query", "schema": { "type": "string", "enum": ["minute", "hour", "day"] } } ]""");

        Dictionary<string, string> files = Harness.Generate(spec, MapDocument("""{ "timespan": { "type": "AggregateTimespan" } }"""));

        Assert.Contains("AggregateTimespan? timespan = null", files["ReferenceGroup.g.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void ATypedParameterDropsTheWireFormatSentence()
    {
        string spec = Document("""[ { "name": "date", "in": "query", "description": "The trading date. Value must be formatted 'yyyy-mm-dd'.", "schema": { "type": "string", "format": "date" } } ]""");

        string group = Harness.Generate(spec, MapDocument())["ReferenceGroup.g.cs"];

        Assert.Contains("LocalDate? date = null", group, StringComparison.Ordinal);
        Assert.Contains("The trading date.", group, StringComparison.Ordinal);
        Assert.DoesNotContain("Value must be", group, StringComparison.Ordinal);
    }

    [Fact]
    public void AStringParameterKeepsTheWireFormatSentence()
    {
        // Only a type that states the format earns the drop; a string passes the caller's text
        // through, so the description's own constraint is all the caller has.
        string spec = Document("""[ { "name": "code", "in": "query", "description": "The code. Value must be an integer.", "schema": { "type": "string" } } ]""");

        string group = Harness.Generate(spec, MapDocument())["ReferenceGroup.g.cs"];

        Assert.Contains("The code. Value must be an integer.", group, StringComparison.Ordinal);
    }

    [Fact]
    public void AFilterDropsTheWireFormatSentenceBeforeAddingItsOwn()
    {
        string spec = Document("""
            [
              { "name": "ex_dividend_date",     "in": "query", "description": "The ex-dividend date. Value must be formatted 'yyyy-mm-dd'.", "schema": { "type": "string", "format": "date" } },
              { "name": "ex_dividend_date.gt",  "in": "query", "schema": { "type": "string", "format": "date" } },
              { "name": "ex_dividend_date.gte", "in": "query", "schema": { "type": "string", "format": "date" } },
              { "name": "ex_dividend_date.lt",  "in": "query", "schema": { "type": "string", "format": "date" } },
              { "name": "ex_dividend_date.lte", "in": "query", "schema": { "type": "string", "format": "date" } }
            ]
            """);

        string group = Harness.Generate(spec, MapDocument())["ReferenceGroup.g.cs"];

        Assert.Contains("RangeFilter<LocalDate>? ex_dividend_date = null", group, StringComparison.Ordinal);
        Assert.Contains("The ex-dividend date. Accepts an exact value or a range.", group, StringComparison.Ordinal);
    }

    [Fact]
    public void ADateOrNanosecondsComparatorGroupBindsARangeFilter()
    {
        string spec = Document("""
            [
              { "name": "timestamp",     "in": "query", "schema": { "type": "string" } },
              { "name": "timestamp.gt",  "in": "query", "schema": { "type": "string" } },
              { "name": "timestamp.gte", "in": "query", "schema": { "type": "string" } },
              { "name": "timestamp.lt",  "in": "query", "schema": { "type": "string" } },
              { "name": "timestamp.lte", "in": "query", "schema": { "type": "string" } }
            ]
            """);

        Dictionary<string, string> files = Harness.Generate(spec, MapDocument("""{ "timestamp": { "type": "DateOrNanoseconds" } }"""));

        string group = files["ReferenceGroup.g.cs"];
        Assert.Contains("RangeFilter<DateOrNanoseconds>? timestamp = null", group, StringComparison.Ordinal);
        Assert.Contains("builder.AppendQuery(\"timestamp\", timestamp);", group, StringComparison.Ordinal);
    }

    [Fact]
    public void AContractTypeParameterRendersItsWireValue()
    {
        string spec = Document("""[ { "name": "contract_type", "in": "query", "schema": { "type": "string", "enum": ["call", "put"] } } ]""");

        Dictionary<string, string> files = Harness.Generate(spec, MapDocument("""{ "contract_type": { "name": "contractType", "type": "ContractType" } }"""));

        string group = files["ReferenceGroup.g.cs"];
        Assert.Contains("ContractType? contractType = null", group, StringComparison.Ordinal);
        Assert.Contains("builder.AppendQuery(\"contract_type\", contractType?.ToWireValue());", group, StringComparison.Ordinal);
    }

    [Fact]
    public void AContractTypeMemberTheEnumLacksIsRefused()
    {
        // The #37 check runs for every core enum the table names; this pins that the new row is
        // in the table rather than falling through to the plain-string arm.
        string spec = Document("""[ { "name": "contract_type", "in": "query", "schema": { "type": "string", "enum": ["call", "put", "straddle"] } } ]""");

        string message = Harness.Refusal(spec, MapDocument("""{ "contract_type": { "type": "ContractType" } }"""));

        Assert.Contains("parameter 'contract_type' declares [straddle]", message, StringComparison.Ordinal);
        Assert.Contains("ContractType", message, StringComparison.Ordinal);
    }
}
