using System.Windows;
using CDriveSmartClean.Desktop.ViewModels;

namespace CDriveSmartClean.Desktop;

internal partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;

    internal MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        viewModel.Dispose();
        base.OnClosed(e);
    }
}
