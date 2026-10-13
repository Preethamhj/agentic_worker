namespace Jarvis.Contracts;

/// <summary>
/// Represents a clock for retrieving current time.
/// Injectable for deterministic time-window and deadline testing.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Default system clock implementation backed by DateTimeOffset.UtcNow.
/// </summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Independent verification contract defined in DESIGN.md section 11.
/// Independent verification engine checking preconditions, authorizations, and OS evidence.
/// </summary>
public interface IVerifier
{
    Task<VerificationResultDto> VerifyAsync(string taskId, string stepId, ExecutionReceiptDto receipt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Details of an action requiring approval.
/// </summary>
public sealed record ApprovalRequest(
    string TaskId,
    string StepId,
    string ToolId,
    IReadOnlyDictionary<string, object?> CanonicalArgs,
    string Risk,
    string? AdvisoryRisk = null,
    string? PolicyRisk = null
);

/// <summary>
/// Handles user authorization requests for high/medium risk steps.
/// Displays canonical arguments to the user before prompt.
/// </summary>
public interface IApprovalHandler
{
    Task<bool> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default);
}

public static class ApprovalHandlerExtensions
{
    public static Task<bool> RequestApprovalAsync(
        this IApprovalHandler handler,
        string taskId,
        string stepId,
        string toolId,
        IReadOnlyDictionary<string, object?> canonicalArgs,
        string risk,
        CancellationToken cancellationToken = default)
    {
        var request = new ApprovalRequest(taskId, stepId, toolId, canonicalArgs, risk);
        return handler.RequestApprovalAsync(request, cancellationToken);
    }
}

public sealed record SafetyEvaluation(bool IsAllowed, string Risk, string? RejectionReason = null);

/// <summary>
/// Evaluates tool invocations against path, tool, and rate policies.
/// Computes authoritative policy risk independently of LLM claims.
/// </summary>
public interface ISafetyGate
{
    Task<SafetyEvaluation> EvaluateStepAsync(string taskId, string stepId, string toolId, IReadOnlyDictionary<string, object?> rawArgs, CancellationToken cancellationToken = default);
}

/// <summary>
/// Requests plan generation from AI server or internal rule engine.
/// </summary>
public interface IPlanner
{
    Task<PlanResponseDto> CreatePlanAsync(string prompt, string? traceId = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes an authorized step and returns an ExecutionReceiptDto.
/// </summary>
public interface IStepExecutor
{
    Task<ExecutionReceiptDto> ExecuteStepAsync(string taskId, string stepId, string toolId, IReadOnlyDictionary<string, object?> args, string risk, CancellationToken cancellationToken = default);
}
