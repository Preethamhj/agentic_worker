using System.Text.Json;
using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Jarvis.Tools;
using Jarvis.Verification;

namespace Jarvis.Composition;

/// <summary>Composes the common CLI and WPF orchestration host.</summary>
public sealed class JarvisSessionFactory : IJarvisSessionFactory
{
    private readonly string _repositoryRoot;

    /// <summary>Creates a composition factory rooted at the repository containing config/settings.json.</summary>
    public JarvisSessionFactory(string? repositoryRoot = null)
    {
        _repositoryRoot = repositoryRoot is null ? FindRepositoryRoot(AppContext.BaseDirectory) : Path.GetFullPath(repositoryRoot);
        if (!File.Exists(Path.Combine(_repositoryRoot, "config", "settings.json")))
            throw new DirectoryNotFoundException($"Jarvis configuration was not found under '{_repositoryRoot}'.");
    }

    /// <summary>Finds the repository root by walking upward from an application directory.</summary>
    public static string FindRepositoryRoot(string startDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);
        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "config", "settings.json")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException($"Could not find config/settings.json above '{startDirectory}'.");
    }

    /// <inheritdoc />
    public JarvisSettingsSnapshot ReadSettings()
    {
        var settingsPath = Path.Combine(_repositoryRoot, "config", "settings.json");
        using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
        var modelName = settings.RootElement.GetProperty("model").GetString();
        if (string.IsNullOrWhiteSpace(modelName))
            throw new InvalidDataException($"The configured model name in '{settingsPath}' is empty.");

        var pathPolicy = new PathPolicy();
        var pythonPath = Path.Combine(_repositoryRoot, "ai-server", ".venv", "Scripts", "python.exe");
        var doctorResults = new[]
        {
            $"Operating system: {Environment.OSVersion.VersionString}",
            $".NET runtime: {Environment.Version}",
            $"Python virtual environment: {(File.Exists(pythonPath) ? "available" : "not found")}"
        };
        return new JarvisSettingsSnapshot(pathPolicy.AllowedRoots.ToArray(), modelName, doctorResults);
    }

    /// <inheritdoc />
    public string? ResolveImagePath(ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.ToolId, "app.launch", StringComparison.OrdinalIgnoreCase)
            || !request.CanonicalArgs.TryGetValue("app_id", out var value)
            || value is not string appId)
            return null;

        var configDirectory = Path.Combine(_repositoryRoot, "config");
        var policy = ToolPolicy.FromFile(
            Path.Combine(configDirectory, "tools.registry.json"),
            Path.Combine(configDirectory, "apps.allowlist.json"));
        return policy.TryResolveAllowedExecutablePath(appId, out var imagePath) ? imagePath : null;
    }

    /// <inheritdoc />
    public async Task<IJarvisSession> StartAsync(
        IApprovalHandler approvalHandler,
        IEscalationHandler escalationHandler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approvalHandler);
        ArgumentNullException.ThrowIfNull(escalationHandler);
        cancellationToken.ThrowIfCancellationRequested();

        var aiServerDirectory = Path.Combine(_repositoryRoot, "ai-server");
        AiServerProcess? serverProcess = null;
        SqliteAuditStore? auditStore = null;
        HttpClient? httpClient = null;
        ExecutionQueue? queue = null;
        try
        {
            serverProcess = await AiServerLauncher.LaunchAsync(
                workingDirectory: aiServerDirectory,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            auditStore = new SqliteAuditStore(Path.Combine(_repositoryRoot, "audit.db"));
            IDpapiKeyStore keyStore = OperatingSystem.IsWindows()
                ? new DpapiKeyStore()
                : new InMemoryDpapiKeyStore();
            var reminderLocation = new DefaultReminderDataLocation();
            var authorizationService = new AuthorizationService(
                keyStore,
                SystemClock.Instance,
                auditStore,
                reminderDataLocation: reminderLocation);

            var configDirectory = Path.Combine(_repositoryRoot, "config");
            var pathPolicy = new PathPolicy();
            var toolPolicy = ToolPolicy.FromFile(
                Path.Combine(configDirectory, "tools.registry.json"),
                Path.Combine(configDirectory, "apps.allowlist.json"));
            var safetyGate = new SafetyGate(
                toolPolicy,
                authorizationService,
                pathPolicy: pathPolicy,
                auditStore: auditStore);
            var reminderDataStore = new ReminderDataFileStore(reminderLocation);
            var executors = new IToolExecutor[]
            {
                new CreateFileExecutor(pathPolicy, authorizationService),
                new CreateFolderExecutor(pathPolicy, authorizationService),
                new SearchFilesExecutor(pathPolicy, authorizationService),
                new AppLaunchExecutor(toolPolicy, authorizationService, new WindowsElevationChecker()),
                new TaskScheduleReminderExecutor(
                    authorizationService,
                    new WindowsReminderTaskScheduler(),
                    dataFileStore: reminderDataStore)
            };
            var stepExecutor = new AuthorizedStepExecutor(authorizationService, executors, toolPolicy);
            var sessionId = $"session-{Guid.NewGuid():N}"[..12];
            var planTimeout = ApiClient.PlanTimeoutFromSettings(Path.Combine(configDirectory, "settings.json"));
            httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var apiClient = new ApiClient(httpClient, serverProcess.BaseUrl, serverProcess.Token, planTimeout: planTimeout);
            var planner = new ApiClientSessionPlanner(apiClient, sessionId);
            queue = new ExecutionQueue();
            var verifier = new VerificationEngine(authorizationService, auditStore);
            var orchestrator = new TaskOrchestrator(
                planner,
                safetyGate,
                approvalHandler,
                queue,
                stepExecutor,
                verifier,
                auditStore,
                escalationHandler: escalationHandler);

            return new JarvisSession(serverProcess, auditStore, httpClient, queue, apiClient, sessionId, orchestrator);
        }
        catch
        {
            if (queue is not null)
                await queue.DisposeAsync().ConfigureAwait(false);
            httpClient?.Dispose();
            auditStore?.Dispose();
            if (serverProcess is not null)
                await serverProcess.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

internal sealed class JarvisSession(
    AiServerProcess serverProcess,
    SqliteAuditStore auditStore,
    HttpClient httpClient,
    ExecutionQueue queue,
    IApiClient apiClient,
    string sessionId,
    TaskOrchestrator orchestrator) : IJarvisSession
{
    private bool _disposed;

    public int AiServerPort => serverProcess.Port;

    public async Task<TaskOrchestratorResult> RunTaskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = await orchestrator.RunTaskAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
        await apiClient.SendReportAsync(CreateReport(sessionId, result), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    public JarvisHistorySnapshot ReadHistory(string? taskId = null) =>
        new(
            auditStore.GetStateTransitions(taskId),
            auditStore.GetPolicyValidations(taskId),
            auditStore.GetApprovalDecisions(taskId),
            auditStore.GetVerifications().Where(record => taskId is null || record.TaskId == taskId).ToArray(),
            auditStore.GetSafetyDecisions(taskId));

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await queue.DisposeAsync().ConfigureAwait(false);
        httpClient.Dispose();
        auditStore.Dispose();
        await serverProcess.DisposeAsync().ConfigureAwait(false);
    }

    private static ReportRequestDto CreateReport(string sessionId, TaskOrchestratorResult result)
    {
        var steps = result.VerificationResults
            .GroupBy(verification => verification.StepId, StringComparer.Ordinal)
            .Select(group =>
            {
                var verification = group.Last();
                var verified = string.Equals(verification.Status, "VERIFIED", StringComparison.OrdinalIgnoreCase);
                var reason = string.Join(" ", verification.Reasons).Trim();
                if (string.IsNullOrWhiteSpace(reason))
                    reason = $"Independent verification returned {verification.Status}.";
                if (reason.Length > 240)
                    reason = reason[..240];
                return new ReportStepSummaryDto(
                    verification.StepId,
                    verified ? "completed" : "failed",
                    verification.Status,
                    verified ? "Step independently verified." : "Step did not pass independent verification.",
                    reason);
            })
            .ToList();
        return new ReportRequestDto(1, sessionId, result.TaskId, steps);
    }
}

internal sealed class ApiClientSessionPlanner(IApiClient apiClient, string sessionId) : IPlanner
{
    public Task<PlanResponseDto> CreatePlanAsync(
        string prompt,
        string? traceId = null,
        CancellationToken cancellationToken = default) =>
        apiClient.GetPlanAsync(
            new PlanRequestDto(1, sessionId, prompt, "en-US"),
            traceId,
            cancellationToken);
}

internal sealed class AuthorizedStepExecutor(
    AuthorizationService authorizationService,
    IEnumerable<IToolExecutor> executors,
    ToolPolicy toolPolicy) : IStepExecutor
{
    private readonly Dictionary<string, IToolExecutor> _executors =
        executors.ToDictionary(executor => executor.ToolId, StringComparer.OrdinalIgnoreCase);

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
            if (!args.TryGetValue("app_id", out var value)
                || value is not string appId
                || !toolPolicy.TryResolveAllowedExecutablePath(appId, out var imagePath)
                || imagePath is null)
                throw new InvalidOperationException("Cannot authorize app.launch without its resolved allowlisted image path.");
            expected = new Dictionary<string, object?>
            {
                ["app_id"] = appId,
                ["image_path"] = imagePath,
                ["running"] = true
            };
        }

        return authorizationService.IssueAuthorization(
            taskId: taskId,
            stepId: stepId,
            toolId: toolId,
            rawArgs: args,
            expected: expected,
            risk: risk,
            approvedBy: approvedBy);
    }

    public async Task<ExecutionReceiptDto> ExecuteStepAsync(
        AuthorizationRecordDto authorization,
        CancellationToken cancellationToken = default)
    {
        if (!_executors.TryGetValue(authorization.ToolId, out var executor))
            return new ExecutionReceiptDto(
                1,
                authorization.StepId,
                authorization.AuthId,
                DateTimeOffset.UtcNow.ToString("o"),
                DateTimeOffset.UtcNow.ToString("o"),
                1,
                false,
                string.Empty,
                $"No executor registered for tool '{authorization.ToolId}'.");
        var verifiedAuthorization = authorizationService.Consume(authorization, authorization.StepId);
        return await executor.ExecuteAsync(verifiedAuthorization, cancellationToken).ConfigureAwait(false);
    }
}
