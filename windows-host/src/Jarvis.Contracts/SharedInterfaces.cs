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
    Task<StepToken> BeginStepAsync(AuthorizationRecordDto authorization, CancellationToken cancellationToken = default);

    Task<VerificationResultDto> VerifyAsync(
        ExecutionReceiptDto receipt,
        ExecutionObservation observation,
        StepToken token,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Verifies a signed authorization and atomically marks its consumed lifecycle as verified.
/// </summary>
public interface IAuthorizationVerifier
{
    Task VerifyAndMarkVerifiedAsync(AuthorizationRecordDto authorization, CancellationToken cancellationToken = default);
}

/// <summary>
/// Indicates that a signed authorization is invalid, expired, unconsumed, or replayed.
/// </summary>
public class AuthorizationVerificationException : Exception
{
    public AuthorizationVerificationException(string message) : base(message) { }

    public AuthorizationVerificationException(string message, Exception innerException) : base(message, innerException) { }
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

/// <summary>Outcome and identity for one approval decision.</summary>
public sealed record ApprovalDecision(bool Approved, string Approver, string? Reason = null);

/// <summary>Action chosen after independent verification returns ESCALATE.</summary>
public enum EscalationAction
{
    RetryVerification,
    Abort,
    TimedOut
}

/// <summary>Decision to retry verification, abort, or report a timeout.</summary>
public sealed record EscalationDecision(EscalationAction Action, string? Reason = null);

/// <summary>
/// Requests a user decision when independent evidence is inconclusive.
/// Retrying permits verification to run again; it does not alter its result.
/// </summary>
public interface IEscalationHandler
{
    Task<EscalationDecision> RequestDecisionAsync(
        VerificationResultDto verification,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Handles user authorization requests for high/medium risk steps.
/// Displays canonical arguments to the user before prompt.
/// </summary>
public interface IApprovalHandler
{
    Task<ApprovalDecision> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default);
}

public static class ApprovalHandlerExtensions
{
    public static Task<ApprovalDecision> RequestApprovalAsync(
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

public sealed record SafetyEvaluation(
    bool IsAllowed,
    string Risk,
    string? RejectionReason = null,
    IReadOnlyDictionary<string, object?>? CanonicalArgs = null);

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
    AuthorizationRecordDto PrepareAuthorization(
        string taskId,
        string stepId,
        string toolId,
        IReadOnlyDictionary<string, object?> args,
        string risk,
        string approvedBy = "user");

    Task<ExecutionReceiptDto> ExecuteStepAsync(
        AuthorizationRecordDto authorization,
        CancellationToken cancellationToken = default);
}
