using System.Collections.Concurrent;
using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Core;
using Xunit;

namespace Jarvis.Core.Tests;

public class OrchestratorAndQueueTests
{
    [Fact]
    public async Task ExecutionQueue_Processes_Items_Sequentially_In_Fifo_Order()
    {
        await using var queue = new ExecutionQueue();
        var executionOrder = new List<int>();

        var task1 = queue.EnqueueAsync("task-1", "step-1", "low", async ct =>
        {
            await Task.Delay(50, ct);
            executionOrder.Add(1);
            return CreateDummyReceipt("step-1");
        });

        var task2 = queue.EnqueueAsync("task-2", "step-2", "low", async ct =>
        {
            await Task.Delay(10, ct);
            executionOrder.Add(2);
            return CreateDummyReceipt("step-2");
        });

        var task3 = queue.EnqueueAsync("task-3", "step-3", "low", async ct =>
        {
            executionOrder.Add(3);
            return CreateDummyReceipt("step-3");
        });

        await Task.WhenAll(task1, task2, task3);

        executionOrder.Should().ContainInOrder(1, 2, 3);
    }

    [Fact]
    public async Task ExecutionQueue_HighRisk_Enforces_Single_Active_Lock()
    {
        await using var queue = new ExecutionQueue();
        int activeHighRiskCount = 0;
        int maxSimultaneousHighRisk = 0;

        async Task<ExecutionReceiptDto> HighRiskWork(CancellationToken ct)
        {
            var current = Interlocked.Increment(ref activeHighRiskCount);
            if (current > maxSimultaneousHighRisk)
            {
                maxSimultaneousHighRisk = current;
            }
            await Task.Delay(60, ct);
            Interlocked.Decrement(ref activeHighRiskCount);
            return CreateDummyReceipt("high-risk-step");
        }

        var t1 = queue.EnqueueAsync("task-1", "step-1", "high", HighRiskWork);
        var t2 = queue.EnqueueAsync("task-2", "step-2", "high", HighRiskWork);

        await Task.WhenAll(t1, t2);

        maxSimultaneousHighRisk.Should().Be(1, "At most one high-risk execution must be active at any time");
    }

