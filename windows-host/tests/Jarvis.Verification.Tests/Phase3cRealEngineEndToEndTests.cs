using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Jarvis.Cli;
using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Jarvis.Tools;
using Xunit;

namespace Jarvis.Verification.Tests;

[CollectionDefinition("Phase3c OS resources", DisableParallelization = true)]
public sealed class Phase3cOsResourceCollection { }

[Collection("Phase3c OS resources")]
public sealed class Phase3cRealEngineEndToEndTests
{
    [Fact]
    public async Task SearchFiles_Real_Executor_Verifies_And_Faulted_Search_Output_Fails()
    {
        var control = await RunScenarioAsync("fs.search_files", injectFault: false);
        var faulted = await RunScenarioAsync("fs.search_files", injectFault: true);

        Assert.True(control.FinalState == TaskState.Completed, Describe(control));
        Assert.Equal("VERIFIED", Assert.Single(control.VerificationResults).Status);
        Assert.Equal(TaskState.Failed, faulted.FinalState);
        Assert.Equal("FAILED", Assert.Single(faulted.VerificationResults).Status);
    }

    [Fact]
    public async Task CreateFolder_Real_Executor_Verifies_And_Removed_Folder_Fails()
    {
        var control = await RunScenarioAsync("fs.create_folder", injectFault: false);
        var faulted = await RunScenarioAsync("fs.create_folder", injectFault: true);

        Assert.True(control.FinalState == TaskState.Completed, Describe(control));
        Assert.Equal("VERIFIED", Assert.Single(control.VerificationResults).Status);
        Assert.Equal(TaskState.Failed, faulted.FinalState);
        Assert.Equal("FAILED", Assert.Single(faulted.VerificationResults).Status);
    }

    [Fact]
    public async Task CreateFile_Real_Executor_Verifies_And_Removed_File_Fails()
    {
        var control = await RunScenarioAsync("fs.create_file", injectFault: false);
        var faulted = await RunScenarioAsync("fs.create_file", injectFault: true);

        Assert.True(control.FinalState == TaskState.Completed, Describe(control));
        Assert.Equal("VERIFIED", Assert.Single(control.VerificationResults).Status);
        Assert.Equal(TaskState.Failed, faulted.FinalState);
        Assert.Equal("FAILED", Assert.Single(faulted.VerificationResults).Status);
    }

    [NonElevatedFact]
    public async Task AppLaunch_Notepad_Real_Executor_Verifies_And_Stopped_Process_Fails()
    {
        var control = await RunScenarioAsync("app.launch", injectFault: false);
        var faulted = await RunScenarioAsync("app.launch", injectFault: true);

        Assert.True(control.FinalState == TaskState.Completed, Describe(control));
        Assert.Equal("VERIFIED", Assert.Single(control.VerificationResults).Status);
        Assert.Equal(TaskState.Failed, faulted.FinalState);
        Assert.Equal("FAILED", Assert.Single(faulted.VerificationResults).Status);
    }

    [Fact]
    public async Task ScheduleReminder_Fake_Scheduler_Real_Engine_Verifies_And_Tampered_Task_Fails()
    {
        var control = await RunScenarioAsync("task.schedule_reminder", injectFault: false);
        var faulted = await RunScenarioAsync("task.schedule_reminder", injectFault: true);

        Assert.True(control.FinalState == TaskState.Completed, Describe(control));
        Assert.Equal("VERIFIED", Assert.Single(control.VerificationResults).Status);
        Assert.Equal(TaskState.Failed, faulted.FinalState);
        Assert.Equal("FAILED", Assert.Single(faulted.VerificationResults).Status);
    }

    [Trait("Category", "RealScheduler")]
    [RealSchedulerFact]
    public async Task ScheduleReminder_Real_Scheduler_Real_Engine_Verifies_And_Cleans_Up()
    {
        var result = await RunScenarioAsync(
            "task.schedule_reminder",
            injectFault: false,
            useRealScheduler: true);

        Assert.True(result.FinalState == TaskState.Completed, Describe(result));
        Assert.Equal("VERIFIED", Assert.Single(result.VerificationResults).Status);
    }

