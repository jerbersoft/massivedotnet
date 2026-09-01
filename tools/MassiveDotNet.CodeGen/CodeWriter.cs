using System.Text;

namespace MassiveDotNet.CodeGen;

/// <summary>Indentation-aware buffer for emitting C# source.</summary>
internal sealed class CodeWriter
{
    private readonly StringBuilder _buffer = new();
    private int _indent;

    public void Line(string text = "")
    {
        if (text.Length == 0)
        {
            _buffer.Append('\n');
            return;
        }

        _buffer.Append(' ', _indent * 4).Append(text).Append('\n');
    }

    /// <summary>Emits an XML doc element, escaping and wrapping the supplied prose.</summary>
    /// <param name="element">The element name, for example <c>summary</c>.</param>
    /// <param name="content">The prose to emit. Nothing is written when this is blank.</param>
    /// <param name="attribute">An optional attribute, for example <c>name="ticker"</c>.</param>
    /// <param name="preserveMarkup">
    /// When true, the content is emitted verbatim because it already contains intentional doc
    /// markup authored in the map. When false, angle brackets and ampersands are escaped.
    /// </param>
    public void Doc(string element, string? content, string? attribute = null, bool preserveMarkup = false)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        string open = attribute is null ? element : $"{element} {attribute}";
        string text = Collapse(content);
        string escaped = preserveMarkup ? text : Escape(text);

        if (escaped.Length <= 96)
        {
            Line($"/// <{open}>{escaped}</{element}>");
            return;
        }

        Line($"/// <{open}>");
        foreach (string line in Wrap(escaped, 96))
        {
            Line($"/// {line}");
        }

        Line($"/// </{element}>");
    }

    public IDisposable Block(string header) => Block([header]);

    /// <summary>Opens a block whose header spans several lines, such as a wrapped signature.</summary>
    public IDisposable Block(IReadOnlyList<string> headerLines)
    {
        foreach (string line in headerLines)
        {
            Line(line);
        }

        Line("{");
        _indent++;
        return new Closer(this);
    }

    public override string ToString() => _buffer.ToString();

    private static string Collapse(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    private static List<string> Wrap(string value, int width)
    {
        List<string> lines = [];
        StringBuilder current = new();

        foreach (string word in value.Split(' '))
        {
            if (current.Length > 0 && current.Length + word.Length + 1 > width)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(word);
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    private sealed class Closer(CodeWriter writer) : IDisposable
    {
        public void Dispose()
        {
            writer._indent--;
            writer.Line("}");
        }
    }
}
