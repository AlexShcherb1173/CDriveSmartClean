using CDriveSmartClean.Application.Scanning.Volumes;

namespace CDriveSmartClean.Application.Analysis;

public interface IStorageClassificationContextProvider
{
    StorageClassificationContext GetClassificationContext(SystemVolumeDescriptor systemVolume);
}
