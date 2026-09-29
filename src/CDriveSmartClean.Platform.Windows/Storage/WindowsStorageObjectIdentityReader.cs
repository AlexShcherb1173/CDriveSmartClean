using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;
using CDriveSmartClean.Application.Scanning.Identity;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Platform.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace CDriveSmartClean.Platform.Windows.Storage;

internal static class WindowsStorageObjectIdentityReader
{
    // Generic IO and programming failures must never become best-effort null identities.
    private static bool IsExpectedIdentityFailure(Exception exception) =>
        exception is UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException
            or StorageObjectIdentityUnavailableException;

    internal static Kernel32FileIdentityNative.FileIdInfo ReadNative(SafeFileHandle handle, string path)
    {
        if (!Kernel32FileIdentityNative.GetFileIdInfo(handle,
            Kernel32FileIdentityNative.FileInfoByHandleClass.FileIdInfo, out var info,
            (uint)Marshal.SizeOf<Kernel32FileIdentityNative.FileIdInfo>()))
        {
            throw NativeFailure(Marshal.GetLastPInvokeError(), path);
        }

        return info;
    }

    internal static void RequireOrdinaryDirectory(SafeFileHandle handle, string path)
    {
        if (!Kernel32FileIdentityNative.GetAttributeTagInfo(handle,
            Kernel32FileIdentityNative.FileInfoByHandleClass.FileAttributeTagInfo, out var attributes,
            (uint)Marshal.SizeOf<Kernel32FileIdentityNative.FileAttributeTagInfo>()))
        {
            throw NativeFailure(Marshal.GetLastPInvokeError(), path);
        }

        if ((attributes.FileAttributes & 0x410) != 0x10)
        {
            throw new StorageTraversalTargetChangedException(path);
        }
    }

    internal static string ReadGuidPath(SafeFileHandle handle, string path)
    {
        var buffer = new char[32768];
        uint length = Kernel32FileIdentityNative.GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 1);
        if (length == 0)
        {
            throw new WindowsStorageProvenanceException("Opened-handle volume provenance unavailable.", NativeFailure(Marshal.GetLastPInvokeError(), path));
        }

        if (length >= buffer.Length)
        {
            throw new WindowsStorageProvenanceException("Opened-handle volume provenance exceeded its buffer.");
        }

        return new string(buffer, 0, checked((int)length));
    }

    internal static void ValidateProvenance(string volumeRoot, string actualPath, ulong serial, ulong actualSerial)
    {
        // Authority comes from the chain, not from matching a serial on independently opened handles.
        if (!actualPath.StartsWith(volumeRoot, StringComparison.OrdinalIgnoreCase) || serial != actualSerial)
        {
            throw new WindowsStorageProvenanceException("Native handle contradicts trusted volume provenance.");
        }
    }

    internal static StorageObjectIdentity FromVerifiedDirectory(WindowsDirectoryHandleChain chain, Guid id, string path) =>
        CreateIdentity(chain.VolumeIdentity, id, path);

    private static StorageObjectIdentity CreateIdentity(VolumeIdentity volume, Guid objectId, string path) =>
        objectId == Guid.Empty ? throw new StorageObjectIdentityUnavailableException(path) : new(volume, objectId);

    internal static Exception NativeFailure(int error, string path) => error switch
    {
        2 => new FileNotFoundException("Native target was not found.", path),
        3 => new DirectoryNotFoundException("Native parent was not found: " + path),
        5 => new UnauthorizedAccessException("Native access denied: " + path),
        1 or 50 or 87 or 32 => new StorageObjectIdentityUnavailableException(path),
        _ => new IOException("Native query failed: " + path, new Win32Exception(error)),
    };
}
