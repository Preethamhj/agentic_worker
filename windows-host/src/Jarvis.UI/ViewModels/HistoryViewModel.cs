using Jarvis.Infrastructure;

namespace Jarvis.UI.ViewModels;

/// <summary>Read-only chronological audit view, including legacy combined safety rows.</summary>
public sealed class HistoryViewModel
{
    /// <summary>Combines state, validation, approval, verification, and legacy audit events.</summary>
    public HistoryViewModel(
        IEnumerable<StateTransitionRecord>? transitions = null,
        IEnumerable<PolicyValidationRecord>? policyValidations = null,
        IEnumerable<ApprovalDecisionRecord>? approvalDecisions = null,
        IEnumerable<VerificationAuditRecord>? verifications = null,
        IEnumerable<SafetyDecisionRecord>? legacySafetyDecisions = null)
    {
        var events = new List<HistoryEntry>();
        events.AddRange((transitions ?? []).Select(record => new HistoryEntry(
            "State transition",
            record.TaskId,
            null,
            record.Timestamp,
            $"{record.FromState} -> {record.ToState}" + FormatReason(record.Reason))));
        events.AddRange((policyValidations ?? []).Select(record => new HistoryEntry(
            "Policy validation",
            record.TaskId,
            record.StepId,
            record.Timestamp,
            $"{record.ToolId}: {(record.IsValid ? "valid" : "invalid")} at {record.FinalRisk} risk" + FormatReason(record.Reason))));
        events.AddRange((approvalDecisions ?? []).Select(record => new HistoryEntry(
            "Approval",
            record.TaskId,
            record.StepId,
            record.Timestamp,
            $"{(record.Approved ? "Approved" : "Denied")} by {record.Approver}" + FormatReason(record.Reason))));
        events.AddRange((verifications ?? []).Select(record => new HistoryEntry(
            "Verification",
            record.TaskId ?? "legacy",
            record.StepId,
            record.VerifiedAt,
            record.OverallStatus + FormatReason(record.Summary))));
        events.AddRange((legacySafetyDecisions ?? []).Select(record => new HistoryEntry(
            "Legacy safety decision",
            record.TaskId,
            record.StepId,
            record.Timestamp,
            $"{(record.Approved ? "Approved" : "Denied")} by {record.ApprovedBy}" + FormatReason(record.Reason))));
        Events = Array.AsReadOnly(events.OrderBy(item => item.Timestamp).ToArray());
    }

    /// <summary>Chronologically ordered immutable audit rows.</summary>
    public IReadOnlyList<HistoryEntry> Events { get; }

    private static string FormatReason(string? reason) => string.IsNullOrWhiteSpace(reason) ? string.Empty : $" ({reason})";
}

/// <summary>One formatted audit row.</summary>
public sealed record HistoryEntry(string Kind, string TaskId, string? StepId, DateTimeOffset Timestamp, string Summary);