    [Fact]
    public async Task ExecutionQueue_Throws_TimeoutException_On_Step_Timeout()
    {
        await using var queue = new ExecutionQueue();

        var act = async () =>
        {
            await queue.EnqueueAsync("task-1", "step-1", "low", async ct =>
            {
                await Task.Delay(2000, ct);
                return CreateDummyReceipt("step-1");
            }, stepTimeout: TimeSpan.FromMilliseconds(50));
        };

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task ExecutionQueue_Cancels_When_CancellationToken_Triggered()
    {
        await using var queue = new ExecutionQueue();
        using var cts = new CancellationTokenSource();

        var act = async () =>
        {
            var task = queue.EnqueueAsync("task-1", "step-1", "low", async ct =>
            {
                await Task.Delay(1000, ct);
                return CreateDummyReceipt("step-1");
            }, cancellationToken: cts.Token);

            cts.Cancel();
            await task;
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TaskOrchestrator_Never_Reaches_Completed_With_StubVerifier()
    {
        var stubVerifier = new StubVerifier();
        var orchestrator = CreateTestOrchestrator(stubVerifier);

        var result = await orchestrator.RunTaskAsync("create a test file");

        result.FinalState.Should().NotBe(TaskState.Completed, "StubVerifier returns ESCALATE so completed must be unreachable");
        result.FinalState.Should().Be(TaskState.Failed);
        result.StatusMessage.Should().Contain("ESCALATE");
    }

    [Fact]
    public async Task TaskOrchestrator_Retry_Escalation_Reverifies_Without_Reexecution()
    {
        var verifier = new SequenceVerifier("ESCALATE", "VERIFIED");
        var escalation = new FakeEscalationHandler(
            new EscalationDecision(EscalationAction.RetryVerification));
        var executor = new FakeStepExecutor();
        var orchestrator = CreateTestOrchestrator(verifier, stepExecutor: executor, escalationHandler: escalation);

        var result = await orchestrator.RunTaskAsync("create a test file");

        result.FinalState.Should().Be(TaskState.Completed);
        verifier.VerifyCallCount.Should().Be(2);
        executor.ExecuteCallCount.Should().Be(1);
        result.VerificationResults.Select(item => item.Status).Should().ContainInOrder("ESCALATE", "VERIFIED");
    }

    [Fact]
    public async Task TaskOrchestrator_Aborting_Escalation_Fails()
    {
        var verifier = new SequenceVerifier("ESCALATE");
        var escalation = new FakeEscalationHandler(new EscalationDecision(EscalationAction.Abort, "User aborted."));
        var orchestrator = CreateTestOrchestrator(verifier, escalationHandler: escalation);

        var result = await orchestrator.RunTaskAsync("create a test file");

        result.FinalState.Should().Be(TaskState.Failed);
        result.FinalState.Should().NotBe(TaskState.Completed);
        result.StatusMessage.Should().Contain("User aborted.");
    }

    [Fact]
    public async Task TaskOrchestrator_Escalation_Timeout_Fails()
    {
        var verifier = new SequenceVerifier("ESCALATE");
        var escalation = new FakeEscalationHandler(new EscalationDecision(EscalationAction.TimedOut, "Escalation timed out."));
        var orchestrator = CreateTestOrchestrator(verifier, escalationHandler: escalation);

        var result = await orchestrator.RunTaskAsync("create a test file");

        result.FinalState.Should().Be(TaskState.Failed);
        result.StatusMessage.Should().Contain("Escalation timed out.");
    }

    [Fact]
    public async Task TaskOrchestrator_Retrying_Escalation_Never_Converts_Escalate_To_Completed()
    {
        var verifier = new SequenceVerifier("ESCALATE", "ESCALATE");
        var escalation = new FakeEscalationHandler(
            new EscalationDecision(EscalationAction.RetryVerification),
            new EscalationDecision(EscalationAction.Abort, "Verification remains uncertain."));
        var orchestrator = CreateTestOrchestrator(verifier, escalationHandler: escalation);

        var result = await orchestrator.RunTaskAsync("create a test file");

        result.FinalState.Should().Be(TaskState.Failed);
        result.FinalState.Should().NotBe(TaskState.Completed);
        verifier.VerifyCallCount.Should().Be(2);
    }

    [Fact]
    public async Task TaskOrchestrator_Uses_Host_Observation_And_Does_Not_Trust_Receipt_Success_Claims()
    {
        var startedAt = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
        var clock = new ManualClock(startedAt);
        var verifier = new RecordingVerifiedVerifier();
        var orchestrator = CreateTestOrchestrator(
            verifier,
            stepExecutor: new FakeStepExecutor(clock: clock, receiptReportsSuccess: false),
            clock: clock);

        var result = await orchestrator.RunTaskAsync("create a test file");

        result.FinalState.Should().Be(TaskState.Completed);
        verifier.Observation.Should().Be(new ExecutionObservation(startedAt, startedAt.AddMilliseconds(125)));
    }

    [Fact]
    public async Task TaskOrchestrator_Reaches_Terminal_Failed_When_Safety_Rejects()
    {
        var stubVerifier = new StubVerifier();
        var orchestrator = CreateTestOrchestrator(
            stubVerifier,
            safetyGate: new FakeSafetyGate(allowed: false, risk: "high", reason: "Blocked path outside allowed root"));

        var result = await orchestrator.RunTaskAsync("delete system folder");

        result.FinalState.Should().Be(TaskState.Failed);
        StateMachine.IsTerminal(result.FinalState).Should().BeTrue();
        result.StatusMessage.Should().Contain("Blocked path");
    }

    [Fact]
    public async Task TaskOrchestrator_Reaches_Terminal_Cancelled_When_User_Rejects_Approval()
    {
        var stubVerifier = new StubVerifier();
        var orchestrator = CreateTestOrchestrator(
            stubVerifier,
            approvalHandler: new FakeApprovalHandler(userApproved: false));

        var result = await orchestrator.RunTaskAsync("execute sensitive action");

        result.FinalState.Should().Be(TaskState.Cancelled);
        StateMachine.IsTerminal(result.FinalState).Should().BeTrue();
    }

    [Fact]
    public async Task TaskOrchestrator_Reaches_Terminal_Timeout_When_Execution_Times_Out()
    {
        var stubVerifier = new StubVerifier();
        var orchestrator = CreateTestOrchestrator(
            stubVerifier,
            stepExecutor: new FakeStepExecutor(shouldTimeout: true));

        var result = await orchestrator.RunTaskAsync("slow task");

        result.FinalState.Should().Be(TaskState.Timeout);
        StateMachine.IsTerminal(result.FinalState).Should().BeTrue();
    }

    private static ExecutionReceiptDto CreateDummyReceipt(string stepId) => new(
        SchemaVersion: 1,
        StepId: stepId,
        AuthId: "auth-dummy",
        StartedAt: DateTimeOffset.UtcNow.ToString("o"),
        CompletedAt: DateTimeOffset.UtcNow.ToString("o"),
        ExitCode: 0,
        ReportedSuccess: true,
        Stdout: "ok",
        Stderr: string.Empty
    );

    private static TaskOrchestrator CreateTestOrchestrator(
        IVerifier verifier,
        ISafetyGate? safetyGate = null,
        IApprovalHandler? approvalHandler = null,
        IStepExecutor? stepExecutor = null,
        IClock? clock = null,
        IEscalationHandler? escalationHandler = null)
    {
        var queue = new ExecutionQueue();
        var planner = new FakePlanner();
        var safety = safetyGate ?? new FakeSafetyGate(allowed: true, risk: "medium");
        var approval = approvalHandler ?? new FakeApprovalHandler(userApproved: true);
        var executor = stepExecutor ?? new FakeStepExecutor();

        return new TaskOrchestrator(
            planner,
            safety,
            approval,
            queue,
            executor,
            verifier,
            clock: clock,
            escalationHandler: escalationHandler);
    }

    private class FakePlanner : IPlanner
    {
        public Task<PlanResponseDto> CreatePlanAsync(string prompt, string? traceId = null, CancellationToken cancellationToken = default)
        {
            var plan = new PlanResponseDto(
                SchemaVersion: 1,
                TraceId: "trace-" + Guid.NewGuid().ToString("N")[..8],
                Status: "ok",
                Intent: new IntentDto(
                    SchemaVersion: 1,
                    IntentId: "intent-1",
                    Kind: "direct",
                    Action: "create_file",
                    Confidence: 1.0,
                    Entities: new Dictionary<string, object?>(),
                    Risk: new RiskDto("medium", new List<string>()),
                    Source: "rule"
                ),
                Steps: new List<PlanStepDto>
                {
                    new(
                        SchemaVersion: 1,
                        StepId: "step-1",
                        Order: 1,
                        Description: "Create requested file",
                        DependsOn: new List<string>(),
                        ToolCall: new ToolCallDto(1, "fs.create_file", new Dictionary<string, object?> { ["path"] = @"C:\allowed\test.txt", ["content"] = "hello" }),
                        VerificationPlan: new VerificationPlanDto(1, new List<CheckDto>(), new List<string>()),
                        AdvisoryRisk: "medium"
                    )
                },
                Message: "Plan generated successfully."
            );
            return Task.FromResult(plan);
        }
    }

    private class FakeSafetyGate : ISafetyGate
    {
        private readonly bool _allowed;
        private readonly string _risk;
        private readonly string? _reason;

        public FakeSafetyGate(bool allowed, string risk, string? reason = null)
        {
            _allowed = allowed;
            _risk = risk;
            _reason = reason;
        }

        public Task<SafetyEvaluation> EvaluateStepAsync(string taskId, string stepId, string toolId, IReadOnlyDictionary<string, object?> rawArgs, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SafetyEvaluation(_allowed, _risk, _reason));
        }
    }

    private class FakeApprovalHandler : IApprovalHandler
    {
        private readonly bool _userApproved;

        public FakeApprovalHandler(bool userApproved)
        {
            _userApproved = userApproved;
        }

        public Task<ApprovalDecision> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ApprovalDecision(
                _userApproved,
                Environment.UserName,
                _userApproved ? null : "User rejected approval."));
        }
    }

