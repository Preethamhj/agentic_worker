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
/// Audit record for authorization lifecycle transitions (Issued, Consumed, Verified).
/// </summary>
public sealed record AuthorizationLifecycleRecord(
    string AuthId,
    string TaskId,
    string StepId,
    string State,
    string Nonce,
    DateTimeOffset Timestamp
);

/// <summary>
/// Audit record for task definitions.
/// </summary>
public sealed record TaskAuditRecord(
    string TaskId,
    string Prompt,
    string? SessionId,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

/// <summary>
/// Audit record for planned steps.
/// </summary>
public sealed record StepAuditRecord(
    string TaskId,
    string StepId,
    string ToolId,
    int OrderIndex,
    string State,
    string Risk,
    DateTimeOffset CreatedAt
);

/// <summary>
/// Audit record for executed tool receipts.
/// </summary>
public sealed record ExecutionAuditRecord(
    string StepId,
    string AuthId,
    int ExitCode,
    bool ReportedSuccess,
    string? Stdout,
    string? Stderr,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt
);

/// <summary>
/// Audit record for verification stage outcomes.
/// </summary>
public sealed record VerificationAuditRecord(
    string StepId,
    string OverallStatus,
    string? Summary,
    DateTimeOffset VerifiedAt
);

/// <summary>
/// Audit record for collected independent evidence.
/// </summary>
public sealed record EvidenceAuditRecord(
    string EvidenceId,
    string StepId,
    string Collector,
    string CheckType,
    bool Match,
    DateTimeOffset CollectedAt,
    string Nonce,
    string RecordSha256
);

/// <summary>
/// Audit record for external and loopback API calls.
/// </summary>
public sealed record ApiCallAuditRecord(
    string CallId,
    string Endpoint,
    string Method,
    string TraceId,
    int StatusCode,
    double DurationMs,
    DateTimeOffset Timestamp
);

/// <summary>
/// Storage interface for system audit records defined in DESIGN.md section 10.
/// </summary>
public interface IAuditStore
{
    Task RecordStateTransitionAsync(StateTransitionRecord record, CancellationToken cancellationToken = default);
    Task RecordSafetyDecisionAsync(SafetyDecisionRecord record, CancellationToken cancellationToken = default);
    Task RecordAuthorizationLifecycleAsync(AuthorizationLifecycleRecord record, CancellationToken cancellationToken = default);
    Task RecordTaskAsync(TaskAuditRecord record, CancellationToken cancellationToken = default);
    Task RecordStepAsync(StepAuditRecord record, CancellationToken cancellationToken = default);
    Task RecordExecutionAsync(ExecutionAuditRecord record, CancellationToken cancellationToken = default);
    Task RecordVerificationAsync(VerificationAuditRecord record, CancellationToken cancellationToken = default);
    Task RecordEvidenceAsync(EvidenceAuditRecord record, CancellationToken cancellationToken = default);
    Task RecordApiCallAsync(ApiCallAuditRecord record, CancellationToken cancellationToken = default);

    IReadOnlyList<StateTransitionRecord> GetStateTransitions(string? taskId = null);
    IReadOnlyList<SafetyDecisionRecord> GetSafetyDecisions(string? taskId = null);
    IReadOnlyList<AuthorizationLifecycleRecord> GetAuthorizationLifecycle(string? authId = null);
    IReadOnlyList<TaskAuditRecord> GetTasks();
    IReadOnlyList<StepAuditRecord> GetSteps(string? taskId = null);
    IReadOnlyList<ExecutionAuditRecord> GetExecutions(string? stepId = null);
    IReadOnlyList<VerificationAuditRecord> GetVerifications(string? stepId = null);
    IReadOnlyList<EvidenceAuditRecord> GetEvidence(string? stepId = null);
    IReadOnlyList<ApiCallAuditRecord> GetApiCalls(string? traceId = null);
}
