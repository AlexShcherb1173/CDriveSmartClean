using CDriveSmartClean.Application.Scanning.Volumes;

namespace CDriveSmartClean.Application.Scanning.Enumeration;

/// <summary>Enumerates only immediate children of the supplied system-volume root.</summary>
public interface IStorageEnumerator
{
    Task EnumerateRootAsync(
        SystemVolumeDescriptor systemVolume,
        IStorageEntrySink entrySink,
        CancellationToken cancellationToken);
}
