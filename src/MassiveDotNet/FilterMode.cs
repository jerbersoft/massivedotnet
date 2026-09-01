namespace MassiveDotNet;

/// <summary>
/// Which comparator forms a filter carries. Read by <see cref="Http.RequestUriBuilder"/> when it
/// renders the filter; never exposed to callers, who express intent through the factories.
/// </summary>
[Flags]
internal enum FilterMode : byte
{
    /// <summary>An unset filter, which renders nothing.</summary>
    None = 0,

    /// <summary>The plain field: <c>field=value</c>. For array fields, "contains".</summary>
    Equal = 1,

    /// <summary><c>field.gt=value</c>.</summary>
    Gt = 2,

    /// <summary><c>field.gte=value</c>.</summary>
    Gte = 4,

    /// <summary><c>field.lt=value</c>.</summary>
    Lt = 8,

    /// <summary><c>field.lte=value</c>.</summary>
    Lte = 16,

    /// <summary><c>field.any_of=a,b</c>.</summary>
    AnyOf = 32,

    /// <summary><c>field.all_of=a,b</c>.</summary>
    AllOf = 64,
}
