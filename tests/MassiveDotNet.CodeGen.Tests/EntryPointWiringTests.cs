using System.Text.RegularExpressions;
using Xunit;

namespace MassiveDotNet.CodeGen.Tests;

/// <summary>
/// Every emitted entry point calls the <c>Build</c>/<c>Send</c> pair its own map row derives, not
/// merely a pair that exists somewhere in the file.
/// </summary>
/// <remarks>
/// <c>Build{Method}Uri</c> returns <c>string</c>, the one seam among the generator's per-operation
/// helpers the compiler does not close: the envelope type argument to <c>EnumerateAsync</c> is
/// distinct per operation, so mis-registering that would not compile, but passing another
/// operation's URI builder compiles cleanly and deserializes that operation's rows under this
/// one's item type -- the silent wrong binding D16 exists to prevent for nested models, arriving
/// here through pagination instead. This runs against the real <c>specs/openapi.json</c> and
/// <c>specs/endpoints.map.json</c> rather than a small in-memory document, so it protects all 56
/// mapped operations, and every one the map gains after this is written, rather than a fixed count
/// asserted once and never revisited.
/// </remarks>
public sealed class EntryPointWiringTests
{
    [Fact]
    public void EveryEntryPointCallsTheBuilderAndSenderItsOwnRowDerives()
    {
        string repositoryRoot = FindRepositoryRoot();
        Spec spec = new(Path.Combine(repositoryRoot, "specs", "openapi.json"));
        Map map = Map.Load(Path.Combine(repositoryRoot, "specs", "endpoints.map.json"));
        Dictionary<string, string> files = new Emitter(spec, map).Emit();

        // Guards against a document swap or a filter typo silently emptying the loop below: a test
        // that iterates zero rows would pass no matter what the generator does.
        Assert.True(map.Endpoints.Count > 40, $"Expected the real map's endpoints, found {map.Endpoints.Count}.");

        foreach (MapEndpoint endpoint in map.Endpoints)
        {
            string file = files[$"{endpoint.Group}Group.g.cs"];

            // The plain entry point -- List or Get -- must build its own URI and hand off to its
            // own Send method.
            string plainBody = EntryPointBody(file, $"{endpoint.Method}Async");
            Assert.Contains($"Build{endpoint.Method}Uri(", plainBody, StringComparison.Ordinal);
            Assert.Contains($"Send{endpoint.Method}Async(", plainBody, StringComparison.Ordinal);

            // The Enumerate counterpart shares its Build method with the List row rather than
            // having one of its own, which is exactly why a mis-wire here would compile: the
            // method it calls is a plain `string`-returning static, indistinguishable by type from
            // any other operation's. Checked only when the operation actually paginates -- Naming
            // only derives an Enumerate name for a "List"-prefixed method, and every paginated
            // operation's Method is List-prefixed or the generator itself would have refused it.
            if (endpoint.Method.StartsWith("List", StringComparison.Ordinal))
            {
                string enumerateName = $"Enumerate{endpoint.Method["List".Length..]}Async";

                if (HasEntryPoint(file, enumerateName))
                {
                    string enumerateBody = EntryPointBody(file, enumerateName);
                    Assert.Contains($"Build{endpoint.Method}Uri(", enumerateBody, StringComparison.Ordinal);
                }
            }
        }
    }

    /// <summary>Whether a public entry point named <paramref name="methodName"/> was emitted.</summary>
    private static bool HasEntryPoint(string file, string methodName) =>
        EntryPointSignature(methodName).IsMatch(file);

    /// <summary>
    /// The full body -- opening and closing brace included -- of the public entry point named
    /// <paramref name="methodName"/>. Anchored on the "public" keyword and a line start so
    /// "SendListFilingFilesAsync" cannot satisfy a search for "ListFilingFilesAsync": that
    /// substring match would make the assertions above pass by finding the private Send method's
    /// own declaration instead of the entry point's body.
    /// </summary>
    private static string EntryPointBody(string file, string methodName)
    {
        Match signature = EntryPointSignature(methodName).Match(file);
        Assert.True(signature.Success, $"No public entry point named '{methodName}' was emitted.");

        int braceOpen = file.IndexOf('{', signature.Index + signature.Length);
        int depth = 0;
        int index = braceOpen;

        // Parameter lists in generated signatures carry no braces (defaults are `null`, `default`,
        // or a literal), so the first `{` after the signature is the method's own block; balancing
        // from there finds its matching close regardless of what the body contains.
        for (; index < file.Length; index++)
        {
            if (file[index] == '{')
            {
                depth++;
            }
            else if (file[index] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    break;
                }
            }
        }

        return file[braceOpen..(index + 1)];
    }

    /// <summary>
    /// Matches a public entry point's signature line: "public" (never "private", which is what
    /// Build and Send are) followed, on the same line, by <paramref name="methodName"/> and its
    /// opening parenthesis.
    /// </summary>
    private static Regex EntryPointSignature(string methodName) =>
        new($@"(?m)^\s{{4}}public\b.*?\b{Regex.Escape(methodName)}\s*\(");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MassiveDotNet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root (MassiveDotNet.slnx).");
    }
}
