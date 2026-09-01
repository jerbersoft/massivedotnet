using System.Text.RegularExpressions;

namespace MassiveDotNet.CodeGen;

/// <summary>Normalizes prose taken from the OpenAPI description for use in XML doc comments.</summary>
internal static partial class Prose
{
    /// <summary>
    /// Strips embedded HTML and collapses whitespace. Several descriptions in the OpenAPI
    /// document contain anchor tags intended for the HTML docs site, which would otherwise be
    /// escaped into the generated comments verbatim.
    /// </summary>
    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string stripped = HtmlTag().Replace(value, string.Empty);
        stripped = Backtick().Replace(stripped, "$1");

        return string.Join(' ', stripped.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex("`([^`]*)`")]
    private static partial Regex Backtick();
}
