using System.Runtime.InteropServices;

namespace CDriveSmartClean.Platform.Windows.Interop;

internal static class Kernel32VolumeNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct DiskSpaceInformation
    {
        internal ulong ActualTotalAllocationUnits;
        internal ulong ActualAvailableAllocationUnits;
        internal ulong ActualPoolUnavailableAllocationUnits;
        internal ulong CallerTotalAllocationUnits;
        internal ulong CallerAvailableAllocationUnits;
        internal ulong CallerPoolUnavailableAllocationUnits;
        internal ulong UsedAllocationUnits;
        internal ulong TotalReservedAllocationUnits;
        internal ulong VolumeStorageReserveAllocationUnits;
        internal ulong AvailableCommittedAllocationUnits;
        internal ulong PoolAvailableAllocationUnits;
        internal uint SectorsPerAllocationUnit;
        internal uint BytesPerSector;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetDiskSpaceInformationW(string rootPath, out DiskSpaceInformation information);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint GetSystemWindowsDirectoryW([Out] char[] buffer, uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetVolumePathNameW(string fileName, [Out] char[] volumePathName, uint bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetVolumeNameForVolumeMountPointW(string volumeMountPoint, [Out] char[] volumeName, uint bufferLength);
}
