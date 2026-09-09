using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Pins the half of publishing that lives in files rather than in a run (#19).
/// </summary>
/// <remarks>
/// <para>
/// <c>publish.yml</c> pushes one file per id in its <c>PACKAGES</c> list, by name, rather than
/// globbing <c>artifacts/*.nupkg</c>. That is what makes a fifth package a decision somebody
/// records, because the workflow fails on a packed package the list does not name. This test closes
/// the other half of the loop: a project added under <c>src/</c> and never added to the list fails
/// here, on its own commit, rather than at the release that silently skips it.
/// </para>
/// <para>
/// Order is asserted, not just membership, and it is load-bearing. The workflow pushes in list
/// order so a refused push never leaves a package on the feed declaring a dependency on one that is
/// not there: core before the two clients, and both of those before the DI package that depends on
/// them. Asserting the set alone would let a reordering through, and the failure that reordering
/// causes is visible only on a partly-refused release.
/// </para>
/// <para>
/// A regex over the one <c>PACKAGES:</c> line is enough for a file of that shape, and this project
/// has no YAML dependency and should not gain one for it. Neither test needs the workflow to run.
/// </para>
/// </remarks>
public sealed partial class PublishWorkflowTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void EveryPackableProjectIsNamedInThePublishWorkflow()
    {
        string[] listed = ListedPackages();
        string[] onDisk = [.. PackageIdsUnderSrc()];

        // An empty entry means a csproj under src/ declares no PackageId, which would pack under
        // its assembly name and so could never match the list.
        Assert.All(onDisk, id => Assert.NotEmpty(id));

        Assert.Equal(onDisk.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A package must be pushed after everything it depends on, or a refused push leaves a package
    /// on the feed whose dependency does not exist. This derives the requirement from the project
    /// references themselves rather than restating the order, so adding a dependency between two
    /// shipped packages fails here unless the list already reflects it.
    /// </summary>
    [Fact]
    public void ThePublishOrderPutsEveryPackageAfterTheOnesItDependsOn()
    {
        string[] listed = ListedPackages();

        foreach (string id in listed)
        {
            int position = Array.IndexOf(listed, id);

            foreach (string dependency in SiblingDependenciesOf(id))
            {
                int dependencyPosition = Array.IndexOf(listed, dependency);

                Assert.True(
                    dependencyPosition >= 0 && dependencyPosition < position,
                    $"PACKAGES pushes {id} at position {position} but its dependency {dependency} "
                        + $"at position {dependencyPosition}. A dependency must be pushed first, or a "
                        + "refused push leaves a package on the feed declaring one that is not there.");
            }
        }
    }

    /// <summary>
    /// The workflow authenticates by exchanging an OIDC token for a credential valid for one hour.
    /// A long-lived API key secret reintroduced here would be a standing credential in a public
    /// repository, which is the posture rule 13 rejects for the Massive key and which has no better
    /// justification for the nuget.org one.
    /// </summary>
    [Fact]
    public void ThePublishWorkflowHoldsNoLongLivedCredential()
    {
        string workflow = File.ReadAllText(PublishWorkflowPath);

        Assert.Contains("NuGet/login@", workflow, StringComparison.Ordinal);
        Assert.Contains("id-token: write", workflow, StringComparison.Ordinal);

        MatchCollection secrets = SecretReference().Matches(workflow);
        string[] offenders = [.. secrets.Select(match => match.Value)];

        Assert.True(
            offenders.Length == 0,
            "publish.yml references a repository secret, so it no longer publishes on OIDC alone: "
                + string.Join(", ", offenders));
    }

    private static string PublishWorkflowPath =>
        Path.Combine(RepositoryRoot, ".github", "workflows", "publish.yml");

    private static string[] ListedPackages()
    {
        string workflow = File.ReadAllText(PublishWorkflowPath);
        Match match = PackagesLine().Match(workflow);

        Assert.True(match.Success, "publish.yml has no PACKAGES: line, so nothing governs what ships.");

        return match.Groups["ids"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static IEnumerable<string> PackageIdsUnderSrc()
    {
        foreach (string project in Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            XDocument document = XDocument.Load(project);

            if (document.Descendants("IsPackable").Any(element => element.Value == "false"))
            {
                continue;
            }

            yield return document.Descendants("PackageId").FirstOrDefault()?.Value ?? string.Empty;
        }
    }

    /// <summary>The shipped packages a given package depends on, read from its project references.</summary>
    private static IEnumerable<string> SiblingDependenciesOf(string id)
    {
        string project = Path.Combine(RepositoryRoot, "src", id, $"{id}.csproj");

        if (!File.Exists(project))
        {
            yield break;
        }

        foreach (XElement reference in XDocument.Load(project).Descendants("ProjectReference"))
        {
            string? include = reference.Attribute("Include")?.Value;

            if (include is not null)
            {
                yield return Path.GetFileNameWithoutExtension(include);
            }
        }
    }

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

    [GeneratedRegex(@"^\s*PACKAGES:\s*(?<ids>.+)$", RegexOptions.Multiline)]
    private static partial Regex PackagesLine();

    [GeneratedRegex(@"secrets\.[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex SecretReference();
}
