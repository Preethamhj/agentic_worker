namespace Jarvis.Infrastructure;

/// <summary>
/// Audit record for a state machine transition.
/// </summary>
public sealed record StateTransitionRecord(
    string TaskId,
    string FromState,
    string ToState,
    string? Reason,
    DateTimeOffset Timestamp
);

/// <summary>
/// Audit record for an evaluated safety gate decision.
/// </summary>
public sealed record SafetyDecisionRecord(
    string TaskId,
    string StepId,
    string ToolId,
    string AdvisoryRisk,
    string PolicyRisk,
    string FinalRisk,
    string ApprovedBy,
    bool Approved,
    string? Reason,
    DateTimeOffset Timestamp
);

/// <summary>
/// Storage interface for system audit records.
/// </summary>
public interface IAuditStore
{
    Task RecordStateTransitionAsync(StateTransitionRecord record, CancellationToken cancellationToken = default);
    Task RecordSafetyDecisionAsync(SafetyDecisionRecord record, CancellationToken cancellationToken = default);
    IReadOnlyList<StateTransitionRecord> GetStateTransitions(string? taskId = null);
    IReadOnlyList<SafetyDecisionRecord> GetSafetyDecisions(string? taskId = null);
}
