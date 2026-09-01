namespace MassiveDotNet.CodeGen;

/// <summary>A single generated method parameter.</summary>
internal sealed record Argument(
    string WireName,
    string Identifier,
    bool Required,
    string In,
    string? Description,
    TypeBinding Binding)
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
        : Binding.QueryExpression(Identifier);

    public string PathAppendMethod => Binding.PathAppendMethod;

    public static Argument Create(SpecParameter parameter, MapParameter? mapped) => new(
        parameter.Name,
        mapped?.Name ?? parameter.Name,
        parameter.Required,
        parameter.In,
        Prose.Clean(parameter.Description),
        TypeBinding.Resolve(mapped?.Type, parameter.Schema));
}
