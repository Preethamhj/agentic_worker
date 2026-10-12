namespace Jarvis.Core;

public sealed record SafetyEvaluation(bool IsAllowed, string Risk, string? RejectionReason = null);

/// <summary>
/// Evaluates tool invocations against path, tool, and rate policies.
/// Computes authoritative policy risk independently of LLM claims.
/// </summary>
public interface ISafetyGate
{
    Task<SafetyEvaluation> EvaluateStepAsync(string taskId, string stepId, string toolId, IReadOnlyDictionary<string, object?> rawArgs, CancellationToken cancellationToken = default);
}
