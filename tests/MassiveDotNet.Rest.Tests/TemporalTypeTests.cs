using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MassiveDotNet.Rest.Tests;

/// <summary>
/// Enforces constitution rule 12: NodaTime is the SDK's only temporal vocabulary.
/// </summary>
/// <remarks>
/// Two layers, because neither is sufficient alone. Reflection describes the compiled surface but
/// cannot see local variables or static calls such as <c>TimeSpan.FromMinutes</c>; the source scan
/// sees everything written but not what a dependency contributes to a signature.
/// <para>
/// This file is itself exempt from the source scan, since naming the forbidden types is how the
/// rule is expressed.
/// </para>
/// </remarks>
public sealed partial class TemporalTypeTests
{
    private static readonly HashSet<Type> ForbiddenTypes =
    [
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(DateOnly),
        typeof(TimeOnly),
        typeof(TimeSpan),
    ];

    /// <summary>
    /// Every tree the source scan reads. <see cref="EveryDirectoryHoldingSourceIsScanned"/> keeps
    /// this list honest: adding a tree of C# without naming it here would leave a machine-checked
    /// rule with a hole in exactly the shape of the new directory.
    /// </summary>
    private static readonly string[] ScannedDirectories = ["benchmarks", "samples", "src", "tests", "tools"];

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    // ---------------------------------------------------------------- layer 1: compiled surface

    /// <summary>
    /// Every library the repository ships. <see cref="EveryShippedLibraryIsInspected"/> keeps this
    /// list from going stale when a fourth package arrives, since an assembly nobody names here is
    /// one this layer silently never looks at.
    /// </summary>
    private static readonly Assembly[] ShippedAssemblies =
    [
        typeof(MassiveClientOptions).Assembly,
        typeof(MassiveRestClient).Assembly,
        typeof(MassiveServiceCollectionExtensions).Assembly,
        typeof(MassiveDotNet.WebSocket.MassiveFeeds).Assembly,
    ];

    [Fact]
    public void EveryShippedLibraryIsInspected()
    {
        // Compared by name rather than by count: a count is satisfied by listing one assembly
        // twice, which is exactly the mistake this guard exists to catch.
        string[] projects =
        [
            .. Directory
                .GetDirectories(Path.Combine(RepositoryRoot, "src"))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
        ];

        string[] inspected =
        [
            .. ShippedAssemblies
                .Select(assembly => assembly.GetName().Name)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
        ];

        Assert.Equal(projects, inspected);
    }

    /// <summary>
    /// The source scan reads a fixed list of directories, so a new tree of C# is covered only if
    /// someone remembers to add it. This compares that list against what the repository actually
    /// holds, in both directions: a tree with source that nobody scans is a hole, and a scanned
    /// tree that no longer exists is a line that has stopped meaning anything.
    /// </summary>
    [Fact]
    public void EveryDirectoryHoldingSourceIsScanned()
    {
        string[] holdingSource =
        [
            .. Directory
                .EnumerateDirectories(RepositoryRoot)
                .Select(Path.GetFileName)
                .OfType<string>()
                // Dot-directories are tooling scratch -- .git, .github, and whatever an agent or
                // an IDE leaves behind. None of them is the SDK's source.
                .Where(name => !name.StartsWith('.'))
                .Where(name => Directory
                    .EnumerateFiles(Path.Combine(RepositoryRoot, name), "*.cs", SearchOption.AllDirectories)
                    .Any(file => !IsBuildOutput(file)))
        ];

        string[] unscanned = [.. holdingSource.Except(ScannedDirectories, StringComparer.Ordinal)];
        string[] absent = [.. ScannedDirectories.Except(holdingSource, StringComparer.Ordinal)];

        Assert.True(
            unscanned.Length == 0 && absent.Length == 0,
            $"The rule 12 source scan covers [{string.Join(", ", ScannedDirectories)}]. "
                + $"Holding C# but never scanned: [{string.Join(", ", unscanned)}]. "
                + $"Scanned but holding no C#: [{string.Join(", ", absent)}].");
    }

