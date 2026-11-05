using Microsoft.Win32.TaskScheduler;
using Jarvis.Contracts;

namespace Jarvis.Tools;

/// <summary>Fixed execution settings for per-user reminder tasks.</summary>
public static class ReminderTaskPolicy
{
    public static readonly TimeSpan ExecutionTimeLimit = TimeSpan.FromSeconds(ReminderSchedulerPolicy.ExecutionTimeLimitSeconds);
    public const bool DisallowStartIfOnBatteries = ReminderSchedulerPolicy.DisallowStartIfOnBatteries;
    public const bool StopIfGoingOnBatteries = ReminderSchedulerPolicy.StopIfGoingOnBatteries;
    public const bool StartWhenAvailable = ReminderSchedulerPolicy.StartWhenAvailable;
    public const TaskInstancesPolicy MultipleInstancesPolicy = TaskInstancesPolicy.IgnoreNew;
    public const TaskLogonType LogonType = TaskLogonType.InteractiveToken;
    public const TaskRunLevel RunLevel = TaskRunLevel.LUA;
}
