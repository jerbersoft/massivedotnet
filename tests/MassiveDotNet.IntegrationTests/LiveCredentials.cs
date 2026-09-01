namespace MassiveDotNet.IntegrationTests;

/// <summary>
/// Locates the Massive API key for locally run integration tests.
/// </summary>
/// <remarks>
/// Looked up from the <c>MASSIVE_API_KEY</c> environment variable first, then from a gitignored
/// <c>.env</c> at the repository root. The key is never committed and never reaches CI
/// (constitution rule 13).
/// </remarks>
internal static class LiveCredentials
{
    private const string VariableName = "MASSIVE_API_KEY";

    private static readonly Lazy<string?> Key = new(Resolve, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The resolved API key, or <see langword="null"/> when none is available.</summary>
    public static string? ApiKey => Key.Value;

    /// <summary>Whether a key was found, and integration tests can therefore run.</summary>
    public static bool IsAvailable => !string.IsNullOrWhiteSpace(Key.Value);

    /// <summary>Explains how to supply a key, for the skip message when none is present.</summary>
    public const string MissingKeyReason =
        "No Massive API key found. These tests call the live API and are intended to be run "
        + "locally: set MASSIVE_API_KEY, or place it in a .env file at the repository root. "
        + "CI never runs them (constitution rule 13).";

    private static string? Resolve()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable(VariableName);

        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        string? envFile = FindEnvFile();

        if (envFile is null)
        {
            return null;
        }

        foreach (string line in File.ReadLines(envFile))
        {
            string trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            int separator = trimmed.IndexOf('=');

            if (separator <= 0 || !trimmed.AsSpan(0, separator).Trim().SequenceEqual(VariableName))
            {
                continue;
            }

            return trimmed[(separator + 1)..].Trim().Trim('"', '\'');
        }

        return null;
    }

    private static string? FindEnvFile()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, ".env");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
