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
    /// <param name="mapType">The map row's <c>type</c>, or <see langword="null"/> to derive one.</param>
    /// <param name="schema">The parameter's schema.</param>
    /// <param name="operationId">The operation, named in any diagnostic.</param>
    /// <param name="parameterName">The parameter's wire name, named in any diagnostic.</param>
    public static TypeBinding Resolve(string? mapType, JsonElement schema, string operationId, string parameterName)
    {
        string type = mapType ?? DefaultParameterType(schema, operationId, parameterName);

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
        string element = mapType ?? DefaultParameterType(schema, operationId, group.BaseName);

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

    /// <summary>
    /// Whether a schema binds only through the map: an object, an array of objects, or an array
    /// of arrays (D-N3). Callers check this before <see cref="FromSchema"/> and raise a diagnostic
    /// that names the operation and field.
    /// </summary>
    public static bool NeedsModel(JsonElement schema) =>
        Spec.Shape(schema) is SchemaShape.Object or SchemaShape.ArrayOfObjects or SchemaShape.ArrayOfArrays;

    /// <summary>Derives the default C# type for a scalar or array-of-scalar schema node.</summary>
    /// <exception cref="InvalidOperationException">
    /// The node is a shape with no default. This is a generator bug, not a map error: every caller
    /// checks <see cref="NeedsModel"/> first and produces a diagnostic with context.
    /// </exception>
    public static string FromSchema(JsonElement schema)
    {
        if (NeedsModel(schema))
        {
            throw new InvalidOperationException(
                "An object or nested array schema has no default binding. The caller must check NeedsModel "
                + "and raise a diagnostic naming the operation and field.");
        }

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
            // An array binds by its element (D-N5); one with no item schema defaults like a typeless node.
            "array" => schema.TryGetProperty("items", out JsonElement items) ? $"{FromSchema(items)}[]" : "string[]",
            // A calendar date is a LocalDate on parameters and models alike (D-F7, D-F9); a
            // timestamp is an Instant on models (D-N7). Parameters never reach the Instant arm:
            // DefaultParameterType refuses a date-time parameter before asking here.
            "string" => format switch
            {
                "date" => "LocalDate",
                "date-time" => "Instant",
                _ => "string",
            },
            _ => "string",
        };
    }

    /// <summary>
    /// The default type for a parameter, refusing the two shapes that have none (D-N3, D-N7).
    /// </summary>
    /// <remarks>
    /// A date-time parameter is refused because the parameter-side <c>Instant</c> renders Unix
    /// milliseconds (<c>ToWireValue</c>), which is not RFC 3339. The map chooses the form; the
    /// only such parameters in the description are the news <c>published_utc</c> family, which
    /// carry no top-level type and so default to string, and which the map binds to LocalDate.
    /// </remarks>
    private static string DefaultParameterType(JsonElement schema, string operationId, string parameterName)
    {
        if (NeedsModel(schema))
        {
            throw new InvalidOperationException(
                $"Operation '{operationId}': parameter '{parameterName}' is {Spec.Describe(Spec.Shape(schema))}, which has no "
                + "default binding. Set \"type\" on its row in specs/endpoints.map.json.");
        }

        if (schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("format", out JsonElement format)
            && format.GetString() == "date-time")
        {
            throw new InvalidOperationException(
                $"Operation '{operationId}': parameter '{parameterName}' is a date-time string, which has no default "
                + "binding on a parameter: Instant renders Unix milliseconds on the wire, not RFC 3339. Set \"type\" "
                + "on its row in specs/endpoints.map.json (LocalDate for the calendar-date form).");
        }

        return FromSchema(schema);
    }
}
