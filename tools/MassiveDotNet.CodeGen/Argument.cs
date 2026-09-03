namespace MassiveDotNet.CodeGen;

/// <summary>A single generated method parameter.</summary>
/// <param name="QueryCallSuffix">
/// Extra arguments appended to the builder call, after the value. Used only by the one base-less
/// set group, which passes <c>hasExactForm: false</c>.
/// </param>
internal sealed record Argument(
    string WireName,
    string Identifier,
    bool Required,
    string In,
    string? Description,
    TypeBinding Binding,
    string QueryCallSuffix = "")
{
    public string CSharpType => Binding.CSharpType;

    /// <summary>The parameter as it appears in the method signature.</summary>
    public string Declaration => Required
        ? $"{CSharpType} {Identifier}"
        : $"{CSharpType}? {Identifier} = null";

    /// <summary>The parameter without a default, for private helpers that always pass every value.</summary>
    public string RequiredDeclaration => Required ? $"{CSharpType} {Identifier}" : $"{CSharpType}? {Identifier}";

    public string PathExpression => Binding.PathExpression(Identifier);

    public string QueryExpression => (Required
        ? Binding.PathExpression(Identifier)
        : Binding.QueryExpression(Identifier)) + QueryCallSuffix;

    public string PathAppendMethod => Binding.PathAppendMethod;

    /// <summary>Creates the argument for a slot: a plain parameter, or one filter for a comparator group.</summary>
    public static Argument Create(ParameterSlot slot, MapParameter? mapped, string operationId)
    {
        if (slot.Group is null)
        {
            return Create(slot.Parameter, mapped, operationId);
        }

        ComparatorGroup group = slot.Group;

        // Nothing in the description requires a field that also carries comparators, and a
        // required filter is a shape this generator does not emit. Refusing is better than
        // silently making it optional.
        if (slot.Parameter.Required)
        {
            throw new InvalidOperationException(
                $"Operation '{operationId}' requires '{group.BaseName}', which also carries comparators. "
                + "Required filter parameters are not supported; this needs a design, not a default.");
        }

        // Only an any_of-only group can lack a plain field (D-F8): equality then travels as a
        // one-element any_of, which is what AppendQuery's SetFilter overload's hasExactForm
        // parameter exists to select. No other filter overload has that fallback, so a future
        // spec sync that drops the plain field from a range or range-and-set group would
        // otherwise generate cleanly and only fail with CS1739 inside the emitted .g.cs.
        if (!group.HasExactForm && group.FilterType(operationId) != "SetFilter")
        {
            throw new InvalidOperationException(
                $"Operation '{operationId}' declares '{group.BaseName}' with no plain field, but its "
                + $"comparators resolve to {group.FilterType(operationId)}<T>, not SetFilter<T>. Only an "
                + "any_of-only group may lack a plain form; this needs a design, not a guess.");
        }

        TypeBinding binding = TypeBinding.ResolveFilter(group, mapped?.Type, slot.Parameter.Schema, operationId);

        // The variants' own descriptions are boilerplate ("Range by ticker."), so the base field's
        // prose carries the meaning and one generated sentence names the accepted forms.
        string? prose = Describe(slot.Parameter, binding);
        string sentence = group.DocSentence(operationId);
        string description = prose is null ? sentence : $"{prose.TrimEnd().TrimEnd('.')}. {sentence}";

        return new Argument(
            group.BaseName,
            mapped?.Name ?? group.BaseName,
            Required: false,
            In: "query",
            description,
            binding,
            QueryCallSuffix: group.HasExactForm ? "" : ", hasExactForm: false");
    }

    public static Argument Create(SpecParameter parameter, MapParameter? mapped, string operationId)
    {
        TypeBinding binding = TypeBinding.Resolve(mapped?.Type, parameter.Schema, operationId, parameter.Name);

        return new Argument(
            parameter.Name,
            mapped?.Name ?? parameter.Name,
            parameter.Required,
            parameter.In,
            Describe(parameter, binding),
            binding);
    }

    /// <summary>
    /// The parameter's prose, minus the wire-format sentence where the bound type already states
    /// it (#35).
    /// </summary>
    private static string? Describe(SpecParameter parameter, TypeBinding binding)
    {
        string? prose = Prose.Clean(parameter.Description);

        return prose is not null && binding.StatesWireFormat ? Prose.WithoutWireFormat(prose) : prose;
    }
}
