using System.Runtime.InteropServices;
using Jarvis.Safety;
using Jarvis.Tools;
using System.Windows.Forms;

namespace Jarvis.Cli;

/// <summary>Displays a task-ID-keyed reminder payload without accepting its text on the command line.</summary>
public sealed class ReminderDispatcher
{
    private readonly ReminderDataFileStore _dataFileStore;
    private readonly IReminderNotifier _notifier;
    private readonly IReminderTaskScheduler _scheduler;
    public string? FailureReason { get; private set; }

    /// <summary>Gets the deterministic payload directory resolved by this dispatcher process.</summary>
    public string ReminderDataDirectory => _dataFileStore.BaseDirectory;

    public ReminderDispatcher(
        ReminderDataFileStore dataFileStore,
        IReminderNotifier notifier,
        IReminderTaskScheduler scheduler)
    {
        _dataFileStore = dataFileStore ?? throw new ArgumentNullException(nameof(dataFileStore));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    }

    /// <summary>Dispatches exactly one valid task ID and removes its payload after display.</summary>
    public int Dispatch(string[] args)
    {
        FailureReason = null;
        if (args.Length != 2
            || !string.Equals(args[0], "--jarvis-reminder", StringComparison.Ordinal)
            || !ToolPolicy.IsValidReminderTaskId(args[1]))
        {
            FailureReason = "Reminder task ID must be exactly 32 hexadecimal characters.";
            return 2;
        }

        ReminderDataPayload payload;
        try
        {
            payload = _dataFileStore.Read(args[1]);
        }
        catch (FileNotFoundException)
        {
            FailureReason = "Reminder data file was not found.";
            return 2;
        }

        try
        {
            _notifier.Show(payload.TaskName, payload.Message);
        }
        finally
        {
            _dataFileStore.Delete(args[1]);
        }

        try
        {
            _scheduler.Delete($@"\Jarvis\reminder-{args[1]}");
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or COMException)
        {
            Console.Error.WriteLine($"Reminder displayed, but its scheduled task could not be removed: {exception.Message}");
        }

        return 0;
    }
}

/// <summary>Displays a reminder title and message to the current interactive user.</summary>
public interface IReminderNotifier
{
    void Show(string title, string message);
}

internal sealed class WindowsReminderNotifier : IReminderNotifier
{
    public void Show(string title, string message)
    {
        using var notification = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Information,
            Visible = true,
            BalloonTipTitle = title,
            BalloonTipText = message
        };
        using var timer = new System.Windows.Forms.Timer { Interval = 15_000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Application.ExitThread();
        };
        timer.Start();
        notification.ShowBalloonTip(10_000);
        Application.Run();
    }
}
