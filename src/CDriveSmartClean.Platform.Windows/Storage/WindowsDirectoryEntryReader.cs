using System.Buffers.Binary;
using System.Runtime.InteropServices;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Platform.Windows.Interop;

namespace CDriveSmartClean.Platform.Windows.Storage;

internal static class WindowsDirectoryEntryReader
{
    internal const int BufferSize = 65536;
    internal const int NameOffset = 88;

    [StructLayout(LayoutKind.Sequential)]
    private struct DirectoryLayout
    {
        internal uint Next;
        internal uint Index;
        internal long Creation;
        internal long Access;
        internal long Write;
        internal long Change;
        internal long End;
        internal long Allocation;
        internal uint Attributes;
        internal uint NameLength;
        internal uint Ea;
        internal uint Tag;
        internal Kernel32FileIdentityNative.FileId128 Id;
        internal ushort FirstChar;
    }

    internal sealed record NativeEntry(string Name, uint Attributes, uint Tag, Guid Id);

    internal static void VerifyLayout()
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            Marshal.SizeOf<DirectoryLayout>() != 96 ||
            Marshal.OffsetOf<DirectoryLayout>(nameof(DirectoryLayout.Next)) != 0 ||
            Marshal.OffsetOf<DirectoryLayout>(nameof(DirectoryLayout.Attributes)) != 56 ||
            Marshal.OffsetOf<DirectoryLayout>(nameof(DirectoryLayout.NameLength)) != 60 ||
            Marshal.OffsetOf<DirectoryLayout>(nameof(DirectoryLayout.Tag)) != 68 ||
            Marshal.OffsetOf<DirectoryLayout>(nameof(DirectoryLayout.Id)) != 72 ||
            Marshal.OffsetOf<DirectoryLayout>(nameof(DirectoryLayout.FirstChar)) != NameOffset)
        {
            throw new PlatformNotSupportedException("Extended directory ABI has only been validated for Windows x64.");
        }
    }

    internal static async Task ReadAsync(WindowsDirectoryHandleChain chain, string path, IStorageEntrySink sink, CancellationToken token)
    {
        VerifyLayout();
        nint buffer = Marshal.AllocHGlobal(BufferSize);
        try
        {
            if ((buffer.ToInt64() & 7) != 0)
            {
                throw new IOException("Native directory buffer is not aligned.");
            }

            var cleared = new byte[BufferSize];
            await ReadBatchesAsync(chain, path, sink, first =>
            {
                Marshal.Copy(cleared, 0, buffer, BufferSize);
                bool success = Kernel32FileIdentityNative.GetDirectoryInfo(chain.Handle,
                    first ? Kernel32FileIdentityNative.FileInfoByHandleClass.FileIdExtdDirectoryRestartInfo :
                        Kernel32FileIdentityNative.FileInfoByHandleClass.FileIdExtdDirectoryInfo, buffer, BufferSize);
                int error = Marshal.GetLastPInvokeError();
                if (!AcceptResult(success, error, path))
                {
                    return null;
                }

                var batch = new byte[BufferSize];
                Marshal.Copy(buffer, batch, 0, batch.Length);
                return batch;
            }, token).ConfigureAwait(false);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static bool AcceptResult(bool success, int error, string path)
    {
        if (success)
        {
            return true;
        }

        if (error == 18)
        {
            return false;
        }

        throw WindowsStorageObjectIdentityReader.NativeFailure(error, path);
    }

    // Instance-scoped native-boundary seam; no global injector and no bypass of chain authorization.
    internal static async Task ReadBatchesAsync(WindowsDirectoryHandleChain chain, string path,
        IStorageEntrySink sink, Func<bool, byte[]?> readBatch, CancellationToken token)
    {
        bool first = true;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            byte[]? bytes = readBatch(first);
            if (bytes is null)
            {
                token.ThrowIfCancellationRequested();
                return;
            }

            first = false;
            // Validate and copy the entire batch before any await; no native pointer escapes.
            foreach (var native in ParseBatch(bytes))
            {
                token.ThrowIfCancellationRequested();
                string child = path.TrimEnd('\\') + "\\" + native.Name;
                var id = native.Id == Guid.Empty ? null : WindowsStorageObjectIdentityReader.FromVerifiedDirectory(chain, native.Id, child);
                var entry = new StorageEntry(chain.VolumeIdentity, id, child,
                    (native.Attributes & 0x10) != 0 ? StorageObjectKind.Directory : StorageObjectKind.File,
                    (native.Attributes & 0x400) != 0 ? ReparseKind.Other : ReparseKind.None);
                await sink.WriteAsync(entry, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
            }
        }
    }

    internal static NativeEntry[] ParseBatch(byte[] bytes)
    {
        if (bytes.Length > BufferSize)
        {
            throw new IOException("Native directory batch exceeds allocation limit.");
        }

        var entries = new List<NativeEntry>();
        int offset = 0;
        while (true)
        {
            if (offset > bytes.Length - NameOffset)
            {
                throw new IOException("Truncated native directory header or missing terminal record.");
            }

            ReadOnlySpan<byte> record = bytes.AsSpan(offset);
            uint next = BinaryPrimitives.ReadUInt32LittleEndian(record);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(record[60..]);
            if (length == 0 || (length & 1) != 0 || length > (uint)(record.Length - NameOffset))
            {
                throw new IOException("Invalid native filename length.");
            }

            if (next != 0 && ((next & 7) != 0 || next < NameOffset + length || next > (uint)(record.Length - NameOffset)))
            {
                throw new IOException("Invalid native record offset.");
            }

            // Construct UTF-16 code units directly: Encoding.Unicode may replace unpaired surrogates.
            var chars = new char[checked((int)length / 2)];
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(record[(NameOffset + i * 2)..]);
            }

            string name = new(chars);
            if (name is not "." and not "..")
            {
                try
                {
                    WindowsDirectoryHandleChain.ValidateComponent(name);
                }
                catch (ArgumentException error)
                {
                    throw new IOException("Invalid native component.", error);
                }

                entries.Add(new(name, BinaryPrimitives.ReadUInt32LittleEndian(record[56..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(record[68..]), new Guid(record.Slice(72, 16))));
            }

            if (next == 0)
            {
                return [.. entries];
            }

            offset = checked(offset + (int)next);
        }
    }
}
