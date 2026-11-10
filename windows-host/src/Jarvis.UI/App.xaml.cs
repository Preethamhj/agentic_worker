using System.Windows;
using Jarvis.Composition;
using Jarvis.UI.Handlers;

namespace Jarvis.UI;

public partial class App : Application
{
    private GoogleAccountServices? _googleAccountServices;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (ReminderStartup.TryDispatch(e.Args, DispatchReminder, out var exitCode))
            {
                Shutdown(exitCode);
                return;
            }
            var composition = new JarvisSessionFactory();
            _googleAccountServices = composition.CreateGoogleAccountServices();
            var presenter = new WpfDecisionDialogPresenter();
            var dispatcher = new WpfUiDispatcher(Dispatcher);
            var approval = new WpfApprovalHandler(
                dispatcher,
                presenter,
                resolvedImagePath: composition.ResolveImagePath);
            var escalation = new WpfEscalationHandler(dispatcher, presenter);
            MainWindow = new MainWindow(new MainWindowViewModel(
                composition,
                approval,
                escalation,
                _googleAccountServices));
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Jarvis could not start: {exception.Message}",
                "Jarvis startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _googleAccountServices?.Dispose();
        base.OnExit(e);
    }

    private static int DispatchReminder(string[] args)
    {
        var result = ReminderDispatchService.Dispatch(args, (title, message) =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information));
        if (result.Error is not null)
            MessageBox.Show(result.Error, "Jarvis reminder error", MessageBoxButton.OK, MessageBoxImage.Error);
        return result.ExitCode;
    }
}
