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
/// 4. ESCALATE may only request a fresh verification attempt or fail the task.
/// </summary>
public sealed class TaskOrchestrator
{
    private readonly IPlanner _planner;
    private readonly ISafetyGate _safetyGate;
    private readonly IApprovalHandler _approvalHandler;
    private readonly ExecutionQueue _queue;
    private readonly IStepExecutor _stepExecutor;
    private readonly IVerifier _verifier;
    private readonly IEscalationHandler? _escalationHandler;
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
        IClock? clock = null,
        IEscalationHandler? escalationHandler = null)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _safetyGate = safetyGate ?? throw new ArgumentNullException(nameof(safetyGate));
        _approvalHandler = approvalHandler ?? throw new ArgumentNullException(nameof(approvalHandler));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _stepExecutor = stepExecutor ?? throw new ArgumentNullException(nameof(stepExecutor));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _escalationHandler = escalationHandler;
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
                var canonicalArgs = safetyEval.CanonicalArgs ?? step.ToolCall.Args;

                // 3b. Approval
                stateMachine.Transition(TaskState.AwaitingApproval, $"Step '{step.StepId}' awaiting approval.");
                var approval = await _approvalHandler.RequestApprovalAsync(taskId, step.StepId, step.ToolCall.ToolId, canonicalArgs, safetyEval.Risk, cancellationToken).ConfigureAwait(false);
                if (_auditStore is not null)
                {
                    await _auditStore.RecordApprovalDecisionAsync(new ApprovalDecisionRecord(
                        taskId,
                        step.StepId,
                        approval.Approved,
                        approval.Approver,
                        approval.Reason,
                        _clock.UtcNow), cancellationToken).ConfigureAwait(false);
                }
                if (!approval.Approved)
                {
                    var terminalState = string.Equals(approval.Approver, "timeout", StringComparison.Ordinal)
                        ? TaskState.Timeout
                        : TaskState.Cancelled;
                    stateMachine.Transition(terminalState, approval.Reason ?? $"Approval was not granted for step '{step.StepId}'.");
                    return new TaskOrchestratorResult(taskId, stateMachine.CurrentState, receipts, verifications, approval.Reason ?? "Approval was not granted.");
                }

                stateMachine.Transition(TaskState.Approved, $"Step '{step.StepId}' approved.");

                var authorization = _stepExecutor.PrepareAuthorization(
                    taskId,
                    step.StepId,
                    step.ToolCall.ToolId,
                    canonicalArgs,
                    safetyEval.Risk,
                    approval.Approver);
                var stepToken = await _verifier.BeginStepAsync(authorization, cancellationToken).ConfigureAwait(false);

                // 3c. Queue & Execute
                stateMachine.Transition(TaskState.Queued, $"Step '{step.StepId}' enqueued.");

                var executionStartedAt = default(DateTimeOffset);
                var executionCompletedAt = default(DateTimeOffset);
                var receipt = await _queue.EnqueueAsync(
                    taskId,
                    step.StepId,
                    safetyEval.Risk,
                    async ct =>
                    {
                        stateMachine.Transition(TaskState.Executing, $"Step '{step.StepId}' executing.");
                        executionStartedAt = _clock.UtcNow;
                        try
                        {
                            return await _stepExecutor.ExecuteStepAsync(authorization, ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            executionCompletedAt = _clock.UtcNow;
                        }
                    },
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);

                receipts.Add(receipt);

                // 3d. Verification
                stateMachine.Transition(TaskState.Verifying, $"Step '{step.StepId}' verifying.");
                var observation = new ExecutionObservation(executionStartedAt, executionCompletedAt);
                var verification = await _verifier.VerifyAsync(receipt, observation, stepToken, cancellationToken).ConfigureAwait(false);
                verifications.Add(verification);

                while (string.Equals(verification.Status, "ESCALATE", StringComparison.OrdinalIgnoreCase)
                    && _escalationHandler is not null)
                {
                    var escalation = await _escalationHandler
                        .RequestDecisionAsync(verification, cancellationToken)
                        .ConfigureAwait(false);
                    if (escalation.Action != EscalationAction.RetryVerification)
                    {
                        var reason = escalation.Reason
                            ?? (escalation.Action == EscalationAction.TimedOut
                                ? "Verification escalation timed out."
                                : "Verification escalation was aborted.");
                        stateMachine.Transition(TaskState.Failed, reason);
                        return new TaskOrchestratorResult(
                            taskId,
                            stateMachine.CurrentState,
                            receipts,
                            verifications,
                            $"Verification returned ESCALATE: {reason} (not verified).");
                    }

                    verification = await _verifier
                        .VerifyAsync(receipt, observation, stepToken, cancellationToken)
                        .ConfigureAwait(false);
                    verifications.Add(verification);
                }

                if (string.Equals(verification.Status, "VERIFIED", StringComparison.OrdinalIgnoreCase))
                {
                    stateMachine.TransitionToCompleted("VERIFIED", $"Step '{step.StepId}' independently verified.");
                }
                else
                {
                    var reason = string.Join(" ", verification.Reasons);
                    var detail = string.IsNullOrWhiteSpace(reason) ? "not verified." : reason;
                    stateMachine.Transition(TaskState.Failed, $"Verification status '{verification.Status}': {detail} (not verified).");
                    return new TaskOrchestratorResult(taskId, stateMachine.CurrentState, receipts, verifications, $"Verification returned {verification.Status}: {detail} (not verified).");
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
