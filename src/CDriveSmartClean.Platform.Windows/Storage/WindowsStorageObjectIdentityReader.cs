using System.ComponentModel;
using System.Runtime.InteropServices;
using CDriveSmartClean.Application.Scanning.Identity;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Platform.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace CDriveSmartClean.Platform.Windows.Storage;

internal static class WindowsStorageObjectIdentityReader
{
    internal static StorageObjectIdentity? TryRead(VolumeIdentity volume, string path)
    {
        try
        {
            using var handle = Open(path, 0, Kernel32FileIdentityNative.ObservationShare);
            return ReadIdentity(handle, volume, path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static SafeFileHandle OpenPinnedDirectory(StorageObjectIdentity expected, string path)
    {
        // A zero-access metadata handle does not enforce delete-sharing exclusion on Windows.
        // Descent needs FILE_LIST_DIRECTORY (read-only), not write/delete or elevated privileges.
        var handle = Open(path, Kernel32FileIdentityNative.DirectoryListAccess, Kernel32FileIdentityNative.TraversalShare);
        try
        {
            if (!Kernel32FileIdentityNative.GetAttributeTagInfo(handle,
                Kernel32FileIdentityNative.FileInfoByHandleClass.FileAttributeTagInfo, out var attributes,
                (uint)Marshal.SizeOf<Kernel32FileIdentityNative.FileAttributeTagInfo>()))
            {
                int error = Marshal.GetLastPInvokeError();
                throw NativeFailure(error, path);
            }

            if ((attributes.FileAttributes & (uint)FileAttributes.Directory) == 0 ||
                (attributes.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0)
            {
                throw new StorageTraversalTargetChangedException(path);
            }

            var actual = ReadIdentity(handle, expected.VolumeIdentity, path);
            if (!actual.Equals(expected))
            {
                throw new StorageTraversalTargetChangedException(path);
            }

            // Caller owns this lease through immediate-child enumeration, including awaited sinks.
            // Contents remain live; this is not a transactional filesystem snapshot.
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static SafeFileHandle Open(string path, uint access, uint share)
    {
        var handle = Kernel32FileIdentityNative.CreateFileW(path, access, share, 0,
            Kernel32FileIdentityNative.OpenExisting, Kernel32FileIdentityNative.IdentityOpenFlags, 0);
        int error = Marshal.GetLastPInvokeError();
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw NativeFailure(error, path);
        }

        return handle;
    }

    private static StorageObjectIdentity ReadIdentity(SafeFileHandle handle, VolumeIdentity volume, string path)
    {
        if (!Kernel32FileIdentityNative.GetFileIdInfo(handle,
            Kernel32FileIdentityNative.FileInfoByHandleClass.FileIdInfo, out var info,
            (uint)Marshal.SizeOf<Kernel32FileIdentityNative.FileIdInfo>()))
        {
            int error = Marshal.GetLastPInvokeError();
            throw NativeFailure(error, path);
        }

        return CreateIdentity(volume, info.FileId.ToOpaqueGuid(), path);
    }

    private static StorageObjectIdentity CreateIdentity(VolumeIdentity volume, Guid objectId, string path) =>
        objectId == Guid.Empty ? throw new StorageObjectIdentityUnavailableException(path) : new(volume, objectId);

    private static Exception NativeFailure(int error, string path) => error switch
    {
        2 => new FileNotFoundException("Native identity target was not found.", path),
        3 => new DirectoryNotFoundException("Native identity parent path was not found: " + path),
        5 => new UnauthorizedAccessException("Native identity access was denied: " + path),
        1 or 50 or 87 => new StorageObjectIdentityUnavailableException(path),
        _ => new IOException("Native identity query failed: " + path, new Win32Exception(error)),
    };
}
