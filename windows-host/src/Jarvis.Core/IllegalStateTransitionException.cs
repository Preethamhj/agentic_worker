namespace Jarvis.Core;

/// <summary>
/// Thrown when an illegal state transition is attempted on a TaskStateMachine.
/// </summary>
public sealed class IllegalStateTransitionException : InvalidOperationException
{
    public TaskState FromState { get; }
    public TaskState ToState { get; }
    public string? Reason { get; }

    public IllegalStateTransitionException(TaskState fromState, TaskState toState, string? reason = null)
        : base($"Illegal state transition from {fromState} to {toState}.{(reason != null ? " Reason: " + reason : "")}")
    {
        FromState = fromState;
        ToState = toState;
        Reason = reason;
    }
}
