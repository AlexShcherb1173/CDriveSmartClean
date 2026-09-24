namespace CDriveSmartClean.Application.Scanning.Volumes;

public interface ISystemVolumeProvider
{
    SystemVolumeDescriptor GetSystemVolume();
}
