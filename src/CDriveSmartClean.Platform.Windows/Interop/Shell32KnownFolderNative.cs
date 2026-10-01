using System.Runtime.InteropServices;

namespace CDriveSmartClean.Platform.Windows.Interop;

internal static class Shell32KnownFolderNative
{
    internal const uint KfFlagDontVerify = 0x00004000;

    /// <summary>
    /// Resolves a known folder for the current process user when token is zero.
    /// The returned CoTaskMem pointer must be released even when conversion fails.
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int SHGetKnownFolderPath(
        in Guid knownFolderId,
        uint flags,
        IntPtr token,
        out IntPtr path);
}
