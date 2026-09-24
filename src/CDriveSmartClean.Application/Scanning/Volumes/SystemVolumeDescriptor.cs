using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Application.Scanning.Volumes;

public sealed class SystemVolumeDescriptor
{
    public SystemVolumeDescriptor(VolumeIdentity volumeIdentity, string rootPath)
    {
        ArgumentNullException.ThrowIfNull(volumeIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        VolumeIdentity = volumeIdentity;
        RootPath = rootPath;
    }

    public VolumeIdentity VolumeIdentity { get; }

    public string RootPath { get; }
}
