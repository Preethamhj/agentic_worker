namespace Jarvis.Infrastructure;

/// <summary>
/// In-memory test double for IAuditStore.
/// </summary>
public sealed class InMemoryAuditStore : IAuditStore
{
    private readonly List<StateTransitionRecord> _stateTransitions = new();
    private readonly List<SafetyDecisionRecord> _safetyDecisions = new();
    private readonly List<PolicyValidationRecord> _policyValidations = new();
    private readonly List<ApprovalDecisionRecord> _approvalDecisions = new();
    private readonly List<AuthorizationLifecycleRecord> _authorizationLifecycles = new();
    private readonly object _lock = new();

    private readonly List<TaskAuditRecord> _tasks = new();
    private readonly List<StepAuditRecord> _steps = new();
    private readonly List<ExecutionAuditRecord> _executions = new();
    private readonly List<VerificationAuditRecord> _verifications = new();
    private readonly List<EvidenceAuditRecord> _evidence = new();
    private readonly List<ApiCallAuditRecord> _apiCalls = new();

    public Task RecordStateTransitionAsync(StateTransitionRecord record, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _stateTransitions.Add(record);
        }
        return Task.CompletedTask;
    }

    public Task RecordSafetyDecisionAsync(SafetyDecisionRecord record, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _safetyDecisions.Add(record);
        }
        return Task.CompletedTask;
    }

    public Task RecordPolicyValidationAsync(PolicyValidationRecord record, CancellationToken cancellationToken = default)
    {
        lock (_lock)
            _policyValidations.Add(record);
        return Task.CompletedTask;
    }

    public Task RecordApprovalDecisionAsync(ApprovalDecisionRecord record, CancellationToken cancellationToken = default)
    {
        lock (_lock)
            _approvalDecisions.Add(record);
        return Task.CompletedTask;
    }

    public Task RecordAuthorizationLifecycleAsync(AuthorizationLifecycleRecord record, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _authorizationLifecycles.Add(record);
        }
        return Task.CompletedTask;
    }

    public Task RecordTaskAsync(TaskAuditRecord record, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _tasks.Add(record);
        }
        return Task.CompletedTask;
    }

    public Task RecordStepAsync(StepAuditRecord record, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _steps.Add(record);
        }
        return Task.CompletedTask;
    }

    public Task RecordExecutionAsync(ExecutionAuditRecord record, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _executions.Add(record);
        }
        return Task.CompletedTask;
    }

    public Task RecordVerificationAsync(VerificationAuditRecord record, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _verifications.Add(record);
        }
        return Task.CompletedTask;
    }

    public Task RecordEvidenceAsync(EvidenceAuditRecord record, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _evidence.Add(record);
        }
        return Task.CompletedTask;
    }

    public Task RecordApiCallAsync(ApiCallAuditRecord record, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _apiCalls.Add(record);
        }
        return Task.CompletedTask;
    }

    public IReadOnlyList<StateTransitionRecord> GetStateTransitions(string? taskId = null)
    {
        lock (_lock)
        {
            return taskId == null
                ? _stateTransitions.ToList()
                : _stateTransitions.Where(r => r.TaskId == taskId).ToList();
        }
    }

    public IReadOnlyList<SafetyDecisionRecord> GetSafetyDecisions(string? taskId = null)
    {
        lock (_lock)
        {
            return taskId == null
                ? _safetyDecisions.ToList()
                : _safetyDecisions.Where(r => r.TaskId == taskId).ToList();
        }
    }

    public IReadOnlyList<PolicyValidationRecord> GetPolicyValidations(string? taskId = null)
    {
        lock (_lock)
            return taskId is null ? _policyValidations.ToList() : _policyValidations.Where(r => r.TaskId == taskId).ToList();
    }

    public IReadOnlyList<ApprovalDecisionRecord> GetApprovalDecisions(string? taskId = null)
    {
        lock (_lock)
            return taskId is null ? _approvalDecisions.ToList() : _approvalDecisions.Where(r => r.TaskId == taskId).ToList();
    }

    public IReadOnlyList<AuthorizationLifecycleRecord> GetAuthorizationLifecycle(string? authId = null)
    {
        lock (_lock)
        {
            return authId == null
                ? _authorizationLifecycles.ToList()
                : _authorizationLifecycles.Where(r => r.AuthId == authId).ToList();
        }
    }

    public IReadOnlyList<TaskAuditRecord> GetTasks()
    {
        lock (_lock)
        {
            return _tasks.ToList();
        }
    }

    public IReadOnlyList<StepAuditRecord> GetSteps(string? taskId = null)
    {
        lock (_lock)
        {
            return taskId == null
                ? _steps.ToList()
                : _steps.Where(s => s.TaskId == taskId).ToList();
        }
    }

    public IReadOnlyList<ExecutionAuditRecord> GetExecutions(string? stepId = null)
    {
        lock (_lock)
        {
            return stepId == null
                ? _executions.ToList()
                : _executions.Where(e => e.StepId == stepId).ToList();
        }
    }

    public IReadOnlyList<VerificationAuditRecord> GetVerifications(string? stepId = null)
    {
        lock (_lock)
        {
            return stepId == null
                ? _verifications.ToList()
                : _verifications.Where(v => v.StepId == stepId).ToList();
        }
    }

    public IReadOnlyList<EvidenceAuditRecord> GetEvidence(string? stepId = null)
    {
        lock (_lock)
        {
            return stepId == null
                ? _evidence.ToList()
                : _evidence.Where(e => e.StepId == stepId).ToList();
        }
    }

    public IReadOnlyList<ApiCallAuditRecord> GetApiCalls(string? traceId = null)
    {
        lock (_lock)
        {
            return traceId == null
                ? _apiCalls.ToList()
                : _apiCalls.Where(a => a.TraceId == traceId).ToList();
        }
    }
}
