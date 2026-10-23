using Microsoft.Win32.TaskScheduler;

namespace Jarvis.Tools;

/// <summary>Fixed execution settings for per-user reminder tasks.</summary>
public static class ReminderTaskPolicy
{
    public static readonly TimeSpan ExecutionTimeLimit = TimeSpan.FromMinutes(5);
    public const bool DisallowStartIfOnBatteries = false;
    public const bool StopIfGoingOnBatteries = false;
    public const bool StartWhenAvailable = true;
    public const TaskInstancesPolicy MultipleInstancesPolicy = TaskInstancesPolicy.IgnoreNew;
    public const TaskLogonType LogonType = TaskLogonType.InteractiveToken;
    public const TaskRunLevel RunLevel = TaskRunLevel.LUA;
}
