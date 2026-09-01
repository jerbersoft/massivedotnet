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

    public string QueryExpression => Required
        ? Binding.PathExpression(Identifier)
        : Binding.QueryExpression(Identifier) + QueryCallSuffix;

    public string PathAppendMethod => Binding.PathAppendMethod;

    /// <summary>Creates the argument for a slot: a plain parameter, or one filter for a comparator group.</summary>
    public static Argument Create(ParameterSlot slot, MapParameter? mapped, string operationId)
    {
        if (slot.Group is null)
        {
            return Create(slot.Parameter, mapped);
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

        // The variants' own descriptions are boilerplate ("Range by ticker."), so the base field's
        // prose carries the meaning and one generated sentence names the accepted forms.
        string? prose = Prose.Clean(slot.Parameter.Description);
        string sentence = group.DocSentence(operationId);
        string description = prose is null ? sentence : $"{prose.TrimEnd().TrimEnd('.')}. {sentence}";

        return new Argument(
            group.BaseName,
            mapped?.Name ?? group.BaseName,
            Required: false,
            In: "query",
            description,
            TypeBinding.ResolveFilter(group, mapped?.Type, slot.Parameter.Schema, operationId),
            QueryCallSuffix: group.HasExactForm ? "" : ", hasExactForm: false");
    }

    public static Argument Create(SpecParameter parameter, MapParameter? mapped) => new(
        parameter.Name,
        mapped?.Name ?? parameter.Name,
        parameter.Required,
        parameter.In,
        Prose.Clean(parameter.Description),
        TypeBinding.Resolve(mapped?.Type, parameter.Schema));
}
