using Jarvis.Contracts;

namespace Jarvis.Core;

/// <summary>
/// Executes an authorized step and returns an ExecutionReceiptDto.
/// </summary>
public interface IStepExecutor
{
    Task<ExecutionReceiptDto> ExecuteStepAsync(string taskId, string stepId, string toolId, IReadOnlyDictionary<string, object?> args, string risk, CancellationToken cancellationToken = default);
}
