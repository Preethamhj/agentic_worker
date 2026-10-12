using Jarvis.Contracts;
using Jarvis.Infrastructure;

namespace Jarvis.Core;

public sealed record TaskOrchestratorResult(
    string TaskId,
    TaskState FinalState,
    IReadOnlyList<ExecutionReceiptDto> Receipts,
    IReadOnlyList<VerificationResultDto> VerificationResults,
    string StatusMessage);

/// <summary>
/// Orchestrates task execution through plan, safety, approval, queue, execute, and verify.
/// Invariants:
/// 1. Every outcome ends in a terminal state (Completed, Failed, Cancelled, Timeout).
/// 2. A crash, timeout, or cancel never leaves a task in a non-terminal state.
/// 3. Completed is reachable only from Verifying with verification status VERIFIED.
/// 4. With StubVerifier (returns ESCALATE), task transitions to Failed and never Completed.
/// </summary>
public sealed class TaskOrchestrator
{
    private readonly IPlanner _planner;
    private readonly ISafetyGate _safetyGate;
    private readonly IApprovalHandler _approvalHandler;
    private readonly ExecutionQueue _queue;
    private readonly IStepExecutor _stepExecutor;
    private readonly IVerifier _verifier;
    private readonly IAuditStore? _auditStore;
    private readonly IClock _clock;

