using Jarvis.Contracts;
using Jarvis.Composition;
using Jarvis.Core;
using Jarvis.Infrastructure;

namespace Jarvis.UI.ViewModels;

/// <summary>Read-only task state timeline and independent verification stage presentation.</summary>
public sealed class TaskViewModel
{
    /// <summary>Creates task presentation data; executor diagnostics are not verification evidence.</summary>
    public TaskViewModel(TaskOrchestratorResult result, IEnumerable<StateTransitionRecord>? transitions = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        TaskId = result.TaskId;
        FinalState = result.FinalState;
        StatusMessage = result.StatusMessage;
        StateTimeline = Array.AsReadOnly((transitions ?? []).OrderBy(item => item.Timestamp).ToArray());
        VerificationStages = Array.AsReadOnly(result.VerificationResults
            .SelectMany(verification => verification.Stages.Select(stage => new VerificationStageViewModel(
                verification.StepId,
                stage.Name,
                NormalizeStageStatus(stage),
                stage.Detail)))
            .ToArray());
        Reasons = Array.AsReadOnly(result.VerificationResults.SelectMany(verification => verification.Reasons).ToArray());
        ExecutorDiagnostics = Array.AsReadOnly(result.Receipts
            .Where(receipt => !string.IsNullOrWhiteSpace(receipt.Stderr))
            .Select(receipt => $"Untrusted executor diagnostic ({receipt.StepId}): {receipt.Stderr}")
            .ToArray());
        SearchResults = VerifiedSearchResults.From(result.VerificationResults);
        FinalMessage = result.FinalState == TaskState.Completed
            && result.VerificationResults.Count > 0
            && result.VerificationResults.All(verification =>
                string.Equals(verification.Status, "VERIFIED", StringComparison.OrdinalIgnoreCase))
                ? "verified"
                : "not verified";
    }

    /// <summary>Task identifier.</summary>
    public string TaskId { get; }

    /// <summary>Terminal or current orchestration state.</summary>
    public TaskState FinalState { get; }

    /// <summary>User-facing orchestration status.</summary>
    public string StatusMessage { get; }

    /// <summary>Audit transitions ordered by timestamp.</summary>
    public IReadOnlyList<StateTransitionRecord> StateTimeline { get; }

    /// <summary>Independent verification stages and their reasons.</summary>
    public IReadOnlyList<VerificationStageViewModel> VerificationStages { get; }

    /// <summary>Independent verifier reasons, including overall failure reasons.</summary>
    public IReadOnlyList<string> Reasons { get; }

    /// <summary>Executor-reported errors, which are not evidence of task success or failure.</summary>
    public IReadOnlyList<string> ExecutorDiagnostics { get; }

    /// <summary>Search results from independently verified evidence, grouped by step.</summary>
    public IReadOnlyList<VerifiedSearchResult> SearchResults { get; }

    /// <summary>Reports success only when the orchestrator and every verifier result are VERIFIED.</summary>
    public string FinalMessage { get; }

    private static string NormalizeStageStatus(VerificationStageResultDto stage) =>
        string.Equals(stage.Outcome, "NOT_RUN", StringComparison.OrdinalIgnoreCase)
            ? "NOT_RUN"
            : stage.Passed
                ? "PASSED"
                : "FAILED";
}

/// <summary>One read-only verification-stage row.</summary>
public sealed record VerificationStageViewModel(string StepId, string Name, string Status, string Reason);
