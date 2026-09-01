namespace MassiveDotNet;

/// <summary>
/// A filter over a field that accepts membership in a set: an exact value, or any of several.
/// </summary>
/// <typeparam name="T">The field's element type.</typeparam>
/// <remarks>
/// Build one with <see cref="SetFilter.AnyOf{T}(T[])"/>, or pass a plain
/// <typeparamref name="T"/> where a filter is expected: it converts implicitly to an equality
/// filter. An unset filter (<see langword="default"/>) renders nothing, exactly like a
/// <see langword="null"/> parameter.
/// </remarks>
public readonly struct SetFilter<T>
{
    private readonly T _value;
    private readonly T[]? _values;
    private readonly FilterMode _mode;

    /// <summary>
    /// Constructs a filter directly from its parts; callers reach this through the factories
    /// and the implicit conversion.
    /// </summary>
    /// <param name="value">The exact value, for an equality filter.</param>
    /// <param name="values">The set's values, for an any-of filter. Not copied from the caller.</param>
    /// <param name="mode">Which form is set.</param>
    internal SetFilter(T value, T[]? values, FilterMode mode)
    {
        _value = value;
        _values = values;
        _mode = mode;
    }

    /// <summary>The exact value, for an equality filter.</summary>
    internal T Value => _value;

    /// <summary>The set's values, for an any-of filter. Not copied from the caller.</summary>
    internal T[]? Values => _values;

    /// <summary>Which form is set.</summary>
    internal FilterMode Mode => _mode;

    /// <summary>Converts a value to an equality filter: <c>field=value</c>.</summary>
    /// <param name="value">The exact value to match.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static implicit operator SetFilter<T>(T value)
    {
        FilterGuard.ThrowIfNull(value, nameof(value));
        return new SetFilter<T>(value, null, FilterMode.Equal);
    }
}

/// <summary>
/// Factories for <see cref="SetFilter{T}"/>. They live on a non-generic class so the element
/// type is inferred from the arguments.
/// </summary>
public static class SetFilter
{
    /// <summary>Results whose field equals any of <paramref name="values"/>: <c>field.any_of=a,b</c>.</summary>
    /// <typeparam name="T">The field's element type.</typeparam>
    /// <param name="values">One or more values. The array is held, not copied.</param>
    /// <returns>The filter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty or contains a <see langword="null"/>.</exception>
    public static SetFilter<T> AnyOf<T>(params T[] values) =>
        new(default!, FilterGuard.ValidateSet(values, nameof(values)), FilterMode.AnyOf);
}
