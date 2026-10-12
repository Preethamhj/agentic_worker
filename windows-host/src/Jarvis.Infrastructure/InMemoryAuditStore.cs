namespace Jarvis.Infrastructure;

/// <summary>
/// In-memory test double for IAuditStore.
/// </summary>
public sealed class InMemoryAuditStore : IAuditStore
{
    private readonly List<StateTransitionRecord> _stateTransitions = new();
    private readonly List<SafetyDecisionRecord> _safetyDecisions = new();
    private readonly object _lock = new();

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
}
