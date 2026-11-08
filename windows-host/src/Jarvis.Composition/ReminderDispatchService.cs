using Jarvis.Tools;

namespace Jarvis.Composition;

/// <summary>Dispatches a scheduled reminder through the shared host boundary.</summary>
public static class ReminderDispatchService
{
    /// <summary>Displays the reminder via the host callback and returns its exit code and diagnostic.</summary>
    public static (int ExitCode, string? Error) Dispatch(string[] args, Action<string, string> show)
    {
        ArgumentNullException.ThrowIfNull(show);
        var dispatcher = new ReminderDispatcher(
            new ReminderDataFileStore(),
            new CallbackNotifier(show),
            new WindowsReminderTaskScheduler());
        var exitCode = dispatcher.Dispatch(args);
        return (exitCode, dispatcher.FailureReason);
    }

    private sealed class CallbackNotifier(Action<string, string> show) : IReminderNotifier
    {
        public void Show(string title, string message) => show(title, message);
    }
}
