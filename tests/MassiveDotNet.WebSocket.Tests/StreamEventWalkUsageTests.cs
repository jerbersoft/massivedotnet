using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace MassiveDotNet.WebSocket.Tests;

/// <summary>
/// Enforces <see cref="MassiveDotNet.WebSocket.Internal.StreamEventWalk"/>'s own <c>&lt;remarks&gt;</c>:
/// keep an instance in a local, never in a field or behind an <c>in</c> or by-value parameter.
/// </summary>
/// <remarks>
/// <para>
/// Nothing but that doc comment stops a defensive copy today. A defensive copy silently loses the
/// walk's skip-debt flag (<c>_valueConsumed</c>) and reinstates the exact missed-skip defect the
/// type exists to make unrepresentable, one level removed -- with no compiler error and no test
/// failure, because the copy still compiles and still runs, it just reads the wrong token the next
/// time <c>NextProperty</c> is called on the original. This is the same argument
/// <c>MassiveDotNet.Rest.Tests.TemporalTypeTests</c> makes for rule 12, applied to one type instead
/// of a closed BCL set, and read that file first -- this one follows its shape deliberately: strip
/// comments and string literals before matching, so line numbers stay accurate and the scanner
/// cannot pass merely because a comment happened to mention the type; then prove the scanner
/// itself works, one test that flags an offending declaration and one that shows it ignoring the
/// same identifier in a comment.
/// </para>
/// <para>
/// The only sanctioned shape is a local variable declaration, <c>StreamEventWalk walk = new(...);</c>,
/// which is exactly the pattern every converter on this branch uses. The classifier below allows
/// three things and nothing else: the struct's own declaration (<c>struct StreamEventWalk</c>), a
/// constructor declaration or invocation (<c>StreamEventWalk</c> immediately followed by <c>(</c>,
/// which a field or parameter can never be -- both always name an identifier between the type and
/// whatever follows), and a local declaration (<c>StreamEventWalk &lt;identifier&gt; = ...</c>) that
/// is not itself preceded by an access modifier, <c>readonly</c>, <c>static</c>, <c>ref</c>,
/// <c>in</c>, <c>out</c>, <c>params</c>, <c>(</c>, or <c>,</c> -- the tokens that mean this
/// occurrence is actually a field or a parameter written with an initializer or default value.
/// Everything else -- a field with no initializer, a parameter, a property, a return type, an array
/// or generic type argument -- falls through to a violation.
/// </para>
/// </remarks>
public sealed partial class StreamEventWalkUsageTests
{
    private const string TypeName = "StreamEventWalk";

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void StreamEventWalkNeverAppearsAsAFieldOrAParameter()
    {
        (List<string> violations, int structDeclarations, int localDeclarations) =
            ScanDirectory(Path.Combine(RepositoryRoot, "src", "MassiveDotNet.WebSocket"));

        Assert.True(
            violations.Count == 0,
            $"{TypeName} must appear only as a local variable -- never a field, a parameter, or "
            + "anything else that could defensively copy it and silently lose its skip-debt flag "
            + $"(see {TypeName}'s own <remarks>).{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations.Select(v => $"  {v}")));

        // Guards the guard: zero violations over a directory the type has moved out of, or one
        // this scan is no longer pointed at, is the same "0 violations" a real, working scan
        // reports -- silently stops guarding rather than failing loudly. Asserting the scan
        // actually recognised the struct's own declaration and at least one sanctioned local
        // usage is what tells the two apart, mirroring
        // MassiveDotNet.Rest.Tests.TemporalTypeTests.EveryDirectoryHoldingSourceIsScanned's reason
        // for existing. See TheFoundSignalGuardFailsOverADirectoryThatNeverMentionsTheType below for
        // proof this actually fires.
        Assert.True(
            structDeclarations >= 1,
            $"The scan found no {TypeName} struct declaration under src/MassiveDotNet.WebSocket -- "
            + "either the type moved, or this scan is no longer pointed at the right directory, and "
            + "either way the 0-violations result above is not evidence of anything.");

        Assert.True(
            localDeclarations >= 1,
            $"The scan found no sanctioned {TypeName} local-variable usage under "
            + "src/MassiveDotNet.WebSocket -- either every converter stopped using the type, or this "
            + "scan is no longer pointed at the right directory, and either way the 0-violations "
            + "result above is not evidence of anything.");
    }

    /// <summary>
    /// Proves the found-signal guard above actually fires rather than being dead code itself: run
    /// the identical <see cref="ScanDirectory"/> logic over a sibling project's source, which is
    /// real, existing C# that never mentions <see cref="TypeName"/> at all -- the same shape a
    /// "moved to another project" regression would leave behind. Both counts must come back zero,
    /// which is exactly what would fail <see cref="StreamEventWalkNeverAppearsAsAFieldOrAParameter"/>'s
    /// two assertions above if this were the real scan.
    /// </summary>
    [Fact]
    public void TheFoundSignalGuardFailsOverADirectoryThatNeverMentionsTheType()
    {
        (List<string> violations, int structDeclarations, int localDeclarations) =
            ScanDirectory(Path.Combine(RepositoryRoot, "src", "MassiveDotNet.Extensions.DependencyInjection"));

        // Sanity check on the fixture itself: a directory with no .cs files at all would trivially
        // report zero of everything, which would prove nothing about the classifier. This one has
        // real source; it is simply source that never names StreamEventWalk.
        Assert.NotEmpty(EnumerateSourceFiles(
            Path.Combine(RepositoryRoot, "src", "MassiveDotNet.Extensions.DependencyInjection")));

        Assert.Empty(violations);
        Assert.Equal(0, structDeclarations);
        Assert.Equal(0, localDeclarations);
    }

    [Fact]
    public void ScannerFlagsAFieldDeclaration()
    {
        const string Offending = """
            internal sealed class Example
            {
                private StreamEventWalk _walk;
            }
            """;

        Assert.Single(FindViolations(StripCommentsAndLiterals(Offending)));
    }

    [Fact]
    public void ScannerFlagsAFieldDeclarationEvenWithAnInitializerThatLooksLikeALocal()
    {
        // The trickiest case: syntactically identical to the one allowed shape,
        // "StreamEventWalk <identifier> = ...", except for the modifier in front of it.
        const string Offending = """
            internal sealed class Example
            {
                private StreamEventWalk _walk = default;
            }
            """;

        Assert.Single(FindViolations(StripCommentsAndLiterals(Offending)));
    }

    [Fact]
    public void ScannerFlagsAByValueParameter()
    {
        const string Offending = """
            internal static class Example
            {
                private static void Consume(StreamEventWalk walk) { }
            }
            """;

        Assert.Single(FindViolations(StripCommentsAndLiterals(Offending)));
    }

    [Fact]
    public void ScannerFlagsAnInParameter()
    {
        const string Offending = """
            internal static class Example
            {
                private static void Consume(in StreamEventWalk walk) { }
            }
            """;

        Assert.Single(FindViolations(StripCommentsAndLiterals(Offending)));
    }

    [Fact]
    public void ScannerIgnoresTheIdentifierInAComment()
    {
        const string Benign = """"
            internal sealed class Example
            {
                // Keep a StreamEventWalk in a local, never a field: private StreamEventWalk _walk;
                /* StreamEventWalk must never be a parameter either */
                private const string Note = "StreamEventWalk in a string literal";
                private const string Raw = """StreamEventWalk in a raw literal""";
            }
            """";

        Assert.Empty(FindViolations(StripCommentsAndLiterals(Benign)));
    }

    [Fact]
    public void ScannerAllowsTheOneSanctionedShape()
    {
        const string Fine = """
            internal sealed class Example
            {
                internal struct StreamEventWalk
                {
                    public StreamEventWalk(int model)
                    {
                    }
                }

                private static void Use()
                {
                    StreamEventWalk walk = new(1);
                }
            }
            """;

        Assert.Empty(FindViolations(StripCommentsAndLiterals(Fine)));
    }

    // ------------------------------------------------------------------------------- helpers

    /// <summary>
    /// Classifies every occurrence of <see cref="TypeName"/> in already-stripped source, returning
    /// the index of each one that is not the struct's own declaration, a constructor declaration or
    /// invocation, or an unqualified local variable declaration.
    /// </summary>
    private static List<int> FindViolations(string strippedSource) => Classify(strippedSource).Violations;

    /// <summary>
    /// Classifies every occurrence of <see cref="TypeName"/> in already-stripped source. Splits out
    /// from <see cref="FindViolations"/> so <see cref="ScanDirectory"/> can additionally count the
    /// struct declaration and local-declaration sites it recognised as allowed -- not just the
    /// violations -- which is what lets the caller tell "found nothing to complain about because
    /// everything is clean" apart from "found nothing to complain about because there was nothing
    /// here to look at".
    /// </summary>
    private static (List<int> Violations, int StructDeclarations, int LocalDeclarations) Classify(
        string strippedSource)
    {
        List<int> violations = [];
        int structDeclarations = 0;
        int localDeclarations = 0;

        foreach (Match occurrence in TypeNameToken().Matches(strippedSource))
        {
            int index = occurrence.Index;
            string before = strippedSource[..index];
            string after = strippedSource[(index + occurrence.Length)..];

            // Allowed: the struct's own declaration, "struct StreamEventWalk".
            if (PrecededByStruct().IsMatch(before))
            {
                structDeclarations++;
                continue;
            }

            // Allowed: immediately followed by '(' -- a constructor declaration
            // ("public StreamEventWalk(...)") or a constructor invocation
            // ("new StreamEventWalk(...)"). Neither a field nor a parameter can take this shape:
            // both always name an identifier between the type and whatever follows it.
            if (FollowedByOpenParen().IsMatch(after))
            {
                continue;
            }

            // Otherwise the only allowed shape is a local declaration: "StreamEventWalk <ident> =",
            // and only when nothing immediately before it says "this is actually a field or a
            // parameter written with an initializer or a default value".
            bool looksLikeADeclarationWithAnIdentifier = FollowedByIdentifierAndEquals().IsMatch(after);
            bool precededByADisqualifyingToken = PrecededByFieldOrParameterToken().IsMatch(before);

            if (looksLikeADeclarationWithAnIdentifier && !precededByADisqualifyingToken)
            {
                localDeclarations++;
                continue;
            }

            violations.Add(index);
        }

        return (violations, structDeclarations, localDeclarations);
    }

    /// <summary>
    /// Scans every <c>*.cs</c> file under <paramref name="root"/>, aggregating violations alongside
    /// how many times the struct's own declaration and a sanctioned local declaration were actually
    /// recognised. The counts are what let <see cref="StreamEventWalkNeverAppearsAsAFieldOrAParameter"/>
    /// tell a genuinely clean scan apart from a vacuous one: zero violations over zero files scanned,
    /// or zero files that happen to mention the type at all -- for instance if the type were ever
    /// moved to another project -- would otherwise report the same "0 violations" pass a real,
    /// working guard reports. <see cref="TheFoundSignalGuardFailsOverADirectoryThatNeverMentionsTheType"/>
    /// proves this actually distinguishes the two.
    /// </summary>
    private static (List<string> Violations, int StructDeclarations, int LocalDeclarations) ScanDirectory(
        string root)
    {
        List<string> violations = [];
        int structDeclarations = 0;
        int localDeclarations = 0;

        foreach (string file in EnumerateSourceFiles(root))
        {
            string code = StripCommentsAndLiterals(File.ReadAllText(file));
            (List<int> fileViolations, int fileStructs, int fileLocals) = Classify(code);

            foreach (int index in fileViolations)
            {
                int line = code.Take(index).Count(c => c == '\n') + 1;
                violations.Add($"{Path.GetRelativePath(RepositoryRoot, file)}:{line}");
            }

            structDeclarations += fileStructs;
            localDeclarations += fileLocals;
        }

        return (violations, structDeclarations, localDeclarations);
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root) =>
        Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file));

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>
    /// Removes comments, string literals, and character literals, replacing them with whitespace so
    /// that line numbers in the remaining text still match the original file. Mirrors
    /// <c>MassiveDotNet.Rest.Tests.TemporalTypeTests.StripCommentsAndLiterals</c> exactly: duplicated
    /// rather than shared, because the two live in separate test projects and neither is production
    /// code a third project should depend on.
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

    [GeneratedRegex(@"\bStreamEventWalk\b")]
    private static partial Regex TypeNameToken();

    [GeneratedRegex(@"\bstruct\s*$")]
    private static partial Regex PrecededByStruct();

    [GeneratedRegex(@"^\s*\(")]
    private static partial Regex FollowedByOpenParen();

    [GeneratedRegex(@"^\s*[A-Za-z_]\w*\s*=(?!=)")]
    private static partial Regex FollowedByIdentifierAndEquals();

    // NOTE: this deliberately flags `ref StreamEventWalk walk` alongside `in StreamEventWalk walk`
    // and a by-value parameter, even though a `ref` parameter would in fact be safe -- `ref` binds
    // to the caller's own storage rather than defensively copying it, which is exactly what the
    // struct's own <remarks> ask for. No `ref StreamEventWalk` parameter exists anywhere today, and
    // being stricter than strictly necessary is harmless while that stays true, so this list is left
    // alone rather than special-cased. Whoever first has a legitimate reason to pass this by `ref`
    // (e.g. a helper the walk is threaded through without becoming a field) should read this comment
    // before concluding the scanner itself is wrong: dropping `ref` from the token list below is the
    // correct, sound loosening at that point, not a workaround.
    [GeneratedRegex(@"(?:\b(?:private|internal|public|protected|readonly|static|volatile|ref|in|out|params)\s*|[(,]\s*)$")]
    private static partial Regex PrecededByFieldOrParameterToken();
}
