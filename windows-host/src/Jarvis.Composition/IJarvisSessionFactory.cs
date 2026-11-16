using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;

namespace Jarvis.Composition;

/// <summary>Read-only settings and doctor values supplied to a host UI.</summary>
public sealed record JarvisSettingsSnapshot(
    IReadOnlyList<string> AllowedRoots,
    string ModelName,
    IReadOnlyList<string> DoctorResults)
{
    /// <summary>Describes the configured rule-first planner and its local LLM fallback.</summary>
    public const string PlannerDescription =
        "Rule-based fast path is first; Ollama model from config is the fallback for unmatched requests.";
}

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

    /// <summary>Creates the approval and signing path for registered browser operations; null when unsupported.</summary>
    BrowserActionAuthority? CreateBrowserActionAuthority(IApprovalHandler approvalHandler) => null;

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

    /// <summary>Resolves an explicit browser-open request to a URL using the local AI server.</summary>
    Task<BrowserResolveResponseDto> ResolveBrowserTargetAsync(string target, CancellationToken cancellationToken = default);

    /// <summary>Prepares a concise web-search query using the local AI server.</summary>
    Task<BrowserSearchQueryResponseDto> PrepareBrowserSearchQueryAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>Interprets one instruction and an explicitly approved safe page projection.</summary>
    Task<BrowserPlanResponseDto> PlanBrowserActionAsync(
        string instruction,
        BrowserPageContextDto? page,
        CancellationToken cancellationToken = default);

    /// <summary>Reads task audit events, including legacy combined safety rows.</summary>
    JarvisHistorySnapshot ReadHistory(string? taskId = null);
}
