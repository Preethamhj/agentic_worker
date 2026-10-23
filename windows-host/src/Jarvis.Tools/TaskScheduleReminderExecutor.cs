using System.Globalization;
using Jarvis.Contracts;
using Jarvis.Safety;

namespace Jarvis.Tools;

/// <summary>
/// Creates a signed, one-time per-user reminder task that invokes the Jarvis notification host.
/// </summary>
public sealed class TaskScheduleReminderExecutor : IToolExecutor
{
    private readonly AuthorizationService _authorizationService;
    private readonly IReminderTaskScheduler _scheduler;
    private readonly ReminderDataFileStore _dataFileStore;
    private readonly IClock _clock;
    private readonly IHostPathProvider _hostPathProvider;

    public TaskScheduleReminderExecutor(
        AuthorizationService authorizationService,
        IReminderTaskScheduler scheduler,
        IClock? clock = null,
        ReminderDataFileStore? dataFileStore = null,
        IHostPathProvider? hostPathProvider = null)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _dataFileStore = dataFileStore ?? new ReminderDataFileStore();
        _clock = clock ?? SystemClock.Instance;
        _hostPathProvider = hostPathProvider ?? new EnvironmentHostPathProvider();
    }

    public string ToolId => "task.schedule_reminder";

    /// <summary>Creates a future reminder from canonical arguments in the consumed authorization.</summary>
    public Task<ExecutionReceiptDto> ExecuteAsync(
        VerifiedAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = _clock.UtcNow.ToString("O");
        _authorizationService.Consume(authorization, authorization.StepId);

        var taskName = ReadArgument(authorization, "task_name");
        var timeIso = ReadArgument(authorization, "time_iso");
        var message = ReadArgument(authorization, "message");
        if (!DateTimeOffset.TryParse(
                timeIso,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var scheduledAt)
            || scheduledAt <= _clock.UtcNow)
        {
            return Task.FromResult(Failure(authorization, startedAt, "Reminder time must be a valid future ISO-8601 timestamp."));
        }
        if (scheduledAt.Ticks % TimeSpan.TicksPerSecond != 0)
            return Task.FromResult(Failure(authorization, startedAt, "Reminder time must have whole-second precision."));

        if (string.IsNullOrWhiteSpace(taskName) || taskName.Length > 63 || taskName.Any(char.IsControl)
            || !ToolPolicy.IsValidReminderMessage(message))
        {
            return Task.FromResult(Failure(
                authorization,
                startedAt,
                $"Reminder title must contain 1-63 non-control characters and message must contain 1-{ToolPolicy.MaxReminderMessageLength} non-control characters."));
        }

        if (!authorization.Record.Expected.TryGetValue("task_path", out var taskPathValue)
            || taskPathValue is not string taskPath
            || !authorization.Record.Expected.TryGetValue("action_path", out var actionPathValue)
            || actionPathValue is not string actionPath
            || !authorization.Record.Expected.TryGetValue("task_id", out var taskIdValue)
            || taskIdValue is not string taskId
            || !authorization.Record.Expected.TryGetValue("action_arguments", out var actionArgumentsValue)
            || actionArgumentsValue is not string actionArguments
            || !authorization.Record.Expected.TryGetValue("reminder_data_path", out var dataPathValue)
            || dataPathValue is not string dataPath)
        {
            return Task.FromResult(Failure(authorization, startedAt, "Signed authorization is missing reminder action or data-file fields."));
        }
        if (!ToolPolicy.IsValidReminderTaskId(taskId)
            || !string.Equals(actionArguments, $"--jarvis-reminder {taskId}", StringComparison.Ordinal)
            || !string.Equals(dataPath, _dataFileStore.GetFilePath(taskId), StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Failure(authorization, startedAt, "Signed reminder task ID, arguments, or data-file path is invalid."));
        }
        if (!Path.IsPathRooted(actionPath)
            || !string.Equals(actionPath, Path.GetFullPath(actionPath), StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Failure(
                authorization,
                startedAt,
                "Signed reminder action executable must be a fixed absolute path."));
        }
        var hostPath = _hostPathProvider.GetHostPath();
        if (string.Equals(Path.GetFileName(hostPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Failure(authorization, startedAt, "Cannot register a reminder from dotnet.exe. Run the published Jarvis.Cli.exe host instead."));
        hostPath = Path.GetFullPath(hostPath);
        if (!string.Equals(actionPath, hostPath, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Failure(authorization, startedAt, "Signed reminder action executable does not match this host."));

        var separator = taskPath.LastIndexOf('\\');
        if (separator <= 0 || separator == taskPath.Length - 1
            || !string.Equals(
                taskPath,
                $@"\Jarvis\{authorization.AuthId}",
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Failure(authorization, startedAt, "Signed task path is outside the Jarvis Task Scheduler folder."));
        }

        _dataFileStore.Create(taskId, taskName, message);
        try
        {
            _scheduler.Register(new ReminderTaskRegistration(
                taskPath[(separator + 1)..],
                taskPath,
                actionPath,
                actionArguments,
                Path.GetDirectoryName(actionPath) ?? AppContext.BaseDirectory,
                scheduledAt,
                "Jarvis reminder",
                ReminderTaskPolicy.DisallowStartIfOnBatteries,
                ReminderTaskPolicy.StopIfGoingOnBatteries,
                ReminderTaskPolicy.ExecutionTimeLimit,
                ReminderTaskPolicy.StartWhenAvailable,
                ReminderTaskPolicy.MultipleInstancesPolicy,
                ReminderTaskPolicy.LogonType,
                ReminderTaskPolicy.RunLevel));
        }
        catch
        {
            _dataFileStore.Delete(taskId);
            throw;
        }

        var completedAt = _clock.UtcNow.ToString("O");
        return Task.FromResult(new ExecutionReceiptDto(
            1,
            authorization.StepId,
            authorization.AuthId,
            startedAt,
            completedAt,
            0,
            true,
            "Reminder task registered.",
            string.Empty));
    }

    private static string ReadArgument(VerifiedAuthorization authorization, string key) =>
        authorization.CanonicalArgs.TryGetValue(key, out var value) && value is string text
            ? text
            : throw new ArgumentException($"Authorization is missing canonical '{key}'.", nameof(authorization));

    private ExecutionReceiptDto Failure(
        VerifiedAuthorization authorization,
        string startedAt,
        string error) =>
        new(
            1,
            authorization.StepId,
            authorization.AuthId,
            startedAt,
            _clock.UtcNow.ToString("O"),
            1,
            false,
            string.Empty,
            error);
}
