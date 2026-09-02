using System.Text.Json;

namespace MassiveDotNet.CodeGen;

/// <summary>A property row: a .NET name, and either a verbatim type or a model the schema binds to (D-N2).</summary>
internal sealed record MapProperty(string? Name, string? Type, string? Model, string? Summary);

/// <summary>A model row: the schema it is generated from, its properties, and, for the page object of a paginated singular result, the property that carries the page's items (D-S2).</summary>
/// <param name="SchemaPointer">The path from the success schema root, or empty for the root itself, which a body payload binds to (D-S1).</param>
/// <param name="Items">The wire name of the array property whose elements <c>Enumerate</c> yields, or <see langword="null"/>.</param>
internal sealed record MapModel(
    string Name,
    string Kind,
    string? Summary,
    string? Remarks,
    string SchemaOperationId,
    string SchemaPointer,
    Dictionary<string, MapProperty> Properties,
    string? Items);

internal sealed record MapParameter(string? Name, string? Type);

/// <summary>An endpoint's payload: one model or an array of it, on a named envelope property or as the body itself (D-S1).</summary>
/// <param name="Kind"><c>array</c> or <c>object</c>.</param>
/// <param name="Property">The envelope property that holds the payload, or <see langword="null"/> when the body is the payload.</param>
internal sealed record MapResult(string Kind, string Model, string? Property);

internal sealed record MapEndpoint(
    string OperationId,
    string Group,
    string Method,
    string? Summary,
    string? Remarks,
    MapResult Result,
    Dictionary<string, MapParameter> Parameters);

internal sealed record MapGroup(string Name, string? Summary);

/// <summary>Reads the curated operation map that supplies what the OpenAPI description lacks.</summary>
internal sealed class Map
{
    public required List<MapGroup> Groups { get; init; }

    public required List<MapModel> Models { get; init; }

    public required List<MapEndpoint> Endpoints { get; init; }

    public static Map Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Parses a map document. The generator loads from disk; tests hand in fragments.</summary>
    public static Map Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

        JsonElement root = document.RootElement;

        List<MapGroup> groups = [];
        foreach (JsonProperty group in root.GetProperty("groups").EnumerateObject())
        {
            groups.Add(new MapGroup(group.Name, String(group.Value, "summary")));
        }

        List<MapModel> models = [];
        foreach (JsonProperty model in root.GetProperty("models").EnumerateObject())
        {
            JsonElement schema = model.Value.GetProperty("schema");
            Dictionary<string, MapProperty> properties = new(StringComparer.Ordinal);

            if (model.Value.TryGetProperty("properties", out JsonElement declared))
            {
                foreach (JsonProperty property in declared.EnumerateObject())
                {
                    properties[property.Name] = new MapProperty(
                        String(property.Value, "name"),
                        String(property.Value, "type"),
                        String(property.Value, "model"),
                        String(property.Value, "summary"));
                }
            }

            models.Add(new MapModel(
                model.Name,
                String(model.Value, "kind") ?? "class",
                String(model.Value, "summary"),
                String(model.Value, "remarks"),
                schema.GetProperty("operationId").GetString()!,
                // An omitted pointer is the success schema root, which Spec.Navigate reads as an
                // empty path: the model is the response body itself (D-S1).
                String(schema, "pointer") ?? string.Empty,
                properties,
                String(model.Value, "items")));
        }

        List<MapEndpoint> endpoints = [];
        foreach (JsonElement endpoint in root.GetProperty("endpoints").EnumerateArray())
        {
            JsonElement result = endpoint.GetProperty("result");
            Dictionary<string, MapParameter> parameters = new(StringComparer.Ordinal);

            if (endpoint.TryGetProperty("parameters", out JsonElement declared))
            {
                foreach (JsonProperty parameter in declared.EnumerateObject())
                {
                    parameters[parameter.Name] = new MapParameter(
                        String(parameter.Value, "name"),
                        String(parameter.Value, "type"));
                }
            }

            endpoints.Add(new MapEndpoint(
                endpoint.GetProperty("operationId").GetString()!,
                endpoint.GetProperty("group").GetString()!,
                endpoint.GetProperty("method").GetString()!,
                String(endpoint, "summary"),
                String(endpoint, "remarks"),
                new MapResult(
                    result.GetProperty("kind").GetString()!,
                    result.GetProperty("model").GetString()!,
                    String(result, "property")),
                parameters));
        }

        return new Map { Groups = groups, Models = models, Endpoints = endpoints };
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
