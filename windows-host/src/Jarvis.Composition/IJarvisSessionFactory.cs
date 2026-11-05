using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;

namespace Jarvis.Composition;

/// <summary>Read-only settings and doctor values supplied to a host UI.</summary>
public sealed record JarvisSettingsSnapshot(
    IReadOnlyList<string> AllowedRoots,
    string ModelName,
    IReadOnlyList<string> DoctorResults);

/// <summary>Task-scoped audit values presented by the UI history view.</summary>
public sealed record JarvisHistorySnapshot(
    IReadOnlyList<StateTransitionRecord> StateTransitions,
    IReadOnlyList<PolicyValidationRecord> PolicyValidations,
    IReadOnlyList<ApprovalDecisionRecord> ApprovalDecisions,
    IReadOnlyList<VerificationAuditRecord> Verifications,
    IReadOnlyList<SafetyDecisionRecord> LegacySafetyDecisions);

/// <summary>Creates sessions that own the shared Jarvis orchestration graph.</summary>
public interface IJarvisSessionFactory
{
    /// <summary>Reads configured allowed roots, model name, and local doctor checks.</summary>
    JarvisSettingsSnapshot ReadSettings();

    /// <summary>Resolves an application identifier to its configured allowlisted image path.</summary>
    string? ResolveImagePath(ApprovalRequest request) => null;

    /// <summary>Starts the AI server and creates a task session using the supplied decision handlers.</summary>
    Task<IJarvisSession> StartAsync(
        IApprovalHandler approvalHandler,
        IEscalationHandler escalationHandler,
        CancellationToken cancellationToken = default);
}

/// <summary>A shared orchestration session used by both command-line and WPF hosts.</summary>
public interface IJarvisSession : IAsyncDisposable
{
    /// <summary>Port of the session's loopback-only AI server.</summary>
    int AiServerPort { get; }

    /// <summary>Plans, executes under existing policy, verifies, and reports a task.</summary>
    Task<TaskOrchestratorResult> RunTaskAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>Reads task audit events, including legacy combined safety rows.</summary>
    JarvisHistorySnapshot ReadHistory(string? taskId = null);
}
