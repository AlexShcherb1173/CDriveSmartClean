using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CDriveSmartClean.Desktop.Localization;
using CDriveSmartClean.Desktop.Presentation;
using CDriveSmartClean.Desktop.ViewModels;
using CDriveSmartClean.Runtime;

namespace CDriveSmartClean.Desktop;

internal partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var preferenceStore = new LanguagePreferenceStore();
        CultureInfo uiCulture = DesktopCulture.Select(preferenceStore.Load(), CultureInfo.CurrentUICulture);
        DesktopCulture.Apply(uiCulture);
        base.OnStartup(e);
        var localizer = new PresentationLocalizer(uiCulture);
        var workflow = new SystemVolumeScanWorkflow();
        SynchronizationContext uiContext = SynchronizationContext.Current ??
            new DispatcherSynchronizationContext(Dispatcher);
        var viewModel = new MainWindowViewModel(workflow.ScanAsync, uiContext, localizer,
            preferenceStore.TrySave, uiCulture.Name);
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
    }
}
