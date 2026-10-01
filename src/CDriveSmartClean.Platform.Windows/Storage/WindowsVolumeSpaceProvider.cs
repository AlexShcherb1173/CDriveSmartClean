using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Platform.Windows.Interop;

namespace CDriveSmartClean.Platform.Windows.Storage;

[SupportedOSPlatform("windows")]
public sealed class WindowsVolumeSpaceProvider : IVolumeSpaceProvider
{
    private readonly Func<SystemVolumeDescriptor> discover;
    private readonly Func<string, (int HResult, int Error, ulong[] Values)> query;

    public WindowsVolumeSpaceProvider() : this(new WindowsSystemVolumeProvider().GetSystemVolume, Query) { }

    // Instance-only test seam. Public construction always uses authoritative discovery and the native API.
    internal WindowsVolumeSpaceProvider(Func<SystemVolumeDescriptor> discover,
        Func<string, (int HResult, int Error, ulong[] Values)> query)
    {
        this.discover = discover;
        this.query = query;
    }

    public VolumeSpaceSnapshot GetVolumeSpace(SystemVolumeDescriptor systemVolume)
    {
        ArgumentNullException.ThrowIfNull(systemVolume);
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        try
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
                return VolumeSpaceSnapshot.Unavailable(systemVolume.VolumeIdentity, timestamp, VolumeSpaceFailure.UnsupportedPlatform);
            SystemVolumeDescriptor trusted = discover();
            if (!trusted.VolumeIdentity.Equals(systemVolume.VolumeIdentity) ||
                !string.Equals(trusted.RootPath.TrimEnd('\\'), systemVolume.RootPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Caller descriptor contradicts the trusted system volume.");
            string root = @"\\?\Volume" + trusted.VolumeIdentity.Id.ToString("B") + "\\";
            var response = query(root);
            if (response.HResult != 0)
            {
                int error = response.HResult < 0 && (response.HResult & 0xFFFF0000) == unchecked((int)0x80070000)
                    ? response.HResult & 0xFFFF : response.Error;
                return VolumeSpaceSnapshot.Unavailable(trusted.VolumeIdentity, timestamp, Failure(error), response.HResult, response.Error);
            }
            return Convert(trusted.VolumeIdentity, timestamp, response.Values);
        }
        catch (EntryPointNotFoundException)
        {
            return VolumeSpaceSnapshot.Unavailable(systemVolume.VolumeIdentity, timestamp, VolumeSpaceFailure.UnsupportedPlatform);
        }
        catch (DllNotFoundException)
        {
            return VolumeSpaceSnapshot.Unavailable(systemVolume.VolumeIdentity, timestamp, VolumeSpaceFailure.UnsupportedPlatform);
        }
        catch (Win32Exception exception)
        {
            return VolumeSpaceSnapshot.Unavailable(systemVolume.VolumeIdentity, timestamp, Failure(exception.NativeErrorCode), exception.NativeErrorCode);
        }
    }

    private static VolumeSpaceFailure Failure(int error) => error switch
    {
        5 => VolumeSpaceFailure.AccessDenied,
        2 or 3 or 15 or 21 or 55 => VolumeSpaceFailure.VolumeUnavailable,
        50 or 120 or 127 => VolumeSpaceFailure.UnsupportedPlatform,
        _ => VolumeSpaceFailure.NativeFailure,
    };

    private static (int HResult, int Error, ulong[] Values) Query(string root)
    {
        int result = Kernel32VolumeNative.GetDiskSpaceInformationW(root, out var info);
        int error = Marshal.GetLastPInvokeError();
        return (result, error, [info.ActualTotalAllocationUnits, info.ActualAvailableAllocationUnits,
            info.ActualPoolUnavailableAllocationUnits, info.CallerTotalAllocationUnits, info.CallerAvailableAllocationUnits,
            info.CallerPoolUnavailableAllocationUnits, info.UsedAllocationUnits, info.TotalReservedAllocationUnits,
            info.VolumeStorageReserveAllocationUnits, info.AvailableCommittedAllocationUnits, info.PoolAvailableAllocationUnits,
            info.SectorsPerAllocationUnit, info.BytesPerSector]);
    }

    private static VolumeSpaceSnapshot Convert(VolumeIdentity identity, DateTimeOffset timestamp, ulong[] values)
    {
        if (values.Length != 13 || values[11] == 0 || values[12] == 0 || values[1] > values[0] ||
            values[4] > values[3] || values[3] > values[0] || values[6] > values[0] || values[8] > values[7])
            return VolumeSpaceSnapshot.Unavailable(identity, timestamp, VolumeSpaceFailure.InvalidNativeData);
        try
        {
            ulong unit = checked(values[11] * values[12]);
            long Bytes(int index) => checked((long)checked(values[index] * unit));
            return VolumeSpaceSnapshot.Available(identity, timestamp, Bytes(0), Bytes(1), Bytes(3), Bytes(4), Bytes(6), Bytes(7), Bytes(8));
        }
        catch (OverflowException)
        {
            return VolumeSpaceSnapshot.Unavailable(identity, timestamp, VolumeSpaceFailure.Overflow);
        }
    }
}
