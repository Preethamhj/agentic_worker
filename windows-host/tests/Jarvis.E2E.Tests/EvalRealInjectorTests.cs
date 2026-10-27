using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using FluentAssertions;
using Jarvis.Cli;
using Jarvis.Contracts;
using Jarvis.Eval;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Jarvis.Tools;
using Xunit;

namespace Jarvis.E2E.Tests;

public sealed class EvalRealInjectorTests
{
    [Theory]
    [InlineData("F1", "fs.create_file")]
    [InlineData("F4", "fs.create_file")]
    [InlineData("F5", "fs.create_file")]
    [InlineData("F6", "fs.create_file")]
    [InlineData("F1", "fs.create_folder")]
    [InlineData("F4", "fs.create_folder")]
    [InlineData("F5", "fs.create_folder")]
    [InlineData("F6", "fs.create_folder")]
    public async Task File_And_Folder_Injectors_Mutate_Real_Disk_State(string faultId, string toolId)
    {
        using var root = new TempRoot();
        var target = Path.Combine(root.Path, toolId == "fs.create_folder" ? "target-folder" : "target.txt");
        var extra = Path.Combine(root.Path, "unauthorized-extra.txt");
        var issued = IssueAuthorization(toolId, target, root.Path);
        var tracked = new List<(Process Process, DateTime StartTime)>();
        var inner = new RealMutationExecutor(toolId, target, "evaluation");
        var injector = new EvalFaultInjectingExecutor(inner, faultId, extra, process => tracked.Add((process, process.StartTime)));
        var receipt = await injector.ExecuteAsync(issued.Service.Consume(issued.Record, issued.Record.StepId));

        receipt.ReportedSuccess.Should().BeTrue();
        faultId.Should().NotBe("F0");
        if (faultId == "F1" || faultId == "F4" || faultId == "F6")
            (toolId == "fs.create_folder" ? Directory.Exists(target) : File.Exists(target)).Should().BeFalse();
        if (faultId == "F4")
            (toolId == "fs.create_folder" ? Directory.Exists(Path.Combine(root.Path, "wrong-location-target-folder")) : File.Exists(Path.Combine(root.Path, "wrong-location-target.txt"))).Should().BeTrue();
        if (faultId == "F5")
            File.Exists(extra).Should().BeTrue();
        if (faultId == "F6")
            File.Exists(target).Should().BeFalse();
        tracked.Should().BeEmpty();
    }

    [Theory]
    [InlineData("F1")]
    [InlineData("F7")]
    [InlineData("F8")]
    public async Task Search_Injectors_Mutate_Real_Receipt_State(string faultId)
    {
        using var root = new TempRoot();
        var searchRoot = Path.Combine(root.Path, "search");
        Directory.CreateDirectory(searchRoot);
        var match = Path.Combine(searchRoot, "match.txt");
        File.WriteAllText(match, "match");
        var issued = IssueAuthorization("fs.search_files", searchRoot, root.Path);
        var injector = new EvalFaultInjectingExecutor(
            new RealMutationExecutor("fs.search_files", searchRoot, "evaluation"),
            faultId,
            Path.Combine(root.Path, "unused"));
        var receipt = await injector.ExecuteAsync(issued.Service.Consume(issued.Record, issued.Record.StepId));
        var reported = string.IsNullOrWhiteSpace(receipt.Stdout)
            ? []
            : JsonSerializer.Deserialize<string[]>(receipt.Stdout) ?? [];

        faultId.Should().NotBe("F0");
        if (faultId == "F1") reported.Should().BeEmpty();
        if (faultId == "F7") reported.Should().NotContain(match);
        if (faultId == "F8") reported.Should().Contain(path => path.Contains("phantom", StringComparison.OrdinalIgnoreCase));
    }

