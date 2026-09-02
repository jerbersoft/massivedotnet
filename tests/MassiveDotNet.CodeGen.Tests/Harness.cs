using Xunit;

namespace MassiveDotNet.CodeGen.Tests;

/// <summary>One GET operation for <see cref="Harness.Document"/>.</summary>
/// <param name="Id">The operation id.</param>
/// <param name="Path">The route.</param>
/// <param name="Envelope">The JSON schema of the 200 response.</param>
/// <param name="Parameters">The JSON array of parameter objects.</param>
/// <param name="Extensions">
/// Extra members of the operation object, such as an <c>x-polygon-deprecation</c> extension,
/// written as JSON members without a trailing comma.
/// </param>
internal sealed record Operation(
    string Id,
    string Path,
    string Envelope,
    string Parameters = "[]",
    string Extensions = "");

/// <summary>
/// Builds the smallest OpenAPI document and map the generator accepts, so each test states only
/// the schema and rows it is about.
/// </summary>
internal static class Harness
{
    /// <summary>A document declaring the given operations and nothing else.</summary>
    public static string Document(params Operation[] operations)
    {
        IEnumerable<string> paths = operations.Select(operation => $$"""
            "{{operation.Path}}": {
              "get": {
                {{(operation.Extensions.Length == 0 ? "" : operation.Extensions + ",")}}
                "operationId": "{{operation.Id}}",
                "parameters": {{operation.Parameters}},
                "responses": {
                  "200": { "content": { "application/json": { "schema": {{operation.Envelope}} } } }
                }
              }
            }
            """);

        return $$"""
            {
              "components": { "parameters": {} },
              "paths": { {{string.Join(",\n", paths)}} }
            }
            """;
    }

    /// <summary>An envelope whose <c>results</c> is an array of the given item schema.</summary>
    public static string Envelope(string items) => $$"""
        {
          "type": "object",
          "properties": {
            "results": { "type": "array", "items": {{items}} },
            "status": { "type": "string" }
          }
        }
        """;

    /// <summary>A map with one group, <c>Reference</c>, plus the given model and endpoint rows.</summary>
    public static string MapDocument(string models, string endpoints) => $$"""
        {
          "groups": { "Reference": { "summary": "Test group." } },
          "models": { {{models}} },
          "endpoints": [ {{endpoints}} ]
        }
        """;

    /// <summary>An endpoint row returning <c>results</c> as an array of the model.</summary>
    /// <param name="method">The .NET method name, defaulting to <c>List</c> plus the plural of the model.</param>
    public static string Endpoint(string operationId, string model, string parameters = "{}", string? method = null) => $$"""
        {
          "operationId": "{{operationId}}",
          "group": "Reference",
          "method": "{{method ?? $"List{model}s"}}",
          "result": { "kind": "array", "model": "{{model}}", "property": "results" },
          "parameters": {{parameters}}
        }
        """;

    /// <summary>Runs the emitter over inline documents.</summary>
    public static Dictionary<string, string> Generate(string spec, string map) =>
        new Emitter(Spec.Parse(spec), Map.Parse(map)).Emit();

    /// <summary>The message of the refusal generation raises for these documents.</summary>
    public static string Refusal(string spec, string map) =>
        Assert.Throws<InvalidOperationException>(() => Generate(spec, map)).Message;
}
