using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Jarvis.Eval;

public sealed record EvalTask(
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("tool_id")] string ToolId,
    [property: JsonPropertyName("args")] JsonObject Args,
    [property: JsonPropertyName("applicable_faults")] IReadOnlyList<string> ApplicableFaultIds);

public static class EvalTaskCatalog
{
    public const int DefaultSeed = 20261025;

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true
    };

    public static IReadOnlyList<EvalTask> Generate(int seed)
    {
        var tasks = new List<EvalTask>(50);
        tasks.AddRange(Enumerable.Range(1, 12).Select(index => CreateFileTask(seed, index)));
        tasks.AddRange(Enumerable.Range(1, 8).Select(index => CreateFolderTask(seed, index)));
        tasks.AddRange(Enumerable.Range(1, 12).Select(index => CreateSearchTask(seed, index)));
        tasks.AddRange(Enumerable.Range(1, 6).Select(index => CreateAppTask(seed, index)));
        tasks.AddRange(Enumerable.Range(1, 12).Select(index => CreateReminderTask(seed, index)));
        return tasks;
    }

    public static IReadOnlyList<EvalCase> ExpandCases(
        IReadOnlyList<EvalTask> tasks,
        int repetition,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        if (repetition < 1)
            throw new ArgumentOutOfRangeException(nameof(repetition));

        return tasks
            .SelectMany(task => task.ApplicableFaultIds.Select(faultId => new EvalCase(
                $"{task.TaskId}-r{repetition}-{faultId}",
                task.ToolId,
                task.TaskId,
                faultId,
                faultId == "F0" ? EvalGroundTruth.Good : EvalGroundTruth.Bad,
                repetition,
                seed)))
            .ToArray();
    }

    public static IReadOnlyList<string> ApplicableFaults(string toolId) =>
        toolId switch
        {
            "fs.create_file" => ["F0", "F1", "F2", "F3", "F4", "F5", "F6", "F18"],
            "fs.create_folder" => ["F0", "F1", "F4", "F5", "F6", "F18"],
            "fs.search_files" => ["F0", "F1", "F7", "F8", "F18"],
            "app.launch" => ["F0", "F1", "F9", "F10", "F11", "F18"],
            "task.schedule_reminder" => ["F0", "F1", "F12", "F13", "F14", "F15", "F16", "F17a", "F17b", "F18"],
            _ => throw new ArgumentException($"Unsupported evaluation tool '{toolId}'.", nameof(toolId))
        };

    private static EvalTask CreateFileTask(int seed, int index)
    {
        var names = new[]
        {
            $"review notes {index:D2}.txt",
            $"ಕನ್ನಡ {index:D2}.txt",
            $"file {index:D2}.txt"
        };
        var sizes = new[] { 0, 1, 32, 256, 1024, 4096, 8192, 16384, 32768, 65536, 512, 2048 };
        var content = new string((char)('a' + index % 26), sizes[index - 1]);
        return Task(
            seed,
            "file",
            index,
            "fs.create_file",
            new JsonObject
            {
                ["path"] = $"files/{names[(index - 1) % names.Length]}",
                ["content"] = content
            });
    }

    private static EvalTask CreateFolderTask(int seed, int index) =>
        Task(
            seed,
            "folder",
            index,
            "fs.create_folder",
            new JsonObject
            {
                ["path"] = $"folders/branch-{index:D2}/nested/leaf"
            });

    private static EvalTask CreateSearchTask(int seed, int index)
    {
        var matchCount = (index - 1) % 9;
        var expectedMatches = new JsonArray();
        for (var match = 1; match <= matchCount; match++)
            expectedMatches.Add($"match-{match:D2}/result-{match:D2}.txt");
        return Task(
            seed,
            "search",
            index,
            "fs.search_files",
            new JsonObject
            {
                ["root_path"] = $"search/query-{index:D2}",
                ["pattern"] = "*.txt",
                ["max_depth"] = 5,
                ["max_results"] = 100,
                ["expected_matches"] = expectedMatches
            });
    }

    private static EvalTask CreateAppTask(int seed, int index) =>
        Task(
            seed,
            "app",
            index,
            "app.launch",
            new JsonObject { ["app_id"] = "notepad" });

    private static EvalTask CreateReminderTask(int seed, int index) =>
        Task(
            seed,
            "reminder",
            index,
            "task.schedule_reminder",
            new JsonObject
            {
                ["task_name"] = $"Evaluation reminder {index:D2}",
                ["time_iso"] = new DateTimeOffset(2099, 1, 1, 12, 0, 0, TimeSpan.Zero)
                    .AddMinutes(index)
                    .ToString("O"),
                ["message"] = $"Evaluation reminder {index:D2}"
            });

    private static EvalTask Task(int seed, string kind, int index, string toolId, JsonObject args) =>
        new(CreateTaskId(seed, kind, index), toolId, args, ApplicableFaults(toolId));

    private static string CreateTaskId(int seed, string kind, int index)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{kind}:{index:D2}"));
        return Convert.ToHexString(bytes.AsSpan(0, 16)).ToLowerInvariant();
    }
}
