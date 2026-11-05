using Jarvis.Contracts;

namespace Jarvis.Verification;

internal interface IScheduledTaskEvidenceCollector
{
    IReadOnlyList<string> CaptureTaskPaths(CancellationToken cancellationToken);

    IndependentEvidenceObservation Collect(
        AuthorizationRecordDto authorization,
        IReadOnlyList<string> beforeTaskPaths,
        CancellationToken cancellationToken);
}