    [NonElevatedTheory]
    [InlineData("F1")]
    [InlineData("F9")]
    [InlineData("F10")]
    [InlineData("F11")]
    public async Task App_Injector_Produces_Real_Process_State_And_Tracks_StartTimes(string faultId)
    {
        using var root = new TempRoot();
        var issued = IssueAuthorization("app.launch", "notepad", root.Path);
        var started = new List<(Process Process, DateTime StartTime)>();
        var injector = new EvalFaultInjectingExecutor(
            new RealAppExecutor(), faultId, Path.Combine(root.Path, "unused"), process => started.Add((process, process.StartTime)));
        try
        {
            var receipt = await injector.ExecuteAsync(issued.Service.Consume(issued.Record, issued.Record.StepId));
            receipt.ReportedSuccess.Should().BeTrue();
            faultId.Should().NotBe("F0");
            if (faultId == "F1") started.Should().BeEmpty();
            if (faultId is "F9" or "F10") started.Should().NotBeEmpty();
            if (faultId == "F11") started.Count.Should().Be(2);
            foreach (var item in started)
                if (faultId == "F11" && item.Process.ProcessName.Equals("ping", StringComparison.OrdinalIgnoreCase))
                    item.Process.HasExited.Should().BeFalse();
        }
        finally
        {
            foreach (var item in started)
                EvalFileRunner.StopOwnedProcessIfMatch(item.Process, item.StartTime);
        }
    }

    [Theory]
    [InlineData("F1")]
    [InlineData("F12")]
    [InlineData("F13")]
    [InlineData("F14")]
    [InlineData("F15")]
    [InlineData("F16")]
    [InlineData("F17a")]
    [InlineData("F17b")]
    public async Task Reminder_Injectors_Mutate_Real_Xml_Or_Data(string faultId)
    {
        using var root = new TempRoot();
        var taskId = "task-1234567890abcdef1234567890abcdef";
        var dataDirectory = Path.Combine(root.Path, "reminders");
        Directory.CreateDirectory(dataDirectory);
        var scheduler = new EvalFakeReminderScheduler(faultId, dataDirectory, taskId);
        var registration = new ReminderTaskRegistration(
            "Reminder", $"\\Jarvis\\reminder-{taskId}", "C:\\Jarvis.Cli.exe", $"--jarvis-reminder {taskId}", root.Path,
            DateTimeOffset.UtcNow.AddMinutes(1), "Reminder");
        scheduler.Register(registration);

        faultId.Should().NotBe("F0");
        if (faultId == "F1") scheduler.TaskXml.Should().BeNull();
        if (faultId == "F14") scheduler.TaskEnabled.Should().BeFalse();
        if (faultId == "F15") scheduler.ExtraTaskExists.Should().BeTrue();
        if (faultId is "F12" or "F13" or "F17a" or "F17b") scheduler.TaskXml.Should().NotBeNull();
        if (faultId == "F13") scheduler.TaskXml.Should().Contain("wrong-id");
        if (faultId == "F17a") scheduler.TaskXml.Should().Contain("DisallowStartIfOnBatteries>true");
        if (faultId == "F17b") scheduler.TaskXml.Should().Contain("EndBoundary");
        if (faultId == "F16")
        {
            File.Exists(Path.Combine(dataDirectory, $"{taskId}.json")).Should().BeTrue();
            File.ReadAllText(Path.Combine(dataDirectory, $"{taskId}.json")).Should().Contain("altered");
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task DiagnoseCase_Prints_Verifier_Result()
    {
        var task = EvalTaskCatalog.Generate(EvalTaskCatalog.DefaultSeed).First(task => task.ToolId == "fs.create_file");
        var caseId = $"{task.TaskId}-r1-F0";
        using var writer = new StringWriter();
        var original = Console.Out;
        Console.SetOut(writer);
        try { await new EvalFileRunner().DiagnoseAsync(caseId); }
        finally { Console.SetOut(original); }
        writer.ToString().Should().Contain("verifier=D");
    }

    [Fact]
    public void Cleanup_Does_Not_Kill_Process_When_Recorded_StartTime_Differs()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "ping.exe"),
            Arguments = "-n 60 127.0.0.1",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        process.Should().NotBeNull();
        var actualStart = process!.StartTime;
        try
        {
            EvalFileRunner.StopOwnedProcessIfMatch(process, actualStart.AddSeconds(-1)).Should().BeFalse();
            process.HasExited.Should().BeFalse();
        }
        finally { EvalFileRunner.StopOwnedProcessIfMatch(process, actualStart); }
    }

