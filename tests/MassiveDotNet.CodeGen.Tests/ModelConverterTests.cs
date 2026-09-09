using Xunit;

namespace MassiveDotNet.CodeGen.Tests;

/// <summary>
/// The converter a struct model carries, and what the emitter refuses rather than guess at (D32).
/// </summary>
public sealed class ModelConverterTests
{
    /// <summary>A document with one operation whose result items have the given schema.</summary>
    private static string Document(string item) =>
        Harness.Document(new Operation("ListThings", "/v1/things", Harness.Envelope(item)));

    /// <summary>A map declaring <c>Thing</c> over the result items, of the given kind.</summary>
    private static string MapDocument(string kind = "struct", string properties = "{}") => Harness.MapDocument(
        $$"""
        "Thing": {
          "kind": "{{kind}}",
          "schema": { "operationId": "ListThings", "pointer": "results/items" },
          "properties": {{properties}}
        }
        """,
        Harness.Endpoint("ListThings", "Thing"));

    private static string ConverterPath => Path.Combine("Serialization", "ThingJsonConverter.g.cs");

    /// <summary>Every scalar the closed set allows, required and optional, plus an array.</summary>
    private const string EveryShape = """
        {
          "type": "object",
          "required": ["name", "codes"],
          "properties": {
            "name":   { "type": "string" },
            "note":   { "type": "string" },
            "live":   { "type": "boolean" },
            "count":  { "type": "integer" },
            "ticks":  { "type": "integer", "format": "int64" },
            "price":  { "type": "number" },
            "codes":  { "type": "array", "items": { "type": "integer" } }
          }
        }
        """;

