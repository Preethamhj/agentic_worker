using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using FluentAssertions;
using Jarvis.Cli;
using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Jarvis.Tools;
using Jarvis.TestSupport;
using Xunit;

namespace Jarvis.E2E.Tests;

[CollectionDefinition("E2E process isolation", DisableParallelization = true)]
public sealed class E2EProcessIsolationCollection { }

[Collection("E2E process isolation")]
public class CliEndToEndTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _testDbPath;
    private readonly PathPolicy _pathPolicy;
    private readonly ToolPolicy _toolPolicy;
    private readonly SqliteAuditStore _auditStore;
    private readonly IDpapiKeyStore _keyStore;
    private readonly AuthorizationService _authService;
    private readonly SafetyGate _safetyGate;
    private readonly StepExecutor _stepExecutor;
    private readonly StubVerifier _stubVerifier;
    private readonly ExecutionQueue _queue;

    public CliEndToEndTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"Jarvis_CLI_E2E_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        _testDbPath = Path.Combine(_tempRoot, "audit.db");
        _auditStore = new SqliteAuditStore(_testDbPath);

        _pathPolicy = new PathPolicy(new[] { _tempRoot });

        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var configDir = Path.Combine(repoRoot, "config");
        var toolsRegistryPath = Path.Combine(configDir, "tools.registry.json");
        _toolPolicy = ToolPolicy.FromJson(
            File.ReadAllText(toolsRegistryPath),
            JsonSerializer.Serialize(new
            {
                schema_version = 1,
                allowed_apps = new[]
                {
                    new
                    {
                        app_id = "testapp",
                        executable = TestApps.Locate(),
                        display_name = "Jarvis Test Helper"
                    }
                }
            }));

        _keyStore = OperatingSystem.IsWindows() ? new DpapiKeyStore() : new InMemoryDpapiKeyStore();
        _authService = new AuthorizationService(_keyStore, SystemClock.Instance, _auditStore);

        _safetyGate = new SafetyGate(_toolPolicy, _authService, pathPolicy: _pathPolicy, auditStore: _auditStore);

        var executors = new IToolExecutor[]
        {
            new CreateFileExecutor(_pathPolicy, _authService),
            new CreateFolderExecutor(_pathPolicy, _authService),
            new SearchFilesExecutor(_pathPolicy, _authService),
            new AppLaunchExecutor(_toolPolicy, _authService, new WindowsElevationChecker()),
            new TaskScheduleReminderExecutor(_authService, new WindowsReminderTaskScheduler())
        };

        _stepExecutor = new StepExecutor(_authService, executors, _toolPolicy);
        _stubVerifier = new StubVerifier();
        _queue = new ExecutionQueue();
    }

    [Fact]
    public void StepExecutor_Signs_Resolved_Allowlisted_Image_Path_For_AppLaunch()
    {
        _toolPolicy.TryResolveAllowedExecutablePath("testapp", out var executable).Should().BeTrue();

        var authorization = _stepExecutor.PrepareAuthorization(
            "task-app-path",
            "step-app-path",
            "app.launch",
            new Dictionary<string, object?> { ["app_id"] = "testapp" },
            "medium");

        authorization.Expected["image_path"].Should().Be(Path.GetFullPath(executable!));
    }

    [Fact]
    public async Task Cli_Runs_FsCreateFile_EndToEnd_WithStubVerifier_Escalates_AndFails()
    {
        var targetFile = Path.Combine(_tempRoot, "notes.txt");
        var planner = new StaticPlanner(new PlanStepDto(
            SchemaVersion: 1,
            StepId: "step-1",
            Order: 1,
            Description: "Create notes file",
            DependsOn: new List<string>(),
            ToolCall: new ToolCallDto(1, "fs.create_file", new Dictionary<string, object?> { ["path"] = targetFile, ["content"] = "meeting notes" }),
            VerificationPlan: new VerificationPlanDto(1, new List<CheckDto>(), new List<string>()),
            AdvisoryRisk: "medium"
        ));

        var approval = new TestApprovalHandler();
        var orchestrator = new TaskOrchestrator(planner, _safetyGate, approval, _queue, _stepExecutor, _stubVerifier, _auditStore);

        var result = await orchestrator.RunTaskAsync("create notes file");

        // 1. File was actually created by executor
        File.Exists(targetFile).Should().BeTrue();
        File.ReadAllText(targetFile).Should().Be("meeting notes");

        // 2. Receipt indicates execution success
        result.Receipts.Should().HaveCount(1);
        result.Receipts[0].ReportedSuccess.Should().BeTrue();
        result.Receipts[0].ExitCode.Should().Be(0);

        // 3. Stub verifier returned ESCALATE
        result.VerificationResults.Should().HaveCount(1);
        result.VerificationResults[0].Status.Should().Be("ESCALATE");

        // 4. Invariant: Task must NEVER be Completed without VERIFIED
        result.VerificationResults.Should().OnlyContain(verification => verification.Status != "VERIFIED");
        result.FinalState.Should().NotBe(TaskState.Completed);
        result.FinalState.Should().Be(TaskState.Failed);
        result.StatusMessage.Should().Contain("not verified");
        AssertFailedOnlyForNotVerified(result);
    }

    [Fact]
    public async Task Cli_Runs_FsCreateFolder_EndToEnd_UpTo_StubVerifier()
    {
        var targetFolder = Path.Combine(_tempRoot, "projects_folder");
        var planner = new StaticPlanner(new PlanStepDto(
            SchemaVersion: 1,
            StepId: "step-1",
            Order: 1,
            Description: "Create projects folder",
            DependsOn: new List<string>(),
            ToolCall: new ToolCallDto(1, "fs.create_folder", new Dictionary<string, object?> { ["path"] = targetFolder }),
            VerificationPlan: new VerificationPlanDto(1, new List<CheckDto>(), new List<string>()),
            AdvisoryRisk: "medium"
        ));

        var approval = new TestApprovalHandler();
        var orchestrator = new TaskOrchestrator(planner, _safetyGate, approval, _queue, _stepExecutor, _stubVerifier, _auditStore);

        var result = await orchestrator.RunTaskAsync("create projects folder");

        Directory.Exists(targetFolder).Should().BeTrue();
        result.Receipts[0].ReportedSuccess.Should().BeTrue();
        result.VerificationResults[0].Status.Should().Be("ESCALATE");
        result.FinalState.Should().Be(TaskState.Failed);
        result.StatusMessage.Should().Contain("not verified");
        AssertFailedOnlyForNotVerified(result);
    }

    [Fact]
    public async Task Cli_Runs_FsSearchFiles_EndToEnd_UpTo_StubVerifier()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(_tempRoot, "b.txt"), "world");

        var planner = new StaticPlanner(new PlanStepDto(
            SchemaVersion: 1,
            StepId: "step-1",
            Order: 1,
            Description: "Search text files",
            DependsOn: new List<string>(),
            ToolCall: new ToolCallDto(1, "fs.search_files", new Dictionary<string, object?> { ["pattern"] = "*.txt", ["root_path"] = _tempRoot }),
            VerificationPlan: new VerificationPlanDto(1, new List<CheckDto>(), new List<string>()),
            AdvisoryRisk: "low"
        ));

        var approval = new TestApprovalHandler();
        var orchestrator = new TaskOrchestrator(planner, _safetyGate, approval, _queue, _stepExecutor, _stubVerifier, _auditStore);

        var result = await orchestrator.RunTaskAsync("search text files");

        result.Receipts[0].ReportedSuccess.Should().BeTrue();
        result.Receipts[0].Stdout.Should().Contain("a.txt");
        result.Receipts[0].Stdout.Should().Contain("b.txt");
        result.VerificationResults[0].Status.Should().Be("ESCALATE");
        result.FinalState.Should().Be(TaskState.Failed);
        result.StatusMessage.Should().Contain("not verified");
        AssertFailedOnlyForNotVerified(result);
    }

    [Fact]
    public async Task Cli_Runs_AppLaunch_EndToEnd_UpTo_StubVerifier()
    {
        var helperProcessesBefore = CaptureTestAppProcessIdentities();
        using var processSandbox = new ProcessSandbox();
        var planner = new StaticPlanner(new PlanStepDto(
            SchemaVersion: 1,
            StepId: "step-1",
            Order: 1,
            Description: "Launch test helper",
            DependsOn: new List<string>(),
            ToolCall: new ToolCallDto(1, "app.launch", new Dictionary<string, object?> { ["app_id"] = "testapp" }),
            VerificationPlan: new VerificationPlanDto(1, new List<CheckDto>(), new List<string>()),
            AdvisoryRisk: "medium"
        ));

        var approval = new TestApprovalHandler();
        var orchestrator = new TaskOrchestrator(planner, _safetyGate, approval, _queue, _stepExecutor, _stubVerifier, _auditStore);

        TaskOrchestratorResult result;
        var startedHelperCount = 0;
        try
        {
            result = await orchestrator.RunTaskAsync("launch test helper");
        }
        finally
        {
            startedHelperCount = TrackNewTestAppProcesses(processSandbox, helperProcessesBefore);
        }

        startedHelperCount.Should().Be(1);
        result.Receipts.Should().ContainSingle();
        var isElevated = OperatingSystem.IsWindows() && new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        if (isElevated)
        {
            result.Receipts[0].ReportedSuccess.Should().BeFalse();
            result.Receipts[0].Stderr.Should().Contain("elevated");
        }
        else
        {
            result.Receipts[0].ReportedSuccess.Should().BeTrue();
        }

        result.VerificationResults[0].Status.Should().Be("ESCALATE");
        result.FinalState.Should().Be(TaskState.Failed);
        result.StatusMessage.Should().Contain("not verified");
        AssertFailedOnlyForNotVerified(result);
    }

    [Fact]
    public async Task Cli_Doctor_Command_Executes_Successfully()
    {
        var exitCode = await Program.DoctorCommandAsync();
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Cli_History_Command_Executes_Successfully()
    {
        var exitCode = await Program.HistoryCommandAsync();
        exitCode.Should().Be(0);
    }

    public void Dispose()
    {
        _auditStore.Dispose();
        _queue.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private static HashSet<(int ProcessId, long StartTimeTicks)> CaptureTestAppProcessIdentities()
    {
        var identities = new HashSet<(int ProcessId, long StartTimeTicks)>();
        var processName = Path.GetFileNameWithoutExtension(TestApps.Locate());
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
                identities.Add((process.Id, process.StartTime.ToUniversalTime().Ticks));
        }
        return identities;
    }

    private static int TrackNewTestAppProcesses(
        ProcessSandbox processSandbox,
        IReadOnlySet<(int ProcessId, long StartTimeTicks)> identitiesBefore)
    {
        var processName = Path.GetFileNameWithoutExtension(TestApps.Locate());
        var startedCount = 0;
        foreach (var process in Process.GetProcessesByName(processName))
        {
            var identity = (process.Id, process.StartTime.ToUniversalTime().Ticks);
            if (identitiesBefore.Contains(identity))
            {
                process.Dispose();
                continue;
            }

            processSandbox.Track(process);
            startedCount++;
        }
        return startedCount;
    }

    private void AssertFailedOnlyForNotVerified(TaskOrchestratorResult result)
    {
        var terminalTransition = _auditStore.GetStateTransitions(result.TaskId)
            .Last(transition => string.Equals(transition.ToState, "failed", StringComparison.OrdinalIgnoreCase));
        terminalTransition.Reason.Should().NotBeNull();
        terminalTransition.Reason.Should().Contain("not verified");
        terminalTransition.Reason.Should().NotContain("execution error");
    }

    private class StaticPlanner : IPlanner
    {
        private readonly PlanStepDto _step;

        public StaticPlanner(PlanStepDto step)
        {
            _step = step;
        }

        public Task<PlanResponseDto> CreatePlanAsync(string prompt, string? traceId = null, CancellationToken cancellationToken = default)
        {
            var plan = new PlanResponseDto(
                SchemaVersion: 1,
                TraceId: traceId ?? "test-trace",
                Status: "ok",
                Intent: new IntentDto(1, "intent-1", "direct", _step.ToolCall.ToolId, 1.0, new Dictionary<string, object?>(), new RiskDto("medium", new List<string>()), "rule"),
                Steps: new List<PlanStepDto> { _step },
                Message: "Plan generated successfully."
            );
            return Task.FromResult(plan);
        }
    }

    private sealed class TestApprovalHandler : IApprovalHandler
    {
        public Task<ApprovalDecision> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApprovalDecision(true, Environment.UserName));
    }
}
