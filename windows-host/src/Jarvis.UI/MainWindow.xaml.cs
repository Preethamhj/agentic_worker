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
    }

    private void CancelTask_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CancelCurrentTask();
    }
}
