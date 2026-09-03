using System.Text.Json;

namespace MassiveDotNet.CodeGen;

/// <summary>A single GET operation from the OpenAPI description.</summary>
internal sealed record SpecOperation(string OperationId, string Path, JsonElement Operation);

/// <summary>A deprecation the description declares on an operation through <c>x-polygon-deprecation</c> (D18).</summary>
/// <param name="ReplacementSlug">
/// The docs-site slug of the operation that supersedes it, such as <c>get_v3_trades__stockticker</c>,
/// or <see langword="null"/> when the description names none.
/// </param>
internal sealed record SpecDeprecation(string? ReplacementSlug);

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

/// <summary>What a schema node is, as far as binding is concerned (D-N3, D-N5).</summary>
internal enum SchemaShape
{
    /// <summary>A string, number, integer, or boolean, or a node with no type, which defaults to string.</summary>
    Scalar,

    /// <summary>An object, whether or not it declares properties.</summary>
    Object,

    /// <summary>An array of scalars, or an array with no item schema.</summary>
    Array,

    /// <summary>An array whose items are objects.</summary>
    ArrayOfObjects,

    /// <summary>An array whose items are arrays. None exists in the description; refused if one arrives.</summary>
    ArrayOfArrays,
}

/// <summary>Reads the OpenAPI description and resolves its composed, anonymous schemas.</summary>
internal sealed class Spec
{
    private readonly JsonDocument _document;
    private readonly Dictionary<string, SpecOperation> _operations = new(StringComparer.Ordinal);
    private readonly JsonElement _componentParameters;

    public Spec(string path)
        : this(JsonDocument.Parse(File.ReadAllBytes(path)))
    {
    }

    /// <summary>Parses a description. The generator loads from disk; tests hand in fragments.</summary>
    public static Spec Parse(string json) => new(JsonDocument.Parse(json));

    private Spec(JsonDocument document)
    {
        _document = document;
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

    /// <summary>The operation whose route a docs-site slug names, or <see langword="null"/>.</summary>
    public SpecOperation? OperationBySlug(string slug) =>
        _operations.Values.FirstOrDefault(operation => Slug(operation.Path) == slug);

    /// <summary>
    /// The docs-site slug of a route, which is how <c>x-polygon-deprecation</c> names a replacement:
    /// <c>get</c>, then the path with every slash and brace as an underscore, lowercased, with the
    /// trailing underscore a closing brace leaves behind trimmed away.
    /// </summary>
    public static string Slug(string path) =>
        ("get" + path.Replace('/', '_').Replace('{', '_').Replace('}', '_')).ToLowerInvariant().TrimEnd('_');

    /// <summary>
    /// Whether Massive publishes an operation as experimental: its route carries a segment that
    /// starts with <c>vX</c> (<c>vX_0</c> is a numbered revision of one), or a <c>dev</c> segment
    /// where a released route carries its version, or it declares <c>x-polygon-experimental</c>.
    /// All three are read because the extension appears on only two of the fourteen <c>vX</c>
    /// routes and on no <c>dev</c> route at all (D18, D22, D23).
    /// </summary>
    public static bool IsExperimental(SpecOperation operation) =>
        operation.Operation.TryGetProperty("x-polygon-experimental", out _)
        || operation.Path.Split('/').Any(segment => segment.StartsWith("vX", StringComparison.Ordinal) || segment is "dev");

    /// <summary>The deprecation an operation declares, or <see langword="null"/> for a live one.</summary>
    public static SpecDeprecation? Deprecation(SpecOperation operation)
    {
        if (!operation.Operation.TryGetProperty("x-polygon-deprecation", out JsonElement deprecation))
        {
            return null;
        }

        string? slug = deprecation.TryGetProperty("replaces", out JsonElement replaces)
            && replaces.TryGetProperty("path", out JsonElement path)
            ? path.GetString()
            : null;

        return new SpecDeprecation(slug);
    }

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

    /// <summary>Classifies a schema node for binding.</summary>
    /// <remarks>
    /// An object is anything typed <c>object</c>, or anything that declares <c>properties</c> or
    /// composes them through <c>allOf</c>, since the description omits the type on some composed
    /// nodes. A free-form object with no properties is still an object: it has no default binding
    /// and the map must name a type for it (D-N6).
    /// </remarks>
    public static SchemaShape Shape(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return SchemaShape.Scalar;
        }

        schema = Unwrap(schema);

        if (IsObject(schema))
        {
            return SchemaShape.Object;
        }

        if (!schema.TryGetProperty("type", out JsonElement type) || type.GetString() != "array")
        {
            return SchemaShape.Scalar;
        }

        if (!schema.TryGetProperty("items", out JsonElement items))
        {
            return SchemaShape.Array;
        }

        return Shape(items) switch
        {
            SchemaShape.Object => SchemaShape.ArrayOfObjects,
            SchemaShape.Scalar => SchemaShape.Array,
            _ => SchemaShape.ArrayOfArrays,
        };
    }