    [Fact]
    public void EmitsAConverterForAStructModelAndPointsTheTypeAtIt()
    {
        Dictionary<string, string> files = Harness.Generate(Document(EveryShape), MapDocument());

        Assert.Contains(ConverterPath, files.Keys);
        Assert.Contains(
            "[JsonConverter(typeof(ThingJsonConverter))]\npublic readonly partial record struct Thing",
            files[Path.Combine("Models", "Thing.g.cs")],
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A class model keeps System.Text.Json's own machinery. The struct/class split is decision D4
    /// answering "does this arrive in bulk", so the converter follows it rather than asking again.
    /// </summary>
    [Fact]
    public void EmitsNoConverterForAClassModel()
    {
        Dictionary<string, string> files = Harness.Generate(Document(EveryShape), MapDocument(kind: "class"));

        Assert.DoesNotContain(ConverterPath, files.Keys);
        Assert.DoesNotContain("JsonConverter(typeof(ThingJsonConverter))", files[Path.Combine("Models", "Thing.g.cs")], StringComparison.Ordinal);
        Assert.DoesNotContain("using MassiveDotNet.Rest.Serialization;", files[Path.Combine("Models", "Thing.g.cs")], StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsEachScalarThroughItsOwnReader()
    {
        string converter = Harness.Generate(Document(EveryShape), MapDocument())[ConverterPath];

        Assert.Contains("""name = JsonValueReader.ReadString(ref reader, "Thing", "name");""", converter, StringComparison.Ordinal);
        Assert.Contains("""note = JsonValueReader.ReadString(ref reader, "Thing", "note");""", converter, StringComparison.Ordinal);
        Assert.Contains("""live = JsonValueReader.ReadNullableBoolean(ref reader, "Thing", "live");""", converter, StringComparison.Ordinal);
        Assert.Contains("""count = JsonValueReader.ReadNullableInt32(ref reader, "Thing", "count");""", converter, StringComparison.Ordinal);
        Assert.Contains("""ticks = JsonValueReader.ReadNullableInt64(ref reader, "Thing", "ticks");""", converter, StringComparison.Ordinal);
        Assert.Contains("""price = JsonValueReader.ReadNullableDouble(ref reader, "Thing", "price");""", converter, StringComparison.Ordinal);
        Assert.Contains("codes = IntArrays.Read(ref reader, typeof(int[]), options);", converter, StringComparison.Ordinal);
    }

    /// <summary>The pooled converter is one shared static, not a fresh instance per row.</summary>
    [Fact]
    public void HoldsOnePooledConverterPerElementType()
    {
        string converter = Harness.Generate(Document(EveryShape), MapDocument())[ConverterPath];

        Assert.Contains("private static readonly PooledArrayConverter<int> IntArrays = new();", converter, StringComparison.Ordinal);
        Assert.Equal(1, converter.Split("PooledArrayConverter<int> IntArrays").Length - 1);
    }

    /// <summary>
    /// Requiredness is a presence check, matching System.Text.Json: the key must appear, and an
    /// explicit null satisfies it. Only the properties the C# type marks <c>required</c> get one.
    /// </summary>
    [Fact]
    public void ChecksThatEveryRequiredPropertyWasPresent()
    {
        string converter = Harness.Generate(Document(EveryShape), MapDocument())[ConverterPath];

        Assert.Contains("""throw new JsonException("Thing is missing the required property 'name'.");""", converter, StringComparison.Ordinal);
        Assert.Contains("""throw new JsonException("Thing is missing the required property 'codes'.");""", converter, StringComparison.Ordinal);
        Assert.DoesNotContain("sawNote", converter, StringComparison.Ordinal);
        Assert.DoesNotContain("sawPrice", converter, StringComparison.Ordinal);
    }

    /// <summary>An unknown property is stepped over whole, so a nested object cannot bleed into the model.</summary>
    [Fact]
    public void SkipsAnUnknownPropertyWhole()
    {
        string converter = Harness.Generate(Document(EveryShape), MapDocument())[ConverterPath];

        Assert.Contains("reader.Skip();", converter, StringComparison.Ordinal);
    }

    /// <summary>A wire name that is a C# keyword yields an escaped local rather than a mangled one.</summary>
    [Fact]
    public void EscapesALocalNamedAfterAKeyword()
    {
        string converter = Harness.Generate(
            Document("""{ "type": "object", "properties": { "lock": { "type": "integer" } } }"""),
            MapDocument())[ConverterPath];

        Assert.Contains("int? @lock = null;", converter, StringComparison.Ordinal);
        Assert.Contains("Lock = @lock,", converter, StringComparison.Ordinal);
    }

    /// <summary>
    /// A type outside the closed set fails generation rather than binding to something that
    /// compiles and throws at deserialization -- the posture D16 takes for an unbound object.
    /// </summary>
    [Fact]
    public void RefusesAStructPropertyItCannotRead()
    {
        string message = Harness.Refusal(
            Document("""{ "type": "object", "properties": { "day": { "type": "string", "format": "date" } } }"""),
            MapDocument());

        Assert.Contains("'Thing' is a struct", message, StringComparison.Ordinal);
        Assert.Contains("'day'", message, StringComparison.Ordinal);
        Assert.Contains("LocalDate", message, StringComparison.Ordinal);
        Assert.Contains("JsonValueReader", message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAStructArrayWhoseElementItCannotRead()
    {
        string message = Harness.Refusal(
            Document("""
                { "type": "object", "properties":
                  { "days": { "type": "array", "items": { "type": "string", "format": "date" } } } }
                """),
            MapDocument());

        Assert.Contains("'days'", message, StringComparison.Ordinal);
        Assert.Contains("element type 'LocalDate'", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same property on a class model is fine, because System.Text.Json reads it. The refusal
    /// is about what the emitter can write, not about what the SDK can represent.
    /// </summary>
    [Fact]
    public void AcceptsOnAClassWhatItRefusesOnAStruct()
    {
        Dictionary<string, string> files = Harness.Generate(
            Document("""{ "type": "object", "properties": { "day": { "type": "string", "format": "date" } } }"""),
            MapDocument(kind: "class"));

        Assert.Contains("public LocalDate? Day { get; init; }", files[Path.Combine("Models", "Thing.g.cs")], StringComparison.Ordinal);
    }

    /// <summary>
    /// A wire name carrying a quote or a backslash cannot be written as a UTF-8 literal, so it is
    /// refused rather than escaped by a scheme nothing in the description would ever exercise.
    /// </summary>
    [Fact]
    public void RefusesAWireNameAUtf8LiteralCannotHold()
    {
        string message = Harness.Refusal(
            Document("""{ "type": "object", "properties": { "we\"ird": { "type": "integer" } } }"""),
            MapDocument());

        Assert.Contains("UTF-8 literals", message, StringComparison.Ordinal);
    }

    /// <summary>A wire string the map types <c>decimal</c>, required and optional (D38).</summary>
    private const string DecimalShape = """
        {
          "type": "object",
          "required": ["ds"],
          "properties": {
            "ds": { "type": "string" },
            "dv": { "type": "string" }
          }
        }
        """;

    private const string DecimalProperties = """
        {
          "ds": { "name": "DecimalSize", "type": "decimal" },
          "dv": { "name": "DecimalVolume", "type": "decimal?" }
        }
        """;

    /// <summary>
    /// The fractional-share family is a number the description declares as a string, so the map
    /// types it <c>decimal</c> and the converter reads it through the round-trip-guarded reader
    /// rather than through <c>ReadString</c> (D38).
    /// </summary>
    [Fact]
    public void ReadsAMapTypedDecimalThroughTheDecimalReader()
    {
        string converter = Harness.Generate(Document(DecimalShape), MapDocument(properties: DecimalProperties))[ConverterPath];

        Assert.Contains("""decimalSize = JsonValueReader.ReadDecimal(ref reader, "Thing", "ds");""", converter, StringComparison.Ordinal);
        Assert.Contains("""decimalVolume = JsonValueReader.ReadNullableDecimal(ref reader, "Thing", "dv");""", converter, StringComparison.Ordinal);
    }

    /// <summary>
    /// It has to go back out as a string. <c>WriteNumber</c> would emit a shape the service never
    /// sends and this SDK's own reader refuses, so a model a caller serialized would not
    /// deserialize.
    /// </summary>
    [Fact]
    public void WritesADecimalBackAsTheStringTheWireSent()
    {
        string converter = Harness.Generate(Document(DecimalShape), MapDocument(properties: DecimalProperties))[ConverterPath];

        Assert.Contains("""JsonValueWriter.WriteDecimalString(writer, "ds", value.DecimalSize);""", converter, StringComparison.Ordinal);
        Assert.Contains("""JsonValueWriter.WriteDecimalString(writer, "dv", decimalVolume);""", converter, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteNumber", converter, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>decimal</c> is a value type, so a required one already has a non-null default and takes
    /// no <c>required</c> modifier -- the same reading every other number on a struct model gets.
    /// </summary>
    [Fact]
    public void TreatsDecimalAsAValueTypeOnTheModel()
    {
        Dictionary<string, string> files = Harness.Generate(Document(DecimalShape), MapDocument(properties: DecimalProperties));

        Assert.Contains("public decimal DecimalSize { get; init; }", files[Path.Combine("Models", "Thing.g.cs")], StringComparison.Ordinal);
        Assert.Contains("public decimal? DecimalVolume { get; init; }", files[Path.Combine("Models", "Thing.g.cs")], StringComparison.Ordinal);
        Assert.DoesNotContain("required decimal", files[Path.Combine("Models", "Thing.g.cs")], StringComparison.Ordinal);
    }

    /// <summary>Round-tripping is preserved: the converter writes every property it reads.</summary>
    [Fact]
    public void WritesEveryPropertyItReads()
    {
        string converter = Harness.Generate(Document(EveryShape), MapDocument())[ConverterPath];

        Assert.Contains("""writer.WriteString("name", value.Name);""", converter, StringComparison.Ordinal);
        Assert.Contains("""writer.WriteNumber("count", count);""", converter, StringComparison.Ordinal);
        Assert.Contains("""writer.WriteNull("count");""", converter, StringComparison.Ordinal);
        Assert.Contains("IntArrays.Write(writer, codes, options);", converter, StringComparison.Ordinal);
    }
}
