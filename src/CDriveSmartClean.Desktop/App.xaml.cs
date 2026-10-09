using System.Windows;
using System.Windows.Threading;
using CDriveSmartClean.Desktop.ViewModels;
using CDriveSmartClean.Runtime;

namespace CDriveSmartClean.Desktop;

internal partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var workflow = new SystemVolumeScanWorkflow();
        SynchronizationContext uiContext = SynchronizationContext.Current ??
            new DispatcherSynchronizationContext(Dispatcher);
        var viewModel = new MainWindowViewModel(workflow.ScanAsync, uiContext);
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
    }
}
