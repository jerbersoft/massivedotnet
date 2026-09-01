using System.Runtime.CompilerServices;
using MassiveDotNet.Internal;
using NodaTime;

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

    /// <summary>Appends a floating-point query parameter in invariant culture, skipping it when <see langword="null"/>.</summary>
    /// <param name="name">The parameter name, which must already be URI-safe.</param>
    /// <param name="value">The parameter value, or <see langword="null"/> to omit the parameter.</param>
    public void AppendQuery(scoped ReadOnlySpan<char> name, double? value)
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

    /// <summary>
    /// Appends a range filter as its comparator parameters, skipping it when <see langword="null"/>
    /// or unset.
    /// </summary>
    /// <typeparam name="T">
    /// The element type: <see cref="string"/>, <see cref="int"/>, <see cref="long"/>,
    /// <see cref="double"/>, <see cref="LocalDate"/>, or <see cref="DateOrTimestamp"/>.
    /// </typeparam>
    /// <param name="name">The field's base name, which must already be URI-safe.</param>
    /// <param name="filter">The filter, or <see langword="null"/> to omit the field entirely.</param>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not one of the supported element types.</exception>
    public void AppendQuery<T>(scoped ReadOnlySpan<char> name, RangeFilter<T>? filter)
    {
        if (filter is not { } value)
        {
            return;
        }

        AppendFilter(name, value.Mode, value.Lower, value.Upper, values: null, hasExactForm: true);
    }

    /// <summary>
    /// Appends a set filter as its comparator parameters, skipping it when <see langword="null"/>
    /// or unset.
    /// </summary>
    /// <typeparam name="T">
    /// The element type: <see cref="string"/>, <see cref="int"/>, <see cref="long"/>,
    /// <see cref="double"/>, <see cref="LocalDate"/>, or <see cref="DateOrTimestamp"/>.
    /// </typeparam>
    /// <param name="name">The field's base name, which must already be URI-safe.</param>
    /// <param name="filter">The filter, or <see langword="null"/> to omit the field entirely.</param>
    /// <param name="hasExactForm">
    /// Whether the endpoint declares the plain <c>field</c> parameter. When it declares only
    /// <c>field.any_of</c>, an equality is rendered as a one-element set, which means the same thing.
    /// </param>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not one of the supported element types.</exception>
    public void AppendQuery<T>(scoped ReadOnlySpan<char> name, SetFilter<T>? filter, bool hasExactForm = true)
    {
        if (filter is not { } value)
        {
            return;
        }

        AppendFilter(name, value.Mode, value.Value, default!, value.Values, hasExactForm);
    }

    /// <summary>
    /// Appends a range-or-set filter as its comparator parameters, skipping it when
    /// <see langword="null"/> or unset.
    /// </summary>
    /// <typeparam name="T">
    /// The element type: <see cref="string"/>, <see cref="int"/>, <see cref="long"/>,
    /// <see cref="double"/>, <see cref="LocalDate"/>, or <see cref="DateOrTimestamp"/>.
    /// </typeparam>
    /// <param name="name">The field's base name, which must already be URI-safe.</param>
    /// <param name="filter">The filter, or <see langword="null"/> to omit the field entirely.</param>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not one of the supported element types.</exception>
    public void AppendQuery<T>(scoped ReadOnlySpan<char> name, Filter<T>? filter)
    {
        if (filter is not { } value)
        {
            return;
        }

        AppendFilter(name, value.Mode, value.Lower, value.Upper, value.Values, hasExactForm: true);
    }

    /// <summary>
    /// Appends an array filter as its comparator parameters, skipping it when <see langword="null"/>
    /// or unset.
    /// </summary>
    /// <typeparam name="T">
    /// The element type: <see cref="string"/>, <see cref="int"/>, <see cref="long"/>,
    /// <see cref="double"/>, <see cref="LocalDate"/>, or <see cref="DateOrTimestamp"/>.
    /// </typeparam>
    /// <param name="name">The field's base name, which must already be URI-safe.</param>
    /// <param name="filter">The filter, or <see langword="null"/> to omit the field entirely.</param>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not one of the supported element types.</exception>
    public void AppendQuery<T>(scoped ReadOnlySpan<char> name, ArrayFilter<T>? filter)
    {
        if (filter is not { } value)
        {
            return;
        }

        AppendFilter(name, value.Mode, value.Value, default!, value.Values, hasExactForm: true);
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

    /// <summary>
    /// Renders every form a filter carries. The order is fixed here -- plain, gt, gte, lt, lte,
    /// any_of, all_of -- whatever order the OpenAPI description happened to declare the variants
    /// in, so two endpoints with the same filter always produce the same query string.
    /// </summary>
    /// <typeparam name="T">The element type. See the public overloads for the supported set.</typeparam>
    /// <param name="name">The field's base name.</param>
    /// <param name="mode">Which comparator forms are set.</param>
    /// <param name="lower">The lower bound, or the exact value for an equality filter.</param>
    /// <param name="upper">The upper bound.</param>
    /// <param name="values">The set's values, for an any-of or all-of filter.</param>
    /// <param name="hasExactForm">Whether equality renders as the plain field or a one-element any-of.</param>
    private void AppendFilter<T>(
        scoped ReadOnlySpan<char> name,
        FilterMode mode,
        T lower,
        T upper,
        T[]? values,
        bool hasExactForm)
    {
        if ((mode & FilterMode.Equal) != 0)
        {
            // A one-element set is equality, and it is the only form an endpoint without a plain
            // parameter can accept.
            AppendComparator(name, hasExactForm ? "" : ".any_of", lower);
        }

        if ((mode & FilterMode.Gt) != 0)
        {
            AppendComparator(name, ".gt", lower);
        }

        if ((mode & FilterMode.Gte) != 0)
        {
            AppendComparator(name, ".gte", lower);
        }

        if ((mode & FilterMode.Lt) != 0)
        {
            AppendComparator(name, ".lt", upper);
        }

        if ((mode & FilterMode.Lte) != 0)
        {
            AppendComparator(name, ".lte", upper);
        }

        if ((mode & FilterMode.AnyOf) != 0)
        {
            AppendSet(name, ".any_of", values!);
        }

        if ((mode & FilterMode.AllOf) != 0)
        {
            AppendSet(name, ".all_of", values!);
        }
    }

    /// <summary>Appends one <c>name[suffix]=value</c> comparator parameter.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="name">The field's base name.</param>
    /// <param name="suffix">The comparator suffix, such as <c>.gt</c>, or empty for the plain field.</param>
    /// <param name="value">The value to render.</param>
    private void AppendComparator<T>(scoped ReadOnlySpan<char> name, scoped ReadOnlySpan<char> suffix, T value)
    {
        AppendSeparator();
        _builder.Append(name);
        _builder.Append(suffix);
        _builder.Append('=');
        AppendElement(value);
    }

    /// <summary>
    /// Elements are escaped one at a time and joined with a literal comma, so a value that itself
    /// contains a comma arrives as <c>%2C</c> and stays distinguishable from the separator.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="name">The field's base name.</param>
    /// <param name="suffix">The comparator suffix, <c>.any_of</c> or <c>.all_of</c>.</param>
    /// <param name="values">The values to render, comma-joined.</param>
    private void AppendSet<T>(scoped ReadOnlySpan<char> name, scoped ReadOnlySpan<char> suffix, T[] values)
    {
        AppendSeparator();
        _builder.Append(name);
        _builder.Append(suffix);
        _builder.Append('=');

        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                _builder.Append(',');
            }

            AppendElement(values[i]);
        }
    }

    /// <summary>
    /// Formats one element. <c>typeof(T)</c> is a constant for each generic instantiation, so for
    /// a value-type <typeparamref name="T"/> the JIT and the Native AOT compiler fold this to the
    /// one matching branch, with <see cref="Unsafe.As{TFrom, TTo}(ref TFrom)"/> reinterpreting
    /// without boxing; the shared canonical body for the reference-type instantiation
    /// (<typeparamref name="T"/> = <see cref="string"/>) still pays one runtime type-handle
    /// comparison, cheap because that branch is checked first. The set is closed on purpose: the
    /// generator refuses any other element type before it reaches here, so the fallback is
    /// unreachable from generated code and exists only to fail loudly for hand-written callers.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="value">The value to format and append.</param>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not one of the supported element types.</exception>
    private void AppendElement<T>(T value)
    {
        if (typeof(T) == typeof(string))
        {
            _builder.Append(Uri.EscapeDataString(Unsafe.As<T, string>(ref value)));
        }
        else if (typeof(T) == typeof(int))
        {
            _builder.Append(Unsafe.As<T, int>(ref value));
        }
        else if (typeof(T) == typeof(long))
        {
            _builder.Append(Unsafe.As<T, long>(ref value));
        }
        else if (typeof(T) == typeof(double))
        {
            _builder.Append(Unsafe.As<T, double>(ref value));
        }
        else if (typeof(T) == typeof(LocalDate))
        {
            // A fixed-form literal: ISO dates need no escaping.
            _builder.Append(Unsafe.As<T, LocalDate>(ref value).ToWireValue());
        }
        else if (typeof(T) == typeof(DateOrTimestamp))
        {
            // Unlike LocalDate, this literal is caller-supplied (DateOrTimestamp.FromLiteral
            // accepts any non-whitespace string), so it must be escaped like any other untrusted
            // value or it could inject a second query parameter.
            _builder.Append(Uri.EscapeDataString(Unsafe.As<T, DateOrTimestamp>(ref value).ToString()));
        }
        else
        {
            throw new NotSupportedException(
                $"{typeof(T)} is not a supported filter element type. Supported: string, int, long, double, LocalDate, DateOrTimestamp.");
        }
    }
}
