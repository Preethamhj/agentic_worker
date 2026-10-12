namespace Jarvis.Safety;

public sealed record ApprovalRequest(
    string TaskId,
    string StepId,
    string ToolId,
    IReadOnlyDictionary<string, object?> CanonicalArgs,
    string AdvisoryRisk,
    string PolicyRisk,
    string FinalRisk
);

/// <summary>
/// Abstraction for requesting human approval for medium and high risk operations.
/// </summary>
public interface IApprovalProvider
{
    Task<bool> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default);
}
