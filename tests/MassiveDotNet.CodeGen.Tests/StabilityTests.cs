using Xunit;

namespace MassiveDotNet.CodeGen.Tests;

/// <summary>
/// Rule 2: deprecated operations ship marked <c>[Obsolete]</c> and <c>vX</c> or <c>dev</c>
/// operations ship marked <c>[Experimental]</c>. Both signals are read from the description, never from the map
/// (D18), so these tests drive the generator with the extensions and paths the description uses.
/// </summary>
public sealed class StabilityTests
{
    private const string Deprecation =
        """
        "x-polygon-deprecation": { "date": 1654056060000, "replaces": { "name": "Things", "path": "get_v1_things" } }
        """;

    private static readonly string Thing = Harness.Envelope("""{ "type": "object", "properties": { "name": { "type": "string" } } }""");

    [Fact]
    public void ADeprecatedOperationIsObsoleteAndNamesItsReplacement()
    {
        string spec = Harness.Document(
            new Operation("ListThings", "/v1/things", Thing),
            new Operation("ListOldThings", "/v1/old-things", Thing, Extensions: Deprecation));

        string map = Harness.MapDocument(
            """
            "Thing": { "schema": { "operationId": "ListThings", "pointer": "results/items" } }
            """,
            Harness.Endpoint("ListThings", "Thing") + "," + Harness.Endpoint("ListOldThings", "Thing", method: "ListOldThings"));

        string group = Harness.Generate(spec, map)["ReferenceGroup.g.cs"];

        Assert.Contains(
            "    [Obsolete(\"Massive has deprecated this operation. Use Reference.ListThingsAsync instead.\", DiagnosticId = \"MASSIVE0002\")]\n"
            + "    public Task<Thing[]> ListOldThingsAsync(",
            group,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAReplacementSlugThatMatchesNoOperation()
    {
        string spec = Harness.Document(new Operation(
            "ListOldThings",
            "/v1/old-things",
            Thing,
            Extensions: """
                "x-polygon-deprecation": { "replaces": { "name": "Gone", "path": "get_v1_gone" } }
                """));

        string map = Harness.MapDocument(
            """
            "Thing": { "schema": { "operationId": "ListOldThings", "pointer": "results/items" } }
            """,
            Harness.Endpoint("ListOldThings", "Thing", method: "ListOldThings"));

        Assert.Equal(
            "Operation 'ListOldThings' is deprecated in favour of 'get_v1_gone', which matches no operation "
            + "in the OpenAPI description. The slug is derived from the replacement's route, so either the "
            + "description changed or the derivation in Spec.Slug needs revisiting.",
            Harness.Refusal(spec, map));
    }

    [Fact]
    public void RefusesAReplacementThatIsNotMapped()
    {
        string spec = Harness.Document(
            new Operation("ListThings", "/v1/things", Thing),
            new Operation("ListOldThings", "/v1/old-things", Thing, Extensions: Deprecation));

        string map = Harness.MapDocument(
            """
            "Thing": { "schema": { "operationId": "ListOldThings", "pointer": "results/items" } }
            """,
            Harness.Endpoint("ListOldThings", "Thing", method: "ListOldThings"));

        Assert.Equal(
            "Operation 'ListOldThings' is deprecated in favour of 'ListThings' (/v1/things), which is not "
            + "mapped. Map the replacement first, so the Obsolete message can name the method that "
            + "supersedes it.",
            Harness.Refusal(spec, map));
    }

    [Fact]
    public void AnExperimentalPathIsMarkedExperimental()
    {
        string spec = Harness.Document(new Operation("ListThings", "/vX/things", Thing));

        string map = Harness.MapDocument(
            """
            "Thing": { "schema": { "operationId": "ListThings", "pointer": "results/items" } }
            """,
            Harness.Endpoint("ListThings", "Thing"));

        string group = Harness.Generate(spec, map)["ReferenceGroup.g.cs"];

        Assert.Contains("using System.Diagnostics.CodeAnalysis;\n", group, StringComparison.Ordinal);
        Assert.Contains(
            "    [Experimental(\"MASSIVE0001\", Message = \"Massive marks this operation experimental: it may change "
            + "or be removed without notice. Suppress MASSIVE0001 to opt in.\")]\n"
            + "    public Task<Thing[]> ListThingsAsync(",
            group,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheExperimentalExtensionMarksAStablePath()
    {
        string spec = Harness.Document(new Operation(
            "ListThings",
            "/v1/things",
            Thing,
            Extensions: """
                "x-polygon-experimental": {}
                """));

        string map = Harness.MapDocument(
            """
            "Thing": { "schema": { "operationId": "ListThings", "pointer": "results/items" } }
            """,
            Harness.Endpoint("ListThings", "Thing"));

        string group = Harness.Generate(spec, map)["ReferenceGroup.g.cs"];

        Assert.Contains("    [Experimental(\"MASSIVE0001\", Message = ", group, StringComparison.Ordinal);
    }

    [Fact]
    public void ADevPathIsMarkedExperimental()
    {
        // Massive's in-development routes carry a dev segment where a released one carries a
        // version; it is read the same way as vX, from the path and never from the map (D22).
        string spec = Harness.Document(new Operation("ListThings", "/stocks/dev/things", Thing));

        string map = Harness.MapDocument(
            """
            "Thing": { "schema": { "operationId": "ListThings", "pointer": "results/items" } }
            """,
            Harness.Endpoint("ListThings", "Thing"));

        string group = Harness.Generate(spec, map)["ReferenceGroup.g.cs"];

        Assert.Contains("    [Experimental(\"MASSIVE0001\", Message = ", group, StringComparison.Ordinal);
    }

    [Fact]
    public void BothEntryPointsOfAPaginatedOperationCarryTheAttribute()
    {
        const string paged = """
            {
              "type": "object",
              "properties": {
                "results": { "type": "array", "items": { "type": "object", "properties": { "name": { "type": "string" } } } },
                "next_url": { "type": "string" },
                "status": { "type": "string" }
              }
            }
            """;

        string spec = Harness.Document(
            new Operation("ListThings", "/v1/things", paged),
            new Operation("ListOldThings", "/v1/old-things", paged, Extensions: Deprecation));

        string map = Harness.MapDocument(
            """
            "Thing": { "schema": { "operationId": "ListThings", "pointer": "results/items" } }
            """,
            Harness.Endpoint("ListThings", "Thing") + "," + Harness.Endpoint("ListOldThings", "Thing", method: "ListOldThings"));

        string group = Harness.Generate(spec, map)["ReferenceGroup.g.cs"];

        const string attribute =
            "    [Obsolete(\"Massive has deprecated this operation. Use Reference.ListThingsAsync instead.\", DiagnosticId = \"MASSIVE0002\")]\n";

        Assert.Contains(attribute + "    public IAsyncEnumerable<Thing> EnumerateOldThingsAsync(", group, StringComparison.Ordinal);
        Assert.Contains(attribute + "    public Task<MassivePage<Thing>> ListOldThingsAsync(", group, StringComparison.Ordinal);
    }

    // The three tests below pin branches the first Obsolete test's implementation covered in the
    // same step, so they passed on their first run; they are here so those branches cannot be
    // lost silently.

    [Fact]
    public void ADeprecationWithoutAReplacementStopsAtTheFirstSentence()
    {
        string spec = Harness.Document(new Operation(
            "ListOldThings",
            "/v1/old-things",
            Thing,
            Extensions: """
                "x-polygon-deprecation": { "date": 1719838800000 }
                """));

        string map = Harness.MapDocument(
            """
            "Thing": { "schema": { "operationId": "ListOldThings", "pointer": "results/items" } }
            """,
            Harness.Endpoint("ListOldThings", "Thing", method: "ListOldThings"));

        string group = Harness.Generate(spec, map)["ReferenceGroup.g.cs"];

        Assert.Contains(
            "    [Obsolete(\"Massive has deprecated this operation.\", DiagnosticId = \"MASSIVE0002\")]\n"
            + "    public Task<Thing[]> ListOldThingsAsync(",
            group,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AStableOperationCarriesNoAttribute()
    {
        string spec = Harness.Document(new Operation("ListThings", "/v1/things", Thing));

        string map = Harness.MapDocument(
            """
            "Thing": { "schema": { "operationId": "ListThings", "pointer": "results/items" } }
            """,
            Harness.Endpoint("ListThings", "Thing"));

        string group = Harness.Generate(spec, map)["ReferenceGroup.g.cs"];

        Assert.DoesNotContain("[Obsolete(", group, StringComparison.Ordinal);
        Assert.DoesNotContain("[Experimental(", group, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Diagnostics.CodeAnalysis;", group, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/v3/trades/{stockTicker}", "get_v3_trades__stockticker")]
    [InlineData("/v1/historic/forex/{from}/{to}/{date}", "get_v1_historic_forex__from___to___date")]
    public void TheSlugFollowsTheDocsSiteRule(string path, string slug)
    {
        // Both pairs are copied from the description: the route of a replacement and the slug the
        // deprecated operation names it by. The rule is undocumented, so this is its only anchor.
        Assert.Equal(slug, Spec.Slug(path));
    }
}
