using System.Windows;

namespace Jarvis.UI;

/// <summary>Main WPF task, evidence, audit history, and read-only settings surface.</summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    /// <summary>Creates the UI around the shared orchestration host view model.</summary>
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
    }

    private async void RunTask_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RunPromptAsync(PromptInput.Text);
        if (_viewModel.MailResults.Count > 0
            || GmailPromptParser.TryParse(PromptInput.Text, out _)
            || GmailPromptParser.IsExplainPrompt(PromptInput.Text))
            MailTab.IsSelected = true;
    }

    private async void ExplainMail_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.ExplainSelectedMailAsync();
    }

    private void CancelTask_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CancelCurrentTask();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel.GoogleAccount is { } googleAccount)
            await googleAccount.LoadAsync();
    }

    private async void ConnectGoogle_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.GoogleAccount is { } googleAccount)
            await googleAccount.ConnectAsync();
    }

    private async void DisconnectGoogle_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.GoogleAccount is { } googleAccount)
            await googleAccount.DisconnectAsync();
    }

    private async void RefreshCalendar_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.GoogleWorkspace is { } workspace)
            await workspace.LoadCalendarAsync();
    }

    private async void RefreshGmail_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.GoogleWorkspace is { } workspace)
            await workspace.LoadGmailAsync();
    }
}
