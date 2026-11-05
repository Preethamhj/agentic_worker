using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Jarvis.UI.ViewModels;

namespace Jarvis.UI.Handlers;

/// <summary>Presents accessible modeless WPF decision dialogs without blocking the dispatcher.</summary>
public sealed class WpfDecisionDialogPresenter : IDecisionDialogPresenter
{
    /// <inheritdoc />
    public Task ShowApprovalAsync(
        ApprovalViewModel viewModel,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var body = new StackPanel { Margin = new Thickness(18), Width = 560 };
        AddRiskBadge(body, viewModel.RiskBadge);
        AddText(body, $"Tool: {viewModel.ToolId}");
        AddText(body, "Canonical arguments (the values approved and signed):");
        var canonical = string.Join(
            Environment.NewLine,
            viewModel.CanonicalArguments.Select(pair =>
                $"{pair.Key}: {JsonSerializer.Serialize(pair.Value)}"));
        AddReadOnlyText(body, canonical);
        if (!string.IsNullOrWhiteSpace(viewModel.ResolvedImagePath))
            AddText(body, $"Resolved application image: {viewModel.ResolvedImagePath}");
        if (viewModel.ReminderTimeLocal is { } reminderLocal)
        {
            AddText(body, $"Reminder local time: {reminderLocal:yyyy-MM-dd HH:mm:ss zzz}");
            AddText(body, $"Reminder ISO time: {viewModel.ReminderTimeIso}");
            AddText(body, $"Countdown: {viewModel.ReminderCountdown}");
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var deny = CreateButton("Deny", "Deny requested action", isDefault: true, isCancel: true);
        var approve = CreateButton("Approve", "Approve requested action", isDefault: false, isCancel: false);
        deny.Click += (_, _) => viewModel.DenyCommand.Execute(null);
        approve.Click += (_, _) => viewModel.ApproveCommand.Execute(null);
        buttons.Children.Add(deny);
        buttons.Children.Add(approve);
        body.Children.Add(buttons);

        var window = CreateWindow("Jarvis - Authorization required", body);
        deny.Click += (_, _) => window.Close();
        approve.Click += (_, _) => window.Close();
        window.Closed += (_, _) => viewModel.Close();
        return ShowAsync(window, timeout, viewModel.Timeout, cancellationToken);
    }

    /// <inheritdoc />
    public Task ShowEscalationAsync(
        EscalationViewModel viewModel,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var body = new StackPanel { Margin = new Thickness(18), Width = 560 };
        AddRiskBadge(body, "VERIFICATION NEEDS REVIEW");
        AddText(body, "Independent evidence summary:");
        AddReadOnlyText(body, viewModel.EvidenceSummary);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var abort = CreateButton("Abort", "Abort task", isDefault: true, isCancel: true);
        var retry = CreateButton("Retry verification", "Retry verification only", isDefault: false, isCancel: false);
        abort.Click += (_, _) => viewModel.AbortCommand.Execute(null);
        retry.Click += (_, _) => viewModel.RetryCommand.Execute(null);
        buttons.Children.Add(abort);
        buttons.Children.Add(retry);
        body.Children.Add(buttons);

        var window = CreateWindow("Jarvis - Verification escalation", body);
        abort.Click += (_, _) => window.Close();
        retry.Click += (_, _) => window.Close();
        window.Closed += (_, _) => viewModel.AbortCommand.Execute(null);
        return ShowAsync(window, timeout, viewModel.Timeout, cancellationToken);
    }

    private static async Task ShowAsync(
        Window window,
        TimeSpan timeout,
        Action timeoutDecision,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher) { Interval = timeout };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timeoutDecision();
            window.Close();
        };
        window.Closed += (_, _) =>
        {
            timer.Stop();
            completion.TrySetResult();
        };
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            _ = window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (window.IsVisible)
                    window.Close();
                else
                    completion.TrySetCanceled(cancellationToken);
            }));
        });

        try
        {
            timer.Start();
            window.Show();
            await completion.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            await window.Dispatcher.InvokeAsync(() =>
            {
                timer.Stop();
                if (window.IsVisible)
                    window.Close();
            }).Task.ConfigureAwait(false);
        }
    }

    private static Window CreateWindow(string title, UIElement content)
    {
        var window = new Window
        {
            Title = title,
            Content = content,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 600,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false,
            Background = SystemColors.WindowBrush,
            Foreground = SystemColors.WindowTextBrush
        };
        AutomationProperties.SetName(window, title);
        if (Application.Current?.MainWindow is { } owner && owner != window)
            window.Owner = owner;
        return window;
    }

    private static void AddRiskBadge(Panel parent, string text)
    {
        var badge = new Border
        {
            BorderBrush = SystemColors.WindowTextBrush,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 14),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = SystemColors.WindowTextBrush
            }
        };
        AutomationProperties.SetName(badge, $"Risk: {text}");
        parent.Children.Add(badge);
    }

    private static void AddText(Panel parent, string text) =>
        parent.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4)
        });

    private static void AddReadOnlyText(Panel parent, string text) =>
        parent.Children.Add(new TextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 180,
            Margin = new Thickness(0, 4, 0, 10)
        });

    private static Button CreateButton(string label, string automationName, bool isDefault, bool isCancel)
    {
        var button = new Button
        {
            Content = label,
            IsDefault = isDefault,
            IsCancel = isCancel,
            MinWidth = 110,
            MinHeight = 36,
            Margin = new Thickness(5, 12, 0, 0),
            Padding = new Thickness(10, 4, 10, 4)
        };
        AutomationProperties.SetName(button, automationName);
        return button;
    }
}
