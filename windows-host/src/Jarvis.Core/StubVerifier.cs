using Jarvis.Contracts;

namespace Jarvis.Core;

/// <summary>
/// Stub verifier for Phase 2b. Always returns status ESCALATE.
/// Invariant: Completed is never reached with StubVerifier.
/// The CLI must print 'not verified' and never 'completed'.
/// </summary>
public sealed class StubVerifier : IVerifier
{
    public Task<StepToken> BeginStepAsync(AuthorizationRecordDto authorization, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new StepToken(
            authorization,
            authorization.Nonce,
            Array.Empty<SnapshotEntryDto>()));
    }

    public Task<VerificationResultDto> VerifyAsync(
        ExecutionReceiptDto receipt,
        ExecutionObservation observation,
        StepToken token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new VerificationResultDto(
            SchemaVersion: 1,
            StepId: receipt.StepId,
            Status: "ESCALATE",
            Stages: new List<VerificationStageResultDto>
            {
                new(1, "Precondition", true, "Precondition verified.", "PASSED"),
                new(2, "Authorization", true, "Authorization record validated.", "PASSED"),
                new(3, "IndependentEvidence", false, "Independent verification engine is stubbed in Phase 2b.", "ESCALATE")
            },
            Evidence: new List<EvidenceDto>(),
            SideEffects: new List<string>(),
            Reasons: new List<string> { "Phase 2b StubVerifier: independent verification stubbed; requires manual escalation." },
            DurationMs: 0.0
        );

        return Task.FromResult(result);
    }
}
