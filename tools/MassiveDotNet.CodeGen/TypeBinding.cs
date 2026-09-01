using System.Text.Json;

namespace MassiveDotNet.CodeGen;

/// <summary>
/// Maps an OpenAPI parameter or property onto a C# type, and knows how to render that type
/// into a request URI.
/// </summary>
/// <param name="CSharpType">The C# type name.</param>
/// <param name="PathAppendMethod">
/// The <c>RequestUriBuilder</c> method used for a path segment. Enum wire values are known-safe
/// literals and skip escaping; caller-supplied values are escaped.
/// </param>
/// <param name="WireConversion">An optional member invoked to produce the wire form.</param>
internal sealed record TypeBinding(string CSharpType, string PathAppendMethod, string? WireConversion)
{
    /// <summary>Renders the expression used to append this value to a path segment.</summary>
    public string PathExpression(string identifier) =>
        WireConversion is null ? identifier : $"{identifier}.{WireConversion}";

    /// <summary>Renders the expression used to append this value as a query parameter.</summary>
    public string QueryExpression(string identifier) =>
        WireConversion is null ? identifier : $"{identifier}?.{WireConversion}";

    /// <summary>Resolves the binding for a parameter, honouring a map-supplied override.</summary>
    public static TypeBinding Resolve(string? mapType, JsonElement schema)
    {
        string type = mapType ?? FromSchema(schema);

        return type switch
        {
            // Enum wire values are fixed literals, so they need no percent-escaping.
            "AggregateTimespan" or "SortOrder" or "MarketType" =>
                new TypeBinding(type, "AppendPathLiteral", "ToWireValue()"),

            "DateOrTimestamp" =>
                new TypeBinding(type, "AppendPathSegment", "ToString()"),

            // Numeric segments use the builder's numeric overloads, which format in place.
            "int" or "long" =>
                new TypeBinding(type, "AppendPathSegment", null),

            _ =>
                new TypeBinding(type, "AppendPathSegment", null),
        };
    }

    /// <summary>Derives the default C# type for an OpenAPI schema node.</summary>
    public static string FromSchema(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("type", out JsonElement type))
        {
            // Some parameters declare only an enum, with no explicit type (for example `sort`).
            return "string";
        }

        string format = schema.TryGetProperty("format", out JsonElement formatValue)
            ? formatValue.GetString() ?? string.Empty
            : string.Empty;

        return type.GetString() switch
        {
            "integer" => format == "int64" ? "long" : "int",
            "number" => "double",
            "boolean" => "bool",
            "array" => "string[]",
            _ => "string",
        };
    }
}
