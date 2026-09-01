namespace MassiveDotNet;

/// <summary>
/// Well-known base addresses for the Massive platform API.
/// </summary>
public static class MassiveEndpoints
{
    /// <summary>The production API host, <c>https://api.massive.com</c>.</summary>
    public static Uri Production { get; } = new("https://api.massive.com", UriKind.Absolute);

    /// <summary>The staging API host, <c>https://api.staging.massive.com</c>.</summary>
    public static Uri Staging { get; } = new("https://api.staging.massive.com", UriKind.Absolute);

    /// <summary>
    /// The legacy Polygon.io API host, <c>https://api.polygon.io</c>. Massive operates this
    /// host in parallel with <see cref="Production"/> for existing integrations.
    /// </summary>
    public static Uri Legacy { get; } = new("https://api.polygon.io", UriKind.Absolute);
}
