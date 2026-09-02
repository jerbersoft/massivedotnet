using System.Text.Json;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Guards the SDK's central promise: every public Massive REST operation is represented.
/// These run against the vendored OpenAPI description and the curated map, so drift in either
/// one fails the build rather than surfacing as a missing method at runtime.
/// </summary>
public sealed class EndpointCoverageTests
{
    /// <summary>
    /// The number of operations mapped so far. This may only ever increase: raise it as
    /// endpoints are added, and the test then prevents anyone silently dropping one.
    /// </summary>
    private const int CoverageBaseline = 7;

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void EveryMappedOperationExistsInTheSpecification()
    {
        HashSet<string> specOperations = SpecOperationIds();
        string[] missing = [.. MappedOperationIds().Where(id => !specOperations.Contains(id))];

        Assert.True(
            missing.Length == 0,
            $"The map references operations that are absent from the OpenAPI description: {string.Join(", ", missing)}");
    }

    [Fact]
    public void MethodNamesAreUniqueWithinAGroup()
    {
        using JsonDocument map = LoadMap();

        List<string> qualified = [];

        foreach (JsonElement endpoint in map.RootElement.GetProperty("endpoints").EnumerateArray())
        {
            string group = endpoint.GetProperty("group").GetString()!;
            string method = endpoint.GetProperty("method").GetString()!;

            qualified.Add($"{group}.{method}");

            // A paginated endpoint also emits an Enumerate counterpart, derived by dropping the
            // List prefix, so two map methods can collide on a name that neither of them spells
            // out. Projecting the derived name here reports that as a named failing test rather
            // than as a duplicate-member compiler error in generated code. Every List-prefixed
            // method is projected, not only the paginated ones: since the generator refuses to
            // derive an Enumerate name from anything else, that is a superset of what it emits,
            // and a collision on a name it might emit is worth failing on either way.
            if (method.StartsWith("List", StringComparison.Ordinal))
            {
                qualified.Add($"{group}.Enumerate{method["List".Length..]}");
            }
        }

        string[] duplicates = [.. qualified
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)];

        Assert.True(
            duplicates.Length == 0,
            $"Two endpoints would generate the same method: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void CoverageDoesNotRegress()
    {
        int mapped = MappedOperationIds().Count;

        Assert.True(
            mapped >= CoverageBaseline,
            $"Endpoint coverage fell from {CoverageBaseline} to {mapped}. "
            + "Endpoints may be added but not removed.");
    }

    [Fact]
    public void ReportsOperationsThatRemainUnmapped()
    {
        HashSet<string> mapped = MappedOperationIds();
        HashSet<string> all = SpecOperationIds();

        int remaining = all.Count - mapped.Count;

        // Not an assertion while the SDK is being built out; this documents the work left.
        Assert.True(
            remaining >= 0,
            $"{mapped.Count}/{all.Count} operations mapped, {remaining} remaining.");
    }

    private static HashSet<string> SpecOperationIds()
    {
        using JsonDocument spec = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(RepositoryRoot, "specs", "openapi.json")));

        HashSet<string> ids = new(StringComparer.Ordinal);

        foreach (JsonProperty path in spec.RootElement.GetProperty("paths").EnumerateObject())
        {
            if (path.Value.TryGetProperty("get", out JsonElement operation)
                && operation.TryGetProperty("operationId", out JsonElement id))
            {
                ids.Add(id.GetString()!);
            }
        }

        return ids;
    }

    private static HashSet<string> MappedOperationIds()
    {
        using JsonDocument map = LoadMap();

        return [.. map.RootElement
            .GetProperty("endpoints")
            .EnumerateArray()
            .Select(e => e.GetProperty("operationId").GetString()!)];
    }

    private static JsonDocument LoadMap() => JsonDocument.Parse(
        File.ReadAllBytes(Path.Combine(RepositoryRoot, "specs", "endpoints.map.json")),
        new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MassiveDotNet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
