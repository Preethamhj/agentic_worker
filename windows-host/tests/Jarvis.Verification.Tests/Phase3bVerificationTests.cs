using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Jarvis.Tools;
using Xunit;

namespace Jarvis.Verification.Tests;

public sealed class Phase3bVerificationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"jarvis-3b-{Guid.NewGuid():N}");
    private readonly TestClock _clock = new();
    private readonly InMemoryAuditStore _auditStore = new();
    private readonly AuthorizationService _authorizationService;
    private readonly VerificationEngine _engine;

    public Phase3bVerificationTests()
    {
        Directory.CreateDirectory(_root);
        _authorizationService = new AuthorizationService(
            new InMemoryDpapiKeyStore(),
            _clock,
            _auditStore);
        _engine = new VerificationEngine(_authorizationService, _auditStore, _clock);
    }

    [Fact]
    public async Task Search_Verifies_Only_Existing_Matching_Paths_Inside_Signed_Root()
    {
        var searchRoot = Path.Combine(_root, "search");
        Directory.CreateDirectory(searchRoot);
        var matchingFile = Path.Combine(searchRoot, "found.txt");
        await File.WriteAllTextAsync(matchingFile, "OS evidence");
        var outsideFile = Path.Combine(_root, "outside.txt");
        await File.WriteAllTextAsync(outsideFile, "outside");

        var authorization = _authorizationService.IssueAuthorization(
            "task-search-verified",
            "step-search-verified",
            "fs.search_files",
            new Dictionary<string, object?> { ["root_path"] = searchRoot, ["pattern"] = "*.txt" },
            risk: "low",
            approvedBy: "auto");
        var token = await _engine.BeginStepAsync(authorization);
        _authorizationService.Consume(authorization, authorization.StepId);
        var observation = HostObservation();
        var receipt = LyingReceipt(authorization) with
        {
            Stdout = JsonSerializer.Serialize(new[] { matchingFile, outsideFile, Path.Combine(searchRoot, "missing.txt") })
        };

        var result = await _engine.VerifyAsync(receipt, observation, token);

        Assert.True(result.Status == "FAILED", string.Join(" | ", result.Reasons));
        result.Stages.Single(stage => stage.Stage == 3).Passed.Should().BeTrue();
        result.Stages.Single(stage => stage.Stage == 5).Passed.Should().BeFalse();
        result.Evidence.Should().ContainSingle(item => item.Match == false);
        _auditStore.GetEvidence(authorization.StepId).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Search_Verifies_A_Matching_Existing_Path_Without_Trusting_Receipt_Success_Fields()
    {
        var searchRoot = Path.Combine(_root, "search-clean");
        Directory.CreateDirectory(searchRoot);
        var matchingFile = Path.Combine(searchRoot, "found.txt");
        await File.WriteAllTextAsync(matchingFile, "OS evidence");
        var authorization = _authorizationService.IssueAuthorization(
            "task-search-clean",
            "step-search-clean",
            "fs.search_files",
            new Dictionary<string, object?> { ["root_path"] = searchRoot, ["pattern"] = "*.txt" },
            risk: "low",
            approvedBy: "auto");
        var token = await _engine.BeginStepAsync(authorization);
        _authorizationService.Consume(authorization, authorization.StepId);
        var observation = HostObservation();
        var receipt = LyingReceipt(authorization) with { Stdout = JsonSerializer.Serialize(new[] { matchingFile }) };

        var result = await _engine.VerifyAsync(receipt, observation, token);

        result.Status.Should().Be("VERIFIED");
        result.Evidence.Should().ContainSingle(item => item.Match);
    }

    [Fact]
    public async Task Search_Fails_On_Malformed_Receipt_Path_Claims()
    {
        var searchRoot = Path.Combine(_root, "search-malformed");
        Directory.CreateDirectory(searchRoot);
        var authorization = _authorizationService.IssueAuthorization(
            "task-search-malformed",
            "step-search-malformed",
            "fs.search_files",
            new Dictionary<string, object?> { ["root_path"] = searchRoot, ["pattern"] = "*.txt" },
            risk: "low",
            approvedBy: "auto");
        var token = await _engine.BeginStepAsync(authorization);
        _authorizationService.Consume(authorization, authorization.StepId);

        var result = await _engine.VerifyAsync(
            LyingReceipt(authorization) with { Stdout = "not a JSON path array" },
            HostObservation(),
            token);

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task AppLaunch_Fails_When_Receipt_Claims_Success_But_No_Child_Process_Exists()
    {
        var imagePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe");
        var authorization = _authorizationService.IssueAuthorization(
            "task-app-missing",
            "step-app-missing",
            "app.launch",
            new Dictionary<string, object?> { ["app_id"] = "ping-fixture" },
            expected: new Dictionary<string, object?>
            {
                ["app_id"] = "ping-fixture",
                ["image_path"] = imagePath,
                ["running"] = true
            },
            allowedEffects: ["launch:ping-fixture"],
            risk: "medium");
        var token = await _engine.BeginStepAsync(authorization);
        _authorizationService.Consume(authorization, authorization.StepId);

        var result = await _engine.VerifyAsync(
            LyingReceipt(authorization) with { Stdout = "Launched PID 1234" },
            HostObservation(),
            token);

        result.Status.Should().Be("FAILED");
        result.Stages.Single(stage => stage.Stage == 5).Passed.Should().BeFalse();
    }

    [Fact]
    public async Task AppLaunch_Verifies_Live_NonElevated_Child_By_OS_Image_And_Parent()
    {
        var imagePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ping.exe");
        File.Exists(imagePath).Should().BeTrue();
        var authorization = _authorizationService.IssueAuthorization(
            "task-app-live",
            "step-app-live",
            "app.launch",
            new Dictionary<string, object?> { ["app_id"] = "ping-fixture" },
            expected: new Dictionary<string, object?>
            {
                ["app_id"] = "ping-fixture",
                ["image_path"] = imagePath,
                ["running"] = true
            },
            allowedEffects: ["launch:ping-fixture"],
            risk: "medium");
        var token = await _engine.BeginStepAsync(authorization);
        _authorizationService.Consume(authorization, authorization.StepId);
        var startedAt = _clock.UtcNow;
        using var child = new Process
        {
            StartInfo = new ProcessStartInfo(imagePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        child.StartInfo.ArgumentList.Add("-n");
        child.StartInfo.ArgumentList.Add("20");
        child.StartInfo.ArgumentList.Add("127.0.0.1");
        child.Start().Should().BeTrue();
        var observation = new ExecutionObservation(startedAt, _clock.UtcNow);

        try
        {
            var result = await _engine.VerifyAsync(
                LyingReceipt(authorization) with { Stdout = "No trusted evidence here" },
                observation,
                token);

            result.Status.Should().Be("VERIFIED");
            result.Evidence.Should().ContainSingle(item => item.Match);
        }
        finally
        {
            if (!child.HasExited)
                child.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task ScheduleReminder_Verification_Fails_When_Task_Is_Missing()
    {
        var future = _clock.UtcNow.AddHours(1);
        var scheduledAt = new DateTimeOffset(
            future.Year,
            future.Month,
            future.Day,
            future.Hour,
            future.Minute,
            future.Second,
            future.Offset);
        var authorization = _authorizationService.IssueAuthorization(
            "task-reminder-missing",
            "step-reminder-missing",
            "task.schedule_reminder",
            new Dictionary<string, object?>
            {
                ["task_name"] = "Missing reminder",
                ["time_iso"] = scheduledAt.ToString("O"),
                ["message"] = "No task should be trusted from the receipt"
            });
        var token = await _engine.BeginStepAsync(authorization);
        _authorizationService.Consume(authorization, authorization.StepId);

        var result = await _engine.VerifyAsync(
            LyingReceipt(authorization),
            HostObservation(),
            token);

        Assert.True(result.Status == "FAILED", string.Join(" | ", result.Reasons));
        result.Stages.Single(stage => stage.Stage == 3).Passed.Should().BeTrue();
        result.Stages.Single(stage => stage.Stage == 5).Passed.Should().BeFalse();
    }

    [Fact]
    public async Task ScheduleReminder_Executor_Rejects_Subsecond_Time_Without_Registering()
    {
        var future = _clock.UtcNow.AddHours(1);
        var scheduledAt = new DateTimeOffset(
            future.Year,
            future.Month,
            future.Day,
            future.Hour,
            future.Minute,
            future.Second,
            123,
            future.Offset);
        var authorization = _authorizationService.IssueAuthorization(
            "task-reminder-subsecond",
            "step-reminder-subsecond",
            "task.schedule_reminder",
            new Dictionary<string, object?>
            {
                ["task_name"] = "Subsecond reminder",
                ["time_iso"] = scheduledAt.ToString("O"),
                ["message"] = "Must not be rounded silently"
            });
        var scheduler = new RecordingTaskScheduler();
        var executor = new TaskScheduleReminderExecutor(_authorizationService, scheduler, _clock);
        var verifiedAuthorization = _authorizationService.Consume(authorization, authorization.StepId);

        var receipt = await executor.ExecuteAsync(verifiedAuthorization);

        receipt.ReportedSuccess.Should().BeFalse();
        receipt.Stderr.Should().Contain("whole-second precision");
        scheduler.WasRegistered.Should().BeFalse();
    }

    [Fact]
    public async Task ScheduleReminder_Executor_Creates_A_Task_That_Independent_Xml_Verification_Accepts()
    {
        var taskName = $"Jarvis test reminder {Guid.NewGuid():N}";
        var future = _clock.UtcNow.AddHours(1);
        var scheduledAt = new DateTimeOffset(
            future.Year,
            future.Month,
            future.Day,
            future.Hour,
            future.Minute,
            future.Second,
            future.Offset);
        var authorization = _authorizationService.IssueAuthorization(
            "task-reminder",
            "step-reminder",
            "task.schedule_reminder",
            new Dictionary<string, object?>
            {
                ["task_name"] = taskName,
                ["time_iso"] = scheduledAt.ToString("O"),
                ["message"] = "Reminder \"evidence\" \\ with data"
            });
        var token = await _engine.BeginStepAsync(authorization);
        var executor = new TaskScheduleReminderExecutor(
            _authorizationService,
            new WindowsReminderTaskScheduler(),
            _clock);
        var taskCreated = false;

        try
        {
            var verifiedAuthorization = _authorizationService.Consume(authorization, authorization.StepId);
            var startedAt = _clock.UtcNow;
            var receipt = await executor.ExecuteAsync(verifiedAuthorization);
            taskCreated = receipt.ReportedSuccess;
            var observation = new ExecutionObservation(startedAt, _clock.UtcNow);

            receipt.ReportedSuccess.Should().BeTrue();
            var result = await _engine.VerifyAsync(receipt, observation, token);

            result.Status.Should().Be("VERIFIED");
            result.Evidence.Should().ContainSingle(item => item.Match);
        }
        finally
        {
            if (taskCreated)
            {
                var taskPath = authorization.Expected["task_path"]!.ToString()!;
                new WindowsReminderTaskScheduler().Delete(taskPath);
            }
        }
    }

    private ExecutionObservation HostObservation()
    {
        var startedAt = _clock.UtcNow;
        var completedAt = _clock.UtcNow;
        _clock.UtcNow = completedAt;
        return new ExecutionObservation(startedAt, completedAt);
    }

    private static ExecutionReceiptDto LyingReceipt(AuthorizationRecordDto authorization) => new(
        1,
        authorization.StepId,
        authorization.AuthId,
        DateTimeOffset.MaxValue.ToString("O"),
        DateTimeOffset.MaxValue.ToString("O"),
        0,
        true,
        "Untrusted executor claim",
        string.Empty);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class RecordingTaskScheduler : IReminderTaskScheduler
    {
        public bool WasRegistered { get; private set; }

        public void Register(ReminderTaskRegistration registration)
        {
            WasRegistered = true;
        }

        public void Delete(string taskPath)
        {
            throw new NotSupportedException();
        }
    }
}
