using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Platform.Windows.Interop;

namespace CDriveSmartClean.Platform.Windows.Storage;

[SupportedOSPlatform("windows")]
public sealed class WindowsSystemVolumeProvider : ISystemVolumeProvider
{
    public SystemVolumeDescriptor GetSystemVolume()
    {
        var windowsBuffer = new char[260];
        uint length = Kernel32VolumeNative.GetSystemWindowsDirectoryW(windowsBuffer, (uint)windowsBuffer.Length);
        if (length == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        if (length >= windowsBuffer.Length)
        {
            windowsBuffer = new char[checked((int)length + 1)];
            length = Kernel32VolumeNative.GetSystemWindowsDirectoryW(windowsBuffer, (uint)windowsBuffer.Length);
            if (length == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            if (length >= windowsBuffer.Length)
            {
                throw new InvalidOperationException("Windows directory exceeded the native output buffer.");
            }
        }

        string windowsDirectory = new(windowsBuffer, 0, checked((int)length));
        var rootBuffer = new char[32768];
        if (!Kernel32VolumeNative.GetVolumePathNameW(windowsDirectory, rootBuffer, (uint)rootBuffer.Length))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        string rootPath = ReadNativeString(rootBuffer);
        var volumeBuffer = new char[50];
        if (!Kernel32VolumeNative.GetVolumeNameForVolumeMountPointW(rootPath, volumeBuffer, (uint)volumeBuffer.Length))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        string volumeName = ReadNativeString(volumeBuffer);
        const string prefix = @"\\?\Volume";
        if (volumeName.Length != 49 ||
            !volumeName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            volumeName[^1] != '\\' ||
            !Guid.TryParseExact(volumeName.AsSpan(prefix.Length, 38), "B", out Guid volumeGuid) ||
            volumeGuid == Guid.Empty)
        {
            throw new InvalidOperationException("Windows returned an invalid volume GUID path.");
        }

        return new SystemVolumeDescriptor(new VolumeIdentity(volumeGuid), rootPath);
    }

    private static string ReadNativeString(char[] buffer)
    {
        int length = Array.IndexOf(buffer, '\0');
        if (length <= 0)
        {
            throw new InvalidOperationException("Windows returned an empty or unterminated path.");
        }

        return new string(buffer, 0, length);
    }
}
