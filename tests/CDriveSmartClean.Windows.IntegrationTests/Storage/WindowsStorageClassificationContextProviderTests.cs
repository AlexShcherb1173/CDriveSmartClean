using System.Reflection;
using System.Runtime.InteropServices;
using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Platform.Windows.Storage;
using Xunit;

namespace CDriveSmartClean.Windows.IntegrationTests.Storage;

public sealed class WindowsStorageClassificationContextProviderTests
{
    [Fact]
    public void DiscoversMandatoryWindowsDirectoryWithoutDriveLetterAssumption()
    {
        SystemVolumeDescriptor system = new WindowsSystemVolumeProvider().GetSystemVolume();
        StorageClassificationContext context =
            new WindowsStorageClassificationContextProvider().GetClassificationContext(system);
        Assert.Equal(system.VolumeIdentity, context.VolumeIdentity);
        Assert.StartsWith(system.RootPath, context.WindowsDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('/', context.WindowsDirectory);
    }

    [Fact]
    public void DiscoversAvailableKnownFoldersAsAbsoluteNativePaths()
    {
        SystemVolumeDescriptor system = new WindowsSystemVolumeProvider().GetSystemVolume();
        StorageClassificationContext context =
            new WindowsStorageClassificationContextProvider().GetClassificationContext(system);
        IEnumerable<string> roots = context.ProgramFilesRoots.Concat(context.CurrentUserAppDataRoots)
            .Concat(new[]
            {
                context.ProgramDataRoot, context.CurrentUserProfileRoot,
                context.PublicRoot, context.UserProfilesRoot,
            }.OfType<string>());
        Assert.NotEmpty(context.ProgramFilesRoots);
        Assert.All(roots, root =>
        {
            Assert.True(Path.IsPathFullyQualified(root));
            Assert.DoesNotContain('/', root);
        });
    }

    [Fact]
    public void SuppliedForeignVolumeContradictionFails()
    {
        SystemVolumeDescriptor trusted = new WindowsSystemVolumeProvider().GetSystemVolume();
        var foreign = new SystemVolumeDescriptor(new VolumeIdentity(Guid.NewGuid()), trusted.RootPath);
        Assert.Throws<InvalidOperationException>(() =>
            new WindowsStorageClassificationContextProvider().GetClassificationContext(foreign));
    }

    [Fact]
    public void SuppliedRootContradictionFails()
    {
        SystemVolumeDescriptor trusted = new WindowsSystemVolumeProvider().GetSystemVolume();
        string wrongRoot = trusted.RootPath.Equals(@"C:\", StringComparison.OrdinalIgnoreCase) ? @"D:\" : @"C:\";
        var contradicted = new SystemVolumeDescriptor(trusted.VolumeIdentity, wrongRoot);
        Assert.Throws<InvalidOperationException>(() =>
            new WindowsStorageClassificationContextProvider().GetClassificationContext(contradicted));
    }

    [Fact]
    public void ShellBoundaryContainsExactlyOneProductionPInvoke()
    {
        Type native = typeof(WindowsStorageClassificationContextProvider).Assembly.GetType(
            "CDriveSmartClean.Platform.Windows.Interop.Shell32KnownFolderNative", throwOnError: true)!;
        MethodInfo[] methods = native.GetMethods(BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo method = Assert.Single(methods,
            item => item.GetCustomAttribute<DllImportAttribute>() is not null);
        Assert.Equal("SHGetKnownFolderPath", method.Name);
        DllImportAttribute attribute = method.GetCustomAttribute<DllImportAttribute>()!;
        Assert.Equal("shell32.dll", attribute.Value, ignoreCase: true);
    }

    [Fact]
    public void OptionalKnownFolderFailureProducesIncompleteContext()
    {
        SystemVolumeDescriptor trusted = new WindowsSystemVolumeProvider().GetSystemVolume();
        WindowsStorageClassificationContextProvider provider = CreateProvider(
            trusted, _ => null, _ => trusted.VolumeIdentity);
        StorageClassificationContext context = provider.GetClassificationContext(trusted);
        Assert.False(context.IsComplete);
        Assert.Empty(context.ProgramFilesRoots);
        Assert.Contains("ProgramData", context.UnavailableContextKeys);
    }

    [Fact]
    public void ForeignVolumeKnownFoldersAreOmitted()
    {
        SystemVolumeDescriptor trusted = new WindowsSystemVolumeProvider().GetSystemVolume();
        var foreign = new VolumeIdentity(Guid.NewGuid());
        WindowsStorageClassificationContextProvider provider = CreateProvider(
            trusted, _ => @"D:\Foreign",
            path => path.Equals(@"C:\Windows", StringComparison.OrdinalIgnoreCase)
                ? trusted.VolumeIdentity : foreign);
        StorageClassificationContext context = provider.GetClassificationContext(trusted);
        Assert.Empty(context.ProgramFilesRoots);
        Assert.Null(context.ProgramDataRoot);
        Assert.False(context.IsComplete);
    }

    [Fact]
    public void MandatoryWindowsDirectoryVolumeContradictionFailsThroughSeam()
    {
        SystemVolumeDescriptor trusted = new WindowsSystemVolumeProvider().GetSystemVolume();
        WindowsStorageClassificationContextProvider provider = CreateProvider(
            trusted, _ => null, _ => new VolumeIdentity(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => provider.GetClassificationContext(trusted));
    }

    [Fact]
    public void ContextAcquisitionDoesNotCreateOrReadUserFiles()
    {
        SystemVolumeDescriptor system = new WindowsSystemVolumeProvider().GetSystemVolume();
        string sentinel = Path.Combine(Path.GetTempPath(), $"f1-14-{Guid.NewGuid():N}");
        Assert.False(File.Exists(sentinel));
        _ = new WindowsStorageClassificationContextProvider().GetClassificationContext(system);
        Assert.False(File.Exists(sentinel));
    }

    private static WindowsStorageClassificationContextProvider CreateProvider(
        SystemVolumeDescriptor trusted,
        Func<Guid, string?> knownFolder,
        Func<string, VolumeIdentity?> volumeIdentity)
    {
        ConstructorInfo constructor = Assert.Single(
            typeof(WindowsStorageClassificationContextProvider).GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic));
        return (WindowsStorageClassificationContextProvider)constructor.Invoke(
        [
            (Func<SystemVolumeDescriptor>)(() => trusted),
            (Func<string>)(() => @"C:\Windows"),
            knownFolder,
            volumeIdentity,
        ]);
    }
}
