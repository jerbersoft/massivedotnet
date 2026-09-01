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

/// <summary>The comparator variants a field declares, such as <c>gt gte lt lte any_of</c>.</summary>
/// <param name="BaseName">The wire name of the field, for example <c>ticker</c>.</param>
/// <param name="Suffixes">The suffixes declared, drawn from <c>gt gte lt lte any_of all_of</c>.</param>
/// <param name="HasExactForm">
/// Whether the operation also declares the plain <c>field</c> parameter. One operation in the
/// description does not (<c>/v1/summaries</c> declares only <c>ticker.any_of</c>).
/// </param>
internal sealed record ComparatorGroup(string BaseName, IReadOnlySet<string> Suffixes, bool HasExactForm)
{
    /// <summary>
    /// The filter type the exact suffix set maps to. Any other set fails generation: a new
    /// combination needs a filter type designed for it, and this is what makes a spec sync that
    /// introduces an unknown suffix fail loudly rather than emit something plausible.
    /// </summary>
    public string FilterType(string operationId) => Key switch
    {
        "gt gte lt lte" => "RangeFilter",
        "any_of" => "SetFilter",
        "any_of gt gte lt lte" => "Filter",
        "all_of any_of" => "ArrayFilter",
        _ => throw new InvalidOperationException(
            $"Operation '{operationId}' declares comparators [{Key}] on '{BaseName}', which is not a "
            + "recognised shape. Known shapes: [gt gte lt lte], [any_of], [any_of gt gte lt lte], "
            + "[all_of any_of]. A new combination needs a filter type designed for it, not a guess."),
    };

    /// <summary>The sentence appended to the field's description, naming the forms it accepts.</summary>
    /// <param name="operationId">The operation the group belongs to, named in any diagnostic.</param>
    /// <returns>One sentence describing the forms the generated parameter accepts.</returns>
    /// <exception cref="InvalidOperationException">The filter type has no sentence of its own.</exception>
    public string DocSentence(string operationId)
    {
        string filter = FilterType(operationId);

        // Every arm is named. A fifth filter type must bring its own sentence rather than
        // inheriting the array filter's by falling through a default nobody would notice.
        return filter switch
        {
            "RangeFilter" => "Accepts an exact value or a range.",
            "SetFilter" => HasExactForm
                ? "Accepts an exact value or a set of values."
                : "Accepts one or more values.",
            "Filter" => "Accepts an exact value, a range, or a set of values.",
            "ArrayFilter" => "Matches arrays containing the value, any of the values, or all of the values.",
            _ => throw new InvalidOperationException(
                $"Operation '{operationId}' resolves '{BaseName}' to filter type '{filter}', which has no "
                + "sentence describing the forms it accepts. Add one to DocSentence."),
        };
    }

    private string Key => string.Join(' ', Suffixes.Order(StringComparer.Ordinal));
}

/// <summary>
/// One generated parameter: a plain spec parameter, or a comparator group standing in for several.
/// </summary>
/// <param name="WireName">The parameter name, or the group's base name. Map rows are keyed by this.</param>
/// <param name="Parameter">
/// The spec parameter that supplies the schema and prose: the plain parameter itself, the
/// group's base field, or its first variant when the description declares no base.
/// </param>
/// <param name="Group">The comparator group, or <see langword="null"/> for a plain parameter.</param>
internal sealed record ParameterSlot(string WireName, SpecParameter Parameter, ComparatorGroup? Group);

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

    private static readonly HashSet<string> ComparatorSuffixes =
        new(StringComparer.Ordinal) { "gt", "gte", "lt", "lte", "any_of", "all_of" };

    /// <summary>
    /// Collapses an operation's parameters into slots. Comparator variants such as
    /// <c>ticker.gte</c> fold into one group keyed by their base name, placed where the group's
    /// first member was declared and sourced from the base field whenever the description declares
    /// one. Everything else passes through unchanged.
    /// </summary>
    /// <remarks>
    /// Only the six known suffixes count. The SEC filings endpoint declares nested field paths
    /// such as <c>entities.company_data.name</c>, which contain a dot but are not comparators and
    /// must stay plain parameters.
    /// </remarks>
    public static List<ParameterSlot> Slots(List<SpecParameter> parameters)
    {
        Dictionary<string, HashSet<string>> suffixesByBase = new(StringComparer.Ordinal);

        foreach (SpecParameter parameter in parameters)
        {
            if (SplitComparator(parameter.Name) is (string baseName, string suffix))
            {
                if (!suffixesByBase.TryGetValue(baseName, out HashSet<string>? suffixes))
                {
                    suffixes = new HashSet<string>(StringComparer.Ordinal);
                    suffixesByBase[baseName] = suffixes;
                }

                suffixes.Add(suffix);
            }
        }

        Dictionary<string, SpecParameter> byName = new(StringComparer.Ordinal);

        foreach (SpecParameter parameter in parameters)
        {
            byName.TryAdd(parameter.Name, parameter);
        }

        HashSet<string> placed = new(StringComparer.Ordinal);
        List<ParameterSlot> slots = [];

        foreach (SpecParameter parameter in parameters)
        {
            string key = SplitComparator(parameter.Name) is (string baseName, _) ? baseName : parameter.Name;

            if (!suffixesByBase.TryGetValue(key, out HashSet<string>? suffixes))
            {
                slots.Add(new ParameterSlot(parameter.Name, parameter, Group: null));
                continue;
            }

            // A later variant of a group that is already in place.
            if (!placed.Add(key))
            {
                continue;
            }

            // The base field carries the prose, the schema, and the required flag, and it is not
            // necessarily declared first: /v3/snapshot/indices declares ticker.any_of ahead of
            // ticker. Looking the base up rather than taking whichever member was encountered
            // first is what makes the slot's documented invariant true. Placement is unaffected --
            // the group still stands where its first member was declared.
            bool hasExactForm = byName.TryGetValue(key, out SpecParameter? baseParameter);

            slots.Add(new ParameterSlot(
                key,
                baseParameter ?? parameter,
                new ComparatorGroup(key, suffixes, hasExactForm)));
        }

        return slots;
    }

    /// <summary>Splits a comparator variant into the field it filters and the suffix it declares.</summary>
    /// <param name="name">A declared parameter name, such as <c>ticker.gte</c>.</param>
    /// <returns>
    /// The base name and suffix, or <see langword="null"/> when the name carries no dot or its last
    /// segment is not one of the six known comparator suffixes -- which is how a nested field path
    /// such as <c>entities.company_data.name</c> stays a plain parameter.
    /// </returns>
    private static (string BaseName, string Suffix)? SplitComparator(string name)
    {
        int dot = name.LastIndexOf('.');

        if (dot <= 0)
        {
            return null;
        }

        string suffix = name[(dot + 1)..];

        return ComparatorSuffixes.Contains(suffix) ? (name[..dot], suffix) : null;
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
