using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Application.Scanning.Volumes;

public interface IVolumeSpaceProvider
{
    VolumeSpaceSnapshot GetVolumeSpace(SystemVolumeDescriptor systemVolume);
}
