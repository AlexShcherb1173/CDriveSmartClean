using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CDriveSmartClean.Platform.Windows.Interop;

internal static class Kernel32FileIdentityNative
{
    internal const uint OpenExisting = 3;
    internal const uint IdentityOpenFlags = 0x02000000 | 0x00200000; // BACKUP_SEMANTICS | OPEN_REPARSE_POINT
    internal const uint ObservationShare = 1 | 2 | 4; // READ | WRITE | DELETE
    internal const uint TraversalShare = 1 | 2; // READ | WRITE, deliberately deny DELETE
    internal const uint DirectoryListAccess = 1; // FILE_LIST_DIRECTORY participates in share checking

    internal enum FileInfoByHandleClass
    {
        FileAttributeTagInfo = 9,
        FileIdInfo = 0x12,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileId128
    {
        internal ulong Low;
        internal ulong High;

        internal readonly Guid ToOpaqueGuid()
        {
            Span<byte> bytes = stackalloc byte[16];
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, Low);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], High);
            // Preserve native bytes, not RFC UUID semantics. Guid is only a carrier.
            return new Guid(bytes);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileIdInfo
    {
        internal ulong VolumeSerialNumber;
        internal FileId128 FileId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileAttributeTagInfo
    {
        internal uint FileAttributes;
        internal uint ReparseTag;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, nint securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileIdInfo(
        SafeFileHandle file, FileInfoByHandleClass informationClass, out FileIdInfo information, uint size);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetAttributeTagInfo(
        SafeFileHandle file, FileInfoByHandleClass informationClass, out FileAttributeTagInfo information, uint size);
}
