using MassiveDotNet.CodeGen;

string repositoryRoot = FindRepositoryRoot();

string specPath = ArgumentOrDefault("--spec", Path.Combine(repositoryRoot, "specs", "openapi.json"));
string mapPath = ArgumentOrDefault("--map", Path.Combine(repositoryRoot, "specs", "endpoints.map.json"));
string outputPath = ArgumentOrDefault(
    "--out",
    Path.Combine(repositoryRoot, "src", "MassiveDotNet.Rest", "Generated"));

Spec spec = new(specPath);
Map map = Map.Load(mapPath);

Console.WriteLine($"spec  {Path.GetRelativePath(repositoryRoot, specPath)}  ({spec.OperationCount} operations)");
Console.WriteLine($"map   {Path.GetRelativePath(repositoryRoot, mapPath)}  ({map.Endpoints.Count} mapped, {map.Models.Count} models)");

Emitter emitter = new(spec, map);
Dictionary<string, string> files = emitter.Emit();

if (Directory.Exists(outputPath))
{
    Directory.Delete(outputPath, recursive: true);
}

foreach ((string relativePath, string content) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
{
    string destination = Path.Combine(outputPath, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.WriteAllText(destination, content);
    Console.WriteLine($"  + Generated/{relativePath.Replace('\\', '/')}");
}

int covered = map.Endpoints.Count;
int total = spec.OperationCount;
Console.WriteLine($"\ncoverage {covered}/{total} operations ({covered * 100.0 / total:F1}%)");

return 0;

static string ArgumentOrDefault(string name, string fallback)
{
    string[] arguments = Environment.GetCommandLineArgs();

    for (int i = 0; i < arguments.Length - 1; i++)
    {
        if (arguments[i] == name)
        {
            return arguments[i + 1];
        }
    }

    return fallback;
}

static string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);

    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MassiveDotNet.slnx")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName
        ?? throw new InvalidOperationException("Could not locate the repository root (MassiveDotNet.slnx).");
}
