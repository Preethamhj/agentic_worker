using Jarvis.Contracts;

namespace Jarvis.Core;

/// <summary>
/// Requests plan generation from AI server or internal rule engine.
/// </summary>
public interface IPlanner
{
    Task<PlanResponseDto> CreatePlanAsync(string prompt, string? traceId = null, CancellationToken cancellationToken = default);
}
