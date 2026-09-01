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
