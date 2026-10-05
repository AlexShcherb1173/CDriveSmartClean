using System.Collections.ObjectModel;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Application.Analysis;

/// <summary>Authoritative roots used by deterministic storage classification.</summary>
/// <remarks>
/// Production callers must obtain this context from a trusted <see cref="IStorageClassificationContextProvider"/>.
/// Providers are responsible for root provenance and same-volume binding. Analysis consumes these roots as trusted
/// input and does not independently reopen or revalidate them against the filesystem. Supplying a
/// <see cref="VolumeIdentity"/> does not by itself make arbitrary caller-created roots authoritative.
/// </remarks>
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
        return Array.AsReadOnly(values.Select(Normalize)
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Order(StringComparer.Ordinal).First())
            .Order(StringComparer.Ordinal).ToArray());
    }

    private static string? Optional(string? path) => path is null ? null : Normalize(path);

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.Equals(path.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Context paths cannot be empty or have surrounding whitespace.");
        if (path.Contains('/') || path.Contains('\0'))
            throw new ArgumentException("Windows context paths must use canonical native separators.");

        int rootLength;
        if (path.Length >= 3 && IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\')
        {
            if (path.AsSpan(2).Contains(':'))
                throw new ArgumentException("Windows context paths cannot contain ADS syntax.");
            rootLength = 3;
        }
        else
        {
            const string volumePrefix = @"\\?\Volume";
            const int guidWithBracesLength = 38;
            int separatorIndex = volumePrefix.Length + guidWithBracesLength;
            if (!path.StartsWith(volumePrefix, StringComparison.OrdinalIgnoreCase) ||
                path.Length <= separatorIndex || path[separatorIndex] != '\\' ||
                !Guid.TryParseExact(path.AsSpan(volumePrefix.Length, guidWithBracesLength), "B", out Guid id) ||
                id == Guid.Empty || path.AsSpan(separatorIndex + 1).Contains(':'))
                throw new ArgumentException("A rooted drive or GUID-volume Windows path is required.");
            rootLength = separatorIndex + 1;
        }

        string remainder = path[rootLength..];
        if (remainder.Length == 0) return path[..rootLength];
        if (remainder.EndsWith('\\'))
        {
            remainder = remainder[..^1];
            if (remainder.Length == 0)
                throw new ArgumentException("Windows context paths must not duplicate the root separator.");
        }
        if (remainder.Split('\\').Any(component => component.Length == 0 || component is "." or ".."))
            throw new ArgumentException("Windows context paths must have canonical components.");
        return path[..rootLength] + remainder;
    }

    private static bool IsAsciiLetter(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}
