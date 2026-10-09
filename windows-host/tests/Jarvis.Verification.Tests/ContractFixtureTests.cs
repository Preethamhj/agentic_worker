using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Json.Schema;
using Xunit;

namespace Jarvis.Verification.Tests;

public class ContractFixtureTests
{
    private static readonly string SchemasDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "contracts", "schemas"));
    private static readonly string ExamplesDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "contracts", "examples"));

    static ContractFixtureTests()
    {
        // Register all schemas in SchemaRegistry.Global for resolving $ref by relative filename
        if (Directory.Exists(SchemasDir))
        {
            foreach (var file in Directory.GetFiles(SchemasDir, "*.schema.json"))
            {
                var fileName = Path.GetFileName(file);
                var schemaObj = JsonSchema.FromText(File.ReadAllText(file));
                SchemaRegistry.Global.Register(new Uri(fileName, UriKind.RelativeOrAbsolute), schemaObj);
                SchemaRegistry.Global.Register(new Uri($"https://json-everything.net/{fileName}", UriKind.RelativeOrAbsolute), schemaObj);
            }
        }
    }

    public static IEnumerable<object[]> GetFixtureFiles()
    {
        var files = Directory.GetFiles(ExamplesDir, "*.json");
        foreach (var file in files)
        {
            yield return new object[] { file };
        }
    }

    [Theory]
    [MemberData(nameof(GetFixtureFiles))]
    public void Validate_Contract_Fixture(string fixturePath)
    {
        var fileName = Path.GetFileName(fixturePath);
        var parts = fileName.Split('.');
        var schemaName = parts[0];
        var expectedValid = parts.Any(p => p.StartsWith("valid"));

        var schemaPath = Path.Combine(SchemasDir, $"{schemaName}.schema.json");
        File.Exists(schemaPath).Should().BeTrue($"Schema file should exist for {schemaName}");

        var schemaJson = File.ReadAllText(schemaPath);
        var schema = JsonSchema.FromText(schemaJson);

        var fixtureJson = File.ReadAllText(fixturePath);
        var node = JsonNode.Parse(fixtureJson);

        var options = new EvaluationOptions
        {
            OutputFormat = OutputFormat.List
        };

        var result = schema.Evaluate(node, options);

        if (expectedValid)
        {
            result.IsValid.Should().BeTrue($"Fixture {fileName} should be VALID but failed: {string.Join(", ", result.Details.Select(d => d.Errors != null ? string.Join("; ", d.Errors.Values) : ""))}");
        }
        else
        {
            result.IsValid.Should().BeFalse($"Fixture {fileName} should be INVALID but passed validation.");
        }
    }
}
