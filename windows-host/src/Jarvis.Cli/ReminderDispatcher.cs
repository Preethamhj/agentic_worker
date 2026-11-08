using Jarvis.Tools;
using System.Windows.Forms;

namespace Jarvis.Cli;

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
