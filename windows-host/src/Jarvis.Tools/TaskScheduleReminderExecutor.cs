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

    /// <summary>Gets the payload store used to create the signed reminder data file.</summary>
    public ReminderDataFileStore ReminderDataFileStore => _dataFileStore;

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
            || dataPathValue is not string dataPath
            || !TryReadSchedulerPolicy(authorization.Record.Expected, out var schedulerPolicy))
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
                $@"\Jarvis\reminder-{taskId}",
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Failure(authorization, startedAt, "Signed task path is outside the Jarvis Task Scheduler folder."));
        }

        _dataFileStore.Create(taskId, taskName, message);
        try
        {
            _scheduler.Register(new ReminderTaskRegistration(
                schedulerPolicy.TaskName,
                taskPath,
                actionPath,
                actionArguments,
                Path.GetDirectoryName(actionPath) ?? AppContext.BaseDirectory,
                scheduledAt,
                "Jarvis reminder",
                schedulerPolicy.DisallowStartIfOnBatteries,
                schedulerPolicy.StopIfGoingOnBatteries,
                schedulerPolicy.ExecutionTimeLimit,
                schedulerPolicy.StartWhenAvailable,
                schedulerPolicy.MultipleInstancesPolicy,
                schedulerPolicy.LogonType,
                schedulerPolicy.RunLevel,
                scheduledAt.Add(schedulerPolicy.EndBoundaryOffset),
                schedulerPolicy.DeleteExpiredTaskAfter));
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

    private static bool TryReadSchedulerPolicy(IReadOnlyDictionary<string, object?> expected, out SchedulerPolicy policy)
    {
        policy = default!;
        if (!TryRead(expected, "scheduler_task_name", out string? taskName)
            || !TryRead(expected, "disallow_start_if_on_batteries", out bool disallowBattery)
            || !TryRead(expected, "stop_if_going_on_batteries", out bool stopBattery)
            || !TryRead(expected, "execution_time_limit_seconds", out int limitSeconds)
            || !TryRead(expected, "start_when_available", out bool startAvailable)
            || !TryRead(expected, "multiple_instances_policy", out string? multiple)
            || !TryRead(expected, "logon_type", out string? logon)
            || !TryRead(expected, "run_level", out string? runLevel)
            || !TryRead(expected, "end_boundary_offset_seconds", out int endBoundarySeconds)
            || !TryRead(expected, "delete_expired_task_after_seconds", out int deleteExpiredSeconds)
            || !Enum.TryParse<Microsoft.Win32.TaskScheduler.TaskInstancesPolicy>(multiple, out var multiplePolicy)
            || !Enum.TryParse<Microsoft.Win32.TaskScheduler.TaskLogonType>(logon, out var logonType)
            || !string.Equals(runLevel, ReminderSchedulerPolicy.RunLevel, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(taskName)
            || limitSeconds <= 0 || endBoundarySeconds <= 0 || deleteExpiredSeconds <= 0)
            return false;
        policy = new SchedulerPolicy(taskName, disallowBattery, stopBattery, TimeSpan.FromSeconds(limitSeconds), startAvailable, multiplePolicy, logonType, Microsoft.Win32.TaskScheduler.TaskRunLevel.LUA, TimeSpan.FromSeconds(endBoundarySeconds), TimeSpan.FromSeconds(deleteExpiredSeconds));
        return true;
    }

    private static bool TryRead<T>(IReadOnlyDictionary<string, object?> values, string key, out T value)
    {
        value = default!;
        if (!values.TryGetValue(key, out var raw) || raw is not T typed)
            return false;
        value = typed;
        return true;
    }

    private sealed record SchedulerPolicy(string TaskName, bool DisallowStartIfOnBatteries, bool StopIfGoingOnBatteries, TimeSpan ExecutionTimeLimit, bool StartWhenAvailable, Microsoft.Win32.TaskScheduler.TaskInstancesPolicy MultipleInstancesPolicy, Microsoft.Win32.TaskScheduler.TaskLogonType LogonType, Microsoft.Win32.TaskScheduler.TaskRunLevel RunLevel, TimeSpan EndBoundaryOffset, TimeSpan DeleteExpiredTaskAfter);

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
