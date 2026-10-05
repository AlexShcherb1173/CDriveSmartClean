using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Platform.Windows.Interop;

namespace CDriveSmartClean.Platform.Windows.Storage;

[SupportedOSPlatform("windows")]
public sealed class WindowsStorageClassificationContextProvider : IStorageClassificationContextProvider
{
    private static readonly Guid ProgramFiles = new("905e63b6-c1bf-494e-b29c-65b732d3d21a");
    private static readonly Guid ProgramFilesX86 = new("7c5a40ef-a0fb-4bfc-874a-c0f2e0b9fa8e");
    private static readonly Guid ProgramData = new("62ab5d82-fdc1-4dc3-a9dd-070d1d495d97");
    private static readonly Guid Profile = new("5e6c858f-0e22-4760-9afe-ea3317b67173");
    private static readonly Guid RoamingAppData = new("3eb685db-65f9-4cf6-a03a-e3ef65729f3d");
    private static readonly Guid LocalAppData = new("f1b32785-6fba-4fcf-9d55-7b8e7f157091");
    private static readonly Guid Public = new("dfdf76a2-c82a-4d63-906a-5644ac457385");
    private static readonly Guid UserProfiles = new("0762d272-c50a-4bb0-a382-697dcd729b80");
    private readonly Func<SystemVolumeDescriptor> trustedVolumeProvider;
    private readonly Func<string> windowsDirectoryProvider;
    private readonly Func<Guid, string?> knownFolderProvider;
    private readonly Func<string, VolumeIdentity?> volumeIdentityProvider;

    public WindowsStorageClassificationContextProvider()
        : this(
            () => new WindowsSystemVolumeProvider().GetSystemVolume(),
            GetWindowsDirectory,
            GetKnownFolder,
            ResolveVolume)
    {
    }

    internal WindowsStorageClassificationContextProvider(
        Func<SystemVolumeDescriptor> trustedVolumeProvider,
        Func<string> windowsDirectoryProvider,
        Func<Guid, string?> knownFolderProvider,
        Func<string, VolumeIdentity?> volumeIdentityProvider)
    {
        ArgumentNullException.ThrowIfNull(trustedVolumeProvider);
        ArgumentNullException.ThrowIfNull(windowsDirectoryProvider);
        ArgumentNullException.ThrowIfNull(knownFolderProvider);
        ArgumentNullException.ThrowIfNull(volumeIdentityProvider);
        this.trustedVolumeProvider = trustedVolumeProvider;
        this.windowsDirectoryProvider = windowsDirectoryProvider;
        this.knownFolderProvider = knownFolderProvider;
        this.volumeIdentityProvider = volumeIdentityProvider;
    }

