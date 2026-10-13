using Jarvis.Contracts;

namespace Jarvis.Core;

/// <summary>
/// Event arguments representing a state transition.
/// </summary>
public sealed class StateTransitionEventArgs : EventArgs
{
    public string TaskId { get; }
    public TaskState FromState { get; }
    public TaskState ToState { get; }
    public string? Reason { get; }
    public DateTimeOffset Timestamp { get; }

    public StateTransitionEventArgs(string taskId, TaskState fromState, TaskState toState, string? reason, DateTimeOffset timestamp)
    {
        TaskId = taskId;
        FromState = fromState;
        ToState = toState;
        Reason = reason;
        Timestamp = timestamp;
    }
}

/// <summary>
/// Enforces the legal task transition table from DESIGN.md section 8.
/// Invariants:
/// 1. Completed is reachable only from Verifying with verification status VERIFIED.
/// 2. Terminal states (Completed, Failed, Cancelled, Timeout) have zero exits.
/// 3. Every illegal transition throws IllegalStateTransitionException.
/// </summary>
public sealed class StateMachine
{
    private static readonly Dictionary<TaskState, TaskState> LegalLinearTransitions = new()
    {
        [TaskState.Created] = TaskState.ContextResolved,
        [TaskState.ContextResolved] = TaskState.Parsed,
        [TaskState.Parsed] = TaskState.Planned,
        [TaskState.Planned] = TaskState.Validated,
        [TaskState.Validated] = TaskState.AwaitingApproval,
        [TaskState.AwaitingApproval] = TaskState.Approved,
        [TaskState.Approved] = TaskState.Queued,
        [TaskState.Queued] = TaskState.Executing,
        [TaskState.Executing] = TaskState.Verifying
    };

    private static readonly HashSet<TaskState> TerminalStates = new()
    {
        TaskState.Completed,
        TaskState.Failed,
        TaskState.Cancelled,
        TaskState.Timeout
    };

    private readonly object _lock = new();

    public string TaskId { get; }
    public IClock Clock { get; }
    public TaskState CurrentState { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public event EventHandler<StateTransitionEventArgs>? StateChanged;

    public StateMachine(string taskId, IClock? clock = null)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("Task ID cannot be null or whitespace.", nameof(taskId));

        TaskId = taskId;
        Clock = clock ?? SystemClock.Instance;
        CurrentState = TaskState.Created;
        CreatedAt = Clock.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public static bool IsTerminal(TaskState state) => TerminalStates.Contains(state);

    /// <summary>
    /// Executes a state transition to a target state with an optional reason.
    /// If target state is Completed, verificationStatus must be provided and must be 'VERIFIED'.
    /// </summary>
    public void Transition(TaskState targetState, string? reason = null, string? verificationStatus = null)
    {
        lock (_lock)
        {
            if (IsTerminal(CurrentState))
            {
                throw new IllegalStateTransitionException(
                    CurrentState,
                    targetState,
                    $"Terminal state '{CurrentState}' has zero exits.");
            }

            if (targetState == TaskState.Completed)
            {
                if (CurrentState != TaskState.Verifying)
                {
                    throw new IllegalStateTransitionException(
                        CurrentState,
                        targetState,
                        "Completed is reachable only from 'Verifying'.");
                }

                if (string.IsNullOrWhiteSpace(verificationStatus) || !string.Equals(verificationStatus, "VERIFIED", StringComparison.OrdinalIgnoreCase))
                {
                    throw new IllegalStateTransitionException(
                        CurrentState,
                        targetState,
                        $"Completed requires verification status 'VERIFIED', but received '{verificationStatus}'.");
                }

                ApplyTransition(targetState, reason);
                return;
            }

            // Exits from any non-terminal state to Failed, Cancelled, Timeout are legal
            if (targetState is TaskState.Failed or TaskState.Cancelled or TaskState.Timeout)
            {
                ApplyTransition(targetState, reason);
                return;
            }

            // Linear happy-path progression
            if (LegalLinearTransitions.TryGetValue(CurrentState, out var nextLegalState) && nextLegalState == targetState)
            {
                ApplyTransition(targetState, reason);
                return;
            }

            throw new IllegalStateTransitionException(
                CurrentState,
                targetState,
                $"Transition from '{CurrentState}' to '{targetState}' is not permitted by DESIGN.md legal transition table.");
        }
    }

    /// <summary>
    /// Convenience method to transition to Completed with verification status.
    /// </summary>
    public void TransitionToCompleted(string verificationStatus, string? reason = null)
    {
        Transition(TaskState.Completed, reason, verificationStatus);
    }

    private void ApplyTransition(TaskState targetState, string? reason)
    {
        var fromState = CurrentState;
        CurrentState = targetState;
        UpdatedAt = Clock.UtcNow;

        StateChanged?.Invoke(this, new StateTransitionEventArgs(TaskId, fromState, targetState, reason, UpdatedAt));
    }
}
