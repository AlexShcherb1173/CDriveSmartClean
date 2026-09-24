using CDriveSmartClean.Platform.Windows.Storage;
using Xunit;

namespace CDriveSmartClean.Windows.IntegrationTests.Storage;

public sealed class WindowsSystemVolumeProviderTests
{
    [Fact]
    public void ActualSystemVolumeHasValidIdentityAndFullyQualifiedRoot()
    {
        var result = new WindowsSystemVolumeProvider().GetSystemVolume();

        Assert.NotNull(result);
        Assert.NotNull(result.VolumeIdentity);
        Assert.NotEqual(Guid.Empty, result.VolumeIdentity.Id);
        Assert.False(string.IsNullOrWhiteSpace(result.RootPath));
        Assert.True(Path.IsPathFullyQualified(result.RootPath));
    }

    [Fact]
    public void DiscoveredRootExists()
    {
        var result = new WindowsSystemVolumeProvider().GetSystemVolume();

        Assert.True(Directory.Exists(result.RootPath));
    }

    [Fact]
    public void WindowsSystemDirectoryIsWithinDiscoveredRoot()
    {
        var result = new WindowsSystemVolumeProvider().GetSystemVolume();
        string root = Path.EndsInDirectorySeparator(result.RootPath)
            ? result.RootPath
            : result.RootPath + Path.DirectorySeparatorChar;

        Assert.StartsWith(root, Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiscoveryIsStable()
    {
        var provider = new WindowsSystemVolumeProvider();
        var first = provider.GetSystemVolume();
        var second = provider.GetSystemVolume();

        Assert.Equal(first.VolumeIdentity, second.VolumeIdentity);
        Assert.Equal(first.RootPath, second.RootPath, StringComparer.OrdinalIgnoreCase);
    }
}
