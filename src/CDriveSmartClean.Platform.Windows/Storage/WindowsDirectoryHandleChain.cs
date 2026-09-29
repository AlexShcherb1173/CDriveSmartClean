using System.Runtime.InteropServices;
using System.Security;
using CDriveSmartClean.Application.Scanning.Identity;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Platform.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace CDriveSmartClean.Platform.Windows.Storage;

internal sealed class WindowsDirectoryHandleChain : IDisposable
{
    internal const int MaximumDepth = 256;
    private readonly List<SafeFileHandle> handles = [];
    private readonly string volumeRoot;
    private readonly ulong serial;

    internal WindowsDirectoryHandleChain(VolumeIdentity volume, CancellationToken token)
    {
        WindowsDirectoryEntryReader.VerifyLayout();
        VolumeIdentity = volume;
        volumeRoot = @"\\?\Volume" + volume.Id.ToString("B") + @"\";
        token.ThrowIfCancellationRequested();
        var root = Kernel32FileIdentityNative.CreateFileW(volumeRoot, Kernel32FileIdentityNative.TraversalAccess,
            Kernel32FileIdentityNative.TraversalShare, 0, Kernel32FileIdentityNative.OpenExisting,
            Kernel32FileIdentityNative.IdentityOpenFlags, 0);
        if (root.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            root.Dispose();
            throw WindowsStorageObjectIdentityReader.NativeFailure(error, volumeRoot);
        }

        handles.Add(root);
        try
        {
            token.ThrowIfCancellationRequested();
            WindowsStorageObjectIdentityReader.RequireOrdinaryDirectory(root, volumeRoot);
            token.ThrowIfCancellationRequested();
            var info = WindowsStorageObjectIdentityReader.ReadNative(root, volumeRoot);
            serial = info.VolumeSerialNumber;
            token.ThrowIfCancellationRequested();
            string actual = WindowsStorageObjectIdentityReader.ReadGuidPath(root, volumeRoot);
            if (!string.Equals(actual, volumeRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new WindowsStorageProvenanceException("Opened volume root does not match authoritative GUID root.");
            }

            Identity = RequireIdentity(info.FileId.ToOpaqueGuid(), volumeRoot);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal VolumeIdentity VolumeIdentity { get; }
    internal SafeFileHandle Handle => handles[^1];
    internal StorageObjectIdentity Identity { get; private set; }

    internal static void ValidateComponent(string component)
    {
        if (component.Length == 0 || component is "." or ".." || component.Length > 32767 ||
            component.IndexOfAny(['\\', '/', ':', '\0']) >= 0)
        {
            throw new ArgumentException("An exact literal relative component is required.", nameof(component));
        }
    }

    internal void Append(IEnumerable<string> components, string path, CancellationToken token)
    {
        foreach (string component in components)
        {
            token.ThrowIfCancellationRequested();
            ValidateComponent(component);
            if (handles.Count >= MaximumDepth + 1)
            {
                throw new IOException("Native directory chain exceeds 256 relative components.");
            }

            var next = OpenRelative(Handle, component, path);
            handles.Add(next); // Own unvalidated handles too, for partial-chain disposal.
            token.ThrowIfCancellationRequested();
            WindowsStorageObjectIdentityReader.RequireOrdinaryDirectory(next, path);
            token.ThrowIfCancellationRequested();
            var info = WindowsStorageObjectIdentityReader.ReadNative(next, path);
            token.ThrowIfCancellationRequested();
            string actual = WindowsStorageObjectIdentityReader.ReadGuidPath(next, path);
            WindowsStorageObjectIdentityReader.ValidateProvenance(volumeRoot, actual, serial, info.VolumeSerialNumber);
            Identity = RequireIdentity(info.FileId.ToOpaqueGuid(), path);
        }
    }

    private StorageObjectIdentity RequireIdentity(Guid id, string path) =>
        id == Guid.Empty ? throw new StorageObjectIdentityUnavailableException(path) : new(VolumeIdentity, id);

    internal void RequireExpected(StorageObjectIdentity expected, string path)
    {
        if (!Identity.Equals(expected))
        {
            throw new StorageTraversalTargetChangedException(path);
        }
    }

    private static SafeFileHandle OpenRelative(SafeFileHandle parent, string component, string path)
    {
        nint text = Marshal.StringToHGlobalUni(component);
        nint name = 0;
        bool added = false;
        try
        {
            name = Marshal.AllocHGlobal(Marshal.SizeOf<NtFileNative.UnicodeString>());
            var unicode = new NtFileNative.UnicodeString
            {
                Length = checked((ushort)(component.Length * 2)),
                MaximumLength = checked((ushort)(component.Length * 2)),
                Buffer = text,
            };
            Marshal.StructureToPtr(unicode, name, false);
            parent.DangerousAddRef(ref added);
            var attributes = new NtFileNative.ObjectAttributes
            {
                Length = (uint)Marshal.SizeOf<NtFileNative.ObjectAttributes>(),
                RootDirectory = parent.DangerousGetHandle(),
                ObjectName = name,
            };
            int status = NtFileNative.NtCreateFile(out var handle, Kernel32FileIdentityNative.TraversalAccess,
                ref attributes, out _, 0, 0, Kernel32FileIdentityNative.TraversalShare, 1,
                NtFileNative.OpenOptions, 0, 0);
            if (status != 0)
            {
                handle?.Dispose();
                int error = unchecked((int)NtFileNative.RtlNtStatusToDosError(status));
                Exception failure = WindowsStorageObjectIdentityReader.NativeFailure(error, path);
                failure.Data["NTSTATUS"] = status;
                failure.Data["Win32Error"] = error;
                throw failure;
            }

            return handle;
        }
        finally
        {
            if (added)
            {
                parent.DangerousRelease();
            }

            Marshal.FreeHGlobal(name);
            Marshal.FreeHGlobal(text);
        }
    }

    public void Dispose()
    {
        for (int i = handles.Count - 1; i >= 0; i--)
        {
            handles[i].Dispose();
        }

        handles.Clear();
    }
}
