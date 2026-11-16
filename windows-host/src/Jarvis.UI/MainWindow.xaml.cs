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
        BrowserWorkspace.ChatPlanner = (text, page, token) => viewModel.PlanBrowserChatAsync(text, page, token);
        BrowserWorkspace.ChatAuthorizer = (query, token) => viewModel.AuthorizeBrowserSearchAsync(query, token);
        BrowserWorkspace.ChatLinkAuthorizer = (disclosure, link, tabId, token) =>
            viewModel.AuthorizeBrowserLinkAsync(disclosure, link, tabId, token);
        BrowserWorkspace.ChatAuthority = () => viewModel.BrowserAuthority;
    }

    private async void RunTask_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserPromptParser.TryParseSearch(PromptInput.Text, out var query))
        {
            BrowserTab.IsSelected = true;
            BrowserWorkspace.ShowStatus("Preparing a Google search with the local Ollama model...");
            var search = await _viewModel.PrepareBrowserSearchAsync(query);
            if (search.Address is not null)
            {
                if (await BrowserWorkspace.OpenInNewTabAsync(search.Address))
                    BrowserWorkspace.ShowStatus(search.StatusMessage ?? "Google results opened. Choose a result.");
                return;
            }

            BrowserWorkspace.ShowStatus(search.StatusMessage ?? "The browser search could not be prepared.");
            return;
        }

        if (BrowserPromptParser.TryParse(PromptInput.Text, out var target))
        {
            BrowserTab.IsSelected = true;
            BrowserWorkspace.ShowStatus(BrowserAddressPolicy.LooksLikeExplicitAddress(target)
                ? "Opening the requested website..."
                : "Resolving the requested website with the local Ollama model...");
            var address = await _viewModel.ResolveBrowserTargetAsync(target);
            if (address is not null)
            {
                await BrowserWorkspace.OpenInNewTabAsync(address);
                return;
            }

            BrowserWorkspace.ShowStatus(_viewModel.ErrorMessage ?? "The browser request could not be resolved.");
            return;
        }

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
