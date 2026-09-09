using System.Text.Json;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Pins the two facts about the documentation site that DocFX itself does not check.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/docfx.json</c> names the projects whose doc comments become the API reference one by one
/// rather than by glob, so that a fifth package is a decision somebody records. This is what stops
/// the decision being skipped by omission: a project under <c>src/</c> that is not listed fails
/// here, and so does a listing for a project that no longer exists. It is the same bargain
/// <see cref="PublishWorkflowTests"/> makes for the <c>PACKAGES</c> list, and it matters for the
/// same reason -- <c>.FlatFiles</c> is planned (#22), and the failure it prevents is a package
/// published with no reference pages and nothing anywhere saying so.
/// </para>
/// <para>
/// The content list is enumerated for a second reason the site cannot state: <c>docs/superpowers/</c>
/// holds designs, plans and measurements, which are working material and not for publication. A
/// well-meaning change to a <c>**.md</c> glob would publish the next plan somebody writes, and DocFX
/// would build it without a word -- an orphan page is not a warning. So the absence is asserted
/// rather than left to whoever reviews that change.
/// </para>
/// <para>
/// Neither test needs DocFX installed, and neither needs the site to have been built.
/// </para>
/// </remarks>
public sealed class DocsSiteTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void EveryShippingProjectIsInTheApiReference()
    {
        string source = Path.Combine(RepositoryRoot, "src");

        string[] onDisk =
        [
            .. Directory.GetFiles(source, "*.csproj", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(source, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal),
        ];

        string[] listed =
        [
            .. Document().RootElement.GetProperty("metadata").EnumerateArray()
                .SelectMany(entry => entry.GetProperty("src").EnumerateArray())
                .SelectMany(entry => entry.GetProperty("files").EnumerateArray())
                .Select(file => file.GetString()!)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(onDisk, listed);
    }

    [Fact]
    public void TheWorkingMaterialUnderSuperpowersIsNotPublished()
    {
        string[] content =
        [
            .. Document().RootElement.GetProperty("build").GetProperty("content").EnumerateArray()
                .SelectMany(entry => entry.GetProperty("files").EnumerateArray())
                .Select(file => file.GetString()!),
        ];

        // A glob broad enough to reach docs/superpowers/ is the failure, not the literal string:
        // "**.md" and "**/*.md" both match it, and neither names it.
        string[] offenders =
        [
            .. content.Where(pattern =>
                pattern.Contains("superpowers", StringComparison.OrdinalIgnoreCase)
                || pattern.StartsWith("**", StringComparison.Ordinal)),
        ];

        Assert.Empty(offenders);
    }

    private static JsonDocument Document() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "docfx.json")));

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