    private static async Task<TaskOrchestratorResult> RunScenarioAsync(
        string toolId,
        bool injectFault,
        bool useRealScheduler = false)
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-3c-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var reminderDirectory = Path.Combine(root, "reminder-data");
        var reminderLocation = new TemporaryReminderDataLocation(reminderDirectory);
        var reminderDataStore = new ReminderDataFileStore(reminderLocation);
        var fakeScheduler = new FakeReminderTaskScheduler();
        IReminderTaskScheduler scheduler = useRealScheduler
            ? new WindowsReminderTaskScheduler()
            : fakeScheduler;
        var recordingScheduler = new RecordingReminderTaskScheduler(scheduler);
        var clock = SystemClock.Instance;
        var auditStore = new InMemoryAuditStore();
        var hostPath = new FixedHostPathProvider(@"C:\Jarvis\Jarvis.Cli.exe");
        var authorizationService = new AuthorizationService(
            new InMemoryDpapiKeyStore(),
            clock,
            auditStore,
            hostPath,
            reminderLocation);
        var pathPolicy = new PathPolicy([root]);
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var toolPolicy = ToolPolicy.FromFile(
            Path.Combine(repositoryRoot, "config", "tools.registry.json"),
            Path.Combine(repositoryRoot, "config", "apps.allowlist.json"));
        var processIds = new List<int>();
        var reminderTaskId = (string?)null;
        var executors = new List<IToolExecutor>
        {
            new CreateFileExecutor(pathPolicy, authorizationService),
            new CreateFolderExecutor(pathPolicy, authorizationService),
            new SearchFilesExecutor(pathPolicy, authorizationService),
            new AppLaunchExecutor(toolPolicy, authorizationService, new WindowsElevationChecker()),
            new TaskScheduleReminderExecutor(
                authorizationService,
                recordingScheduler,
                clock,
                reminderDataStore,
                hostPath)
        };

        var arguments = CreateArguments(toolId, root);
        if (toolId == "fs.search_files")
        {
            Directory.CreateDirectory((string)arguments["root_path"]!);
            await File.WriteAllTextAsync(Path.Combine((string)arguments["root_path"]!, "found.txt"), "independent search evidence");
        }

        var targetExecutor = executors.Single(executor => executor.ToolId == toolId);
        executors.Remove(targetExecutor);
        executors.Add(new ExecutorDecorator(targetExecutor, (authorization, receipt) =>
        {
            if (toolId == "app.launch")
            {
                var marker = "PID ";
                var markerIndex = receipt.Stdout.LastIndexOf(marker, StringComparison.Ordinal);
                if (markerIndex < 0
                    || !int.TryParse(receipt.Stdout[(markerIndex + marker.Length)..], out var processId))
                    throw new InvalidOperationException("The real app.launch executor did not report its launched PID.");
                processIds.Add(processId);
                if (injectFault)
                    StopNotepadProcess(processId);
            }

            if (toolId == "task.schedule_reminder")
            {
                reminderTaskId = (string)authorization.Record.Expected["task_id"]!;
                if (injectFault && !useRealScheduler)
                    recordingScheduler.CorruptFakeRegistration(registration => registration with { TaskName = registration.TaskName + "-fault" });
            }

            if (injectFault && toolId == "fs.create_file")
                File.Delete((string)authorization.CanonicalArgs["path"]!);
            if (injectFault && toolId == "fs.create_folder")
                Directory.Delete((string)authorization.CanonicalArgs["path"]!, recursive: true);
            if (injectFault && toolId == "fs.search_files")
                return receipt with
                {
                    Stdout = JsonSerializer.Serialize(new[]
                    {
                        Path.Combine((string)authorization.CanonicalArgs["root_path"]!, "missing.txt")
                    })
                };
            return receipt;
        }));

