using System.Runtime.Versioning;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Application.Scanning.Volumes;

namespace CDriveSmartClean.Platform.Windows.Storage;

[SupportedOSPlatform("windows")]
public sealed class WindowsStorageEnumerator : IStorageEnumerator
{
    public async Task EnumerateRootAsync(
        SystemVolumeDescriptor systemVolume,
        IStorageEntrySink entrySink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(systemVolume);
        ArgumentNullException.ThrowIfNull(entrySink);
        cancellationToken.ThrowIfCancellationRequested();

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0,
        };
        var root = new DirectoryInfo(systemVolume.RootPath);
        using var children = root.EnumerateFileSystemInfos("*", options).GetEnumerator();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!children.MoveNext())
            {
                break;
            }

            FileSystemInfo child = children.Current;
            FileAttributes attributes = child.Attributes;
            var objectKind = (attributes & FileAttributes.Directory) != 0
                ? StorageObjectKind.Directory
                : StorageObjectKind.File;
            var reparseKind = (attributes & FileAttributes.ReparsePoint) != 0
                ? ReparseKind.Other
                : ReparseKind.None;
            var entry = new StorageEntry(systemVolume.VolumeIdentity, child.FullName, objectKind, reparseKind);
            await entrySink.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }
}
