using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Platform.Windows.Storage;
using Microsoft.Win32.SafeHandles;

namespace CDriveSmartClean.Windows.IntegrationTests.Storage;

internal class WindowsStorageFixture : IDisposable
{
    private const uint FileFileCompression = 0x00000010;
    private const uint GenericReadWrite = 0xC0000000;
    private readonly List<string> files = [];
    private readonly List<string> directories = [];
    private readonly List<string> junctions = [];

    internal WindowsStorageFixture()
    {
        Root = System.IO.Directory.CreateTempSubdirectory("CDriveSmartClean-R2B-").FullName;
        try
        {
            AddDirectory("child");
            AddFile("ordinary.bin");
            AddFile("hidden.bin");
            AddFile("child\\grandchild.bin");
            File.SetAttributes(Hidden, FileAttributes.Hidden | FileAttributes.System);
            (Volume, Anchor, Enumerator) = Bind(Root);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal string Root { get; }
    internal string Child => Path.Combine(Root, "child");
    internal string Ordinary => Path.Combine(Root, "ordinary.bin");
    internal string Hidden => Path.Combine(Root, "hidden.bin");
    internal string Grandchild => Path.Combine(Child, "grandchild.bin");
    internal SystemVolumeDescriptor Volume { get; }
    internal object Anchor { get; }
    internal WindowsStorageEnumerator Enumerator { get; }

    internal string Owned(string relative)
    {
        string path = Path.GetFullPath(Path.Combine(Root, relative));
        if (!path.StartsWith(Root + "\\", StringComparison.Ordinal))
        {
            throw new ArgumentException("Not a fixture-owned path.", nameof(relative));
        }

        return path;
    }

    internal string AddDirectory(string relative)
    {
        string path = Owned(relative);
        System.IO.Directory.CreateDirectory(path);
        directories.Add(path);
        return path;
    }

    internal string AddFile(string relative)
    {
        string path = Owned(relative);
        File.WriteAllText(path, "test-owned fixture");
        files.Add(path);
        return path;
    }

    internal string AddEmptyFile(string relative)
    {
        string path = Owned(relative);
        File.WriteAllBytes(path, []);
        files.Add(path);
        return path;
    }

    internal bool TryAddSparseFile(string relative, out string path, out int error)
    {
        path = AddEmptyFile(relative);
        using (SafeFileHandle handle = Open(path, 0x40000000))
        {
            if (!DeviceIoControl(handle, 0x000900C4, [], 0, 0, 0, out _, 0))
            {
                error = Marshal.GetLastPInvokeError();
                return false;
            }
        }

        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write,
                   FileShare.ReadWrite | FileShare.Delete))
        {
            stream.SetLength(1024 * 1024);
        }

        error = 0;
        return true;
    }

