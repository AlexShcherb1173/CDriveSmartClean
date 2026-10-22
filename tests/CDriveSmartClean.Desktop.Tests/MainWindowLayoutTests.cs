using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using CDriveSmartClean.Desktop.Presentation;
using CDriveSmartClean.Desktop.Resources;
using CDriveSmartClean.Desktop.ViewModels;
using CDriveSmartClean.Runtime;
using Xunit;

namespace CDriveSmartClean.Desktop.Tests;

public sealed class MainWindowLayoutTests
{
    [Fact]
    public void RussianMinimumLayoutKeepsCriticalControlsUsable()
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Strings.Culture = CultureInfo.GetCultureInfo("ru");
                var localizer = new PresentationLocalizer(Strings.Culture);
                using var viewModel = new MainWindowViewModel(
                    (_, _, _) => Task.FromResult<ProductScanResult>(null!), new SynchronizationContext(),
                    localizer, _ => true, "ru");
                var window = new MainWindow(viewModel);
                var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);

                content.Measure(new Size(900, 600));
                content.Arrange(new Rect(0, 0, 900, 600));
                content.UpdateLayout();

                Assert.True(window.SafetyBannerText.ActualWidth > 0);
                Assert.True(window.SafetyBannerText.ActualHeight >= window.SafetyBannerText.DesiredSize.Height);
                Assert.True(window.StartScanButton.ActualWidth >= 110);
                Assert.True(window.CancelButton.ActualWidth >= 90);
                Assert.True(window.LanguageSelector.ActualWidth >= 100);
                Assert.True(window.TopControls.ActualHeight >= window.LanguageSelector.ActualHeight);
                Assert.True(window.MainTabs.ActualHeight > 200);
                Assert.Equal(2, window.MainTabs.Items.Count);
                Assert.Equal(12, window.FindingsGrid.Columns.Count);
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                Strings.Culture = null;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF layout verification timed out.");
        failure?.Throw();
    }
}
