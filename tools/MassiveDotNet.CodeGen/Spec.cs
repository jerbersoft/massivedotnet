using System.Text.Json;

namespace MassiveDotNet.CodeGen;

/// <summary>A single GET operation from the OpenAPI description.</summary>
internal sealed record SpecOperation(string OperationId, string Path, JsonElement Operation);

/// <summary>A parameter as described by the OpenAPI document.</summary>
internal sealed record SpecParameter(
    string Name,
    string In,
    bool Required,
    string? Description,
    JsonElement Schema);

/// <summary>A property of a (possibly composed) OpenAPI object schema.</summary>
internal sealed record SpecProperty(string Name, bool Required, string? Description, JsonElement Schema);

/// <summary>Reads the OpenAPI description and resolves its composed, anonymous schemas.</summary>
internal sealed class Spec
{
    private readonly JsonDocument _document;
    private readonly Dictionary<string, SpecOperation> _operations = new(StringComparer.Ordinal);
    private readonly JsonElement _componentParameters;

    public Spec(string path)
    {
        _document = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root = _document.RootElement;

        _componentParameters = root.GetProperty("components").GetProperty("parameters");

        foreach (JsonProperty pathItem in root.GetProperty("paths").EnumerateObject())
        {
            if (!pathItem.Value.TryGetProperty("get", out JsonElement operation))
            {
                continue;
            }

            if (!operation.TryGetProperty("operationId", out JsonElement operationId))
            {
                continue;
            }

            string id = operationId.GetString()!;
            _operations[id] = new SpecOperation(id, pathItem.Name, operation);
        }
    }

    public int OperationCount => _operations.Count;

    public IEnumerable<string> OperationIds => _operations.Keys;

    public SpecOperation Operation(string operationId) =>
        _operations.TryGetValue(operationId, out SpecOperation? operation)
            ? operation
            : throw new InvalidOperationException(
                $"Operation '{operationId}' is not present in the OpenAPI description.");

    /// <summary>Returns an operation's parameters in declaration order, resolving component references.</summary>
    public List<SpecParameter> Parameters(SpecOperation operation)
    {
        List<SpecParameter> results = [];

        if (!operation.Operation.TryGetProperty("parameters", out JsonElement parameters))
        {
            return results;
        }

        foreach (JsonElement parameter in parameters.EnumerateArray())
        {
            JsonElement resolved = parameter;

            if (parameter.TryGetProperty("$ref", out JsonElement reference))
            {
                string name = reference.GetString()!.Split('/')[^1];
                resolved = _componentParameters.GetProperty(name);
            }

            results.Add(new SpecParameter(
                resolved.GetProperty("name").GetString()!,
                resolved.GetProperty("in").GetString()!,
                resolved.TryGetProperty("required", out JsonElement required) && required.GetBoolean(),
                resolved.TryGetProperty("description", out JsonElement description) ? description.GetString() : null,
                resolved.TryGetProperty("schema", out JsonElement schema) ? schema : default));
        }

        return results;
    }

    /// <summary>Returns the JSON schema of an operation's 200 response.</summary>
    public static JsonElement SuccessSchema(SpecOperation operation) =>
        operation.Operation
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

    /// <summary>
    /// Whether an operation's success envelope carries a <c>next_url</c> cursor.
    /// </summary>
    /// <remarks>
    /// Read from the description rather than declared in the map, so it cannot drift: if Massive
    /// starts paginating an endpoint that previously did not, the next spec sync grows the
    /// enumeration surface without anyone having to notice.
    /// </remarks>
    public static bool IsPaginated(SpecOperation operation) =>
        Properties(SuccessSchema(operation)).Exists(p => p.Name == "next_url");

    /// <summary>
    /// Flattens an object schema, merging every <c>allOf</c> branch. No operation in the
    /// description uses <c>$ref</c> for its response, and 27 compose their envelope from
    /// several branches, so merging is required before properties can be read.
    /// </summary>
    public static List<SpecProperty> Properties(JsonElement schema)
    {
        Dictionary<string, SpecProperty> properties = new(StringComparer.Ordinal);
        List<string> order = [];
        HashSet<string> required = new(StringComparer.Ordinal);

        Collect(schema, properties, order, required);

        return [.. order.Select(name => properties[name] with { Required = required.Contains(name) })];
    }

    /// <summary>Navigates a slash-separated pointer such as <c>results/items</c>.</summary>
    public static JsonElement Navigate(JsonElement schema, string pointer)
    {
        JsonElement current = schema;

        foreach (string segment in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "items")
            {
                current = current.GetProperty("items");
                continue;
            }

            SpecProperty property = Properties(current).FirstOrDefault(p => p.Name == segment)
                ?? throw new InvalidOperationException($"Pointer segment '{segment}' was not found.");

            current = property.Schema;
        }

        return current;
    }

    private static void Collect(
        JsonElement schema,
        Dictionary<string, SpecProperty> properties,
        List<string> order,
        HashSet<string> required)
    {
        if (schema.TryGetProperty("allOf", out JsonElement branches))
        {
            foreach (JsonElement branch in branches.EnumerateArray())
            {
                Collect(branch, properties, order, required);
            }
        }

        if (schema.TryGetProperty("required", out JsonElement requiredNames))
        {
            foreach (JsonElement name in requiredNames.EnumerateArray())
            {
                required.Add(name.GetString()!);
            }
        }

        if (!schema.TryGetProperty("properties", out JsonElement schemaProperties))
        {
            return;
        }

        foreach (JsonProperty property in schemaProperties.EnumerateObject())
        {
            if (!properties.ContainsKey(property.Name))
            {
                order.Add(property.Name);
            }

            properties[property.Name] = new SpecProperty(
                property.Name,
                Required: false,
                property.Value.TryGetProperty("description", out JsonElement description)
                    ? description.GetString()
                    : null,
                property.Value);
        }
    }
}
