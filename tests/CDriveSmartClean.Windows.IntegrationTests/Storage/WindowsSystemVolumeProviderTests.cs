using System.Reflection;
using System.Runtime.InteropServices;
using CDriveSmartClean.Platform.Windows.Storage;
using Xunit;

namespace CDriveSmartClean.Windows.IntegrationTests.Storage;

public sealed class WindowsSystemVolumeProviderTests
{
    [Fact]
    public void NativeInteropUsesSharedSystemWindowsDirectoryEntryPoint()
    {
        Type? interopType = typeof(WindowsSystemVolumeProvider).Assembly.GetType(
            "CDriveSmartClean.Platform.Windows.Interop.Kernel32VolumeNative");
        Assert.NotNull(interopType);
        Assert.False(interopType.IsVisible);

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        MethodInfo[] nativeMethods = interopType.GetMethods(flags)
            .Where(method => (method.Attributes & MethodAttributes.PinvokeImpl) != 0)
            .ToArray();
        Assert.Equal(
            ["GetSystemWindowsDirectoryW", "GetVolumeNameForVolumeMountPointW", "GetVolumePathNameW"],
            nativeMethods.Select(method => method.Name).Order(StringComparer.Ordinal));
        Assert.Null(interopType.GetMethod("GetWindowsDirectoryW", flags));

        MethodInfo? sharedMethod = interopType.GetMethod("GetSystemWindowsDirectoryW", flags);
        Assert.NotNull(sharedMethod);
        Assert.True(sharedMethod.IsStatic);
        Assert.Equal(typeof(uint), sharedMethod.ReturnType);
        Assert.Equal(
            [typeof(char[]), typeof(uint)],
            sharedMethod.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.True((sharedMethod.Attributes & MethodAttributes.PinvokeImpl) != 0);

        DllImportAttribute? import = sharedMethod.GetCustomAttribute<DllImportAttribute>();
        Assert.NotNull(import);
        Assert.Equal("kernel32.dll", import.Value);
        Assert.Equal(CharSet.Unicode, import.CharSet);
        Assert.True(import.ExactSpelling);
        Assert.True(import.SetLastError);
    }

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