    public TaskOrchestrator(
        IPlanner planner,
        ISafetyGate safetyGate,
        IApprovalHandler approvalHandler,
        ExecutionQueue queue,
        IStepExecutor stepExecutor,
        IVerifier verifier,
        IAuditStore? auditStore = null,
        IClock? clock = null)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _safetyGate = safetyGate ?? throw new ArgumentNullException(nameof(safetyGate));
        _approvalHandler = approvalHandler ?? throw new ArgumentNullException(nameof(approvalHandler));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _stepExecutor = stepExecutor ?? throw new ArgumentNullException(nameof(stepExecutor));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _auditStore = auditStore;
        _clock = clock ?? SystemClock.Instance;
    }

    public async Task<TaskOrchestratorResult> RunTaskAsync(
        string userPrompt,
        string? traceId = null,
        CancellationToken cancellationToken = default)
    {
        var taskId = $"task-{Guid.NewGuid():N}"[..13];
        var stateMachine = new StateMachine(taskId, _clock);

        stateMachine.StateChanged += (_, args) =>
        {
            _auditStore?.RecordStateTransitionAsync(new StateTransitionRecord(
                args.TaskId,
                args.FromState.ToWireString(),
                args.ToState.ToWireString(),
                args.Reason,
                args.Timestamp
            ));
        };

        var receipts = new List<ExecutionReceiptDto>();
        var verifications = new List<VerificationResultDto>();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 1. ContextResolved & Parsed
            stateMachine.Transition(TaskState.ContextResolved, "User context resolved.");
            stateMachine.Transition(TaskState.Parsed, "Prompt parsed.");

            // 2. Planning
            var plan = await _planner.CreatePlanAsync(userPrompt, traceId, cancellationToken).ConfigureAwait(false);
            stateMachine.Transition(TaskState.Planned, $"Plan generated with {plan.Steps.Count} steps.");

            if (plan.Steps.Count == 0)
            {
                stateMachine.Transition(TaskState.Failed, "Plan contains 0 steps.");
                return new TaskOrchestratorResult(taskId, stateMachine.CurrentState, receipts, verifications, "Plan produced no executable steps.");
            }

            // 3. Process Steps
            foreach (var step in plan.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 3a. Safety Gate Evaluation
                var safetyEval = await _safetyGate.EvaluateStepAsync(taskId, step.StepId, step.ToolCall.ToolId, step.ToolCall.Args, cancellationToken).ConfigureAwait(false);
                if (!safetyEval.IsAllowed)
                {
                    stateMachine.Transition(TaskState.Validated, "Validated with safety rejection.");
                    stateMachine.Transition(TaskState.Failed, $"Safety gate rejected step '{step.StepId}': {safetyEval.RejectionReason}");
                    return new TaskOrchestratorResult(taskId, stateMachine.CurrentState, receipts, verifications, $"Safety gate rejected step: {safetyEval.RejectionReason}");
                }

                stateMachine.Transition(TaskState.Validated, $"Step '{step.StepId}' validated. Risk: {safetyEval.Risk}.");

                // 3b. Approval
                stateMachine.Transition(TaskState.AwaitingApproval, $"Step '{step.StepId}' awaiting approval.");
                var approved = await _approvalHandler.RequestApprovalAsync(taskId, step.StepId, step.ToolCall.ToolId, step.ToolCall.Args, safetyEval.Risk, cancellationToken).ConfigureAwait(false);
                if (!approved)
                {
                    stateMachine.Transition(TaskState.Cancelled, $"User rejected approval for step '{step.StepId}'.");
                    return new TaskOrchestratorResult(taskId, stateMachine.CurrentState, receipts, verifications, "User rejected approval.");
                }

                stateMachine.Transition(TaskState.Approved, $"Step '{step.StepId}' approved.");

                // 3c. Queue & Execute
                stateMachine.Transition(TaskState.Queued, $"Step '{step.StepId}' enqueued.");

                var receipt = await _queue.EnqueueAsync(
                    taskId,
                    step.StepId,
                    safetyEval.Risk,
                    async ct =>
                    {
                        stateMachine.Transition(TaskState.Executing, $"Step '{step.StepId}' executing.");
                        return await _stepExecutor.ExecuteStepAsync(taskId, step.StepId, step.ToolCall.ToolId, step.ToolCall.Args, safetyEval.Risk, ct).ConfigureAwait(false);
                    },
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);

                receipts.Add(receipt);

                if (!receipt.ReportedSuccess)
                {
                    stateMachine.Transition(TaskState.Verifying, $"Execution reported failure for step '{step.StepId}'.");
                    stateMachine.Transition(TaskState.Failed, $"Execution failed: {receipt.Stderr}");
                    return new TaskOrchestratorResult(taskId, stateMachine.CurrentState, receipts, verifications, $"Step execution failed: {receipt.Stderr}");
                }

                // 3d. Verification
                stateMachine.Transition(TaskState.Verifying, $"Step '{step.StepId}' verifying.");
                var verification = await _verifier.VerifyAsync(taskId, step.StepId, receipt, cancellationToken).ConfigureAwait(false);
                verifications.Add(verification);

                if (string.Equals(verification.Status, "VERIFIED", StringComparison.OrdinalIgnoreCase))
                {
                    stateMachine.TransitionToCompleted("VERIFIED", $"Step '{step.StepId}' independently verified.");
                }
                else
                {
                    // If StubVerifier returned ESCALATE or status is not VERIFIED:
                    stateMachine.Transition(TaskState.Failed, $"Verification status '{verification.Status}': not verified.");
                    return new TaskOrchestratorResult(taskId, stateMachine.CurrentState, receipts, verifications, $"Verification returned {verification.Status}: not verified.");
                }
            }

            return new TaskOrchestratorResult(taskId, stateMachine.CurrentState, receipts, verifications, "All steps completed successfully.");
        }
        catch (OperationCanceledException)
        {
            EnsureTerminalState(stateMachine, TaskState.Cancelled, "Task was cancelled.");
            return new TaskOrchestratorResult(taskId, stateMachine.CurrentState, receipts, verifications, "Task was cancelled.");
        }
        catch (TimeoutException tex)
        {
            EnsureTerminalState(stateMachine, TaskState.Timeout, tex.Message);
            return new TaskOrchestratorResult(taskId, stateMachine.CurrentState, receipts, verifications, $"Task timed out: {tex.Message}");
        }
        catch (Exception ex)
        {
            EnsureTerminalState(stateMachine, TaskState.Failed, ex.Message);
            return new TaskOrchestratorResult(taskId, stateMachine.CurrentState, receipts, verifications, $"Task execution error: {ex.Message}");
        }
    }

    private static void EnsureTerminalState(StateMachine stateMachine, TaskState terminalState, string reason)
    {
        if (!StateMachine.IsTerminal(stateMachine.CurrentState))
        {
            try
            {
                stateMachine.Transition(terminalState, reason);
            }
            catch
            {
                // Invariant: guarantee terminal state
            }
        }
    }
}