    /// <summary>A shape as it reads in a diagnostic: "an object", "an array of objects".</summary>
    public static string Describe(SchemaShape shape) => shape switch
    {
        SchemaShape.Object => "an object",
        SchemaShape.ArrayOfObjects => "an array of objects",
        SchemaShape.ArrayOfArrays => "an array of arrays",
        SchemaShape.Array => "an array of scalars",
        _ => "a scalar",
    };

    /// <summary>
    /// The ways a reuse site's schema differs from the schema a model was generated from (D-N4).
    /// Property names must match exactly; a property the model's schema requires must be required
    /// at the site; both comparisons recurse through nested objects and arrays of objects. Scalar
    /// types are not compared: the model row is the curated truth, and the description disagrees
    /// with itself at sites that are plainly the same thing. Empty when the site matches.
    /// </summary>
    /// <param name="model">The schema at the model's own pointer.</param>
    /// <param name="site">The schema at the property that names the model.</param>
    /// <returns>One sentence per difference, in the description's declaration order.</returns>
    public static List<string> StructuralDifferences(JsonElement model, JsonElement site)
    {
        List<string> differences = [];
        Compare(model, site, "", differences);
        return differences;
    }

    private static void Compare(JsonElement model, JsonElement site, string path, List<string> differences)
    {
        List<SpecProperty> modelProperties = Properties(model);
        List<SpecProperty> siteProperties = Properties(site);

        foreach (SpecProperty extra in siteProperties.Where(s => !modelProperties.Exists(m => m.Name == s.Name)))
        {
            differences.Add($"'{path}{extra.Name}' is declared at the site but not on the model");
        }

        foreach (SpecProperty expected in modelProperties)
        {
            SpecProperty? actual = siteProperties.Find(s => s.Name == expected.Name);

            if (actual is null)
            {
                differences.Add($"'{path}{expected.Name}' is on the model but not declared at the site");
                continue;
            }

            if (expected.Required && !actual.Required)
            {
                differences.Add($"'{path}{expected.Name}' is required on the model but optional at the site");
            }

            SchemaShape modelShape = Shape(expected.Schema);
            SchemaShape siteShape = Shape(actual.Schema);

            if (modelShape == SchemaShape.Object && siteShape == SchemaShape.Object)
            {
                Compare(expected.Schema, actual.Schema, $"{path}{expected.Name}/", differences);
            }
            else if (modelShape == SchemaShape.ArrayOfObjects && siteShape == SchemaShape.ArrayOfObjects)
            {
                Compare(
                    expected.Schema.GetProperty("items"),
                    actual.Schema.GetProperty("items"),
                    $"{path}{expected.Name}/items/",
                    differences);
            }
            else if (modelShape != siteShape)
            {
                // Array-ness is a shape fact, not a scalar type: a model property that is an array
                // of scalars versus a plain scalar at a reuse site, or an array versus an array of
                // arrays, is flagged the same as object-versus-scalar. Scalar types themselves stay
                // uncompared -- both sides land on SchemaShape.Scalar, so modelShape == siteShape and
                // this branch never runs for them.
                differences.Add(
                    $"'{path}{expected.Name}' is {Describe(modelShape)} on the model but {Describe(siteShape)} at the site");
            }
        }
    }

    private static bool IsObject(JsonElement schema) =>
        (schema.TryGetProperty("type", out JsonElement type) && type.GetString() == "object")
        || schema.TryGetProperty("properties", out _)
        || schema.TryGetProperty("allOf", out _);

    /// <summary>
    /// The node a schema binds as: itself, or the single branch of a one-branch <c>oneOf</c>.
    /// The description uses that form once, on the ticker events items, and without this the
    /// array had no item type and bound <c>string[]</c> (D24).
    /// </summary>
    /// <remarks>
    /// A <c>oneOf</c> of scalars still reads as a scalar, as it always has: the news parameters
    /// declare one and go through <see cref="Shape"/> too. A union with an object branch has no
    /// model binding, and reading it as a scalar would bind <c>string</c> where the wire carries
    /// objects, so it is refused. No operation declares one today.
    /// </remarks>
    private static JsonElement Unwrap(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("oneOf", out JsonElement branches)
            || branches.ValueKind != JsonValueKind.Array)
        {
            return schema;
        }

        if (branches.GetArrayLength() == 1)
        {
            return Unwrap(branches[0]);
        }

        foreach (JsonElement branch in branches.EnumerateArray())
        {
            // A branch that is itself a one-branch oneOf is its branch under D24, so it must be unwrapped before judging.
            if (IsObject(Unwrap(branch)))
            {
                throw new InvalidOperationException(
                    $"A oneOf with {branches.GetArrayLength()} branches, at least one an object, has no model binding. "
                    + "The generator reads only a one-branch oneOf as its branch (D24); a union needs a design, not a guess.");
            }
        }

        return schema;
    }

    private static void Collect(
        JsonElement schema,
        Dictionary<string, SpecProperty> properties,
        List<string> order,
        HashSet<string> required)
    {
        schema = Unwrap(schema);

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
