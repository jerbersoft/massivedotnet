namespace MassiveDotNet;

/// <summary>
/// A filter over an array-valued field such as <c>tickers</c>: arrays that contain a value, any
/// of several values, or all of them.
/// </summary>
/// <typeparam name="T">The array's element type.</typeparam>
/// <remarks>
/// Build one with the factories on <see cref="ArrayFilter"/>. A plain <typeparamref name="T"/>
/// converts implicitly to <see cref="ArrayFilter.Contains{T}(T)"/>, and a
/// <see cref="SetFilter{T}"/> converts to the matching form, so <c>SetFilter.AnyOf</c> can be
/// passed wherever an array filter is expected. An unset filter (<see langword="default"/>)
/// renders nothing, exactly like a <see langword="null"/> parameter.
/// </remarks>
public readonly struct ArrayFilter<T>
{
    private readonly T _value;
    private readonly T[]? _values;
    private readonly FilterMode _mode;

    /// <summary>
    /// Constructs a filter directly from its parts; callers reach this through the factories
    /// and the implicit conversion.
    /// </summary>
    /// <param name="value">The single value, for a contains filter.</param>
    /// <param name="values">The set's values, for an any-of or all-of filter. Not copied from the caller.</param>
    /// <param name="mode">Which form is set.</param>
    internal ArrayFilter(T value, T[]? values, FilterMode mode)
    {
        _value = value;
        _values = values;
        _mode = mode;
    }

    /// <summary>The single value, for a contains filter.</summary>
    internal T Value => _value;

    /// <summary>The set's values, for an any-of or all-of filter. Not copied from the caller.</summary>
    internal T[]? Values => _values;

    /// <summary>Which form is set.</summary>
    internal FilterMode Mode => _mode;

    /// <summary>Converts a value to a contains filter: <c>field=value</c>.</summary>
    /// <param name="value">The value the array must contain.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static implicit operator ArrayFilter<T>(T value) => ArrayFilter.Contains(value);

    /// <summary>
    /// Converts a set filter. Any-of stays any-of; an equality becomes a contains filter, which is
    /// what the plain form of an array field means.
    /// </summary>
    /// <param name="filter">The set filter.</param>
    public static implicit operator ArrayFilter<T>(SetFilter<T> filter) =>
        new(filter.Value, filter.Values, filter.Mode);
}

/// <summary>
/// Factories for <see cref="ArrayFilter{T}"/>. They live on a non-generic class so the element
/// type is inferred from the arguments.
/// </summary>
public static class ArrayFilter
{
    /// <summary>Results whose array contains <paramref name="value"/>: <c>field=value</c>.</summary>
    /// <typeparam name="T">The array's element type.</typeparam>
    /// <param name="value">The value the array must contain.</param>
    /// <returns>The filter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static ArrayFilter<T> Contains<T>(T value)
    {
        FilterGuard.ThrowIfNull(value, nameof(value));
        return new ArrayFilter<T>(value, null, FilterMode.Equal);
    }

    /// <summary>Results whose array contains any of <paramref name="values"/>: <c>field.any_of=a,b</c>.</summary>
    /// <typeparam name="T">The array's element type.</typeparam>
    /// <param name="values">One or more values. The array is held, not copied.</param>
    /// <returns>The filter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty or contains a <see langword="null"/>.</exception>
    public static ArrayFilter<T> AnyOf<T>(params T[] values) =>
        new(default!, FilterGuard.ValidateSet(values, nameof(values)), FilterMode.AnyOf);

    /// <summary>Results whose array contains every one of <paramref name="values"/>: <c>field.all_of=a,b</c>.</summary>
    /// <typeparam name="T">The array's element type.</typeparam>
    /// <param name="values">One or more values. The array is held, not copied.</param>
    /// <returns>The filter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty or contains a <see langword="null"/>.</exception>
    public static ArrayFilter<T> AllOf<T>(params T[] values) =>
        new(default!, FilterGuard.ValidateSet(values, nameof(values)), FilterMode.AllOf);
}
