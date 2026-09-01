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
}
