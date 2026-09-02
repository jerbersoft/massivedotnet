using Xunit;

namespace MassiveDotNet.CodeGen.Tests;

/// <summary>
/// One generated-shape test per row of the D-S1 table: what the envelope, the group methods, and
/// the serialization context look like for a paginated object with items, a paginated object
/// without them, a plain object, a body object, and a body array.
/// </summary>
public sealed class SingularResultTests
{
    /// <summary>A page object: values, which continue across pages, and an underlying that belongs to each page.</summary>
    private const string Series = """
        {
          "type": "object",
          "properties": {
            "values":     { "type": "array", "items": { "type": "object", "properties": { "timestamp": { "type": "integer", "format": "int64" }, "value": { "type": "number" } } } },
            "underlying": { "type": "object", "properties": { "url": { "type": "string" } } }
          }
        }
        """;

    private const string Trade = """
        { "type": "object", "required": ["p"], "properties": { "p": { "type": "number" }, "s": { "type": "number" } } }
        """;

    private const string SeriesModels = """
        "Series": {
          "schema": { "operationId": "ListSeries", "pointer": "results" },
          "items": "values",
          "properties": {
            "values":     { "name": "Values",     "model": "Value" },
            "underlying": { "name": "Underlying", "model": "Underlying" }
          }
        },
        "Value":      { "kind": "struct", "schema": { "operationId": "ListSeries", "pointer": "results/values/items" } },
        "Underlying": { "schema": { "operationId": "ListSeries", "pointer": "results/underlying" } }
        """;

    /// <summary>An envelope whose <c>results</c> is the given object, with or without a cursor.</summary>
    public static string ObjectEnvelope(string result, bool paginated, bool requestId = true) => $$"""
        {
          "type": "object",
          "required": ["status"],
          "properties": {
            {{(paginated ? "\"next_url\": { \"type\": \"string\" }," : "")}}
            {{(requestId ? "\"request_id\": { \"type\": \"string\" }," : "")}}
            "results": {{result}},
            "status": { "type": "string" }
          }
        }
        """;

    /// <summary>An endpoint row of any kind, with or without a payload property.</summary>
    public static string Endpoint(string operationId, string method, string kind, string model, string? property) => $$"""
        {
          "operationId": "{{operationId}}",
          "group": "Reference",
          "method": "{{method}}",
          "result": { "kind": "{{kind}}", "model": "{{model}}"{{(property is null ? "" : $", \"property\": \"{property}\"")}} }
        }
        """;

    private static string Group(Dictionary<string, string> files) => files["ReferenceGroup.g.cs"];

