using Jarvis.Contracts;

namespace Jarvis.Eval;

public enum EvalGroundTruth
{
    Good,
    Bad
}

public enum EvalStatus
{
    Verified,
    Failed,
    Escalate,
    Error
}

public sealed record EvalCase(
    string Id,
    string ToolId,
    string TaskId,
    string FaultId,
    EvalGroundTruth GroundTruth,
    int Repetition,
    int Seed);

public sealed record EvalExecutionContext(
    AuthorizationRecordDto Authorization,
    ExecutionReceiptDto Receipt,
    ExecutionObservation Observation,
    StepToken VerificationToken,
    object State,
    Func<CancellationToken, ValueTask<bool>> ExecutorPostcondition,
    IReadOnlyList<string> ReportedPaths,
    IEvalReminderPredicateReader? ReminderPredicateReader = null);

public sealed record EvalVerdict(
    string VerifierId,
    EvalStatus Status,
    IReadOnlyList<string> Reasons,
    TimeSpan Latency,
    string? Error = null);

public sealed record EvalRunResult(EvalCase Case, IReadOnlyList<EvalVerdict> Verdicts);

public interface IEvalVerifier
{
    string VerifierId { get; }

    Task<EvalVerdict> VerifyAsync(
        EvalCase evalCase,
        EvalExecutionContext execution,
        CancellationToken cancellationToken = default);
}
