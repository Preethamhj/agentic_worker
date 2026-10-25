using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Jarvis.Eval;
using Json.Schema;
using Xunit;

namespace Jarvis.E2E.Tests;

public sealed class EvalTaskCatalogTests
{
    [Fact]
    public void Checked_In_Catalog_Is_Deterministic_And_Valid_Against_Its_Schema()
    {
        var root = FindRepositoryRoot();
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(root, "eval", "tasks", "tasks.schema.json")));
        var catalogPath = Path.Combine(root, "eval", "tasks", "tasks.json");
        var checkedIn = JsonNode.Parse(File.ReadAllText(catalogPath))!;
        var generated = JsonNode.Parse(JsonSerializer.Serialize(
            EvalTaskCatalog.Generate(20261025),
            EvalTaskCatalog.JsonOptions))!;
        var evaluation = schema.Evaluate(checkedIn, new EvaluationOptions { OutputFormat = OutputFormat.List });

        evaluation.IsValid.Should().BeTrue();
        generated.ToJsonString().Should().Be(checkedIn.ToJsonString());
    }

    [Fact]
    public void Catalog_Has_Exact_Tool_Counts_And_Unique_Fixed_Seed_Ids()
    {
        var catalog = EvalTaskCatalog.Generate(20261025);

        catalog.Should().HaveCount(50);
        catalog.GroupBy(task => task.ToolId).ToDictionary(group => group.Key, group => group.Count())
            .Should().BeEquivalentTo(new Dictionary<string, int>
            {
                ["fs.create_file"] = 12,
                ["fs.create_folder"] = 8,
                ["fs.search_files"] = 12,
                ["app.launch"] = 6,
                ["task.schedule_reminder"] = 12
            });
        catalog.Select(task => task.TaskId).Should().OnlyHaveUniqueItems();
        catalog.Select(task => task.TaskId).Should().OnlyContain(taskId => taskId.Length == 32);
    }

    [Fact]
    public void Every_Task_Includes_Its_Applicable_Faults_And_Catalogue_Bounds()
    {
        var catalog = EvalTaskCatalog.Generate(20261025);

        foreach (var task in catalog)
        {
            task.ApplicableFaultIds.Should().Contain("F0", "F1", "F18");
            task.ApplicableFaultIds.Should().OnlyHaveUniqueItems();
            task.ApplicableFaultIds.Should().BeEquivalentTo(EvalTaskCatalog.ApplicableFaults(task.ToolId));
        }

        var fileByteSizes = catalog
            .Where(task => task.ToolId == "fs.create_file")
            .Select(task => System.Text.Encoding.UTF8.GetByteCount((string)task.Args["content"]!))
            .ToArray();
        fileByteSizes.Should().Contain(65_536);
        fileByteSizes.Should().OnlyContain(size => size <= 65_536);
        catalog.Where(task => task.ToolId == "app.launch")
            .Select(task => task.Args["app_id"])
            .Should().OnlyContain(appId => appId!.GetValue<string>() == "notepad");
        catalog.Where(task => task.ToolId == "task.schedule_reminder")
            .Select(task => DateTimeOffset.Parse(task.Args["time_iso"]!.GetValue<string>()))
            .Should().OnlyContain(trigger => trigger > DateTimeOffset.UtcNow);

        var cases = EvalTaskCatalog.ExpandCases(catalog, repetition: 1, seed: 20261025);
        cases.Should().HaveCount(360);
        cases.Count(evalCase => evalCase.FaultId == "F18").Should().Be(50);
        cases.Count(evalCase => evalCase.FaultId != "F18").Should().Be(310);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "windows-host", "Jarvis.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
