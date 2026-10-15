using System.Globalization;
using System.Text;
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
    private readonly IClock _clock;

    public TaskScheduleReminderExecutor(
        AuthorizationService authorizationService,
        IReminderTaskScheduler scheduler,
        IClock? clock = null)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _clock = clock ?? SystemClock.Instance;
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

        if (string.IsNullOrWhiteSpace(taskName) || taskName.Length > 63
            || string.IsNullOrWhiteSpace(message) || message.Length > 255)
        {
            return Task.FromResult(Failure(
                authorization,
                startedAt,
                "Reminder title must contain 1-63 characters and message must contain 1-255 characters."));
        }

        if (!authorization.Record.Expected.TryGetValue("task_path", out var taskPathValue)
            || taskPathValue is not string taskPath
            || !authorization.Record.Expected.TryGetValue("action_path", out var actionPathValue)
            || actionPathValue is not string actionPath)
        {
            return Task.FromResult(Failure(authorization, startedAt, "Signed authorization is missing the task path or action executable."));
        }

        var separator = taskPath.LastIndexOf('\\');
        if (separator <= 0 || separator == taskPath.Length - 1
            || !string.Equals(
                taskPath,
                $@"\Jarvis\{authorization.AuthId}",
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Failure(authorization, startedAt, "Signed task path is outside the Jarvis Task Scheduler folder."));
        }

        var actionArguments = BuildReminderArguments(taskName, message);
        _scheduler.Register(new ReminderTaskRegistration(
            taskPath[(separator + 1)..],
            taskPath,
            actionPath,
            actionArguments,
            Path.GetDirectoryName(actionPath) ?? AppContext.BaseDirectory,
            scheduledAt,
            $"Jarvis reminder: {taskName}"));

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

    private static string BuildReminderArguments(string taskName, string message) =>
        $"--jarvis-reminder {QuoteArgument(taskName)} {QuoteArgument(message)}";

    private static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1);
                result.Append('"');
                backslashes = 0;
                continue;
            }
            result.Append('\\', backslashes);
            result.Append(character);
            backslashes = 0;
        }
        result.Append('\\', backslashes * 2);
        result.Append('"');
        return result.ToString();
    }

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