    public StorageClassificationContext GetClassificationContext(SystemVolumeDescriptor systemVolume)
    {
        ArgumentNullException.ThrowIfNull(systemVolume);
        SystemVolumeDescriptor trusted = trustedVolumeProvider();
        if (trusted is null) throw new InvalidOperationException("Trusted system-volume provider returned null.");
        if (!trusted.VolumeIdentity.Equals(systemVolume.VolumeIdentity) ||
            !Normalize(trusted.RootPath).Equals(Normalize(systemVolume.RootPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Supplied system volume contradicts the trusted Windows system volume.");

        string windowsDirectory = windowsDirectoryProvider();
        ArgumentException.ThrowIfNullOrWhiteSpace(windowsDirectory);
        RequireVolume(windowsDirectory, systemVolume.VolumeIdentity);
        var unavailable = new List<string>();
        var programRoots = new List<string>();
        AddOptional(programRoots, ProgramFiles, "ProgramFiles", systemVolume.VolumeIdentity, unavailable);
        AddOptional(programRoots, ProgramFilesX86, "ProgramFilesX86", systemVolume.VolumeIdentity, unavailable);
        string? programData = Optional(ProgramData, "ProgramData", systemVolume.VolumeIdentity, unavailable);
        string? profile = Optional(Profile, "Profile", systemVolume.VolumeIdentity, unavailable);
        var appData = new List<string>();
        AddOptional(appData, RoamingAppData, "RoamingAppData", systemVolume.VolumeIdentity, unavailable);
        AddOptional(appData, LocalAppData, "LocalAppData", systemVolume.VolumeIdentity, unavailable);
        string? publicRoot = Optional(Public, "Public", systemVolume.VolumeIdentity, unavailable);
        string? userProfiles = Optional(UserProfiles, "UserProfiles", systemVolume.VolumeIdentity, unavailable);
        return new StorageClassificationContext(systemVolume.VolumeIdentity, windowsDirectory, programRoots,
            programData, profile, appData, publicRoot, userProfiles, unavailable);
    }

    private void AddOptional(List<string> target, Guid id, string key,
        VolumeIdentity expected, List<string> unavailable)
    {
        string? path = Optional(id, key, expected, unavailable);
        if (path is not null) target.Add(path);
    }

    private string? Optional(Guid id, string key, VolumeIdentity expected, List<string> unavailable)
    {
        string? path = knownFolderProvider(id);
        VolumeIdentity? actual = path is null ? null : volumeIdentityProvider(path);
        if (path is null || !expected.Equals(actual))
        {
            unavailable.Add(key);
            return null;
        }
        return Normalize(path);
    }

    private static string? GetKnownFolder(Guid id)
    {
        IntPtr pointer = IntPtr.Zero;
        try
        {
            int result = Shell32KnownFolderNative.SHGetKnownFolderPath(
                in id, Shell32KnownFolderNative.KfFlagDontVerify, IntPtr.Zero, out pointer);
            if (result != 0 || pointer == IntPtr.Zero) return null;
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer);
        }
    }

    private static string GetWindowsDirectory()
    {
        var buffer = new char[260];
        uint length = Kernel32VolumeNative.GetSystemWindowsDirectoryW(buffer, (uint)buffer.Length);
        if (length == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (length >= buffer.Length)
        {
            buffer = new char[checked((int)length + 1)];
            length = Kernel32VolumeNative.GetSystemWindowsDirectoryW(buffer, (uint)buffer.Length);
            if (length == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (length >= buffer.Length) throw new InvalidOperationException("Windows directory exceeded the native buffer.");
        }
        return Normalize(new string(buffer, 0, checked((int)length)));
    }

    private void RequireVolume(string path, VolumeIdentity expected)
    {
        if (!expected.Equals(volumeIdentityProvider(path)))
            throw new InvalidOperationException("Mandatory Windows directory volume contradiction.");
    }

    private static VolumeIdentity? ResolveVolume(string path) =>
        TryVolume(path, out VolumeIdentity? identity) ? identity : null;

    private static bool TryVolume(string path, out VolumeIdentity? identity)
    {
        identity = null;
        var root = new char[32768];
        if (!Kernel32VolumeNative.GetVolumePathNameW(path, root, (uint)root.Length)) return false;
        string rootPath = Read(root);
        var volume = new char[50];
        if (!Kernel32VolumeNative.GetVolumeNameForVolumeMountPointW(rootPath, volume, (uint)volume.Length)) return false;
        string value = Read(volume);
        const string prefix = @"\\?\Volume";
        if (value.Length != 49 || !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            value[^1] != '\\' || !Guid.TryParseExact(value.AsSpan(prefix.Length, 38), "B", out Guid id) ||
            id == Guid.Empty)
            return false;
        identity = new VolumeIdentity(id);
        return true;
    }

    private static string Read(char[] buffer)
    {
        int length = Array.IndexOf(buffer, '\0');
        if (length <= 0) throw new InvalidOperationException("Windows returned an empty native path.");
        return new string(buffer, 0, length);
    }

    private static string Normalize(string path)
    {
        string value = path.Trim();
        return value.Length > 3 ? value.TrimEnd('\\') : value;
    }
}