    private static (AuthorizationService Service, AuthorizationRecordDto Record) IssueAuthorization(string toolId, string value, string root)
    {
        var audit = new InMemoryAuditStore();
        var auth = new AuthorizationService(new InMemoryDpapiKeyStore(), SystemClock.Instance, audit);
        var policy = LoadPolicy();
        var step = new StepExecutor(auth, [], policy);
        var args = toolId switch
        {
            "fs.create_file" => new Dictionary<string, object?> { ["path"] = value, ["content"] = "evaluation" },
            "fs.create_folder" => new Dictionary<string, object?> { ["path"] = value },
            "fs.search_files" => new Dictionary<string, object?> { ["root_path"] = value, ["pattern"] = "*.txt", ["max_depth"] = 5, ["max_results"] = 100 },
            "app.launch" => new Dictionary<string, object?> { ["app_id"] = "notepad" },
            _ => throw new ArgumentException(toolId)
        };
        return (auth, step.PrepareAuthorization("task-1234567890abcdef1234567890abcdef", "step-1", toolId, args, "low"));
    }

    private static ToolPolicy LoadPolicy()
    {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        return ToolPolicy.FromFile(Path.Combine(repo, "config", "tools.registry.json"), Path.Combine(repo, "config", "apps.allowlist.json"));
    }

    private sealed class TempRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"jarvis-real-injector-{Guid.NewGuid():N}");
        public TempRoot() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }

    private sealed class RealMutationExecutor(string toolId, string path, string content) : IToolExecutor
    {
        public string ToolId => toolId;
        public Task<ExecutionReceiptDto> ExecuteAsync(VerifiedAuthorization auth, CancellationToken cancellationToken = default)
        {
            if (toolId == "fs.create_file") File.WriteAllText(path, content);
            if (toolId == "fs.create_folder") Directory.CreateDirectory(path);
            var stdout = toolId == "fs.search_files" ? JsonSerializer.Serialize(Directory.GetFiles(path, "*.txt")) : string.Empty;
            return Task.FromResult(new ExecutionReceiptDto(1, auth.StepId, auth.AuthId, "start", "end", 0, true, stdout, string.Empty));
        }
    }

    private sealed class RealAppExecutor : IToolExecutor
    {
        public string ToolId => "app.launch";
        public Task<ExecutionReceiptDto> ExecuteAsync(VerifiedAuthorization auth, CancellationToken cancellationToken = default)
        {
            var process = Process.Start(new ProcessStartInfo { FileName = "notepad.exe", UseShellExecute = true });
            if (process is null) throw new InvalidOperationException("Could not start Notepad.");
            return Task.FromResult(new ExecutionReceiptDto(1, auth.StepId, auth.AuthId, "start", "end", 0, true, $"PID {process.Id}", string.Empty));
        }
    }
}

public sealed class NonElevatedFactAttribute : FactAttribute
{
    public NonElevatedFactAttribute()
    {
        if (OperatingSystem.IsWindows() && new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            Skip = "App process tests require a non-elevated desktop session.";
    }
}

public sealed class NonElevatedTheoryAttribute : TheoryAttribute
{
    public NonElevatedTheoryAttribute()
    {
        if (OperatingSystem.IsWindows() && new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            Skip = "App process tests require a non-elevated desktop session.";
    }
}
