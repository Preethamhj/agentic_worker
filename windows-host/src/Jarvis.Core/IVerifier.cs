using Jarvis.Contracts;

namespace Jarvis.Core;

/// <summary>
/// Verifier contract defined in DESIGN.md section 11.
/// Independent verification engine that checks preconditions, authorizations, and OS evidence.
/// </summary>
public interface IVerifier
{
    Task<VerificationResultDto> VerifyAsync(string taskId, string stepId, ExecutionReceiptDto receipt, CancellationToken cancellationToken = default);
}
