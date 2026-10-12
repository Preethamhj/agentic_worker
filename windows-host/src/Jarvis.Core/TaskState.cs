namespace Jarvis.Core;

/// <summary>
/// Lifecycle states for a Jarvis task as defined in DESIGN.md section 8.
/// </summary>
public enum TaskState
{
    Created,
    ContextResolved,
    Parsed,
    Planned,
    Validated,
    AwaitingApproval,
    Approved,
    Queued,
    Executing,
    Verifying,
    Completed,
    Failed,
    Cancelled,
    Timeout
}

/// <summary>
/// Helper extensions for TaskState.
/// </summary>
public static class TaskStateExtensions
{
    public static string ToWireString(this TaskState state) => state switch
    {
        TaskState.Created => "created",
        TaskState.ContextResolved => "context_resolved",
        TaskState.Parsed => "parsed",
        TaskState.Planned => "planned",
        TaskState.Validated => "validated",
        TaskState.AwaitingApproval => "awaiting_approval",
        TaskState.Approved => "approved",
        TaskState.Queued => "queued",
        TaskState.Executing => "executing",
        TaskState.Verifying => "verifying",
        TaskState.Completed => "completed",
        TaskState.Failed => "failed",
        TaskState.Cancelled => "cancelled",
        TaskState.Timeout => "timeout",
        _ => state.ToString().ToLowerInvariant()
    };
}
