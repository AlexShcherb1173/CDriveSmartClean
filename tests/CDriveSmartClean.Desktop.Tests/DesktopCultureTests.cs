using System.Globalization;
using CDriveSmartClean.Desktop.Localization;
using Xunit;

namespace CDriveSmartClean.Desktop.Tests;

public sealed class DesktopCultureTests
{
    [Theory]
    [InlineData(null, "ru-RU", "ru")]
    [InlineData(null, "en-US", "en")]
    [InlineData(null, "de-DE", "en")]
    [InlineData("en", "ru-RU", "en")]
    [InlineData("ru", "en-US", "ru")]
    [InlineData("invalid", "ru-RU", "ru")]
    public void SelectionFollowsPreferenceThenWindowsPolicy(string? preference, string windowsCulture, string expected) =>
        Assert.Equal(expected, DesktopCulture.Select(preference, CultureInfo.GetCultureInfo(windowsCulture)).Name);

    [Fact]
    public void ApplyChangesOnlyUiCulture()
    {
        CultureInfo originalUi = CultureInfo.CurrentUICulture;
        CultureInfo? originalDefaultUi = CultureInfo.DefaultThreadCurrentUICulture;
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo? originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        try
        {
            DesktopCulture.Apply(CultureInfo.GetCultureInfo("ru"));
            Assert.Equal("ru", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("ru", CultureInfo.DefaultThreadCurrentUICulture?.Name);
            Assert.Equal(originalCulture.Name, CultureInfo.CurrentCulture.Name);
            Assert.Equal(originalDefaultCulture?.Name, CultureInfo.DefaultThreadCurrentCulture?.Name);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUi;
        }
    }
}
