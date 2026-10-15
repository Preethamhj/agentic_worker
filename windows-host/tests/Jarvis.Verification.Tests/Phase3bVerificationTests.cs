using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using AsyncTask = System.Threading.Tasks.Task;
using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Jarvis.Tools;
using Microsoft.Win32.TaskScheduler;
using System.Security.Principal;
using Xunit;
using Xunit.Abstractions;

namespace Jarvis.Verification.Tests;

[SupportedOSPlatform("windows")]
public sealed class Phase3bVerificationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"jarvis-3b-{Guid.NewGuid():N}");
    private readonly TestClock _clock = new();
    private readonly InMemoryAuditStore _auditStore = new();
    private readonly AuthorizationService _authorizationService;
    private readonly VerificationEngine _engine;
    private readonly ITestOutputHelper _output;

    public Phase3bVerificationTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
        _authorizationService = new AuthorizationService(
            new InMemoryDpapiKeyStore(),
            _clock,
            _auditStore);
        _engine = new VerificationEngine(_authorizationService, _auditStore, _clock);
    }

    [Fact]
    public async AsyncTask Search_Verifies_Only_Existing_Matching_Paths_Inside_Signed_Root()
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
    public async AsyncTask Search_Verifies_A_Matching_Existing_Path_Without_Trusting_Receipt_Success_Fields()
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
    public async AsyncTask Search_Fails_On_Malformed_Receipt_Path_Claims()
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
    public async AsyncTask Search_UnderReporting_Is_Failed_By_Independent_FindFirstFileEx_Enumeration()
    {
        var searchRoot = Path.Combine(_root, "search-under-report");
        Directory.CreateDirectory(searchRoot);
        var first = Path.Combine(searchRoot, "first.txt");
        var second = Path.Combine(searchRoot, "second.txt");
        await File.WriteAllTextAsync(first, "1");
        await File.WriteAllTextAsync(second, "2");
        var authorization = IssueSearchAuthorization(searchRoot, "*.txt", "under-report");
        var token = await _engine.BeginStepAsync(authorization);
        _authorizationService.Consume(authorization, authorization.StepId);

        var result = await _engine.VerifyAsync(
            LyingReceipt(authorization) with { Stdout = JsonSerializer.Serialize(new[] { first }) },
            HostObservation(),
            token);

        result.Status.Should().Be("FAILED");
        result.Evidence.Single().Observed["independently_enumerated_count"].Should().Be(2);
        result.Evidence.Single().Observed["reported_path_count"].Should().Be(1);
    }

    [Fact]
    public async AsyncTask Search_OverReporting_Is_Failed_By_Independent_FindFirstFileEx_Enumeration()
    {
        var searchRoot = Path.Combine(_root, "search-over-report");
        Directory.CreateDirectory(searchRoot);
        var matching = Path.Combine(searchRoot, "found.txt");
        var nonmatching = Path.Combine(searchRoot, "not-found.log");
        await File.WriteAllTextAsync(matching, "matched");
        await File.WriteAllTextAsync(nonmatching, "not matched");
        var authorization = IssueSearchAuthorization(searchRoot, "*.txt", "over-report");
        var token = await _engine.BeginStepAsync(authorization);
        _authorizationService.Consume(authorization, authorization.StepId);

        var result = await _engine.VerifyAsync(
            LyingReceipt(authorization) with { Stdout = JsonSerializer.Serialize(new[] { matching, nonmatching }) },
            HostObservation(),
            token);

        result.Status.Should().Be("FAILED");
        result.Evidence.Single().Observed["independently_enumerated_count"].Should().Be(1);
        result.Evidence.Single().Observed["reported_path_count"].Should().Be(2);
    }

    [Fact]
    public async AsyncTask Search_Reaching_Result_Cap_Escalates_Instead_Of_Passing()
    {
        var searchRoot = Path.Combine(_root, "search-result-cap");
        Directory.CreateDirectory(searchRoot);
        var first = Path.Combine(searchRoot, "first.txt");
        var second = Path.Combine(searchRoot, "second.txt");
        await File.WriteAllTextAsync(first, "1");
        await File.WriteAllTextAsync(second, "2");
        var authorization = IssueSearchAuthorization(
            searchRoot,
            "*.txt",
            "result-cap",
            new Dictionary<string, object?> { ["max_results"] = 1 });
        var token = await _engine.BeginStepAsync(authorization);
        _authorizationService.Consume(authorization, authorization.StepId);

        var result = await _engine.VerifyAsync(
            LyingReceipt(authorization) with { Stdout = JsonSerializer.Serialize(new[] { first }) },
            HostObservation(),
            token);

        result.Status.Should().Be("ESCALATE");
        result.Stages.Single(stage => stage.Stage == 5).Outcome.Should().Be("ESCALATE");
        result.Evidence.Single().Observed["enumeration_hit_result_cap"].Should().Be(true);
    }

    [Fact]
    public async AsyncTask AppLaunch_Fails_When_Receipt_Claims_Success_But_No_Child_Process_Exists()
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
    public async AsyncTask AppLaunch_Verifies_Live_NonElevated_Child_By_OS_Image_And_Parent()
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
    public async AsyncTask ScheduleReminder_Verification_Fails_When_Task_Is_Missing()
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
    public async AsyncTask ScheduleReminder_Executor_Rejects_Subsecond_Time_Without_Registering()
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
    public async AsyncTask ScheduleReminder_With_Wrong_Time_Is_Failed()
    {
        var authorization = IssueReminderAuthorization("wrong-time");
        var token = await _engine.BeginStepAsync(authorization);
        _authorizationService.Consume(authorization, authorization.StepId);
        var taskPath = (string)authorization.Expected["task_path"]!;
        var expectedTime = DateTimeOffset.Parse((string)authorization.CanonicalArgs["time_iso"]!);
        var registered = false;
        try
        {
            RegisterRawTask(
                authorization,
                expectedTime.AddMinutes(1),
                (string)authorization.Expected["action_path"]!,
                ExpectedReminderArguments(authorization),
                enabled: true);
            registered = true;
            var result = await VerifyReminder(authorization, token);
            result.Status.Should().Be("FAILED");
            result.Evidence.Single().Observed["scheduler_method"].Should().Be("COM");
        }
        finally
        {
            if (registered)
                new WindowsReminderTaskScheduler().Delete(taskPath);
        }
    }

    [Fact]
    public async AsyncTask ScheduleReminder_With_Wrong_Action_Is_Failed()
    {
        var authorization = IssueReminderAuthorization("wrong-action");
        var token = await _engine.BeginStepAsync(authorization);
        _authorizationService.Consume(authorization, authorization.StepId);
        var taskPath = (string)authorization.Expected["task_path"]!;
        var scheduledTime = DateTimeOffset.Parse((string)authorization.CanonicalArgs["time_iso"]!);
        var wrongAction = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe");
        var registered = false;
        try
        {
            RegisterRawTask(
                authorization,
                scheduledTime,
                wrongAction,
                ExpectedReminderArguments(authorization),
                enabled: true);
            registered = true;
            var result = await VerifyReminder(authorization, token);
            result.Status.Should().Be("FAILED");
        }
        finally
        {
            if (registered)
                new WindowsReminderTaskScheduler().Delete(taskPath);
        }
    }

    [Fact]
    public async AsyncTask ScheduleReminder_Disabled_Task_Is_Failed()
    {
        var authorization = IssueReminderAuthorization("disabled");
        var token = await _engine.BeginStepAsync(authorization);
        _authorizationService.Consume(authorization, authorization.StepId);
        var taskPath = (string)authorization.Expected["task_path"]!;
        var scheduledTime = DateTimeOffset.Parse((string)authorization.CanonicalArgs["time_iso"]!);
        var registered = false;
        try
        {
            RegisterRawTask(
                authorization,
                scheduledTime,
                (string)authorization.Expected["action_path"]!,
                ExpectedReminderArguments(authorization),
                enabled: false);
            registered = true;
            var result = await VerifyReminder(authorization, token);
            result.Status.Should().Be("FAILED");
            result.Evidence.Single().Observed["task_enabled"].Should().Be(false);
        }
        finally
        {
            if (registered)
                new WindowsReminderTaskScheduler().Delete(taskPath);
        }
    }

    [Fact]
    public async AsyncTask ScheduleReminder_Extra_Unauthorized_Task_Is_Failed()
    {
        var authorization = IssueReminderAuthorization("extra-task");
        var token = await _engine.BeginStepAsync(authorization);
        _authorizationService.Consume(authorization, authorization.StepId);
        var scheduler = new WindowsReminderTaskScheduler();
        var authorizedTaskPath = (string)authorization.Expected["task_path"]!;
        var extraTaskPath = $@"\Jarvis\extra-{Guid.NewGuid():N}";
        var scheduledTime = DateTimeOffset.Parse((string)authorization.CanonicalArgs["time_iso"]!);
        var authorizedRegistered = false;
        var extraRegistered = false;
        try
        {
            scheduler.Register(new ReminderTaskRegistration(
                authorization.AuthId,
                authorizedTaskPath,
                (string)authorization.Expected["action_path"]!,
                ExpectedReminderArguments(authorization),
                AppContext.BaseDirectory,
                scheduledTime,
                "authorized reminder"));
            authorizedRegistered = true;
            scheduler.Register(new ReminderTaskRegistration(
                extraTaskPath[(extraTaskPath.LastIndexOf('\\') + 1)..],
                extraTaskPath,
                (string)authorization.Expected["action_path"]!,
                "--jarvis-reminder \"unauthorized\" \"extra\"",
                AppContext.BaseDirectory,
                scheduledTime,
                "unauthorized reminder"));
            extraRegistered = true;

            var result = await VerifyReminder(authorization, token);

            result.Status.Should().Be("FAILED");
            result.Evidence.Single().Observed["unexpected_new_tasks"].Should().BeEquivalentTo(new[] { extraTaskPath });
        }
        finally
        {
            if (extraRegistered)
                scheduler.Delete(extraTaskPath);
            if (authorizedRegistered)
                scheduler.Delete(authorizedTaskPath);
        }
    }

    [Fact]
    public async AsyncTask ScheduleReminder_Executor_Creates_A_Task_That_Independent_Xml_Verification_Accepts()
    {
        var taskName = "Jarvis \"quoted\" & 100%";
        var message = "Line 1\r\nLine 2 & 100% ಕನ್ನಡ ನಮಸ್ಕಾರ";
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
                ["message"] = message
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
            Console.WriteLine($"Result: {result.Status} | {string.Join(" | ", result.Reasons)}");
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result.Evidence));

            result.Status.Should().Be("VERIFIED");
            result.Evidence.Should().ContainSingle(item => item.Match);
            var observed = result.Evidence.Single().Observed;
            observed["scheduler_method"].Should().Be("COM");
            observed["action_command"].Should().Be(Path.GetFullPath(Environment.ProcessPath!));
            var expectedArguments = BuildExpectedReminderArguments(taskName, message);
            var xmlArguments = observed["action_arguments"] as string ?? string.Empty;
            NormalizeReminderXmlArguments(xmlArguments).Should().Be(NormalizeReminderXmlArguments(expectedArguments));
            _output.WriteLine($"Scheduler method: {observed["scheduler_method"]}");
            _output.WriteLine($"Task executable: {observed["action_command"]}");
            _output.WriteLine($"Task arguments: {xmlArguments}");
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

    private AuthorizationRecordDto IssueSearchAuthorization(
        string root,
        string pattern,
        string suffix,
        Dictionary<string, object?>? extraArgs = null)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["root_path"] = root,
            ["pattern"] = pattern
        };
        if (extraArgs is not null)
            foreach (var pair in extraArgs)
                arguments[pair.Key] = pair.Value;
        return _authorizationService.IssueAuthorization(
            $"task-search-{suffix}",
            $"step-search-{suffix}",
            "fs.search_files",
            arguments,
            risk: "low",
            approvedBy: "auto");
    }

    private AuthorizationRecordDto IssueReminderAuthorization(string suffix)
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
        return _authorizationService.IssueAuthorization(
            $"task-reminder-{suffix}",
            $"step-reminder-{suffix}",
            "task.schedule_reminder",
            new Dictionary<string, object?>
            {
                ["task_name"] = $"Reminder {suffix}",
                ["time_iso"] = scheduledAt.ToString("O"),
                ["message"] = "message"
            });
    }

    private async System.Threading.Tasks.Task<VerificationResultDto> VerifyReminder(
        AuthorizationRecordDto authorization,
        StepToken token) =>
        await _engine.VerifyAsync(
            LyingReceipt(authorization),
            HostObservation(),
            token);

    [SupportedOSPlatform("windows")]
    private static void RegisterRawTask(
        AuthorizationRecordDto authorization,
        DateTimeOffset scheduledAt,
        string actionPath,
        string arguments,
        bool enabled)
    {
        var userSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Current user SID is unavailable.");
        var taskPath = (string)authorization.Expected["task_path"]!;
        var taskName = taskPath[(taskPath.LastIndexOf('\\') + 1)..];
        using var service = new TaskService();
        TaskFolder folder;
        try
        {
            folder = service.GetFolder(@"\Jarvis");
        }
        catch (FileNotFoundException)
        {
            folder = service.RootFolder.CreateFolder("Jarvis");
        }
        using (folder)
        using (var definition = service.NewTask())
        {
            definition.RegistrationInfo.Author = "Jarvis test";
            definition.Principal.UserId = userSid;
            definition.Principal.LogonType = TaskLogonType.InteractiveToken;
            definition.Principal.RunLevel = TaskRunLevel.LUA;
            definition.Settings.Enabled = enabled;
            definition.Triggers.Add(new TimeTrigger(scheduledAt.ToLocalTime().DateTime));
            definition.Actions.Add(new ExecAction(
                Path.GetFullPath(actionPath),
                arguments,
                Path.GetDirectoryName(actionPath)));
            folder.RegisterTaskDefinition(
                taskName,
                definition,
                TaskCreation.CreateOrUpdate,
                userSid,
                null,
                TaskLogonType.InteractiveToken);
        }
    }

    private static string ExpectedReminderArguments(AuthorizationRecordDto authorization) =>
        BuildExpectedReminderArguments(
            (string)authorization.CanonicalArgs["task_name"]!,
            (string)authorization.CanonicalArgs["message"]!);

    private static string BuildExpectedReminderArguments(string taskName, string message) =>
        $"--jarvis-reminder {QuoteWindowsArgument(taskName)} {QuoteWindowsArgument(message)}";

    private static string NormalizeReminderXmlArguments(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

    private static string QuoteWindowsArgument(string value)
    {
        var result = new System.Text.StringBuilder("\"");
        var slashCount = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                slashCount++;
                continue;
            }
            if (character == '"')
            {
                result.Append('\\', slashCount * 2 + 1).Append('"');
                slashCount = 0;
                continue;
            }
            result.Append('\\', slashCount).Append(character);
            slashCount = 0;
        }
        result.Append('\\', slashCount * 2).Append('"');
        return result.ToString();
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
