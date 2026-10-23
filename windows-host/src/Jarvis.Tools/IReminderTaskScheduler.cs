namespace Jarvis.Tools;

/// <summary>
/// Immutable registration data for a per-user Jarvis reminder task.
/// </summary>
public sealed record ReminderTaskRegistration(
    string TaskName,
    string TaskPath,
    string ActionPath,
    string ActionArguments,
    string WorkingDirectory,
    DateTimeOffset ScheduledAtUtc,
    string Description,
    bool DisallowStartIfOnBatteries = ReminderTaskPolicy.DisallowStartIfOnBatteries,
    bool StopIfGoingOnBatteries = ReminderTaskPolicy.StopIfGoingOnBatteries,
    TimeSpan? ExecutionTimeLimit = null,
    bool StartWhenAvailable = ReminderTaskPolicy.StartWhenAvailable,
    Microsoft.Win32.TaskScheduler.TaskInstancesPolicy MultipleInstancesPolicy = ReminderTaskPolicy.MultipleInstancesPolicy,
    Microsoft.Win32.TaskScheduler.TaskLogonType LogonType = ReminderTaskPolicy.LogonType,
    Microsoft.Win32.TaskScheduler.TaskRunLevel RunLevel = ReminderTaskPolicy.RunLevel);

/// <summary>
/// Isolates Windows Task Scheduler operations from reminder argument validation.
/// </summary>
public interface IReminderTaskScheduler
{
    /// <summary>Registers a one-time interactive reminder task.</summary>
    void Register(ReminderTaskRegistration registration);

    /// <summary>Removes a task created by Jarvis, primarily for controlled cleanup.</summary>
    void Delete(string taskPath);
}
