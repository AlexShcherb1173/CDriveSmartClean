using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CDriveSmartClean.Platform.Windows.Interop;

internal static class NtFileNative
{
    internal const uint OpenOptions = 0x00600020; // NO_RECALL | OPEN_REPARSE_POINT | SYNCHRONOUS_IO_NONALERT

    [StructLayout(LayoutKind.Sequential)]
    internal struct UnicodeString
    {
        internal ushort Length;
        internal ushort MaximumLength;
        internal nint Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ObjectAttributes
    {
        internal uint Length;
        internal nint RootDirectory;
        internal nint ObjectName;
        internal uint Attributes;
        internal nint SecurityDescriptor;
        internal nint SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IoStatusBlock
    {
        internal nint Status;
        internal nuint Information;
    }

    [DllImport("ntdll.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int NtCreateFile(out SafeFileHandle handle, uint desiredAccess,
        ref ObjectAttributes attributes, out IoStatusBlock status, nint allocationSize,
        uint fileAttributes, uint shareAccess, uint disposition, uint options, nint eaBuffer, uint eaLength);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint RtlNtStatusToDosError(int status);
}