        var stepExecutor = new StepExecutor(authorizationService, executors, toolPolicy);
        IScheduledTaskEvidenceCollector schedulerCollector = useRealScheduler
            ? new ScheduledTaskEvidenceCollector()
            : new FakeReminderEvidenceCollector(fakeScheduler, reminderDataStore);
        var verifier = new VerificationEngine(
            authorizationService,
            auditStore,
            clock,
            10_000,
            TimeSpan.FromSeconds(30),
            null,
            schedulerCollector);

        try
        {
            await using var queue = new ExecutionQueue();
            var orchestrator = new TaskOrchestrator(
                new SingleStepPlanner(toolId, arguments),
                new AllowAllSafetyGate(),
                new TestApprovalHandler(),
                queue,
                stepExecutor,
                verifier,
                auditStore,
                clock);
            return await orchestrator.RunTaskAsync($"phase 3c {toolId} test");
        }
        finally
        {
            foreach (var processId in processIds)
                StopNotepadProcess(processId);

            if (recordingScheduler.LastRegistration is { } registration)
                recordingScheduler.Delete(registration.TaskPath);
            if (reminderTaskId is not null && File.Exists(reminderDataStore.GetFilePath(reminderTaskId)))
                reminderDataStore.Delete(reminderTaskId);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static Dictionary<string, object?> CreateArguments(string toolId, string root)
    {
        return toolId switch
        {
            "fs.search_files" => new Dictionary<string, object?>
            {
                ["root_path"] = Path.Combine(root, "search"),
                ["pattern"] = "*.txt"
            },
            "fs.create_folder" => new Dictionary<string, object?>
            {
                ["path"] = Path.Combine(root, "created-folder")
            },
            "fs.create_file" => new Dictionary<string, object?>
            {
                ["path"] = Path.Combine(root, "created-file.txt"),
                ["content"] = "independent file evidence"
            },
            "app.launch" => new Dictionary<string, object?> { ["app_id"] = "notepad" },
            "task.schedule_reminder" => new Dictionary<string, object?>
            {
                ["time_iso"] = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds())
                    .ToString("O", CultureInfo.InvariantCulture),
                ["message"] = "Phase 3c test reminder"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(toolId), toolId, "Unsupported end-to-end test tool.")
        };
    }

    private static string Describe(TaskOrchestratorResult result) =>
        $"{result.StatusMessage}{Environment.NewLine}{string.Join(Environment.NewLine, result.VerificationResults.SelectMany(verification => verification.Reasons.Concat(verification.Stages.Select(stage => $"{stage.Name}: {stage.Outcome} - {stage.Detail}")).Concat(verification.Evidence.Select(evidence => JsonSerializer.Serialize(evidence.Observed)))))}{Environment.NewLine}{string.Join(Environment.NewLine, result.Receipts.Select(receipt => receipt.Stdout))}";

