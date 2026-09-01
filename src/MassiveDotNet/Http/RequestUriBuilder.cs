using MassiveDotNet.Internal;

namespace MassiveDotNet.Http;

/// <summary>
/// Composes a relative request URI, escaping path segments and query values as it goes.
/// </summary>
/// <remarks>
/// <para>
/// This is a <see langword="ref struct"/>, so it cannot cross an <see langword="await"/>.
/// Build the URI to completion with <see cref="ToUriString"/> before issuing the request.
/// </para>
/// <para>
/// Optional query parameters are skipped when <see langword="null"/>, which lets generated
/// endpoint code append every supported parameter unconditionally.
/// </para>
/// </remarks>
public ref struct RequestUriBuilder
{
    private ValueStringBuilder _builder;
    private bool _hasQuery;

    /// <summary>Initializes a builder over caller-supplied scratch space.</summary>
    /// <param name="initialBuffer">Scratch space to use before growing into pooled memory.</param>
    public RequestUriBuilder(Span<char> initialBuffer)
    {
        _builder = new ValueStringBuilder(initialBuffer);
        _hasQuery = false;
    }

    /// <summary>Appends a literal, already-safe portion of the path, such as <c>"/v2/aggs/ticker/"</c>.</summary>
    /// <param name="value">The literal path text to append verbatim.</param>
    public void AppendPathLiteral(scoped ReadOnlySpan<char> value) => _builder.Append(value);

    /// <summary>Appends a caller-supplied path segment, percent-escaping it.</summary>
    /// <param name="value">The segment value, for example a ticker symbol.</param>
    public void AppendPathSegment(string value) => _builder.Append(Uri.EscapeDataString(value));

    /// <summary>Appends an integer path segment.</summary>
    /// <param name="value">The segment value, for example an aggregate multiplier.</param>
    public void AppendPathSegment(int value) => _builder.Append(value);

    /// <summary>Appends a string query parameter, skipping it when <see langword="null"/>.</summary>
    /// <param name="name">The parameter name, which must already be URI-safe.</param>
    /// <param name="value">The parameter value, or <see langword="null"/> to omit the parameter.</param>
    public void AppendQuery(scoped ReadOnlySpan<char> name, string? value)
    {
        if (value is null)
        {
            return;
        }

        AppendSeparator();
        _builder.Append(name);
        _builder.Append('=');
        _builder.Append(Uri.EscapeDataString(value));
    }

    /// <summary>Appends a boolean query parameter, skipping it when <see langword="null"/>.</summary>
    /// <param name="name">The parameter name, which must already be URI-safe.</param>
    /// <param name="value">The parameter value, or <see langword="null"/> to omit the parameter.</param>
    public void AppendQuery(scoped ReadOnlySpan<char> name, bool? value)
    {
        if (value is null)
        {
            return;
        }

        AppendSeparator();
        _builder.Append(name);
        _builder.Append('=');
        _builder.Append(value.Value ? "true" : "false");
    }

    /// <summary>Appends an integer query parameter, skipping it when <see langword="null"/>.</summary>
    /// <param name="name">The parameter name, which must already be URI-safe.</param>
    /// <param name="value">The parameter value, or <see langword="null"/> to omit the parameter.</param>
    public void AppendQuery(scoped ReadOnlySpan<char> name, int? value)
    {
        if (value is null)
        {
            return;
        }

        AppendSeparator();
        _builder.Append(name);
        _builder.Append('=');
        _builder.Append(value.Value);
    }

    /// <summary>Appends a 64-bit integer query parameter, skipping it when <see langword="null"/>.</summary>
    /// <param name="name">The parameter name, which must already be URI-safe.</param>
    /// <param name="value">The parameter value, or <see langword="null"/> to omit the parameter.</param>
    public void AppendQuery(scoped ReadOnlySpan<char> name, long? value)
    {
        if (value is null)
        {
            return;
        }

        AppendSeparator();
        _builder.Append(name);
        _builder.Append('=');
        _builder.Append(value.Value);
    }

    /// <summary>Materializes the composed URI and returns any pooled buffer.</summary>
    /// <returns>The relative URI, including its query string when one was appended.</returns>
    public string ToUriString() => _builder.ToString();

    /// <summary>Returns any pooled buffer without materializing a string.</summary>
    public void Dispose() => _builder.Dispose();

    private void AppendSeparator()
    {
        _builder.Append(_hasQuery ? '&' : '?');
        _hasQuery = true;
    }
}
