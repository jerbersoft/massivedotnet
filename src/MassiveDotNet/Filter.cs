namespace MassiveDotNet;

/// <summary>
/// A filter over a field that accepts both a range and set membership: an exact value, bounds,
/// or any of several values.
/// </summary>
/// <typeparam name="T">The field's element type.</typeparam>
/// <remarks>
/// There are no factories on this type. It is built by conversion from a plain
/// <typeparamref name="T"/> (equality), a <see cref="RangeFilter{T}"/>, or a
/// <see cref="SetFilter{T}"/>, so callers only ever write <c>RangeFilter.Between(a, b)</c> or
/// <c>SetFilter.AnyOf(a, b)</c> and meet this type in signatures alone. An unset filter
/// (<see langword="default"/>) renders nothing, exactly like a <see langword="null"/> parameter.
/// </remarks>
public readonly struct Filter<T>
{
    private readonly T _lower;
    private readonly T _upper;
    private readonly T[]? _values;
    private readonly FilterMode _mode;

    /// <summary>
    /// Constructs a filter directly from its parts; callers reach this through the implicit
    /// conversions.
    /// </summary>
    /// <param name="lower">The lower bound, or the exact value for an equality filter.</param>
    /// <param name="upper">The upper bound.</param>
    /// <param name="values">The set's values, for an any-of filter. Not copied from the caller.</param>
    /// <param name="mode">Which forms are set.</param>
    private Filter(T lower, T upper, T[]? values, FilterMode mode)
    {
        _lower = lower;
        _upper = upper;
        _values = values;
        _mode = mode;
    }

    /// <summary>The lower bound, or the exact value for an equality filter.</summary>
    internal T Lower => _lower;

    /// <summary>The upper bound.</summary>
    internal T Upper => _upper;

    /// <summary>The set's values, for an any-of filter. Not copied from the caller.</summary>
    internal T[]? Values => _values;

    /// <summary>Which forms are set.</summary>
    internal FilterMode Mode => _mode;

    /// <summary>Converts a value to an equality filter: <c>field=value</c>.</summary>
    /// <param name="value">The exact value to match.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static implicit operator Filter<T>(T value)
    {
        FilterGuard.ThrowIfNull(value, nameof(value));
        return new Filter<T>(value, default!, null, FilterMode.Equal);
    }

    /// <summary>Converts a range filter, unchanged.</summary>
    /// <param name="filter">The range filter.</param>
    public static implicit operator Filter<T>(RangeFilter<T> filter) =>
        new(filter.Lower, filter.Upper, null, filter.Mode);

    /// <summary>Converts a set filter, unchanged.</summary>
    /// <param name="filter">The set filter.</param>
    public static implicit operator Filter<T>(SetFilter<T> filter) =>
        new(filter.Value, default!, filter.Values, filter.Mode);
}
