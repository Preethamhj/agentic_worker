using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Jarvis.Cli;
using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Jarvis.Tools;
using Xunit;
using Xunit.Abstractions;

namespace Jarvis.Verification.Tests;

[CollectionDefinition("Phase3c OS resources", DisableParallelization = true)]
public sealed class Phase3cOsResourceCollection { }

[Collection("Phase3c OS resources")]
public sealed class Phase3cRealEngineEndToEndTests
{
    private readonly ITestOutputHelper _output;

    public Phase3cRealEngineEndToEndTests(ITestOutputHelper output) => _output = output;

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

#if DEBUG
    [Fact]
    public async Task Debug_Executor_Lie_Reports_Success_But_Real_Verifier_Fails()
    {
        var result = await RunScenarioAsync("fs.create_file", injectFault: false, debugExecutorLie: true);

        Assert.Equal(TaskState.Failed, result.FinalState);
        Assert.Equal("FAILED", Assert.Single(result.VerificationResults).Status);
        Assert.True(Assert.Single(result.Receipts).ReportedSuccess);
        Assert.Contains(
            result.VerificationResults.SelectMany(verification => verification.Reasons),
            reason => reason.Contains("target is absent", StringComparison.Ordinal));
    }
#endif

#if !DEBUG
    [Fact]
    public void Release_Executor_Assembly_Does_Not_Contain_Debug_Lie_Switch()
    {
        const string switchName = "DebugReportSuccessWithoutCreatingFile";
        var members = typeof(CreateFileExecutor).GetMembers(
            System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(members, member => string.Equals(member.Name, switchName, StringComparison.Ordinal));
    }
#endif

    [NonElevatedFact]
    public async Task AppLaunch_Notepad_Nonmatching_Process_Fails_Within_Timeout()
    {
        var control = await RunScenarioAsync("app.launch", injectFault: false);
        var timer = Stopwatch.StartNew();
        var faulted = await RunScenarioAsync("app.launch", injectFault: true);

        Assert.True(control.FinalState == TaskState.Completed, Describe(control));
        Assert.Equal("VERIFIED", Assert.Single(control.VerificationResults).Status);
        Assert.Equal(TaskState.Failed, faulted.FinalState);
        Assert.Equal("FAILED", Assert.Single(faulted.VerificationResults).Status);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(30), $"Non-matching process verification exceeded 30 seconds ({timer.Elapsed}).");
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

    private async Task<TaskOrchestratorResult> RunScenarioAsync(
        string toolId,
        bool injectFault,
        bool useRealScheduler = false,
        bool debugExecutorLie = false)
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-3c-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var reminderDirectory = Path.Combine(root, "reminder-data");
        var reminderLocation = new TemporaryReminderDataLocation(reminderDirectory);
        var reminderDataStore = new ReminderDataFileStore(reminderLocation);
        var fakeScheduler = new FakeReminderTaskScheduler();
        var fakeXmlCollector = new FakeXmlScheduledTaskEvidenceCollector();
        IReminderTaskScheduler scheduler = useRealScheduler
            ? new WindowsReminderTaskScheduler()
            : fakeScheduler;
        var recordingScheduler = new RecordingReminderTaskScheduler(scheduler);
        var clock = SystemClock.Instance;
        var auditStore = new InMemoryAuditStore();
        var hostPath = new FixedHostPathProvider(useRealScheduler
            ? Environment.ProcessPath ?? throw new InvalidOperationException("The test host executable path is unavailable.")
            : @"C:\Jarvis\Jarvis.Cli.exe");
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
        var createFileExecutor = new CreateFileExecutor(pathPolicy, authorizationService);
#if DEBUG
        createFileExecutor.DebugReportSuccessWithoutCreatingFile = debugExecutorLie;
#endif
        var executors = new List<IToolExecutor>
        {
            createFileExecutor,
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
                if (!useRealScheduler)
                {
                    var xml = ScheduledTaskEvidenceCollectorTests.CreateRealTaskSchedulerXml(authorization.Record);
                    if (injectFault)
                        xml = xml.Replace("<StartWhenAvailable>true</StartWhenAvailable>", "<StartWhenAvailable>false</StartWhenAvailable>", StringComparison.Ordinal);
                    fakeXmlCollector.SetXml(xml);
                }
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
            : fakeXmlCollector;
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
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            return await orchestrator.RunTaskAsync(
                    $"phase 3c {toolId} test",
                    cancellationToken: timeout.Token)
                .WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            foreach (var processId in processIds)
            {
                StopNotepadProcess(processId);
                _output.WriteLine($"Stopped test-launched Notepad PID {processId}.");
            }

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
            if (!process.WaitForExit(5_000))
                throw new TimeoutException($"Test-launched Notepad PID {processId} did not exit within five seconds.");
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
            inner.Register(registration);
            LastRegistration = registration;
        }

        public void Delete(string taskPath) => inner.Delete(taskPath);

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

    }

    private sealed class FakeXmlScheduledTaskEvidenceCollector : IScheduledTaskEvidenceCollector
    {
        private string? _taskXml;

        public void SetXml(string taskXml) => _taskXml = taskXml;

        public IReadOnlyList<string> CaptureTaskPaths(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return [];
        }

        public IndependentEvidenceObservation Collect(
            AuthorizationRecordDto authorization,
            IReadOnlyList<string> beforeTaskPaths,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var xml = _taskXml ?? throw new InvalidOperationException("Fake scheduler did not provide task XML.");
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var root = XDocument.Parse(xml).Root
                ?? throw new InvalidOperationException("Fake scheduler task XML has no root.");
            var taskPath = root.Element(ns + "RegistrationInfo")?.Element(ns + "URI")?.Value
                ?? throw new InvalidOperationException("Fake scheduler task XML has no registration URI.");
            var taskName = taskPath[(taskPath.LastIndexOf('\\') + 1)..];
            var afterTaskPaths = beforeTaskPaths.Append(taskPath).ToArray();
            return new ScheduledTaskEvidenceCollector().ReadTaskXml(
                authorization,
                taskPath,
                taskName,
                xml,
                taskEnabled: true,
                beforeTaskPaths,
                afterTaskPaths);
        }
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
