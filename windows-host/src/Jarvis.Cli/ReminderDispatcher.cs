using Jarvis.Safety;
using Jarvis.Tools;
using System.Windows.Forms;

namespace Jarvis.Cli;

/// <summary>Displays a task-ID-keyed reminder payload without accepting its text on the command line.</summary>
public sealed class ReminderDispatcher
{
    private readonly ReminderDataFileStore _dataFileStore;
    private readonly IReminderNotifier _notifier;

    /// <summary>Gets the deterministic payload directory resolved by this dispatcher process.</summary>
    public string ReminderDataDirectory => _dataFileStore.BaseDirectory;

    public ReminderDispatcher(ReminderDataFileStore dataFileStore, IReminderNotifier notifier)
    {
        _dataFileStore = dataFileStore ?? throw new ArgumentNullException(nameof(dataFileStore));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
    }

    /// <summary>Dispatches exactly one valid task ID and removes its payload after display.</summary>
    public int Dispatch(string[] args)
    {
        if (args.Length != 2
            || !string.Equals(args[0], "--jarvis-reminder", StringComparison.Ordinal)
            || !ToolPolicy.IsValidReminderTaskId(args[1]))
        {
            return 2;
        }

        ReminderDataPayload payload;
        try
        {
            payload = _dataFileStore.Read(args[1]);
        }
        catch (FileNotFoundException)
        {
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
