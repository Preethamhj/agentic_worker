namespace Jarvis.Contracts;

/// <summary>Authorization-safe names and values for an interactive reminder task's immutable scheduler policy.</summary>
public static class ReminderSchedulerPolicy
{
    public const bool DisallowStartIfOnBatteries = false;
    public const bool StopIfGoingOnBatteries = false;
    public const int ExecutionTimeLimitSeconds = 300;
    public const bool StartWhenAvailable = true;
    public const string MultipleInstances = "IgnoreNew";
    public const string LogonType = "InteractiveToken";
    public const string RunLevel = "LeastPrivilege";
    public const int EndBoundaryOffsetSeconds = 600;
    public const int DeleteExpiredTaskAfterSeconds = 3600;
}
