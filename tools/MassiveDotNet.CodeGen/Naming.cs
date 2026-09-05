using System.Text;

namespace MassiveDotNet.CodeGen;

/// <summary>Converts wire names to .NET naming conventions.</summary>
internal static class Naming
{
    /// <summary>
    /// Converts a wire property name to PascalCase. Handles snake_case (<c>request_id</c>),
    /// kebab-case, and already-camelCase names (<c>queryCount</c>).
    /// </summary>
    public static string Pascal(string wireName)
    {
        StringBuilder builder = new(wireName.Length);

        foreach (string part in wireName.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries))
        {
            builder.Append(char.ToUpperInvariant(part[0])).Append(part[1..]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// A local variable name for a property: <c>TradeId</c> becomes <c>tradeId</c>.
    /// </summary>
    /// <remarks>
    /// Escapes a result that collides with a C# keyword rather than mangling it, because a wire
    /// name is the service's to choose and <c>"do"</c> or <c>"for"</c> is a legal one. Only the
    /// reserved words need this; a contextual keyword such as <c>value</c> is already a legal
    /// identifier, and escaping it would emit <c>@value</c> where <c>value</c> reads better.
    /// </remarks>
    /// <param name="pascal">A PascalCase property name.</param>
    /// <returns>The camelCase form, prefixed with <c>@</c> when it is a reserved word.</returns>
    public static string Camel(string pascal)
    {
        string camel = char.ToLowerInvariant(pascal[0]) + pascal[1..];

        return Reserved.Contains(camel) ? $"@{camel}" : camel;
    }

    /// <summary>The C# reserved words, which cannot be used as a bare identifier.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while",
    };

    /// <summary>
    /// The enumerating counterpart of a paginated endpoint's method name:
    /// <c>ListAggregates</c> becomes <c>EnumerateAggregates</c>.
    /// </summary>
    /// <remarks>
    /// Mirrors the BCL's own distinction between <c>Directory.GetFiles</c> and
    /// <c>Directory.EnumerateFiles</c> -- a materialized result versus a lazy sequence.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The method name is not <c>List</c>-prefixed, so no counterpart can be derived from it.
    /// </exception>
    public static string Enumerate(string method, string operationId)
    {
        // Failing beats guessing. Prefixing whatever the map said would silently mint names like
        // EnumerateGetTradesAsync, and a name in a shipped public API cannot be taken back --
        // generation is the last moment this is free to fix. The map author decides, not a
        // fallback nobody reviewed.
        if (!method.StartsWith("List", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Operation '{operationId}' is paginated, so it emits an Enumerate counterpart, but "
                + $"its mapped method '{method}' is not List-prefixed -- the derived name would be "
                + $"'Enumerate{method}Async'. Rename it to 'List...' in specs/endpoints.map.json.");
        }

        return $"Enumerate{method["List".Length..]}";
    }
}
