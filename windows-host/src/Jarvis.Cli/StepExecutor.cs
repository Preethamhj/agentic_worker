using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Safety;
using Jarvis.Tools;

namespace Jarvis.Cli;

/// <summary>
/// Executes authorized tool steps by issuing an authorization record,
/// consuming it into a VerifiedAuthorization token, and delegating to the registered IToolExecutor.
/// Invariant: Executors work strictly with a signed, consumed VerifiedAuthorization.
/// </summary>
public sealed class StepExecutor : IStepExecutor
{
    private readonly AuthorizationService _authService;
    private readonly Dictionary<string, IToolExecutor> _executors;
    private readonly ToolPolicy? _toolPolicy;

    public StepExecutor(
        AuthorizationService authService,
        IEnumerable<IToolExecutor> executors,
        ToolPolicy? toolPolicy = null)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _toolPolicy = toolPolicy;
        _executors = new Dictionary<string, IToolExecutor>(StringComparer.OrdinalIgnoreCase);

        foreach (var executor in executors)
        {
            _executors[executor.ToolId] = executor;
        }
    }

    public AuthorizationRecordDto PrepareAuthorization(
        string taskId,
        string stepId,
        string toolId,
        IReadOnlyDictionary<string, object?> args,
        string risk,
        string approvedBy = "user")
    {
        IReadOnlyDictionary<string, object?>? expected = null;
        if (string.Equals(toolId, "app.launch", StringComparison.OrdinalIgnoreCase))
        {
            if (_toolPolicy is null
                || !args.TryGetValue("app_id", out var appIdValue)
                || appIdValue is not string appId
                || !_toolPolicy.TryResolveAllowedExecutablePath(appId, out var imagePath)
                || imagePath is null)
            {
                throw new InvalidOperationException("Cannot authorize app.launch without its resolved allowlisted image path.");
            }
            expected = new Dictionary<string, object?>
            {
                ["app_id"] = appId,
                ["image_path"] = imagePath,
                ["running"] = true
            };
        }

        return _authService.IssueAuthorization(
            taskId: taskId,
            stepId: stepId,
            toolId: toolId,
            rawArgs: args,
            expected: expected,
            risk: risk,
            approvedBy: approvedBy
        );
    }

    public async Task<ExecutionReceiptDto> ExecuteStepAsync(
        AuthorizationRecordDto authorization,
        CancellationToken cancellationToken = default)
    {
        if (!_executors.TryGetValue(authorization.ToolId, out var executor))
        {
            return new ExecutionReceiptDto(
                SchemaVersion: 1,
                StepId: authorization.StepId,
                AuthId: authorization.AuthId,
                StartedAt: DateTimeOffset.UtcNow.ToString("o"),
                CompletedAt: DateTimeOffset.UtcNow.ToString("o"),
                ExitCode: 1,
                ReportedSuccess: false,
                Stdout: string.Empty,
                Stderr: $"No executor registered for tool '{authorization.ToolId}'."
            );
        }

        var auth = _authService.Consume(authorization, authorization.StepId);
        return await executor.ExecuteAsync(auth, cancellationToken).ConfigureAwait(false);
    }
}
