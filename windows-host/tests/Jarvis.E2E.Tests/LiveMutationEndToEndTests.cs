using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Jarvis.Cli;
using Jarvis.Composition;
using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Jarvis.Tools;
using Jarvis.Verification;
using Xunit;

namespace Jarvis.E2E.Tests;

public sealed class LiveMutationEndToEndTests
{
    [LiveFact]
    [Trait("Category", "Live")]
    public async Task FileAndFolderPrompts_RespectApprovalPathsAndAuditContracts()
    {
        var repoRoot = JarvisSessionFactory.FindRepositoryRoot(AppContext.BaseDirectory);
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "config", "settings.json")));
        var model = settings.RootElement.GetProperty("model").GetString();
        model.Should().NotBeNullOrWhiteSpace();

        using var liveScope = new LiveTestScope();
        var liveRoot = liveScope.RootPath;
        var workspace = liveScope.WorkspacePath;
        var outsidePath = Path.Combine(
            Path.GetPathRoot(workspace)!,
            "JarvisLiveOutside",
            Guid.NewGuid().ToString("N"),
            "blocked.txt");

        await using var server = await LiveTestAiServer.StartAsync(repoRoot, liveRoot);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
        var sessionId = $"live-{Guid.NewGuid():N}";
        var apiClient = new ApiClient(
            httpClient,
            $"http://127.0.0.1:{server.Port}",
            server.Token,
            planTimeout: TimeSpan.FromSeconds(90));
        using var auditStore = new SqliteAuditStore(Path.Combine(liveRoot, "audit.db"));
        var authorizationService = new AuthorizationService(
            new InMemoryDpapiKeyStore(),
            SystemClock.Instance,
            auditStore);
        var pathPolicy = new PathPolicy(new[] { workspace });
        var configDirectory = Path.Combine(repoRoot, "config");
        var toolPolicy = ToolPolicy.FromFile(
            Path.Combine(configDirectory, "tools.registry.json"),
            Path.Combine(configDirectory, "apps.allowlist.json"));
        var safetyGate = new SafetyGate(
            toolPolicy,
            authorizationService,
            auditStore: auditStore,
            pathPolicy: pathPolicy);
        var stepExecutor = new StepExecutor(
            authorizationService,
            new IToolExecutor[]
            {
                new CreateFileExecutor(pathPolicy, authorizationService),
                new CreateFolderExecutor(pathPolicy, authorizationService)
            },
            toolPolicy);
        await using var queue = new ExecutionQueue();
        var stopwatch = Stopwatch.StartNew();

        async Task<ScenarioOutcome> RunScenarioAsync(
            string prompt,
            Func<ApprovalRequest, ApprovalDecision> decide)
        {
            var planner = new RecordingPlanner(new ApiClientPlanner(apiClient, sessionId));
            var approval = new ScenarioApprovalHandler(decide);
            var orchestrator = new TaskOrchestrator(
                planner,
                safetyGate,
                approval,
                queue,
                stepExecutor,
                new VerificationEngine(authorizationService, auditStore),
                auditStore);
            var result = await orchestrator.RunTaskAsync(prompt);
            var report = Program.CreateReport(sessionId, result);
            var response = await apiClient.SendReportAsync(report);
            return new ScenarioOutcome(result, planner.Response, approval.Requests, report, response);
        }

        const string exactContent = "Approved content: ಕನ್ನಡ ನಮಸ್ಕಾರ";
        var approvedFile = Path.Combine(workspace, "approved.txt");
        var fileOutcome = await RunScenarioAsync(
            $"Please create a file at the exact location '{approvedFile}' containing exactly '{exactContent}'.",
            _ => new ApprovalDecision(true, "live-test-approval"));
        AssertPlannedTool(fileOutcome, "fs.create_file");
        AssertVerifiedCompletion(fileOutcome);
        File.ReadAllText(approvedFile).Should().Be(exactContent);
        AssertAuditEvents(auditStore, fileOutcome.Result.TaskId, valid: true, approved: true, approver: "live-test-approval");

        var approvedFolder = Path.Combine(workspace, "approved-folder");
        var folderOutcome = await RunScenarioAsync(
            $"Please create a directory at the exact location '{approvedFolder}'.",
            _ => new ApprovalDecision(true, "live-test-approval"));
        AssertPlannedTool(folderOutcome, "fs.create_folder");
        AssertVerifiedCompletion(folderOutcome);
        Directory.Exists(approvedFolder).Should().BeTrue();
        AssertAuditEvents(auditStore, folderOutcome.Result.TaskId, valid: true, approved: true, approver: "live-test-approval");

        var deniedFile = Path.Combine(workspace, "denied.txt");
        var denialOutcome = await RunScenarioAsync(
            $"Please create a file at the exact location '{deniedFile}' containing 'must not be written'.",
            _ => new ApprovalDecision(false, "user", "Denied by live test."));
        AssertPlannedTool(denialOutcome, "fs.create_file");
        denialOutcome.Result.FinalState.Should().Be(TaskState.Cancelled);
        File.Exists(deniedFile).Should().BeFalse();
        AssertAuditEvents(auditStore, denialOutcome.Result.TaskId, valid: true, approved: false, approver: "user");

        var timedOutFolder = Path.Combine(workspace, "timed-out-folder");
        var timeoutOutcome = await RunScenarioAsync(
            $"Please create a directory at the exact location '{timedOutFolder}'.",
            _ => new ApprovalDecision(false, "timeout", "Approval timed out."));
        AssertPlannedTool(timeoutOutcome, "fs.create_folder");
        timeoutOutcome.Result.FinalState.Should().Be(TaskState.Timeout);
        Directory.Exists(timedOutFolder).Should().BeFalse();
        AssertAuditEvents(auditStore, timeoutOutcome.Result.TaskId, valid: true, approved: false, approver: "timeout");

        var kannadaFile = Path.Combine(workspace, "ನಮಸ್ಕಾರ.txt");
        const string kannadaContent = "ಕನ್ನಡ ಪಠ್ಯ";
        var kannadaOutcome = await RunScenarioAsync(
            $"Please write a file at the exact location '{kannadaFile}' containing exactly '{kannadaContent}'.",
            _ => new ApprovalDecision(true, "live-test-approval"));
        AssertPlannedTool(kannadaOutcome, "fs.create_file");
        AssertVerifiedCompletion(kannadaOutcome);
        File.ReadAllText(kannadaFile).Should().Be(kannadaContent);
        AssertAuditEvents(auditStore, kannadaOutcome.Result.TaskId, valid: true, approved: true, approver: "live-test-approval");

        var existingFile = Path.Combine(workspace, "existing.txt");
        const string existingContent = "do not overwrite";
        File.WriteAllText(existingFile, existingContent);
        var existingOutcome = await RunScenarioAsync(
            $"Please create a file at the exact location '{existingFile}' containing 'replacement text'.",
            _ => new ApprovalDecision(true, "live-test-approval"));
        AssertPlannedTool(existingOutcome, "fs.create_file");
        File.ReadAllText(existingFile).Should().Be(existingContent);
        existingOutcome.Result.FinalState.Should().NotBe(TaskState.Completed);
        AssertAuditEvents(auditStore, existingOutcome.Result.TaskId, valid: true, approved: true, approver: "live-test-approval");

        var outsideOutcome = await RunScenarioAsync(
            $"Please create a file at the exact location '{outsidePath}' containing 'must remain blocked'.",
            _ => new ApprovalDecision(true, "live-test-approval"));
        AssertPlannedTool(outsideOutcome, "fs.create_file");
        outsideOutcome.Result.FinalState.Should().Be(TaskState.Failed);
        outsideOutcome.Approvals.Should().BeEmpty();
        File.Exists(outsidePath).Should().BeFalse();
        AssertAuditEvents(auditStore, outsideOutcome.Result.TaskId, valid: false, approved: null, approver: null);

        stopwatch.Stop();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            test = nameof(FileAndFolderPrompts_RespectApprovalPathsAndAuditContracts),
            model,
            scenarios = new[]
            {
                fileOutcome, folderOutcome, denialOutcome, timeoutOutcome,
                kannadaOutcome, existingOutcome, outsideOutcome
            }.Select(item => new
            {
                item.Result.FinalState,
                verification = item.Result.VerificationResults.Select(result => result.Status),
                approvals = item.Approvals.Count
            }),
            latency_ms = stopwatch.Elapsed.TotalMilliseconds
        }));
    }

    private static void AssertPlannedTool(ScenarioOutcome outcome, string toolId)
    {
        outcome.Plan.Should().NotBeNull();
        outcome.Plan!.Intent.Source.Should().Be("llm");
        outcome.Plan.Steps.Should().ContainSingle(step => step.ToolCall.ToolId == toolId);
    }

    private static void AssertVerifiedCompletion(ScenarioOutcome outcome)
    {
        outcome.Result.FinalState.Should().Be(TaskState.Completed, outcome.Result.StatusMessage);
        outcome.Result.VerificationResults.Should().ContainSingle(result => result.Status == "VERIFIED");
        outcome.Report.Steps.Should().ContainSingle(step =>
            step.Status == "completed" && step.VerificationStatus == "VERIFIED");
    }

    private static void AssertAuditEvents(
        SqliteAuditStore auditStore,
        string taskId,
        bool valid,
        bool? approved,
        string? approver)
    {
        var validations = auditStore.GetPolicyValidations(taskId);
        validations.Should().ContainSingle(item => item.IsValid == valid);
        var decisions = auditStore.GetApprovalDecisions(taskId);
        if (approved is null)
        {
            decisions.Should().BeEmpty("policy rejection must not be recorded as an approval decision");
            return;
        }

        decisions.Should().ContainSingle(item =>
            item.Approved == approved.Value && item.Approver == approver);
        validations[0].TaskId.Should().Be(decisions[0].TaskId);
        validations[0].StepId.Should().Be(decisions[0].StepId);
    }

    private sealed record ScenarioOutcome(
        TaskOrchestratorResult Result,
        PlanResponseDto? Plan,
        IReadOnlyList<ApprovalRequest> Approvals,
        ReportRequestDto Report,
        ReportResponseDto ReportResponse);

    private sealed class RecordingPlanner(IPlanner inner) : IPlanner
    {
        public PlanResponseDto? Response { get; private set; }

        public async Task<PlanResponseDto> CreatePlanAsync(
            string prompt,
            string? traceId = null,
            CancellationToken cancellationToken = default)
        {
            Response = await inner.CreatePlanAsync(prompt, traceId, cancellationToken);
            return Response;
        }
    }

    private sealed class ScenarioApprovalHandler(
        Func<ApprovalRequest, ApprovalDecision> decide) : IApprovalHandler
    {
        public List<ApprovalRequest> Requests { get; } = [];

        public Task<ApprovalDecision> RequestApprovalAsync(
            ApprovalRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(decide(request));
        }
    }
}
