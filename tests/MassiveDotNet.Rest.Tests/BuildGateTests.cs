using System.Xml.Linq;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Guards the build configuration that makes constitution rule 9 a gate rather than a claim.
/// </summary>
/// <remarks>
/// <c>EnforceCodeStyleInBuild</c> makes the style analyzers run, but they run at whatever severity
/// they are configured to, and without an <c>.editorconfig</c> that is "suggestion" for all of them.
/// An unused <c>using</c> reached master that way. These tests assert the two pieces that turn the
/// floor rules into build failures, because both are single lines that a future edit could drop
/// without any test going red.
/// <para>
/// This guards the configuration against silent removal, not the rules against being wrong. The
/// gate itself is the build: the floor's real proof is that a deliberate violation fails it.
/// </para>
/// </remarks>
public sealed class BuildGateTests
{
    /// <summary>
    /// The rules the repository promises will fail a build. Each had zero violations when the floor
    /// was set, so the list is a forward commitment rather than a record of what was cleaned up.
    /// </summary>
    private static readonly string[] FloorRules =
    [
        "IDE0005", // unused using directive
        "IDE0035", // unreachable code
        "IDE0051", // unused private member
        "IDE0052", // private member assigned but never read
    ];

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void TheEditorConfigRaisesEveryFloorRuleToWarning()
    {
        string path = Path.Combine(RepositoryRoot, ".editorconfig");
        Assert.True(File.Exists(path), $"{path} is missing, so no style rule can fail a build.");

        string configuration = File.ReadAllText(path);
        string[] missing = [.. FloorRules.Where(rule => !SetsSeverityToWarning(configuration, rule))];

        Assert.True(
            missing.Length == 0,
            $".editorconfig does not raise these rules to 'warning': {string.Join(", ", missing)}.");
    }

    /// <summary>
    /// IDE0005 does not run at all unless <c>GenerateDocumentationFile</c> is set, which is why the
    /// test projects were exempt while <c>src</c> was not. The root sets it for every project, and
    /// MSBuild stops at the first <c>Directory.Build.props</c> it finds walking up — so a nested one
    /// that forgets to import its parent silently takes the setting away from everything beneath it.
    /// </summary>
    [Fact]
    public void TheDocumentationFileThatIde0005RequiresReachesEveryProject()
    {
        string rootProperties = Path.Combine(RepositoryRoot, "Directory.Build.props");
        Assert.Contains(
            "<GenerateDocumentationFile>true</GenerateDocumentationFile>",
            File.ReadAllText(rootProperties));

        string[] orphaned =
        [
            .. Directory
                .EnumerateFiles(RepositoryRoot, "Directory.Build.props", SearchOption.AllDirectories)
                .Where(file => !PathEquals(file, rootProperties))
                .Where(file => !File.ReadAllText(file).Contains("GetPathOfFileAbove", StringComparison.Ordinal))
                .Select(file => Path.GetRelativePath(RepositoryRoot, file))
        ];

        Assert.True(
            orphaned.Length == 0,
            $"These props files do not import the one above them, so the root's settings never "
                + $"reach the projects under them: {string.Join(", ", orphaned)}.");
    }

    /// <summary>
    /// Rule 9's carve-out is deliberately narrow: the documentation warnings are relaxed outside
    /// <c>src</c>, in shared props files, so the relaxation is reviewable in one place. A per-project
    /// <c>NoWarn</c> is the form the rule forbids, because it hides in a csproj nobody re-reads.
    /// </summary>
    [Fact]
    public void TheDocumentationSuppressionLivesInSharedPropsNotPerProject()
    {
        string[] offenders =
        [
            .. Directory
                .EnumerateFiles(RepositoryRoot, "*.csproj", SearchOption.AllDirectories)
                .Where(file => !IsUnderBuildOutput(file))
                .Where(file => File.ReadAllText(file) is string text
                    && (text.Contains("CS1591", StringComparison.Ordinal)
                        || text.Contains("CS1573", StringComparison.Ordinal)))
                .Select(file => Path.GetRelativePath(RepositoryRoot, file))
        ];

        Assert.True(
            offenders.Length == 0,
            $"These projects suppress a documentation warning themselves instead of inheriting the "
                + $"shared suppression: {string.Join(", ", offenders)}.");
    }

    /// <summary>
    /// A project that exists on disk but is absent from the solution still builds, still passes its
    /// own tests, and is invisible to every other gate here, because nothing walks the solution to
    /// notice it is gone. That is what makes it worth a test: the benchmarks project was dropped
    /// from <c>MassiveDotNet.slnx</c> by an editor rewrite on 2026-09-04, leaving an empty folder
    /// entry behind, and the tree stayed green. It would have quietly falsified D31's claim that
    /// the benchmarks compile in CI, and rule 13's same bargain for the live tier.
    /// </summary>
    /// <remarks>
    /// The disk-to-solution direction is the one that fails silently and is the reason this exists.
    /// The reverse, a solution entry whose file is gone, already fails the build loudly with
    /// MSB3202; it is asserted anyway because the comparison is symmetric and costs nothing.
    /// </remarks>
    [Fact]
    public void TheSolutionListsExactlyTheProjectsOnDisk()
    {
        string solution = Path.Combine(RepositoryRoot, "MassiveDotNet.slnx");

        string[] onDisk =
        [
            .. Directory
                .EnumerateFiles(RepositoryRoot, "*.csproj", SearchOption.AllDirectories)
                .Where(file => !IsUnderBuildOutput(file))
                .Select(Relative)
                .Order(StringComparer.Ordinal)
        ];

        string[] listed =
        [
            .. XDocument
                .Load(solution)
                .Descendants("Project")
                .Select(element => (string?)element.Attribute("Path"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
        ];

        string[] unregistered = [.. onDisk.Except(listed, StringComparer.Ordinal)];
        string[] absent = [.. listed.Except(onDisk, StringComparer.Ordinal)];

        Assert.True(
            unregistered.Length == 0,
            $"These projects exist but are not in MassiveDotNet.slnx, so nothing builds them and "
                + $"public API drift cannot break them: {string.Join(", ", unregistered)}.");

        Assert.True(
            absent.Length == 0,
            $"MassiveDotNet.slnx names these projects, but no such file exists: "
                + $"{string.Join(", ", absent)}.");
    }

    private static bool SetsSeverityToWarning(string configuration, string rule) =>
        configuration
            .Split('\n')
            .Select(line => line.Trim())
            .Any(line => line.StartsWith($"dotnet_diagnostic.{rule}.severity", StringComparison.Ordinal)
                && line.EndsWith("warning", StringComparison.Ordinal));

    private static string Relative(string path) =>
        Path.GetRelativePath(RepositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsUnderBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static bool PathEquals(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.Ordinal);

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