    [Fact]
    public void APaginatedObjectWithItemsReturnsAPagedResultAndEnumeratesItsItems()
    {
        string spec = Harness.Document(new Operation("ListSeries", "/v1/series", ObjectEnvelope(Series, paginated: true)));
        string map = Harness.MapDocument(SeriesModels, Endpoint("ListSeries", "ListSeries", "object", "Series", "results"));

        Dictionary<string, string> files = Harness.Generate(spec, map);

        string envelopes = files["Envelopes.g.cs"];
        Assert.Contains("using MassiveDotNet.Http;", envelopes, StringComparison.Ordinal);
        Assert.Contains("internal sealed class ListSeriesResponse : IPagedEnvelope<Value>", envelopes, StringComparison.Ordinal);
        Assert.Contains("public Series? Results { get; init; }", envelopes, StringComparison.Ordinal);
        Assert.Contains("Value[]? IPagedEnvelope<Value>.Results => Results?.Values;", envelopes, StringComparison.Ordinal);

        string group = Group(files);
        Assert.Contains("using System.Net;", group, StringComparison.Ordinal);
        Assert.Contains("public IAsyncEnumerable<Value> EnumerateSeriesAsync(", group, StringComparison.Ordinal);
        Assert.Contains("return _transport.EnumerateAsync<ListSeriesResponse, Value>(", group, StringComparison.Ordinal);
        Assert.Contains("public Task<MassivePagedResult<Series>> ListSeriesAsync(", group, StringComparison.Ordinal);
        Assert.Contains("Series result = response?.Results", group, StringComparison.Ordinal);
        Assert.Contains("$\"The response from '{requestUri}' carried no 'results' payload.\",", group, StringComparison.Ordinal);
        Assert.Contains("return new MassivePagedResult<Series>(", group, StringComparison.Ordinal);
        Assert.Contains("    !string.IsNullOrWhiteSpace(response.NextUrl),", group, StringComparison.Ordinal);
        Assert.Contains("    response.RequestId);", group, StringComparison.Ordinal);
        // The remark is longer than the doc writer's 96-column wrap, which puts the line break
        // between "yields" and this clause; the assertion stays inside one emitted line.
        Assert.Contains("each page's <c>values</c> in turn", group, StringComparison.Ordinal);
        Assert.Contains("<returns>Every <c>values</c> entry across every page.</returns>", group, StringComparison.Ordinal);

        Assert.Contains("[JsonSerializable(typeof(ListSeriesResponse))]", files["MassiveRestJsonContext.g.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void APaginatedObjectWithoutItemsEmitsAGuardedGet()
    {
        string spec = Harness.Document(new Operation("GetTrade", "/v1/trade", ObjectEnvelope(Trade, paginated: true)));
        string map = Harness.MapDocument(
            """
            "Trade": { "schema": { "operationId": "GetTrade", "pointer": "results" } }
            """,
            Endpoint("GetTrade", "GetTrade", "object", "Trade", "results"));

        Dictionary<string, string> files = Harness.Generate(spec, map);

        string envelopes = files["Envelopes.g.cs"];
        Assert.DoesNotContain("IPagedEnvelope", envelopes, StringComparison.Ordinal);
        Assert.DoesNotContain("using MassiveDotNet.Http;", envelopes, StringComparison.Ordinal);
        Assert.Contains("public string? NextUrl { get; init; }", envelopes, StringComparison.Ordinal);

        string group = Group(files);
        Assert.Contains("public Task<Trade> GetTradeAsync(", group, StringComparison.Ordinal);
        Assert.Contains("MassiveHttpTransport.ThrowIfUnfollowableCursor(response?.NextUrl, requestUri, response?.RequestId);", group, StringComparison.Ordinal);
        Assert.Contains("return response?.Results", group, StringComparison.Ordinal);
        // No Enumerate is emitted, so the List-prefix rule does not apply and a Get name is accepted (D-S3).
        Assert.DoesNotContain("Enumerate", group, StringComparison.Ordinal);
    }

    [Fact]
    public void AnObjectReturnsTheModelAndThrowsWithoutIt()
    {
        string spec = Harness.Document(new Operation("GetTrade", "/v1/trade", ObjectEnvelope(Trade, paginated: false)));
        string map = Harness.MapDocument(
            """
            "Trade": { "kind": "struct", "schema": { "operationId": "GetTrade", "pointer": "results" } }
            """,
            Endpoint("GetTrade", "GetTrade", "object", "Trade", "results"));

        Dictionary<string, string> files = Harness.Generate(spec, map);

        Assert.Contains("public Trade? Results { get; init; }", files["Envelopes.g.cs"], StringComparison.Ordinal);

        string group = Group(files);
        Assert.Contains("public Task<Trade> GetTradeAsync(", group, StringComparison.Ordinal);
        Assert.Contains("return response?.Results\n", group, StringComparison.Ordinal);
        Assert.Contains("?? throw new MassiveApiException(", group, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.OK,", group, StringComparison.Ordinal);
        Assert.Contains("$\"The response from '{requestUri}' carried no 'results' payload.\",", group, StringComparison.Ordinal);
        Assert.Contains("response?.RequestId);", group, StringComparison.Ordinal);
        Assert.Contains("or with a success that carried no payload.</exception>", group, StringComparison.Ordinal);
        Assert.DoesNotContain("ThrowIfUnfollowableCursor", group, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEnvelopeWithoutARequestIdThrowsWithoutOne()
    {
        string spec = Harness.Document(new Operation("GetTrade", "/v1/trade", ObjectEnvelope(Trade, paginated: false, requestId: false)));
        string map = Harness.MapDocument(
            """
            "Trade": { "schema": { "operationId": "GetTrade", "pointer": "results" } }
            """,
            Endpoint("GetTrade", "GetTrade", "object", "Trade", "results"));

        string group = Group(Harness.Generate(spec, map));

        Assert.Contains("$\"The response from '{requestUri}' carried no 'results' payload.\");", group, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestId", group, StringComparison.Ordinal);
    }

    [Fact]
    public void ABodyObjectDeserializesTheModelDirectly()
    {
        string spec = Harness.Document(new Operation("GetDay", "/v1/day", """
            { "type": "object", "required": ["symbol"], "properties": { "symbol": { "type": "string" }, "status": { "type": "string" }, "open": { "type": "number" } } }
            """));
        string map = Harness.MapDocument(
            """
            "Day": { "schema": { "operationId": "GetDay" } }
            """,
            Endpoint("GetDay", "GetDay", "object", "Day", property: null));

        Dictionary<string, string> files = Harness.Generate(spec, map);

        // No envelope exists, so none is emitted: a file of nothing but usings fails the build (D-S5).
        Assert.False(files.ContainsKey("Envelopes.g.cs"));

        string model = files[Path.Combine("Models", "Day.g.cs")];
        Assert.Contains("public required string Symbol { get; init; }", model, StringComparison.Ordinal);
        Assert.Contains("public string? Status { get; init; }", model, StringComparison.Ordinal);

        string context = files["MassiveRestJsonContext.g.cs"];
        Assert.Contains("using MassiveDotNet.Rest.Models;", context, StringComparison.Ordinal);
        Assert.Contains("[JsonSerializable(typeof(Day))]", context, StringComparison.Ordinal);

        string group = Group(files);
        Assert.Contains("using System.Net;", group, StringComparison.Ordinal);
        Assert.Contains("public Task<Day> GetDayAsync(", group, StringComparison.Ordinal);
        Assert.Contains("Day? response = await _transport", group, StringComparison.Ordinal);
        Assert.Contains("MassiveRestJsonContext.Default.Day, cancellationToken)", group, StringComparison.Ordinal);
        Assert.Contains("return response\n", group, StringComparison.Ordinal);
        Assert.Contains("?? throw new MassiveApiException(", group, StringComparison.Ordinal);
        Assert.Contains("$\"The response from '{requestUri}' carried no payload.\");", group, StringComparison.Ordinal);
        Assert.Contains("<returns>The response body, deserialized as one object.</returns>", group, StringComparison.Ordinal);
    }

    [Fact]
    public void ABodyArrayDeserializesAnArrayOfTheModel()
    {
        string spec = Harness.Document(new Operation("GetHolidays", "/v1/holidays", """
            { "type": "array", "items": { "type": "object", "properties": { "name": { "type": "string" } } } }
            """));
        string map = Harness.MapDocument(
            """
            "Holiday": { "schema": { "operationId": "GetHolidays", "pointer": "items" } }
            """,
            Endpoint("GetHolidays", "ListHolidays", "array", "Holiday", property: null));

        Dictionary<string, string> files = Harness.Generate(spec, map);

        Assert.False(files.ContainsKey("Envelopes.g.cs"));
        Assert.Contains("[JsonSerializable(typeof(Holiday[]))]", files["MassiveRestJsonContext.g.cs"], StringComparison.Ordinal);

        string group = Group(files);
        Assert.DoesNotContain("using System.Net;", group, StringComparison.Ordinal);
        Assert.Contains("public Task<Holiday[]> ListHolidaysAsync(", group, StringComparison.Ordinal);
        Assert.Contains("Holiday[]? response = await _transport", group, StringComparison.Ordinal);
        Assert.Contains("MassiveRestJsonContext.Default.HolidayArray, cancellationToken)", group, StringComparison.Ordinal);
        Assert.Contains("return response ?? [];", group, StringComparison.Ordinal);
        Assert.Contains("<returns>The response body, an array that is empty when the server returned none.</returns>", group, StringComparison.Ordinal);
    }

    [Fact]
    public void ABodyModelSharedByTwoOperationsIsRegisteredOnce()
    {
        const string Day = """{ "type": "object", "properties": { "symbol": { "type": "string" } } }""";

        string spec = Harness.Document(
            new Operation("GetDay", "/v1/day", Day),
            new Operation("GetOtherDay", "/v1/other-day", Day));
        string map = Harness.MapDocument(
            """
            "Day": { "schema": { "operationId": "GetDay" } }
            """,
            Endpoint("GetDay", "GetDay", "object", "Day", property: null) + "," + Endpoint("GetOtherDay", "GetOtherDay", "object", "Day", property: null));

        string context = Harness.Generate(spec, map)["MassiveRestJsonContext.g.cs"];

        Assert.Equal(1, context.Split("[JsonSerializable(typeof(Day))]").Length - 1);
    }

    [Fact]
    public void ABodyModelMayBeReusedAsANestedProperty()
    {
        const string Day = """{ "type": "object", "properties": { "symbol": { "type": "string" } } }""";

        string spec = Harness.Document(
            new Operation("GetDay", "/v1/day", Day),
            new Operation("ListThings", "/v1/things", Harness.Envelope($$"""{ "type": "object", "properties": { "day": {{Day}} } }""")));
        string map = Harness.MapDocument(
            $$"""
            "Day":   { "schema": { "operationId": "GetDay" } },
            "Thing": { "schema": { "operationId": "ListThings", "pointer": "results/items" }, "properties": { "day": { "model": "Day" } } }
            """,
            Endpoint("GetDay", "GetDay", "object", "Day", property: null) + "," + Harness.Endpoint("ListThings", "Thing"));

        Dictionary<string, string> files = Harness.Generate(spec, map);

        Assert.Contains("public Day? Day { get; init; }", files[Path.Combine("Models", "Thing.g.cs")], StringComparison.Ordinal);
    }

    [Fact]
    public void ABodyModelReuseThatDiffersNamesTheBodyAsItsOrigin()
    {
        string spec = Harness.Document(
            new Operation("GetDay", "/v1/day", """{ "type": "object", "properties": { "symbol": { "type": "string" } } }"""),
            new Operation("ListThings", "/v1/things", Harness.Envelope("""{ "type": "object", "properties": { "day": { "type": "object", "properties": { "symbol": { "type": "string" }, "extra": { "type": "string" } } } } }""")));
        string map = Harness.MapDocument(
            """
            "Day":   { "schema": { "operationId": "GetDay" } },
            "Thing": { "schema": { "operationId": "ListThings", "pointer": "results/items" }, "properties": { "day": { "model": "Day" } } }
            """,
            Endpoint("GetDay", "GetDay", "object", "Day", property: null) + "," + Harness.Endpoint("ListThings", "Thing"));

        string message = Harness.Refusal(spec, map);

        Assert.Contains("generated from operation 'GetDay' at the response body", message, StringComparison.Ordinal);
        Assert.Contains("'extra' is declared at the site but not on the model", message, StringComparison.Ordinal);
    }
}

/// <summary>One refusal per rule in D-S6, each naming the fix.</summary>
public sealed class SingularResultRefusalTests
{
    private const string Trade = """
        { "type": "object", "required": ["p"], "properties": { "p": { "type": "number" }, "s": { "type": "number" } } }
        """;

    private const string TradeModel = """
        "Trade": { "schema": { "operationId": "GetTrade", "pointer": "results" } }
        """;

    private static string SeriesDocument(string values) => Harness.Document(new Operation("ListSeries", "/v1/series", SingularResultTests.ObjectEnvelope($$"""
        { "type": "object", "properties": { "values": {{values}} } }
        """, paginated: true)));

    private const string ObjectValues = """
        { "type": "array", "items": { "type": "object", "properties": { "value": { "type": "number" } } } }
        """;

    private static string SeriesMap(string items, string valuesRow) => Harness.MapDocument(
        $$"""
        "Series": {
          "schema": { "operationId": "ListSeries", "pointer": "results" },
          "items": "{{items}}",
          "properties": { "values": {{valuesRow}} }
        },
        "Value": { "schema": { "operationId": "ListSeries", "pointer": "results/values/items" } }
        """,
        SingularResultTests.Endpoint("ListSeries", "ListSeries", "object", "Series", "results"));

    [Fact]
    public void AnObjectKindOnAnArraySiteIsRefused()
    {
        string spec = Harness.Document(new Operation("GetTrade", "/v1/trade", Harness.Envelope(Trade)));
        string map = Harness.MapDocument(
            """
            "Trade": { "schema": { "operationId": "GetTrade", "pointer": "results/items" } }
            """,
            SingularResultTests.Endpoint("GetTrade", "GetTrade", "object", "Trade", "results"));

        string message = Harness.Refusal(spec, map);

        Assert.Contains("Endpoint 'GetTrade' (operation 'GetTrade'): result kind 'object' expects an object at 'results', but the schema there is an array of objects.", message, StringComparison.Ordinal);
        Assert.Contains("Use kind \"array\" for an array of the model.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnArrayKindOnAnObjectSiteIsRefused()
    {
        string spec = Harness.Document(new Operation("GetTrade", "/v1/trade", SingularResultTests.ObjectEnvelope(Trade, paginated: false)));
        string map = Harness.MapDocument(TradeModel, SingularResultTests.Endpoint("GetTrade", "ListTrades", "array", "Trade", "results"));

        string message = Harness.Refusal(spec, map);

        Assert.Contains("result kind 'array' expects an array of objects at 'results', but the schema there is an object.", message, StringComparison.Ordinal);
        Assert.Contains("Use kind \"object\" for a single model.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AScalarBodyIsRefused()
    {
        string spec = Harness.Document(new Operation("GetText", "/v1/text", """{ "type": "string" }"""));
        string map = Harness.MapDocument(
            """
            "Text": { "schema": { "operationId": "GetText" } }
            """,
            SingularResultTests.Endpoint("GetText", "GetText", "object", "Text", property: null));

        string message = Harness.Refusal(spec, map);

        Assert.Contains("expects an object at the response body, but the schema there is a scalar.", message, StringComparison.Ordinal);
        Assert.Contains("Only an object or an array of objects can be a result.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownKindIsRefused()
    {
        string spec = Harness.Document(new Operation("GetTrade", "/v1/trade", SingularResultTests.ObjectEnvelope(Trade, paginated: false)));
        string map = Harness.MapDocument(TradeModel, SingularResultTests.Endpoint("GetTrade", "GetTrade", "envelope", "Trade", "results"));

        string message = Harness.Refusal(spec, map);

        Assert.Contains("result kind 'envelope' is not recognised. Use \"array\" for an array of the model or \"object\" for a single one", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AResultPropertyTheSchemaDoesNotDeclareIsRefused()
    {
        string spec = Harness.Document(new Operation("GetTrade", "/v1/trade", SingularResultTests.ObjectEnvelope(Trade, paginated: false)));
        string map = Harness.MapDocument(TradeModel, SingularResultTests.Endpoint("GetTrade", "GetTrade", "object", "Trade", "payload"));

        string message = Harness.Refusal(spec, map);

        Assert.Contains("result names property 'payload', which the success schema does not declare.", message, StringComparison.Ordinal);
        Assert.Contains("omit \"property\" when the body itself is the payload", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ItemsNamingAnAbsentPropertyIsRefused()
    {
        string message = Harness.Refusal(SeriesDocument(ObjectValues), SeriesMap("points", """{ "model": "Value" }"""));

        Assert.Contains("Model 'Series' (operation 'ListSeries'): \"items\" names 'points', which the schema at 'results' does not declare.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ItemsNamingAnArrayOfScalarsIsRefused()
    {
        string spec = SeriesDocument("""{ "type": "array", "items": { "type": "number" } }""");

        string message = Harness.Refusal(spec, SeriesMap("values", """{ "type": "double[]" }"""));

        Assert.Contains("\"items\" names 'values', which is an array of scalars, not an array of objects.", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ItemsWhoseRowNamesNoModelIsRefused()
    {
        // A verbatim type keeps the array-of-objects property bound (D-N3), so this reaches the
        // items check rather than the unbound-object refusal that runs first.
        string message = Harness.Refusal(SeriesDocument(ObjectValues), SeriesMap("values", """{ "type": "object[]" }"""));

        Assert.Contains("\"items\" names 'values', whose row does not name a \"model\".", message, StringComparison.Ordinal);
        Assert.Contains("that model is what Enumerate yields", message, StringComparison.Ordinal);
    }

    [Fact]
    public void APaginatedObjectWithItemsNeedsAListName()
    {
        string message = Harness.Refusal(SeriesDocument(ObjectValues), SeriesMap("values", """{ "model": "Value" }""")
            .Replace("\"method\": \"ListSeries\"", "\"method\": \"GetSeries\"", StringComparison.Ordinal));

        Assert.Contains("Operation 'ListSeries' is paginated, so it emits an Enumerate counterpart, but its mapped method 'GetSeries' is not List-prefixed", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABodyPayloadWithACursorIsRefused()
    {
        string spec = Harness.Document(new Operation("GetDay", "/v1/day", """
            { "type": "object", "properties": { "symbol": { "type": "string" }, "next_url": { "type": "string" } } }
            """));
        string map = Harness.MapDocument(
            """
            "Day": { "schema": { "operationId": "GetDay" } }
            """,
            SingularResultTests.Endpoint("GetDay", "GetDay", "object", "Day", property: null));

        string message = Harness.Refusal(spec, map);

        Assert.Contains("result declares no \"property\", so the body is the payload, but the success schema declares next_url at its root.", message, StringComparison.Ordinal);
    }
}
