using System.Windows;
using Jarvis.Composition;
using Jarvis.UI.Handlers;

namespace Jarvis.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var composition = new JarvisSessionFactory();
            var presenter = new WpfDecisionDialogPresenter();
            var dispatcher = new WpfUiDispatcher(Dispatcher);
            var approval = new WpfApprovalHandler(
                dispatcher,
                presenter,
                resolvedImagePath: composition.ResolveImagePath);
            var escalation = new WpfEscalationHandler(dispatcher, presenter);
            MainWindow = new MainWindow(new MainWindowViewModel(composition, approval, escalation));
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
}
