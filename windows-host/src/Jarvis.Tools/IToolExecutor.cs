using Jarvis.Contracts;
using Jarvis.Safety;

namespace Jarvis.Tools;

/// <summary>
/// Tool executor contract defined in DESIGN.md sections 2, 7, and 9.
/// Invariant: Every tool execution requires a signed, consumed VerifiedAuthorization token.
/// </summary>
public interface IToolExecutor
{
    string ToolId { get; }

    Task<ExecutionReceiptDto> ExecuteAsync(VerifiedAuthorization auth, CancellationToken cancellationToken = default);
}