    private static void StopNotepadProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!string.Equals(process.ProcessName, "notepad", StringComparison.OrdinalIgnoreCase))
                return;
            process.Kill();
            process.WaitForExit(5_000);
        }
        catch (ArgumentException)
        {
        }
    }

    private sealed class ExecutorDecorator(
        IToolExecutor inner,
        Func<VerifiedAuthorization, ExecutionReceiptDto, ExecutionReceiptDto> afterExecution) : IToolExecutor
    {
        public string ToolId => inner.ToolId;

        public async Task<ExecutionReceiptDto> ExecuteAsync(
            VerifiedAuthorization authorization,
            CancellationToken cancellationToken = default)
        {
            var receipt = await inner.ExecuteAsync(authorization, cancellationToken);
            return afterExecution(authorization, receipt);
        }
    }

    private sealed class SingleStepPlanner(string toolId, Dictionary<string, object?> arguments) : IPlanner
    {
        public Task<PlanResponseDto> CreatePlanAsync(
            string prompt,
            string? traceId = null,
            CancellationToken cancellationToken = default)
        {
            var plan = new PlanResponseDto(
                1,
                "trace-phase-3c",
                "ok",
                new IntentDto(1, "intent-phase-3c", "direct", toolId, 1, [], new RiskDto("medium", []), "rule"),
                [
                    new PlanStepDto(
                        1,
                        "step-phase-3c",
                        1,
                        $"Execute {toolId}",
                        [],
                        new ToolCallDto(1, toolId, arguments),
                        new VerificationPlanDto(1, [], []),
                        "medium")
                ],
                "plan ready");
            return Task.FromResult(plan);
        }
    }

    private sealed class AllowAllSafetyGate : ISafetyGate
    {
        public Task<SafetyEvaluation> EvaluateStepAsync(
            string taskId,
            string stepId,
            string toolId,
            IReadOnlyDictionary<string, object?> rawArgs,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SafetyEvaluation(true, "medium", CanonicalArgs: new Dictionary<string, object?>(rawArgs)));
    }

    private sealed class TestApprovalHandler : IApprovalHandler
    {
        public Task<ApprovalDecision> RequestApprovalAsync(
            ApprovalRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApprovalDecision(true, "phase-3c-test"));
    }

    private sealed class RecordingReminderTaskScheduler(IReminderTaskScheduler inner) : IReminderTaskScheduler
    {
        public ReminderTaskRegistration? LastRegistration { get; private set; }

        public void Register(ReminderTaskRegistration registration)
        {
            LastRegistration = registration;
            inner.Register(registration);
        }

        public void Delete(string taskPath) => inner.Delete(taskPath);

        public void CorruptFakeRegistration(Func<ReminderTaskRegistration, ReminderTaskRegistration> corruption)
        {
            if (inner is not FakeReminderTaskScheduler fake)
                throw new InvalidOperationException("Only fake scheduler registrations can be fault-injected.");
            fake.Corrupt(corruption);
            LastRegistration = fake.Registration;
        }
    }

    private sealed class FakeReminderTaskScheduler : IReminderTaskScheduler
    {
        public ReminderTaskRegistration? Registration { get; private set; }

        public void Register(ReminderTaskRegistration registration) => Registration = registration;

        public void Delete(string taskPath)
        {
            if (Registration?.TaskPath == taskPath)
                Registration = null;
        }

        public void Corrupt(Func<ReminderTaskRegistration, ReminderTaskRegistration> corruption)
        {
            if (Registration is null)
                throw new InvalidOperationException("No fake task is available for fault injection.");
            Registration = corruption(Registration);
        }
    }

    private sealed class FakeReminderEvidenceCollector(
        FakeReminderTaskScheduler scheduler,
        ReminderDataFileStore dataStore) : IScheduledTaskEvidenceCollector
    {
        public IReadOnlyList<string> CaptureTaskPaths(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return scheduler.Registration is { } registration ? [registration.TaskPath] : [];
        }

        public IndependentEvidenceObservation Collect(
            AuthorizationRecordDto authorization,
            IReadOnlyList<string> beforeTaskPaths,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expected = authorization.Expected;
            var registration = scheduler.Registration;
            if (registration is null)
                return Mismatch("Fake scheduler did not retain a registered task.");

            var scheduledAt = DateTimeOffset.Parse(
                (string)expected["time_iso"]!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            var endBoundary = scheduledAt.AddSeconds(Convert.ToDouble(expected["end_boundary_offset_seconds"], CultureInfo.InvariantCulture));
            var taskId = (string)expected["task_id"]!;
            var payload = dataStore.Read(taskId);
            var settingsMatch = new Dictionary<string, bool>
            {
                ["task_name"] = string.Equals(registration.TaskName, (string)expected["scheduler_task_name"]!, StringComparison.Ordinal),
                ["task_path"] = string.Equals(registration.TaskPath, (string)expected["task_path"]!, StringComparison.OrdinalIgnoreCase),
                ["action_path"] = string.Equals(registration.ActionPath, (string)expected["action_path"]!, StringComparison.OrdinalIgnoreCase),
                ["action_arguments"] = string.Equals(registration.ActionArguments, (string)expected["action_arguments"]!, StringComparison.Ordinal),
                ["trigger_time"] = registration.ScheduledAtUtc.ToUniversalTime() == scheduledAt.ToUniversalTime(),
                ["disallow_start_if_on_batteries"] = registration.DisallowStartIfOnBatteries.Equals(expected["disallow_start_if_on_batteries"]),
                ["stop_if_going_on_batteries"] = registration.StopIfGoingOnBatteries.Equals(expected["stop_if_going_on_batteries"]),
                ["execution_time_limit"] = registration.ExecutionTimeLimit == TimeSpan.FromSeconds(Convert.ToDouble(expected["execution_time_limit_seconds"], CultureInfo.InvariantCulture)),
                ["start_when_available"] = registration.StartWhenAvailable.Equals(expected["start_when_available"]),
                ["multiple_instances_policy"] = EnumValueMatches(registration.MultipleInstancesPolicy, expected["multiple_instances_policy"]),
                ["logon_type"] = EnumValueMatches(registration.LogonType, expected["logon_type"]),
                ["run_level"] = registration.RunLevel == Microsoft.Win32.TaskScheduler.TaskRunLevel.LUA
                    && string.Equals(
                        Convert.ToString(expected["run_level"], CultureInfo.InvariantCulture),
                        ReminderSchedulerPolicy.RunLevel,
                        StringComparison.Ordinal),
                ["end_boundary"] = registration.EndBoundaryUtc?.ToUniversalTime() == endBoundary.ToUniversalTime(),
                ["delete_expired_task_after"] = registration.DeleteExpiredTaskAfter == TimeSpan.FromSeconds(Convert.ToDouble(expected["delete_expired_task_after_seconds"], CultureInfo.InvariantCulture)),
                ["reminder_title"] = string.Equals(payload.TaskName, (string)authorization.CanonicalArgs["task_name"]!, StringComparison.Ordinal),
                ["reminder_message"] = string.Equals(payload.Message, (string)authorization.CanonicalArgs["message"]!, StringComparison.Ordinal),
                ["reminder_data_exists"] = File.Exists((string)expected["reminder_data_path"]!),
                ["task_path_is_nonempty"] = !string.IsNullOrEmpty(registration.TaskPath),
                ["no_unexpected_tasks"] = !beforeTaskPaths.Contains(registration.TaskPath, StringComparer.OrdinalIgnoreCase)
            };
            var matches = settingsMatch.Values.All(match => match);
            var detail = matches
                ? "Fake scheduler registration and reminder payload matched signed authorization."
                : $"Fake scheduler registration or reminder payload mismatches: {string.Join(", ", settingsMatch.Where(item => !item.Value).Select(item => item.Key))}.";
            return new IndependentEvidenceObservation(
                "task_exists",
                nameof(FakeReminderEvidenceCollector),
                new Dictionary<string, object?>
                {
                    ["task_name"] = registration.TaskName,
                    ["task_path"] = registration.TaskPath,
                    ["action_path"] = registration.ActionPath,
                    ["action_arguments"] = registration.ActionArguments,
                    ["scheduled_at_utc"] = registration.ScheduledAtUtc,
                    ["end_boundary_utc"] = registration.EndBoundaryUtc,
                    ["delete_expired_task_after"] = registration.DeleteExpiredTaskAfter?.ToString(),
                    ["setting_matches"] = settingsMatch,
                    ["reminder_data_exists"] = File.Exists((string)expected["reminder_data_path"]!)
                },
                matches,
                detail,
                matches ? "PASSED" : "FAILED");
        }

        private static IndependentEvidenceObservation Mismatch(string detail) =>
            new("task_exists", nameof(FakeReminderEvidenceCollector), new Dictionary<string, object?>(), false, detail, "FAILED");

        private static bool EnumValueMatches<T>(T actual, object? expected) where T : struct, Enum =>
            string.Equals(actual.ToString(), Convert.ToString(expected, CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private sealed class TemporaryReminderDataLocation(string directoryPath) : IReminderDataLocation
    {
        public string DirectoryPath { get; } = directoryPath;
    }

    private sealed class FixedHostPathProvider(string path) : IHostPathProvider
    {
        public string GetHostPath() => path;
    }
}
