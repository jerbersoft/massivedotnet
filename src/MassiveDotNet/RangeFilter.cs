namespace MassiveDotNet;

/// <summary>
/// A filter over an ordered field: an exact value, a lower bound, an upper bound, or both.
/// </summary>
/// <typeparam name="T">The field's element type.</typeparam>
/// <remarks>
/// <para>
/// Build one with the factories on <see cref="RangeFilter"/>, or pass a plain
/// <typeparamref name="T"/> where a filter is expected: it converts implicitly to an equality
/// filter, so <c>ticker: "AAPL"</c> keeps compiling on an endpoint that also accepts a range.
/// </para>
/// <para>
/// A lower bound accepts an upper bound afterwards and vice versa, so a half-open window is
/// <c>RangeFilter.Gte(start).Lt(end)</c>. An unset filter (<see langword="default"/>) renders
/// nothing, exactly like a <see langword="null"/> parameter.
/// </para>
/// </remarks>
public readonly struct RangeFilter<T>
{
    private readonly T _lower;
    private readonly T _upper;
    private readonly FilterMode _mode;

    /// <summary>
    /// Constructs a filter directly from its parts; callers reach this through the factories
    /// and the implicit conversion.
    /// </summary>
    /// <param name="lower">The lower bound, or the value for an equality filter.</param>
    /// <param name="upper">The upper bound.</param>
    /// <param name="mode">Which comparator forms are set.</param>
    internal RangeFilter(T lower, T upper, FilterMode mode)
    {
        _lower = lower;
        _upper = upper;
        _mode = mode;
    }

    /// <summary>The lower bound, or the exact value for an equality filter.</summary>
    internal T Lower => _lower;

    /// <summary>The upper bound.</summary>
    internal T Upper => _upper;

    /// <summary>Which forms are set.</summary>
    internal FilterMode Mode => _mode;

    /// <summary>Adds an exclusive lower bound: <c>field.gt=value</c>.</summary>
    /// <param name="value">The value results must exceed.</param>
    /// <returns>The filter with the bound added.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The filter is an equality, or already has a lower bound.</exception>
    public RangeFilter<T> Gt(T value) => WithLower(value, FilterMode.Gt);

    /// <summary>Adds an inclusive lower bound: <c>field.gte=value</c>.</summary>
    /// <param name="value">The value results must reach.</param>
    /// <returns>The filter with the bound added.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The filter is an equality, or already has a lower bound.</exception>
    public RangeFilter<T> Gte(T value) => WithLower(value, FilterMode.Gte);

    /// <summary>Adds an exclusive upper bound: <c>field.lt=value</c>.</summary>
    /// <param name="value">The value results must stay below.</param>
    /// <returns>The filter with the bound added.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The filter is an equality, or already has an upper bound.</exception>
    public RangeFilter<T> Lt(T value) => WithUpper(value, FilterMode.Lt);

    /// <summary>Adds an inclusive upper bound: <c>field.lte=value</c>.</summary>
    /// <param name="value">The value results must not exceed.</param>
    /// <returns>The filter with the bound added.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The filter is an equality, or already has an upper bound.</exception>
    public RangeFilter<T> Lte(T value) => WithUpper(value, FilterMode.Lte);

    /// <summary>Converts a value to an equality filter: <c>field=value</c>.</summary>
    /// <param name="value">The exact value to match.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static implicit operator RangeFilter<T>(T value)
    {
        FilterGuard.ThrowIfNull(value, nameof(value));
        return new RangeFilter<T>(value, default!, FilterMode.Equal);
    }

    private RangeFilter<T> WithLower(T value, FilterMode bound)
    {
        FilterGuard.ThrowIfNull(value, nameof(value));
        ThrowIfEquality();

        if ((_mode & (FilterMode.Gt | FilterMode.Gte)) != 0)
        {
            throw new InvalidOperationException("This filter already has a lower bound.");
        }

        return new RangeFilter<T>(value, _upper, _mode | bound);
    }

    private RangeFilter<T> WithUpper(T value, FilterMode bound)
    {
        FilterGuard.ThrowIfNull(value, nameof(value));
        ThrowIfEquality();

        if ((_mode & (FilterMode.Lt | FilterMode.Lte)) != 0)
        {
            throw new InvalidOperationException("This filter already has an upper bound.");
        }

        return new RangeFilter<T>(_lower, value, _mode | bound);
    }

    private void ThrowIfEquality()
    {
        if ((_mode & FilterMode.Equal) != 0)
        {
            throw new InvalidOperationException("An equality filter cannot take a bound.");
        }
    }
}

/// <summary>
/// Factories for <see cref="RangeFilter{T}"/>. They live on a non-generic class so the element
/// type is inferred from the argument: <c>RangeFilter.Gt(0.5)</c> rather than
/// <c>RangeFilter&lt;double&gt;.Gt(0.5)</c>.
/// </summary>
public static class RangeFilter
{
    /// <summary>Results strictly greater than <paramref name="value"/>: <c>field.gt=value</c>.</summary>
    /// <typeparam name="T">The field's element type.</typeparam>
    /// <param name="value">The exclusive lower bound.</param>
    /// <returns>The filter.</returns>
    public static RangeFilter<T> Gt<T>(T value) => default(RangeFilter<T>).Gt(value);

    /// <summary>Results at or above <paramref name="value"/>: <c>field.gte=value</c>.</summary>
    /// <typeparam name="T">The field's element type.</typeparam>
    /// <param name="value">The inclusive lower bound.</param>
    /// <returns>The filter.</returns>
    public static RangeFilter<T> Gte<T>(T value) => default(RangeFilter<T>).Gte(value);

    /// <summary>Results strictly below <paramref name="value"/>: <c>field.lt=value</c>.</summary>
    /// <typeparam name="T">The field's element type.</typeparam>
    /// <param name="value">The exclusive upper bound.</param>
    /// <returns>The filter.</returns>
    public static RangeFilter<T> Lt<T>(T value) => default(RangeFilter<T>).Lt(value);

    /// <summary>Results at or below <paramref name="value"/>: <c>field.lte=value</c>.</summary>
    /// <typeparam name="T">The field's element type.</typeparam>
    /// <param name="value">The inclusive upper bound.</param>
    /// <returns>The filter.</returns>
    public static RangeFilter<T> Lte<T>(T value) => default(RangeFilter<T>).Lte(value);

    /// <summary>
    /// Results at or above <paramref name="lower"/> and at or below <paramref name="upper"/>:
    /// <c>field.gte=lower&amp;field.lte=upper</c>. Inclusive at both ends; ordering is not checked.
    /// </summary>
    /// <typeparam name="T">The field's element type.</typeparam>
    /// <param name="lower">The inclusive lower bound.</param>
    /// <param name="upper">The inclusive upper bound.</param>
    /// <returns>The filter.</returns>
    public static RangeFilter<T> Between<T>(T lower, T upper) => Gte(lower).Lte(upper);
}
