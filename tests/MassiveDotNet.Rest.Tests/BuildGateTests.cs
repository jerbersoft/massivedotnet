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

    private static bool SetsSeverityToWarning(string configuration, string rule) =>
        configuration
            .Split('\n')
            .Select(line => line.Trim())
            .Any(line => line.StartsWith($"dotnet_diagnostic.{rule}.severity", StringComparison.Ordinal)
                && line.EndsWith("warning", StringComparison.Ordinal));

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
