using System.Collections.ObjectModel;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Application.Analysis;

public sealed class StorageClassificationContext
{
    public StorageClassificationContext(VolumeIdentity volumeIdentity, string windowsDirectory,
        IEnumerable<string> programFilesRoots, string? programDataRoot, string? currentUserProfileRoot,
        IEnumerable<string> currentUserAppDataRoots, string? publicRoot, string? userProfilesRoot,
        IEnumerable<string> unavailableContextKeys)
    {
        ArgumentNullException.ThrowIfNull(volumeIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowsDirectory);
        VolumeIdentity = volumeIdentity;
        WindowsDirectory = Normalize(windowsDirectory);
        ProgramFilesRoots = CopyPaths(programFilesRoots, nameof(programFilesRoots));
        ProgramDataRoot = Optional(programDataRoot);
        CurrentUserProfileRoot = Optional(currentUserProfileRoot);
        CurrentUserAppDataRoots = CopyPaths(currentUserAppDataRoots, nameof(currentUserAppDataRoots));
        PublicRoot = Optional(publicRoot);
        UserProfilesRoot = Optional(userProfilesRoot);
        ArgumentNullException.ThrowIfNull(unavailableContextKeys);
        string[] keys = unavailableContextKeys.ToArray();
        if (keys.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Unavailable context keys cannot be empty.", nameof(unavailableContextKeys));
        UnavailableContextKeys = Array.AsReadOnly(keys.Select(key => key.Trim()).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray());
    }

    public VolumeIdentity VolumeIdentity { get; }
    public string WindowsDirectory { get; }
    public IReadOnlyList<string> ProgramFilesRoots { get; }
    public string? ProgramDataRoot { get; }
    public string? CurrentUserProfileRoot { get; }
    public IReadOnlyList<string> CurrentUserAppDataRoots { get; }
    public string? PublicRoot { get; }
    public string? UserProfilesRoot { get; }
    public IReadOnlyList<string> UnavailableContextKeys { get; }
    public bool IsComplete => UnavailableContextKeys.Count == 0;

    private static ReadOnlyCollection<string> CopyPaths(IEnumerable<string> paths, string name)
    {
        ArgumentNullException.ThrowIfNull(paths, name);
        string[] values = paths.ToArray();
        if (values.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Paths cannot be empty.", name);
        return Array.AsReadOnly(values.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal).ToArray());
    }

    private static string? Optional(string? path) => path is null ? null : Normalize(path);

    private static string Normalize(string path)
    {
        string value = path.Trim();
        if (value.Contains('/')) throw new ArgumentException("Windows context paths must use native separators.");
        if (value.Length < 3 || value[1] != ':' && !value.StartsWith(@"\\?\Volume", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An absolute Windows path is required.");
        return value.Length > 3 ? value.TrimEnd('\\') : value;
    }
}