    private class FakeStepExecutor : IStepExecutor
    {
        private readonly bool _shouldTimeout;
        private readonly ManualClock? _clock;
        private readonly bool _receiptReportsSuccess;
        public int ExecuteCallCount { get; private set; }

        public FakeStepExecutor(bool shouldTimeout = false, ManualClock? clock = null, bool receiptReportsSuccess = true)
        {
            _shouldTimeout = shouldTimeout;
            _clock = clock;
            _receiptReportsSuccess = receiptReportsSuccess;
        }

        public AuthorizationRecordDto PrepareAuthorization(
            string taskId,
            string stepId,
            string toolId,
            IReadOnlyDictionary<string, object?> args,
            string risk,
            string approvedBy = "user") =>
            new(
                1,
                "auth-dummy",
                taskId,
                stepId,
                toolId,
                new Dictionary<string, object?>(args),
                new Dictionary<string, object?>(),
                new List<string>(),
                new List<string>(),
                risk,
                approvedBy,
                DateTimeOffset.UtcNow.ToString("o"),
                DateTimeOffset.UtcNow.AddMinutes(1).ToString("o"),
                DateTimeOffset.UtcNow.AddMinutes(1).ToString("o"),
                DateTimeOffset.UtcNow.AddMinutes(2).ToString("o"),
                "nonce-dummy",
                "hmac-dummy");

