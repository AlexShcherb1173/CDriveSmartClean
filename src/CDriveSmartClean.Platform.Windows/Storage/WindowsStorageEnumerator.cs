using System.Runtime.Versioning;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Application.Scanning.Traversal;
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

        await EnumerateDirectoryAsync(systemVolume, systemVolume.RootPath, entrySink, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnumerateChildrenAsync(
        SystemVolumeDescriptor systemVolume,
        StorageEntry directory,
        IStorageEntrySink entrySink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(systemVolume);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(entrySink);
        cancellationToken.ThrowIfCancellationRequested();
        if (!directory.VolumeIdentity.Equals(systemVolume.VolumeIdentity))
        {
            throw new ArgumentException("Directory must belong to the system volume.", nameof(directory));
        }

        if (directory.ObjectKind != StorageObjectKind.Directory || directory.ReparseKind != ReparseKind.None)
        {
            throw new ArgumentException("Only an ordinary non-reparse directory may be enumerated.", nameof(directory));
        }

        string rootPath = Path.GetFullPath(systemVolume.RootPath);
        string childPath = Path.GetFullPath(directory.CanonicalPath);
        string relative = Path.GetRelativePath(rootPath, childPath);
        if (relative == "." || relative == ".." || Path.IsPathRooted(relative) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Directory must be strictly within the system-volume root.", nameof(directory));
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Lexical scope and current attributes are not native object identity or a race-free guarantee.
        FileAttributes attributes = File.GetAttributes(childPath);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new StorageTraversalTargetChangedException(directory.CanonicalPath);
        }

        await EnumerateDirectoryAsync(systemVolume, childPath, entrySink, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnumerateDirectoryAsync(
        SystemVolumeDescriptor systemVolume,
        string directoryPath,
        IStorageEntrySink entrySink,
        CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0,
        };
        var root = new DirectoryInfo(directoryPath);
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
