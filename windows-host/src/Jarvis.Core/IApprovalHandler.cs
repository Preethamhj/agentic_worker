namespace Jarvis.Core;

/// <summary>
/// Handles user authorization requests for high/medium risk steps.
/// Displays canonical arguments to the user before prompt.
/// </summary>
public interface IApprovalHandler
{
    Task<bool> RequestApprovalAsync(string taskId, string stepId, string toolId, IReadOnlyDictionary<string, object?> canonicalArgs, string risk, CancellationToken cancellationToken = default);
}