    internal bool TryAddCompressedFile(string relative, out string path, out int error)
    {
        path = AddEmptyFile(relative);
        if (!SupportsFileCompression(path))
        {
            error = 0;
            return false;
        }

        using (SafeFileHandle handle = Open(path, GenericReadWrite))
        {
            byte[] format = [1, 0]; // COMPRESSION_FORMAT_DEFAULT
            if (!DeviceIoControl(handle, 0x0009C040, format, (uint)format.Length, 0, 0, out _, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

        File.WriteAllBytes(path, new byte[64 * 1024]);
        error = 0;
        return true;
    }

    private static bool SupportsFileCompression(string path)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(path))!;
        if (!GetVolumeInformationW(root, 0, 0, out _, out _, out uint flags, 0, 0))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return (flags & FileFileCompression) != 0;
    }

    internal void AddHardLink(string relative, string existing)
    {
        string path = Owned(relative);
        if (!CreateHardLinkW(path, Owned(existing), 0))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        files.Add(path);
    }

    internal void AddJunction(string relative, string targetRelative)
    {
        string path = Owned(relative);
        string target = Owned(targetRelative);
        System.IO.Directory.CreateDirectory(path);
        junctions.Add(path);
        directories.Add(path);
        byte[] substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
        byte[] print = Encoding.Unicode.GetBytes(target);
        byte[] data = new byte[16 + substitute.Length + 2 + print.Length + 2];
        BitConverter.GetBytes(0xA0000003u).CopyTo(data, 0);
        BitConverter.GetBytes(checked((ushort)(data.Length - 8))).CopyTo(data, 4);
        BitConverter.GetBytes(checked((ushort)substitute.Length)).CopyTo(data, 10);
        BitConverter.GetBytes(checked((ushort)(substitute.Length + 2))).CopyTo(data, 12);
        BitConverter.GetBytes(checked((ushort)print.Length)).CopyTo(data, 14);
        substitute.CopyTo(data, 16);
        print.CopyTo(data, 18 + substitute.Length);
        using var handle = Open(path, 0x40000000);
        if (!DeviceIoControl(handle, 0x900A4, data, (uint)data.Length, 0, 0, out _, 0))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    internal static (SystemVolumeDescriptor Volume, object Anchor, WindowsStorageEnumerator Enumerator) Bind(string root)
    {
        using var handle = Open(root);
        string final = Final(handle);
        string guidRoot = final[..49];
        var volume = new VolumeIdentity(Guid.Parse(guidRoot.AsSpan(10, 38)));
        var descriptor = new SystemVolumeDescriptor(volume, root);
        var expected = ReadIdentity(volume, root)!;
        object anchor = Invoke("WindowsStorageRootAnchor", "Fixture", null, descriptor,
            final[49..].Split('\\', StringSplitOptions.RemoveEmptyEntries), expected)!;
        var enumerator = (WindowsStorageEnumerator)Activator.CreateInstance(typeof(WindowsStorageEnumerator),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [anchor], null)!;
        return (descriptor, anchor, enumerator);
    }

    internal static Type ProductionType(string name) => typeof(WindowsStorageEnumerator).Assembly.GetType(
        "CDriveSmartClean.Platform.Windows.Storage." + name, true)!;

    internal static object? Invoke(string type, string method, object? instance, params object?[] args)
    {
        try
        {
            return ProductionType(type).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!.Invoke(instance, args);
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    internal static StorageObjectIdentity? ReadIdentity(VolumeIdentity volume, string path)
    {
        using var handle = CreateFileW(path, 0, 7, 0, 3, 0x02200000, 0);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error is 2 or 3 or 5 or 32 or 50)
            {
                return null;
            }

            throw new Win32Exception(error);
        }

        string final = Final(handle);
        if (Guid.Parse(final.AsSpan(10, 38)) != volume.Id)
        {
            throw new System.Security.SecurityException("Test identity read has a different real volume.");
        }

        if (!GetFileInformationByHandleEx(handle, 18, out var info, 24))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return new(volume, info.Id);
    }

    private static SafeFileHandle Open(string path, uint access = 0)
    {
        var handle = CreateFileW(path, access, 7, 0, 3, 0x02200000, 0);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        return handle;
    }

    private static string Final(SafeFileHandle handle)
    {
        var buffer = new char[32768];
        uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 1);
        if (length == 0 || length >= buffer.Length)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return new(buffer, 0, (int)length);
    }

    public void Dispose()
    {
        foreach (string junction in junctions.Distinct())
        {
            if (System.IO.Directory.Exists(junction) &&
                (File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0)
            {
                System.IO.Directory.Delete(junction); // Unlink only; never recurse into its target.
            }
        }

        foreach (string file in files)
        {
            if (File.Exists(file))
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
        }

        foreach (string directory in directories.Distinct().OrderByDescending(p => p.Length))
        {
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory);
            }
        }

        if (System.IO.Directory.Exists(Root))
        {
            System.IO.Directory.Delete(Root);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInfo { internal ulong Serial; internal Guid Id; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, [Out] char[] buffer, uint size, uint flags);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out NativeInfo info, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string path, string existing, nint security);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] data, uint length, nint output, uint size, out uint returned, nint overlapped);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(string rootPathName, nint volumeNameBuffer,
        uint volumeNameSize, out uint volumeSerialNumber, out uint maximumComponentLength,
        out uint fileSystemFlags, nint fileSystemNameBuffer, uint fileSystemNameSize);
}

// Each chain pins shared ancestors (including Temp); concurrent fixture renames would test each other.
[Xunit.CollectionDefinition("WindowsNativeTraversal", DisableParallelization = true)]
public sealed class WindowsNativeTraversalDefinition
{
}