        public async Task<ExecutionReceiptDto> ExecuteStepAsync(
            AuthorizationRecordDto authorization,
            CancellationToken cancellationToken = default)
        {
            ExecuteCallCount++;
            if (_shouldTimeout)
            {
                throw new TimeoutException("Step timed out after 50ms");
            }

            await Task.Yield();
            if (_clock != null)
                _clock.UtcNow = _clock.UtcNow.AddMilliseconds(125);
            return CreateDummyReceipt(authorization.StepId) with
            {
                AuthId = authorization.AuthId,
                StartedAt = DateTimeOffset.MaxValue.ToString("o"),
                CompletedAt = DateTimeOffset.MaxValue.ToString("o"),
                ReportedSuccess = _receiptReportsSuccess
            };
        }
    }

    private sealed class SequenceVerifier(params string[] statuses) : IVerifier
    {
        private readonly Queue<string> _statuses = new(statuses);

        public int VerifyCallCount { get; private set; }

        public Task<StepToken> BeginStepAsync(AuthorizationRecordDto authorization, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StepToken(authorization, authorization.Nonce, Array.Empty<SnapshotEntryDto>()));

        public Task<VerificationResultDto> VerifyAsync(
            ExecutionReceiptDto receipt,
            ExecutionObservation observation,
            StepToken token,
            CancellationToken cancellationToken = default)
        {
            VerifyCallCount++;
            var status = _statuses.Dequeue();
            return Task.FromResult(new VerificationResultDto(
                1,
                receipt.StepId,
                status,
                new List<VerificationStageResultDto>(),
                new List<EvidenceDto>(),
                new List<string>(),
                new List<string> { status == "ESCALATE" ? "Evidence is inconclusive." : "Evidence verified." },
                0));
        }
    }

    private sealed class FakeEscalationHandler(params EscalationDecision[] decisions) : IEscalationHandler
    {
        private readonly Queue<EscalationDecision> _decisions = new(decisions);

        public Task<EscalationDecision> RequestDecisionAsync(
            VerificationResultDto verification,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_decisions.Dequeue());
    }

    private sealed class ManualClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class RecordingVerifiedVerifier : IVerifier
    {
        public ExecutionObservation? Observation { get; private set; }

        public Task<StepToken> BeginStepAsync(AuthorizationRecordDto authorization, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StepToken(authorization, authorization.Nonce, Array.Empty<SnapshotEntryDto>()));

        public Task<VerificationResultDto> VerifyAsync(
            ExecutionReceiptDto receipt,
            ExecutionObservation observation,
            StepToken token,
            CancellationToken cancellationToken = default)
        {
            Observation = observation;
            return Task.FromResult(new VerificationResultDto(
                1,
                receipt.StepId,
                "VERIFIED",
                new List<VerificationStageResultDto>(),
                new List<EvidenceDto>(),
                new List<string>(),
                new List<string>(),
                0));
        }
    }
}
