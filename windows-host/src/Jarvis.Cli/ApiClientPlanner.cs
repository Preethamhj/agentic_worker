using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;

namespace Jarvis.Cli;

/// <summary>
/// Adapts IApiClient from Jarvis.Infrastructure to IPlanner for TaskOrchestrator.
/// </summary>
public sealed class ApiClientPlanner : IPlanner
{
    private readonly IApiClient _apiClient;
    private readonly string _sessionId;

    public ApiClientPlanner(IApiClient apiClient, string? sessionId = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _sessionId = sessionId ?? $"session-{Guid.NewGuid():N}"[..12];
    }

    public async Task<PlanResponseDto> CreatePlanAsync(string prompt, string? traceId = null, CancellationToken cancellationToken = default)
    {
        var request = new PlanRequestDto(
            SchemaVersion: 1,
            SessionId: _sessionId,
            Text: prompt,
            Locale: "en-US"
        );

        return await _apiClient.GetPlanAsync(request, traceId, cancellationToken).ConfigureAwait(false);
    }
}
