using CDriveSmartClean.Application.Scanning.Volumes;

namespace CDriveSmartClean.Application.Scanning.Enumeration;

/// <summary>Enumerates immediate children within the supplied system-volume scope.</summary>
public interface IStorageEnumerator
{
    Task EnumerateRootAsync(
        SystemVolumeDescriptor systemVolume,
        IStorageEntrySink entrySink,
        CancellationToken cancellationToken);

    Task EnumerateChildrenAsync(
        SystemVolumeDescriptor systemVolume,
        StorageEntry directory,
        IStorageEntrySink entrySink,
        CancellationToken cancellationToken);
}
