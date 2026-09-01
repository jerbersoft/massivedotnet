namespace MassiveDotNet;

/// <summary>Argument checks shared by the filter types.</summary>
/// <remarks>
/// <see cref="ArgumentNullException.ThrowIfNull(object?, string?)"/> takes <see cref="object"/>,
/// which boxes a value-type <c>T</c> on every call. The <c>is null</c> pattern compiles to a plain
/// reference check for reference types and to nothing at all for value types.
/// </remarks>
internal static class FilterGuard
{
    /// <summary>Throws when a filter value is <see langword="null"/>.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="value">The value to check.</param>
    /// <param name="parameterName">The name reported in the exception.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static void ThrowIfNull<T>(T value, string parameterName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }
    }

    /// <summary>Validates the values of a set filter and returns them unchanged.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="values">The caller-supplied array. It is not copied.</param>
    /// <param name="parameterName">The name reported in the exception.</param>
    /// <returns><paramref name="values"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty or contains a <see langword="null"/>.</exception>
    public static T[] ValidateSet<T>(T[] values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);

        if (values.Length == 0)
        {
            throw new ArgumentException("A set filter needs at least one value.", parameterName);
        }

        foreach (T value in values)
        {
            if (value is null)
            {
                throw new ArgumentException("A set filter cannot contain a null value.", parameterName);
            }
        }

        return values;
    }
}
