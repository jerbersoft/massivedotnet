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

    /// <summary>
    /// Drops the trailing sentence with which the description names a parameter's wire format,
    /// restoring the period that sentence sometimes stands in for. Applied only where the .NET
    /// type states the format itself, so the prose stops reading as if a string were passed (#35).
    /// </summary>
    /// <remarks>
    /// Enumerated rather than general: the description uses exactly these five sentences, always
    /// last, and twice runs the preceding sentence into them with no period. Anything else,
    /// including a description that is only the sentence, is left alone.
    /// </remarks>
    public static string WithoutWireFormat(string description) =>
        WireFormatSentence().Replace(description, ".");

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(
        @"(?<=\S)\.?\s+Value must be (?:formatted 'yyyy-mm-dd'|a floating point number|an integer"
        + @"(?: timestamp in (?:nano)?seconds, formatted 'yyyy-mm-dd', or ISO 8601/RFC 3339 \(e\.g\. '[^']*'\))?)\.?$")]
    private static partial Regex WireFormatSentence();

    [GeneratedRegex("`([^`]*)`")]
    private static partial Regex Backtick();
}
