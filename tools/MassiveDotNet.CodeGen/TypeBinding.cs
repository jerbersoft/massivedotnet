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

            // NodaTime types are the SDK's temporal vocabulary (constitution rule 12).
            // Their wire forms are fixed literals, so they need no percent-escaping.
            "LocalDate" or "Instant" =>
                new TypeBinding(type, "AppendPathLiteral", "ToWireValue()"),

            // Numeric segments use the builder's numeric overloads, which format in place.
            "int" or "long" =>
                new TypeBinding(type, "AppendPathSegment", null),

            _ =>
                new TypeBinding(type, "AppendPathSegment", null),
        };
    }

    /// <summary>
    /// The element types a filter can render. This mirrors the dispatch in
    /// <c>RequestUriBuilder.AppendElement</c>; extend the two together.
    /// </summary>
    private static readonly HashSet<string> FilterElementTypes =
        new(StringComparer.Ordinal) { "string", "int", "long", "double", "LocalDate", "DateOrTimestamp" };

    /// <summary>
    /// Resolves the binding for a comparator group: the filter type over the field's element
    /// type, which the map may override on the base field's row.
    /// </summary>
    /// <exception cref="InvalidOperationException">The element type is outside the set the builder renders.</exception>
    public static TypeBinding ResolveFilter(ComparatorGroup group, string? mapType, JsonElement schema, string operationId)
    {
        string element = mapType ?? FromSchema(schema);

        if (!FilterElementTypes.Contains(element))
        {
            throw new InvalidOperationException(
                $"Operation '{operationId}' filters on '{group.BaseName}' with element type '{element}', which "
                + $"RequestUriBuilder cannot render. Supported: {string.Join(", ", FilterElementTypes.Order(StringComparer.Ordinal))}. "
                + $"Set 'type' on the '{group.BaseName}' row in specs/endpoints.map.json to one of these -- the element "
                + "type, never the filter type.");
        }

        // The builder overload takes the filter itself, so there is no wire conversion to name.
        // Filters are query-only by construction, so the path method is never consulted.
        return new TypeBinding($"{group.FilterType(operationId)}<{element}>", "AppendPathSegment", null);
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
            // A calendar date is a LocalDate on both parameters and model properties (D-F7, D-F9).
            "string" => format == "date" ? "LocalDate" : "string",
            _ => "string",
        };
    }
}