    [Fact]
    public void PublicApiUsesNodaTimeForAllTemporalTypes()
    {
        List<string> violations = [];

        foreach (Assembly assembly in ShippedAssemblies)
        {
            foreach (Type type in assembly.GetExportedTypes())
            {
                InspectType(type, violations);
            }
        }

        Assert.True(
            violations.Count == 0,
            "BCL date and time types must not appear in the public API. Use NodaTime instead "
            + $"(Instant, LocalDate, Duration, ZonedDateTime).{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    // ------------------------------------------------------------------- layer 2: written source

    [Fact]
    public void NoBclTemporalTypeIsNamedAnywhereInSource()
    {
        List<string> violations = [];

        foreach (string file in EnumerateSourceFiles())
        {
            // The scan would otherwise flag this file's own definition of the rule.
            if (Path.GetFileName(file) == "TemporalTypeTests.cs")
            {
                continue;
            }

            string code = StripCommentsAndLiterals(File.ReadAllText(file));

            foreach (Match match in ForbiddenIdentifier().Matches(code))
            {
                int line = code.Take(match.Index).Count(c => c == '\n') + 1;
                violations.Add($"{Path.GetRelativePath(RepositoryRoot, file)}:{line}  {match.Value}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "BCL date and time types must not be named anywhere in source. Where a BCL API "
            + "traffics in TimeSpan, convert inline via Duration.ToTimeSpan() or "
            + $"Duration.FromTimeSpan() so the type is never named.{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));
    }

    [Fact]
    public void SourceScanDetectsAViolation()
    {
        // Guards the guard: proves the scan is not passing because its stripping ate everything.
        const string Offending = """
            public sealed class Example
            {
                public TimeSpan Elapsed { get; set; }
            }
            """;

        string stripped = StripCommentsAndLiterals(Offending);
        Assert.Single(ForbiddenIdentifier().Matches(stripped));
    }

    [Fact]
    public void SourceScanIgnoresCommentsAndLiterals()
    {
        const string Benign = """"
            public sealed class Example
            {
                // TimeSpan is deliberately not used here.
                /* nor DateTime here */
                private const string Note = "DateTimeOffset in a literal";
                private const string Raw = """DateOnly in a raw literal""";
            }
            """";

        string stripped = StripCommentsAndLiterals(Benign);
        Assert.Empty(ForbiddenIdentifier().Matches(stripped));
    }

    // ------------------------------------------------------------------------------- helpers

    private static void InspectType(Type type, List<string> violations)
    {
        const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (PropertyInfo property in type.GetProperties(Flags))
        {
            if (IsForbidden(property.PropertyType))
            {
                violations.Add($"{type.FullName}.{property.Name} : {Describe(property.PropertyType)}");
            }
        }

        foreach (FieldInfo field in type.GetFields(Flags))
        {
            if (IsForbidden(field.FieldType))
            {
                violations.Add($"{type.FullName}.{field.Name} : {Describe(field.FieldType)}");
            }
        }

        foreach (MethodInfo method in type.GetMethods(Flags))
        {
            // Property accessors are covered above. Operators are deliberately NOT skipped:
            // an implicit conversion from a BCL type would reintroduce it into the surface.
            if (method.Name.StartsWith("get_", StringComparison.Ordinal)
                || method.Name.StartsWith("set_", StringComparison.Ordinal))
            {
                continue;
            }

            if (IsForbidden(method.ReturnType))
            {
                violations.Add($"{type.FullName}.{method.Name}() -> {Describe(method.ReturnType)}");
            }

            foreach (ParameterInfo parameter in method.GetParameters())
            {
                if (IsForbidden(parameter.ParameterType))
                {
                    violations.Add(
                        $"{type.FullName}.{method.Name}({parameter.Name}: {Describe(parameter.ParameterType)})");
                }
            }
        }

        foreach (ConstructorInfo constructor in type.GetConstructors(Flags))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                if (IsForbidden(parameter.ParameterType))
                {
                    violations.Add(
                        $"{type.FullName}..ctor({parameter.Name}: {Describe(parameter.ParameterType)})");
                }
            }
        }
    }

    private static bool IsForbidden(Type type)
    {
        Type target = Nullable.GetUnderlyingType(type) ?? type;

        if (target.IsArray)
        {
            target = target.GetElementType()!;
        }

        return ForbiddenTypes.Contains(Nullable.GetUnderlyingType(target) ?? target);
    }

    private static string Describe(Type type) =>
        Nullable.GetUnderlyingType(type) is { } underlying ? $"{underlying.Name}?" : type.Name;

    private static IEnumerable<string> EnumerateSourceFiles() =>
        ScannedDirectories
            .Select(directory => Path.Combine(RepositoryRoot, directory))
            // Every entry is proven to exist by EveryDirectoryHoldingSourceIsScanned, so this
            // guard only keeps the failure readable rather than papering over a missing tree.
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(file => !IsBuildOutput(file));

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>
    /// Removes comments, string literals, and character literals, replacing them with whitespace so
    /// that line numbers in the remaining text still match the original file.
    /// </summary>
    private static string StripCommentsAndLiterals(string source)
    {
        StringBuilder result = new(source.Length);
        int i = 0;

        while (i < source.Length)
        {
            if (StartsWith(source, i, "\"\"\""))
            {
                i = SkipRawString(source, i, result);
            }
            else if (StartsWith(source, i, "@\""))
            {
                i = SkipVerbatimString(source, i, result);
            }
            else if (StartsWith(source, i, "//"))
            {
                i = SkipToEndOfLine(source, i, result);
            }
            else if (StartsWith(source, i, "/*"))
            {
                i = SkipBlockComment(source, i, result);
            }
            else if (source[i] is '"' or '\'')
            {
                i = SkipQuoted(source, i, result);
            }
            else
            {
                result.Append(source[i]);
                i++;
            }
        }

        return result.ToString();
    }

    private static bool StartsWith(string source, int index, string value) =>
        index + value.Length <= source.Length
        && source.AsSpan(index, value.Length).SequenceEqual(value);

    private static int SkipRawString(string source, int start, StringBuilder result)
    {
        int fence = 0;
        while (start + fence < source.Length && source[start + fence] == '"')
        {
            fence++;
        }

        string terminator = new('"', fence);
        int end = source.IndexOf(terminator, start + fence, StringComparison.Ordinal);
        end = end < 0 ? source.Length : end + fence;

        PreserveNewlines(source, start, end, result);
        return end;
    }

    private static int SkipVerbatimString(string source, int start, StringBuilder result)
    {
        int i = start + 2;

        while (i < source.Length)
        {
            if (source[i] == '"')
            {
                if (StartsWith(source, i, "\"\""))
                {
                    i += 2;
                    continue;
                }

                i++;
                break;
            }

            i++;
        }

        PreserveNewlines(source, start, i, result);
        return i;
    }

    private static int SkipQuoted(string source, int start, StringBuilder result)
    {
        char quote = source[start];
        int i = start + 1;

        while (i < source.Length && source[i] != quote)
        {
            i += source[i] == '\\' ? 2 : 1;
        }

        i = Math.Min(i + 1, source.Length);
        PreserveNewlines(source, start, i, result);
        return i;
    }

    private static int SkipToEndOfLine(string source, int start, StringBuilder result)
    {
        int end = source.IndexOf('\n', start);
        end = end < 0 ? source.Length : end;

        PreserveNewlines(source, start, end, result);
        return end;
    }

    private static int SkipBlockComment(string source, int start, StringBuilder result)
    {
        int end = source.IndexOf("*/", start + 2, StringComparison.Ordinal);
        end = end < 0 ? source.Length : end + 2;

        PreserveNewlines(source, start, end, result);
        return end;
    }

    private static void PreserveNewlines(string source, int start, int end, StringBuilder result)
    {
        for (int i = start; i < end; i++)
        {
            if (source[i] == '\n')
            {
                result.Append('\n');
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

    [GeneratedRegex(@"\b(DateTime|DateTimeOffset|DateOnly|TimeOnly|TimeSpan)\b")]
    private static partial Regex ForbiddenIdentifier();
}
